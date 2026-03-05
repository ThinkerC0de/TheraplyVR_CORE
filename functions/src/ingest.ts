import * as admin from "firebase-admin";
import { onRequest } from "firebase-functions/v2/https";
import { logger } from "firebase-functions/v2";
import {
  SessionIngestBatchRequest,
  SessionIngestBatchResponse,
  SessionIngestEventEnvelope,
  SessionIngestEventResult,
  ControllerConnectionPayload,
  GameEndPayload,
  GameStartPayload,
  InteractionEventPayload,
  SessionStartPayload,
  SessionStopPayload,
} from "./types";

const db = admin.firestore();

// ─── Auth helper ─────────────────────────────────────────────────────────────
// Unity/Quest does not use Firebase Auth. Auth is implicit: the sessionId is a
// capability token created by an authenticated therapist on the mobile app.
// We verify the session exists in Firestore before accepting events for it.

const _sessionExistsCache = new Map<string, number>();
const SESSION_CACHE_TTL_MS = 60_000;

async function sessionExists(sessionId: string): Promise<boolean> {
  const now = Date.now();
  const cached = _sessionExistsCache.get(sessionId);
  if (cached !== undefined && now - cached < SESSION_CACHE_TTL_MS) return true;

  const snap = await db.collection("therapy_sessions").doc(sessionId).get();
  if (snap.exists) {
    _sessionExistsCache.set(sessionId, now);
    return true;
  }
  return false;
}

// ─── Payload parse helper ────────────────────────────────────────────────────

function parsePayload<T>(payloadJson: string): T {
  try {
    return JSON.parse(payloadJson) as T;
  } catch {
    return {} as T;
  }
}

// ─── Event type routing ──────────────────────────────────────────────────────

type EventStatus = "ok" | "already_processed" | "error";

async function processEvent(
  env: SessionIngestEventEnvelope,
): Promise<{ status: EventStatus; errorCode?: string }> {
  const sessionRef = db.collection("therapy_sessions").doc(env.sessionId);

  const et = env.eventType.toLowerCase();

  // session lifecycle
  if (et === "session_start") return handleSessionStart(sessionRef, env);
  if (et === "session_stop") return handleSessionStop(sessionRef, env);
  if (et === "error" || et === "session_error") return handleSessionError(sessionRef, env);

  // game lifecycle
  if (et === "game_start" || et === "game_started") return handleGameStart(sessionRef, env);
  if (et === "game_end" || et === "game_completed" || et === "game_failed")
    return handleGameEnd(sessionRef, env);

  // interaction
  if (et === "interaction_event") return handleInteractionEvent(sessionRef, env);

  // controller connectivity
  if (et === "controller_connected" || et === "controller_reconnected" || et === "controller_disconnected")
    return handleControllerEvent(sessionRef, env);

  // motion trace (payload carries the trace metadata, not the binary)
  if (et === "motion_trace_metadata") return handleMotionTrace(sessionRef, env);

  // unknown → raw_events
  return handleRawEvent(sessionRef, env);
}

// ─── Handlers ────────────────────────────────────────────────────────────────

async function handleSessionStart(
  sessionRef: admin.firestore.DocumentReference,
  env: SessionIngestEventEnvelope,
): Promise<{ status: EventStatus }> {
  const p = parsePayload<SessionStartPayload>(env.payloadJson);
  const startedAtUtc = p.startedAtUtc ?? env.createdAtUtc;

  await sessionRef.set(
    {
      patientId: env.patientId,
      therapistId: env.therapistId,
      deviceId: env.deviceId,
      status: "active",
      startedAtUtc,
      endedAtUtc: null,
      durationSec: null,
      endReason: null,
      summary: {
        totalInteractions: 0,
        totalHits: 0,
        totalMisses: 0,
        gamesPlayedCount: 0,
      },
      connectionEvents: [],
    },
    // merge so a retry doesn't overwrite data written by later events
    { merge: true },
  );
  return { status: "ok" };
}

async function handleSessionStop(
  sessionRef: admin.firestore.DocumentReference,
  env: SessionIngestEventEnvelope,
): Promise<{ status: EventStatus }> {
  const p = parsePayload<SessionStopPayload>(env.payloadJson);
  const endedAtUtc = p.endedAtUtc ?? env.createdAtUtc;

  await sessionRef.set(
    {
      status: "completed",
      endedAtUtc,
      endReason: p.endReason ?? null,
      ...(p.durationSec != null ? { durationSec: p.durationSec } : {}),
    },
    { merge: true },
  );
  return { status: "ok" };
}

async function handleSessionError(
  sessionRef: admin.firestore.DocumentReference,
  env: SessionIngestEventEnvelope,
): Promise<{ status: EventStatus }> {
  await sessionRef.set({ status: "error" }, { merge: true });

  // also store in raw_events for debugging
  await sessionRef.collection("raw_events").doc(env.eventId).set({
    eventType: env.eventType,
    payloadJson: env.payloadJson,
    createdAtUtc: env.createdAtUtc,
    sequence: env.sequence,
  });
  return { status: "ok" };
}

async function handleGameStart(
  sessionRef: admin.firestore.DocumentReference,
  env: SessionIngestEventEnvelope,
): Promise<{ status: EventStatus }> {
  const p = parsePayload<GameStartPayload>(env.payloadJson);
  const gameRunId = p.gameRunId ?? p.taskRunId ?? env.eventId;

  const gameRunRef = sessionRef.collection("game_runs").doc(gameRunId);
  const snap = await gameRunRef.get();
  if (snap.exists) return { status: "already_processed" };

  await gameRunRef.set({
    gameId: p.gameId ?? "",
    startedAtUtc: p.startedAtUtc ?? env.createdAtUtc,
    endedAtUtc: null,
    finalState: null,
    durationSec: null,
    configVersion: p.configVersion ?? null,
    summary: {
      interactionCount: 0,
      hitCount: 0,
      missCount: 0,
      avgReactionTimeMs: null,
    },
  });

  await sessionRef.set(
    { summary: { gamesPlayedCount: admin.firestore.FieldValue.increment(1) } },
    { merge: true },
  );
  return { status: "ok" };
}

async function handleGameEnd(
  sessionRef: admin.firestore.DocumentReference,
  env: SessionIngestEventEnvelope,
): Promise<{ status: EventStatus }> {
  const p = parsePayload<GameEndPayload>(env.payloadJson);
  const gameRunId = p.gameRunId ?? p.taskRunId ?? env.eventId;

  const finalState =
    env.eventType.toLowerCase() === "game_completed"
      ? "completed"
      : env.eventType.toLowerCase() === "game_failed"
        ? "failed"
        : (p.finalState ?? "completed");

  const update: admin.firestore.UpdateData<Record<string, unknown>> = {
    endedAtUtc: p.endedAtUtc ?? env.createdAtUtc,
    finalState,
  };
  if (p.durationSec != null) update.durationSec = p.durationSec;
  if (p.interactionCount != null) update["summary.interactionCount"] = p.interactionCount;
  if (p.hitCount != null) update["summary.hitCount"] = p.hitCount;
  if (p.missCount != null) update["summary.missCount"] = p.missCount;
  if (p.avgReactionTimeMs != null) update["summary.avgReactionTimeMs"] = p.avgReactionTimeMs;

  await sessionRef.collection("game_runs").doc(gameRunId).set(update, { merge: true });
  return { status: "ok" };
}

async function handleInteractionEvent(
  sessionRef: admin.firestore.DocumentReference,
  env: SessionIngestEventEnvelope,
): Promise<{ status: EventStatus }> {
  const p = parsePayload<InteractionEventPayload>(env.payloadJson);

  const interactionRef = sessionRef.collection("interactions").doc(env.eventId);

  // idempotency: use create-or-skip pattern
  const snap = await interactionRef.get();
  if (snap.exists) return { status: "already_processed" };

  const batch = db.batch();

  batch.set(interactionRef, {
    occurredAtUtc: p.occurredAtUtc ?? env.createdAtUtc,
    sequenceNo: env.sequence,
    gameRunId: p.taskRunId ?? "",
    eventCategory: p.interactionType ?? "",
    actionOutcome: p.actionOutcome ?? "",
    targetId: p.targetId ?? "",
    targetName: p.targetName ?? "",
    inputHand: p.inputHand ?? "",
    inputSource: p.inputSource ?? "",
    inputValue: p.inputValue ?? null,
    sourceComponent: p.sourceComponent ?? "",
    reasonCode: p.reasonCode ?? null,
    traceRef: p.trace_ref ?? null,
  });

  // session summary increments
  const sessionInc: Record<string, admin.firestore.FieldValue> = {
    "summary.totalInteractions": admin.firestore.FieldValue.increment(1),
  };
  if (p.actionOutcome === "CORRECT") {
    sessionInc["summary.totalHits"] = admin.firestore.FieldValue.increment(1);
  } else if (p.actionOutcome === "INCORRECT") {
    sessionInc["summary.totalMisses"] = admin.firestore.FieldValue.increment(1);
  }
  batch.set(sessionRef, sessionInc, { merge: true });

  // game_run summary increments (only if we know the gameRunId)
  if (p.taskRunId) {
    const gameRunRef = sessionRef.collection("game_runs").doc(p.taskRunId);
    const gameRunInc: Record<string, admin.firestore.FieldValue> = {
      "summary.interactionCount": admin.firestore.FieldValue.increment(1),
    };
    if (p.actionOutcome === "CORRECT") {
      gameRunInc["summary.hitCount"] = admin.firestore.FieldValue.increment(1);
    } else if (p.actionOutcome === "INCORRECT") {
      gameRunInc["summary.missCount"] = admin.firestore.FieldValue.increment(1);
    }
    batch.set(gameRunRef, gameRunInc, { merge: true });
  }

  await batch.commit();
  return { status: "ok" };
}

async function handleControllerEvent(
  sessionRef: admin.firestore.DocumentReference,
  env: SessionIngestEventEnvelope,
): Promise<{ status: EventStatus }> {
  const p = parsePayload<ControllerConnectionPayload>(env.payloadJson);

  const eventType = env.eventType.toLowerCase();
  const type = eventType.includes("connected") && !eventType.includes("dis")
    ? "connected"
    : eventType.includes("reconnected")
      ? "reconnected"
      : "disconnected";

  const connectionEvent = {
    type,
    eventId: env.eventId, // enables natural arrayUnion idempotency
    controllerIp: p.controllerIp ?? "",
    occurredAt: p.occurredAt ?? env.createdAtUtc,
  };

  await sessionRef.set(
    { connectionEvents: admin.firestore.FieldValue.arrayUnion(connectionEvent) },
    { merge: true },
  );
  return { status: "ok" };
}

async function handleMotionTrace(
  sessionRef: admin.firestore.DocumentReference,
  env: SessionIngestEventEnvelope,
): Promise<{ status: EventStatus }> {
  const metadata = parsePayload<Record<string, unknown>>(env.payloadJson);

  const traceRef = sessionRef.collection("motion_traces").doc(env.eventId);
  const snap = await traceRef.get();
  if (snap.exists) return { status: "already_processed" };

  await traceRef.set({ metadata });
  return { status: "ok" };
}

async function handleRawEvent(
  sessionRef: admin.firestore.DocumentReference,
  env: SessionIngestEventEnvelope,
): Promise<{ status: EventStatus }> {
  const rawRef = sessionRef.collection("raw_events").doc(env.eventId);
  const snap = await rawRef.get();
  if (snap.exists) return { status: "already_processed" };

  await rawRef.set({
    eventType: env.eventType,
    payloadJson: env.payloadJson,
    createdAtUtc: env.createdAtUtc,
    sequence: env.sequence,
  });
  return { status: "ok" };
}

// ─── HTTP Cloud Function ─────────────────────────────────────────────────────

export const sessionIngest = onRequest(
  { cors: false, timeoutSeconds: 60 },
  async (req, res) => {
    if (req.method !== "POST") {
      res.status(405).json({ success: false, errorCode: "METHOD_NOT_ALLOWED", eventResults: [] });
      return;
    }

    // contract validation
    const body = req.body as Partial<SessionIngestBatchRequest>;
    if (!body || body.contractVersion !== 1 || !Array.isArray(body.events)) {
      res.status(400).json({ success: false, errorCode: "BAD_CONTRACT", eventResults: [] });
      return;
    }

    const eventResults: SessionIngestEventResult[] = [];
    const batchId = body.ingestBatchId ?? "unknown";
    const eventCount = body.events.length;
    const sessionIds = [...new Set(body.events.map(e => e?.sessionId ?? "").filter(Boolean))];
    const eventTypes = [...new Set(body.events.map(e => e?.eventType ?? ""))];
    logger.info("ingest: batch received", { batchId, eventCount, sessionIds, eventTypes });

    for (const envelope of body.events) {
      if (!envelope?.eventId || !envelope?.sessionId) {
        eventResults.push({ eventId: envelope?.eventId ?? "", status: "error", errorCode: "MISSING_FIELDS" });
        continue;
      }

      // Auth: sessionId is a capability token created by an authenticated therapist.
      // Reject events for sessions that don't exist in Firestore.
      if (!await sessionExists(envelope.sessionId)) {
        logger.warn("ingest: SESSION_NOT_FOUND", { sessionId: envelope.sessionId, eventType: envelope.eventType, eventId: envelope.eventId });
        eventResults.push({ eventId: envelope.eventId, status: "error", errorCode: "SESSION_NOT_FOUND" });
        continue;
      }

      try {
        const result = await processEvent(envelope);
        if (result.status !== "already_processed") {
          logger.info("ingest: event ok", { eventType: envelope.eventType, sessionId: envelope.sessionId, status: result.status });
        }
        eventResults.push({ eventId: envelope.eventId, ...result });
      } catch (err) {
        logger.error("ingest: event failed", { eventId: envelope.eventId, err });
        eventResults.push({ eventId: envelope.eventId, status: "error", errorCode: "INTERNAL" });
      }
    }

    const response: SessionIngestBatchResponse = { success: true, eventResults };
    res.status(200).json(response);
  },
);
