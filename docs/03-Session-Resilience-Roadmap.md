# Session Resilience Roadmap

## Purpose
Build a session and data pipeline that is resilient to disconnects, app crashes, device restarts, and unstable internet.

This roadmap is focused on correctness of patient/student data handling.

## Scope
- No data loss for in-progress sessions.
- Deterministic resume/restart behavior after interruptions.
- Correct per-patient data isolation.
- Idempotent control commands from therapist app.
- Reliable upload/sync to backend when connectivity returns.

## Non-Goals (for now)
- Game-specific wand mechanics from Focus and Calm are not a must-have.
- Visual polish changes are out of scope unless needed for safety/recovery UX.

## Hard Product Rules
1. Quest runtime is the source of truth during active play.
2. Every session change is persisted locally before network send.
3. Critical commands require ACK and retry (`START`, `PAUSE`, `RESUME`, `STOP`, `END_SESSION`).
4. Exactly one active session per patient per headset.
5. Reconnect must attach to existing session state, never create a hidden duplicate.
6. `COMPLETED` session requires full completion criteria; partial progress is still persisted as `INTERRUPTED`.
7. Persistence/sync logic must not block the render loop; FPS safety has priority over telemetry throughput.

## Runtime Performance Guardrails (Quest)
- No synchronous disk or DB writes on gameplay hot path.
- Durable writes must be enqueued and processed by background worker(s).
- Use bounded queues/backpressure to avoid unbounded CPU or memory growth.
- SQLite must run in WAL mode with non-blocking settings tuned for runtime safety.
- If durable backend fails, degrade to fallback persistence and surface mode/status for diagnostics.

## Failure Scenarios We Must Cover
1. Mobile app disconnects while Quest continues.
2. Internet/Wi-Fi loss mid-session.
3. Therapist sends `END_SESSION` but Quest does not execute it.
4. App crash on Quest.
5. Quest restart/battery drop.
6. Phone app restart and reconnect.
7. Duplicate command send from mobile (double tap/retry storm).
8. Patient switch while old session is still active.
9. Null reference in scene manager code.
10. Long session with multiple mini-games and partial completion.
11. Save/upload happens late and therapist cannot see immediate mobile preview.
12. Controller/hardware interruptions and temporary pause.

## Target Architecture

### A. Session State Machine
Allowed states:
- `CREATED`
- `IN_PROGRESS`
- `PAUSED`
- `INTERRUPTED`
- `COMPLETED`
- `ABORTED_BY_THERAPIST`
- `FAILED_TECHNICAL`

State transitions must be explicit and logged.

### B. Durable Local Storage (Quest)
- Append-only event log for session events.
- Periodic snapshot for fast resume.
- Preferred backend: SQLite with WAL.
- Fallback allowed for first increment: JSONL + atomic file rotation.

### C. Event Envelope (common contract)
Required fields:
- `eventId` (UUID)
- `sessionId`
- `patientId`
- `deviceId`
- `sequence` (monotonic per session)
- `eventType`
- `eventVersion`
- `createdAtUtc`
- `payload`
- `checksum`

### D. Command Envelope + ACK
Required fields:
- `commandId`
- `messageId`
- `sessionId`
- `issuedAtUtc`
- `expiresAtUtc` (optional)
- `payload`

ACK/NACK fields:
- `messageId`
- `status` (`ACK` or `NACK`)
- `reasonCode`
- `processedAtUtc`

### E. Sync Engine
- Outbox queue on Quest for upload.
- Retry with exponential backoff + jitter.
- Idempotent server upsert by `eventId`.
- Reconciliation by `sessionId + sequence`.

### F. Reconnect Semantics
- Reconnect attaches to current active session.
- If session is already active, UI must show `Resume` or `Start New`.
- `Start New` requires explicit therapist confirmation.

### G. Backup/Recovery Format
- Export bundle:
  - `manifest.json`
  - `snapshot.bin` or `snapshot.json`
  - `events.ndjson`
  - `checksums.sha256`
- Import process validates checksums and sequence continuity.

## Delivery Phases

## Phase P0 - Protocol and Session Safety Baseline
Target: remove most severe field failures immediately.

Deliverables:
1. Session FSM in core runtime.
2. Critical command ACK/retry.
3. Active session lock (no duplicate sessions on reconnect).
4. Immediate local write on key events (`session_start`, `game_start`, `game_end`, `session_stop`, `error`).
5. Minimal therapist-visible status (`connected`, `playing`, `paused`, `interrupted`, `sync_pending`).

Exit Criteria:
- Reconnect does not create overlapping sessions.
- `END_SESSION` either executes or is shown as failed with reason.
- Partial session remains visible and recoverable.

## Phase P1 - Durable Store and Resume
Target: robust resume after process/device failure.

Deliverables:
1. SQLite WAL session/event store.
2. Snapshot every N seconds and at phase boundaries.
3. Auto-recovery on app boot to `INTERRUPTED` session.
4. Resume flow from therapist app.

Exit Criteria:
- Crash/restart test recovers session and preserves progress.
- No data corruption across at least 100 forced interruption tests.

## Phase P2 - Sync and Reconciliation
Target: guaranteed eventual server consistency.

Deliverables:
1. Outbox uploader with retry/backoff.
2. Idempotent backend ingestion.
3. Reconciliation report (`local events`, `server events`, `missing events`).
4. Manual re-sync action for therapist/support.

Exit Criteria:
- Offline session uploads successfully after reconnection.
- Duplicate uploads do not duplicate server records.

## Phase P3 - Safety Guardrails and Operability
Target: reduce runtime incidents and support burden.

Deliverables:
1. Null-guard wrappers for scene critical services.
2. Session watchdog + health heartbeat.
3. Structured crash report linking to `sessionId`.
4. Operational dashboard fields (`sync lag`, `pending outbox`, `last ack`, `last snapshot`).

Exit Criteria:
- Critical null references do not drop session data.
- Support can diagnose session failure path from logs + IDs.

## Phase P4 - Validation and Hardening
Target: confidence before broad rollout.

Deliverables:
1. Chaos test suite:
  - network toggle
  - app kill
  - device reboot
  - delayed ACK
  - duplicate commands
2. Regression suite for save/resume.
3. Rollout playbook and incident response SOP.

Exit Criteria:
- Predefined reliability threshold reached (example: >= 99.9% no-loss sessions in stress tests).

## Phase P5 - Operational Automation Hardening
Target: make resilience validation repeatable and CLI-driven for every work block.

Deliverables:
1. Automated Unity CLI compile/build validation scripts and execute-methods.
2. Documentation with exact commands, prerequisites, expected outputs, and common failures.
3. Validation discipline: `flutter analyze`, `flutter test`, `flutter build apk --debug`, plus Unity CLI compile/build check in each resilience hardening cycle.

Exit Criteria:
- Unity compile/build validation can be executed from terminal with one documented command.
- Worklog entries explicitly state whether Unity CLI compile/build was run and include the concrete command used.

## Tracking and Execution Rules
1. Every completed task must update `docs/05-Session-Resilience-Worklog.md`.
2. Each implementation PR must map to roadmap item IDs.
3. No mini-game migration starts before P0 completion.
