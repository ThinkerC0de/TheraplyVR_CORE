// ─── Unity → Cloud Function contract (contractVersion = 1) ──────────────────

export interface SessionIngestBatchRequest {
  contractVersion: 1;
  ingestBatchId: string;
  sourceDeviceId: string;
  submittedAtUtc: string;
  events: SessionIngestEventEnvelope[];
}

export interface SessionIngestEventEnvelope {
  eventId: string;
  sessionId: string;
  patientId: string;
  therapistId: string;
  deviceId: string;
  sequence: number;
  eventType: string;
  eventVersion: number;
  createdAtUtc: string;
  payloadJson: string;
  checksum: string;
}

export interface SessionIngestBatchResponse {
  success: boolean;
  errorCode?: string;
  eventResults: SessionIngestEventResult[];
}

export interface SessionIngestEventResult {
  eventId: string;
  status: "ok" | "already_processed" | "error";
  errorCode?: string;
}

// ─── Event payload shapes (parsed from envelope.payloadJson) ─────────────────

export interface SessionStartPayload {
  startedAtUtc?: string;
}

export interface SessionStopPayload {
  endedAtUtc?: string;
  endReason?: string;
  durationSec?: number;
}

export interface GameStartPayload {
  /** taskRunId in interaction events ↔ gameRunId here */
  gameRunId?: string;
  taskRunId?: string;
  gameId?: string;
  startedAtUtc?: string;
  configVersion?: string;
}

export interface GameEndPayload {
  gameRunId?: string;
  taskRunId?: string;
  endedAtUtc?: string;
  /** "completed" | "failed" */
  finalState?: string;
  durationSec?: number;
  interactionCount?: number;
  hitCount?: number;
  missCount?: number;
  avgReactionTimeMs?: number;
}

export interface ControllerConnectionPayload {
  controllerIp?: string;
  occurredAt?: string;
}

/** Canonical THERAPLY_INTERACTION_SCHEMA */
export interface InteractionEventPayload {
  schema?: string;
  schemaVersion?: string;
  eventId?: string;
  sequenceNumber?: number;
  taskRunId?: string;
  attemptId?: string;
  gameId?: string;
  eventType?: string;
  interactionType?: string;
  actionOutcome?: string; // "OBSERVED" | "CORRECT" | "INCORRECT"
  occurredAtUtc?: string;
  monotonicSec?: number;
  sourceComponent?: string;
  inputHand?: string;
  inputSource?: string;
  inputControl?: string;
  inputValue?: number;
  targetId?: string;
  targetName?: string;
  targetValid?: boolean;
  reasonCode?: string;
  // eslint-disable-next-line camelcase
  trace_ref?: string;
  details?: Record<string, unknown>;
}

// ─── Firestore document shapes ───────────────────────────────────────────────

export interface SessionDoc {
  patientId: string;
  therapistId: string;
  deviceId: string;
  status: "active" | "completed" | "error" | "unknown";
  startedAtUtc: string | null;
  endedAtUtc: string | null;
  durationSec: number | null;
  endReason: string | null;
  summary: SessionSummary;
  connectionEvents: ConnectionEvent[];
}

export interface SessionSummary {
  totalInteractions: number;
  totalHits: number;
  totalMisses: number;
  gamesPlayedCount: number;
}

export interface ConnectionEvent {
  type: string; // "connected" | "reconnected" | "disconnected"
  controllerIp: string;
  occurredAt: string;
}

export interface GameRunDoc {
  gameId: string;
  startedAtUtc: string;
  endedAtUtc: string | null;
  finalState: string | null;
  durationSec: number | null;
  configVersion: string | null;
  summary: GameRunSummary;
}

export interface GameRunSummary {
  interactionCount: number;
  hitCount: number;
  missCount: number;
  avgReactionTimeMs: number | null;
}

export interface InteractionDoc {
  occurredAtUtc: string;
  sequenceNo: number;
  gameRunId: string;
  eventCategory: string;
  actionOutcome: string;
  targetId: string;
  targetName: string;
  inputHand: string;
  inputSource: string;
  inputValue: number | null;
  sourceComponent: string;
  reasonCode: string | null;
  traceRef: string | null;
}

export interface MotionTraceDoc {
  metadata: {
    frameCount?: number;
    encoding?: string;
    checksum?: string;
    gameRunId?: string;
    occurredAt?: string;
    reasonCode?: string;
  };
}

export interface RawEventDoc {
  eventType: string;
  payloadJson: string;
  createdAtUtc: string;
  sequence: number;
}
