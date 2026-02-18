# Unity Firebase Reconnect Expected Outcomes

Date baseline: 2026-02-18
Scope: Unity Editor runtime path (`_simulateFirebase=false`) for online/offline/reconnect validation.

## Online phase (baseline)

- Runtime can start selected `gameId`.
- `SESSION_INGEST` accepts data points from current session.
- Outbox pending count remains low/stable after sync cycle.

Expected markers:
- `[FirebaseNetworkValidation] Online phase: ...`
- accepted ingest count > 0.

## Offline phase (interruption)

- New gameplay events are still queued locally.
- Outbox pending count increases while backend is unavailable.
- Runtime should not crash or lose active session identity.

Expected markers:
- `[FirebaseNetworkValidation] Offline phase: ...`
- pending outbox after offline phase > pending outbox before offline phase.

## Reconnect phase (recovery)

- Queued outbox events are drained after backend returns.
- Backend accepted event count increases vs online-only baseline.
- Runtime keeps deterministic session/game identity (no hidden duplicate session).

Expected markers:
- `[FirebaseNetworkValidation] Reconnect phase: ...`
- `[FirebaseNetworkValidation] PASS: gameId=...`

## Failure conditions

- No PASS marker in validation log.
- Reconnect does not increase accepted backend events.
- Outbox remains stuck pending after reconnect timeout.
- Runtime loses or duplicates active session unexpectedly during recovery.

## Operator evidence checklist

- Validation log file path with timestamp.
- `gameId` used in run.
- Session id emitted by validation output.
- Short PASS/FAIL note with UTC timestamp.
