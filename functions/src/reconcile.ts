/**
 * reconcile — recomputes session & game_run summaries from stored interactions.
 *
 * Called when live incremental counters diverge from reality (e.g. after a
 * manual correction or a deduplication sweep).
 *
 * Callable: { sessionId: string, gameRunId?: string }
 * Returns:  { ok: true }
 */
import * as admin from "firebase-admin";
import { onCall, HttpsError } from "firebase-functions/v2/https";

const db = admin.firestore();

interface ReconcileData {
  sessionId: string;
  /** if omitted, reconciles the whole session */
  gameRunId?: string;
}

interface GameRunSummaryAccum {
  interactionCount: number;
  hitCount: number;
  missCount: number;
}

async function reconcileGameRun(
  sessionId: string,
  gameRunId: string,
): Promise<GameRunSummaryAccum> {
  const interactionsSnap = await db
    .collection("therapy_sessions")
    .doc(sessionId)
    .collection("interactions")
    .where("gameRunId", "==", gameRunId)
    .get();

  const acc: GameRunSummaryAccum = { interactionCount: 0, hitCount: 0, missCount: 0 };
  for (const doc of interactionsSnap.docs) {
    const data = doc.data();
    acc.interactionCount++;
    if (data.actionOutcome === "CORRECT") acc.hitCount++;
    else if (data.actionOutcome === "INCORRECT") acc.missCount++;
  }

  await db
    .collection("therapy_sessions")
    .doc(sessionId)
    .collection("game_runs")
    .doc(gameRunId)
    .set(
      {
        summary: {
          interactionCount: acc.interactionCount,
          hitCount: acc.hitCount,
          missCount: acc.missCount,
        },
      },
      { merge: true },
    );

  return acc;
}

async function recomputeSessionSummary(sessionId: string): Promise<void> {
  const gameRunsSnap = await db
    .collection("therapy_sessions")
    .doc(sessionId)
    .collection("game_runs")
    .get();

  let totalInteractions = 0;
  let totalHits = 0;
  let totalMisses = 0;
  const gamesPlayedCount = gameRunsSnap.size;

  for (const grDoc of gameRunsSnap.docs) {
    const acc = await reconcileGameRun(sessionId, grDoc.id);
    totalInteractions += acc.interactionCount;
    totalHits += acc.hitCount;
    totalMisses += acc.missCount;
  }

  await db
    .collection("therapy_sessions")
    .doc(sessionId)
    .set(
      {
        summary: { totalInteractions, totalHits, totalMisses, gamesPlayedCount },
      },
      { merge: true },
    );
}

export const reconcileSession = onCall<ReconcileData>(
  { enforceAppCheck: false },
  async (request) => {
    // only admin_operator custom claim may trigger reconcile
    const token = request.auth?.token;
    const isAdmin =
      token?.admin_operator === true || token?.role === "admin_operator";
    if (!isAdmin) {
      throw new HttpsError("permission-denied", "Admin only");
    }

    const { sessionId, gameRunId } = request.data;
    if (!sessionId) throw new HttpsError("invalid-argument", "sessionId required");

    if (gameRunId) {
      await reconcileGameRun(sessionId, gameRunId);
    } else {
      await recomputeSessionSummary(sessionId);
    }

    return { ok: true };
  },
);
