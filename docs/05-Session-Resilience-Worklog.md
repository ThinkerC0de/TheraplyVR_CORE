# Session Resilience Worklog

## Status Legend
- `TODO`
- `IN_PROGRESS`
- `DONE`
- `BLOCKED`

## Current Program
- Program: Session Resilience and Data Correctness
- Start Date: 2026-02-17
- Main Project: `C:\Users\licen\Projects\theraply-vr-framework`
- Reference (read-only):
  - `<external_reference_project_1>`
  - `<external_reference_project_2>`

## Completed
1. `DONE` - `R-DOC-001` - Created resilience roadmap doc.
2. `DONE` - `R-DOC-002` - Created resume prompting guide doc.
3. `DONE` - `R-DOC-003` - Created this worklog.
4. `DONE` - `R-P0-001` - Added shared session FSM contract and transition enforcement across Unity runtime and Flutter signaling.
5. `DONE` - `R-P0-002` - Added critical command envelope (`messageId`, `sessionId`, `commandId`, `issuedAtUtc`/`expiresAtUtc`) for START/PAUSE/RESUME/STOP/END_SESSION.
6. `DONE` - `R-P0-003` - Implemented ACK/NACK for critical commands with timeout-based retry and reason codes.
7. `DONE` - `R-P0-004` - Added active session lock to reject mismatched `sessionId` on active sessions and prevent duplicate concurrent sessions.
8. `DONE` - `R-P0-005` - Persist key session events locally before queue/network pipeline.
9. `DONE` - `R-P0-006` - Added therapist-visible runtime status stream with runtime + sync pending signaling.
10. `DONE` - `R-P0-007` - Added reconnect path test for mobile disconnect while Quest session continues.
11. `DONE` - `R-P0-008` - Added END_SESSION reliability tests with explicit ACK/NACK verification and retry outcomes.
12. `DONE` - `R-P1-001` - Introduced asynchronous SQLite WAL session/event store with fallback and FPS-first guardrails.
13. `DONE` - `R-P1-002` - Added periodic snapshotting and restore service with phase-boundary checkpoints.
14. `DONE` - `R-P1-003` - Added automatic app-restart recovery to `INTERRUPTED` session from latest snapshot.
15. `DONE` - `R-P1-004` - Added therapist decision gate (`Resume` vs `Start New`) for detected active Quest session.
16. `DONE` - `R-P2-001` - Added durable outbox sync engine with exponential backoff + jitter and runtime sync-pending integration.
17. `DONE` - `R-P2-002` - Added idempotent ingest contract with `eventId` dedup semantics and partial batch ACK handling.
18. `DONE` - `R-P2-003` - Added reconciliation report by `sessionId + sequence` with local/server diff and missing sequence detection.
19. `DONE` - `R-P2-004` - Added manual support re-sync trigger/report pipeline and production Firebase backend calls for ingest + reconciliation.
20. `DONE` - `R-P3-001` - Added null-guard and fallback wrappers to prevent telemetry/data loss when critical runtime dependencies are temporarily unavailable.
21. `DONE` - `R-P3-002` - Added session watchdog heartbeat stream with hung-state detection and guarded auto-interrupt flow for inconsistent active runtime/session states.
22. `DONE` - `R-P3-003` - Added structured crash context capture linked to active `sessionId`, with local NDJSON crash report persistence and critical `error` telemetry payload enrichment.
23. `DONE` - `R-P4-001` - Added automated chaos fault matrix coverage for network toggle, app kill, device reboot, delayed ACK, and duplicate-command fault noise in Flutter signaling tests.
24. `DONE` - `R-P4-002` - Added save/resume regression suite covering decision-gate policy matrix and protocol-level resume/start-new session continuity assertions.
25. `DONE` - `R-P4-003` - Added rollout checklist and incident response SOP with staged rollout gates, no-go thresholds, and fault-specific runbooks.
26. `DONE` - `R-P5-001` - Added automated Unity CLI compile/build validation flow with documentation and executed full validation set (Unity + Flutter).
27. `DONE` - `R-DOC-004` - Standardized runtime/docs naming from `MiniGame` to `Game` across contracts, services, scene wiring, and API references.
28. `DONE` - `R-P5-002` - Added practical smoke execution flow with explicit game selection (`smoke_test_game`) across mobile + Unity runtime command payloads.
29. `DONE` - `R-P5-003` - Activated real Firebase endpoint path by default for resilience test scene and added backend diagnostics logging for ingest/reconciliation success/failure reason codes.
30. `DONE` - `R-P5-004` - Added deterministic Unity automation for online/offline/reconnect resilience validation and executed full M4 command set with passing Unity/Flutter checks.
31. `DONE` - `R-P5-005` - Removed hardcoded validation `gameId` from Firebase automation and added dynamic runtime/example module resolution with CLI override support (`-validationGameId`).

## P0 Backlog - Protocol and Session Safety Baseline

| Item ID | Task | Status | Notes |
|---|---|---|---|
| R-P0-001 | Define session FSM contract in code (`CREATED`, `IN_PROGRESS`, `PAUSED`, `INTERRUPTED`, `COMPLETED`, `ABORTED_BY_THERAPIST`, `FAILED_TECHNICAL`) | DONE | Unity: enum + transition contract + runtime transition hooks + `SESSION_STATE_UPDATE`; Flutter: mirrored FSM contract + signaling parser/UI binding |
| R-P0-002 | Add critical command envelope with `messageId`, `sessionId`, `commandId`, timestamps | DONE | Flutter sends envelope via `sendCriticalCommand`; Unity command bus validates/unpacks envelope with legacy fallback; runtime handles added `END_SESSION` command |
| R-P0-003 | Implement ACK/NACK handling and retry for critical commands | DONE | Unity sends `COMMAND_ACK` with `ACK/NACK` + `reasonCode`; Flutter retries critical commands on NACK/timeout and surfaces failure |
| R-P0-004 | Add active session lock to prevent duplicate concurrent sessions | DONE | Unity command bus validates critical envelope `sessionId` against active session and returns NACK `SESSION_LOCK_CONFLICT`; bootstrap allowlist keeps first `START_GAME` attach path from `CREATED` |
| R-P0-005 | Persist key session events locally before network send | DONE | `FirebaseDataService` now durably appends critical events to `Application.persistentDataPath/session_resilience/events.ndjson` before queue/send; runtime emits `session_start`, `game_start`, `game_end`, `session_stop`, `error` |
| R-P0-006 | Add therapist-visible runtime status stream (`connected`, `playing`, `paused`, `interrupted`, `sync_pending`) | DONE | Unity now emits `RUNTIME_STATUS_UPDATE` on session/connection/sync queue changes; Flutter parses it and shows runtime status in control UI |
| R-P0-007 | Add reconnect path test: mobile disconnect while Quest continues | DONE | Added automated Flutter socket test covering disconnect/reconnect and verifying next critical command keeps Quest `sessionId` from `RUNTIME_STATUS_UPDATE` (no local fork) |
| R-P0-008 | Add `END_SESSION` reliability test with ACK verification | DONE | Added automated Flutter socket tests: success only after `ACK`, retry on `NACK`, and terminal failure after max retries with reason propagation |

## P1 Backlog - Durable Store and Resume

| Item ID | Task | Status | Notes |
|---|---|---|---|
| R-P1-001 | Introduce SQLite WAL session/event store | DONE | Added `SessionEventStore` with background writer queue, SQLite WAL backend (`session_events.db`), NDJSON fallback/mirror, sequence + checksum envelope fields, and durable queue stats |
| R-P1-002 | Implement periodic snapshotting and restore | DONE | Added `GameSessionSnapshotService` (`snapshot.json`) with periodic + state-boundary snapshots and startup restore of non-terminal sessions into `GameSessionContext`; writes are queued to background worker |
| R-P1-003 | Auto-recovery flow on app restart to `INTERRUPTED` | DONE | On boot snapshot restore now maps non-terminal snapshot states to `INTERRUPTED` with `AUTO_RECOVERY_APP_RESTART`; context auto-start defers when recoverable snapshot exists |
| R-P1-004 | Therapist decision gate: `Resume` vs `Start New` | DONE | `ControlScreen` now blocks critical commands until operator chooses; `Resume` attaches to Quest session, `Start New` explicitly sends `END_SESSION` before creating new mobile session id |

## P2 Backlog - Sync and Reconciliation

| Item ID | Task | Status | Notes |
|---|---|---|---|
| R-P2-001 | Add outbox sync engine with exponential backoff | DONE | SQLite `sync_outbox` table wired to durable event writes; background claim/upload/ack/retry loop with bounded batch, exponential backoff + jitter, retry scheduling, and outbox stats surfaced to runtime status |
| R-P2-002 | Add idempotent ingest contract (`eventId` dedup) | DONE | Added ingest request/response contract for durable outbox events; per-event statuses (`ACCEPTED`/`DUPLICATE`/`RETRY`) map to mark-synced or retry scheduling, and duplicates are acknowledged without duplicate persistence |
| R-P2-003 | Implement reconciliation report by `sessionId + sequence` | DONE | Added local sequence index reader (`session_events` + outbox status), reconciliation request/response contract, server/local sequence diff, and report object exposing `local events`, `server events`, `missing on server`, `missing on device` |
| R-P2-004 | Add manual re-sync operation for support | DONE | Added `MANUAL_RESYNC` trigger from Flutter Control UI, runtime handler + `MANUAL_RESYNC_REPORT` response, outbox force-pending by selected event ids, immediate sync cycle trigger, and support-facing reconciliation delta output (`before/after missing`, outbox pending, sequence preview) |

## P3 Backlog - Guardrails and Operability

| Item ID | Task | Status | Notes |
|---|---|---|---|
| R-P3-001 | Add critical null-guard wrappers and safe fallback | DONE | `FirebaseDataService` now guards null points and can fall back to volatile queue when durable write fails; `GameTelemetryService` now buffers telemetry when `FirebaseDataService` is unavailable and flushes buffered events when dependency recovers |
| R-P3-002 | Add session watchdog heartbeat | DONE | Unity runtime now emits `SESSION_WATCHDOG_HEARTBEAT` with health metadata (`healthy`, `healthCode`, heartbeat/stale timings), detects `IN_PROGRESS`/`PAUSED` state drift against active game state, logs/telemetry on hung detection, and can transition to `INTERRUPTED` via `WATCHDOG_HUNG_STATE`; Flutter parses/visualizes watchdog freshness in Control UI |
| R-P3-003 | Add structured crash context tied to `sessionId` | DONE | `GameRuntimeService` now captures Unity exception/unhandled crash signals, enriches with session/runtime/device context, persists NDJSON reports (`session_resilience/crash_reports.ndjson`), and emits structured critical `error` telemetry (`eventName=crash_context`) with `sessionId` linkage |

## P4 Backlog - Hardening

| Item ID | Task | Status | Notes |
|---|---|---|---|
| R-P4-001 | Build chaos test matrix (network/app/device faults) | DONE | Added `flutter_controller/test/chaos_fault_matrix_test.dart` covering network toggle, app kill, device reboot, delayed ACK, and duplicate command fault isolation |
| R-P4-002 | Build save/resume regression suite | DONE | Added `flutter_controller/test/save_resume_regression_test.dart` with save/resume decision-policy matrix and resume/start-new protocol regression scenarios |
| R-P4-003 | Define rollout checklist and incident SOP | DONE | Added `docs/06-Session-Resilience-Rollout-SOP.md` with pre-rollout checklist, staged rollout gates, go/no-go thresholds, incident severity model, and runbooks |

## P5 Backlog - Operational Automation Hardening

| Item ID | Task | Status | Notes |
|---|---|---|---|
| R-P5-001 | Add automated Unity CLI compile/build validation and operational docs | DONE | Added `scripts/unity_cli_validate.ps1` and `UnityCliValidation` execute-methods; documented command usage/prerequisites/outputs/failures in `README.md` and `docs/01-System-Components-Guide.md`; validated Unity CLI + Flutter command set |
| R-P5-002 | Add minimal smoke game execution path (mobile selector + runtime explicit `gameId`) | DONE | Added `smoke_test_game` module, selector in `ControlScreen`, and explicit `gameId` payload propagation for START/PAUSE/RESUME/STOP; runtime now resolves/starts by explicit `gameId` with auto-discovery fallback |
| R-P5-003 | Enable real Firebase endpoint validation path with diagnostics | DONE | `FirebaseDataService` defaults to real mode (`_simulateFirebase=false`) and logs backend ingest/reconciliation diagnostics + reason codes; test scene endpoints wired to local validation backend |
| R-P5-004 | Add Unity Firebase online/offline/reconnect automation and run full validation set | DONE | Added `FirebaseNetworkValidation` execute-method automation and validated M3 + M4 commands in this workspace |
| R-P5-005 | Remove core hardcoded validation game IDs and keep game-specific wiring in examples | DONE | `FirebaseNetworkValidation` now resolves `gameId` dynamically (CLI/runtime/registry) and can bootstrap example modules for validation without core `smoke_test_game` literals |

## P6 Backlog - Catalog, Entitlements, and Guided Session Modes

| Item ID | Task | Status | Notes |
|---|---|---|---|
| R-P6-001 | Define shared game catalog contract for mobile and Quest (`gameId`, `sceneKey`, `contentVersion`, `parameterSchema`, `entitlementKey`) | DONE | Shared contract is now wired in Flutter + admin seed pipeline + Unity contract definitions with backward-compatible defaults (`sceneKey->gameId`, `contentVersion->targetContentVersion`, `entitlementKey->game:{gameId}`, `deliveryMode->bundled`) and contract-gate validation |
| R-P6-002 | Add mobile game-catalog UI with runtime availability and install state | DONE | Catalog/store UI is active in `ControlScreen` with `GAME_INSTALL_STATUS` chips/actions and launch-gate that now requires Quest-reported status (`READY` + no `updateRequired`) before `START_GAME` |
| R-P6-003 | Implement role/license entitlement model (`THERAPIST_FULL`, `PARENT_PURCHASED_PACKS`) | TODO | One source of truth for visible + launchable games across Flutter and Unity |
| R-P6-004 | Implement Quest game content lifecycle (manifest sync, download, verify, install, activate, rollback) | IN_PROGRESS | Dev simulator lifecycle state now persists to `Application.persistentDataPath/session_resilience/content_delivery_state.json` and restores on startup for deterministic reconnect/restart behavior; real manifest/download/verify/rollback path remains TODO |
| R-P6-005 | Extend protocol with content-management commands/events | DONE | `SYNC_CATALOG`, `INSTALL_GAME`, `UNINSTALL_GAME`, and `GAME_INSTALL_STATUS` are wired end-to-end; content management commands now use critical envelope ACK/NACK path with retry semantics (`COMMAND_ACK`) |
| R-P6-006 | Build therapist session-template/program builder with per-game parameter presets | TODO | Enables reusable plans and home mode handoff |
| R-P6-007 | Build parent one-button guided mode from therapist-defined plan | TODO | Parent flow must minimize decisions and auto-drive next safe step |
| R-P6-008 | Define adaptive continuation policy for interrupted/incomplete child sessions | TODO | System should safely continue or reschedule unfinished plan segments without data loss |
| R-P6-009 | Enforce per-child session closure lock in UX + protocol | TODO | Block start of new session for same child until previous is explicitly ended/aborted |
| R-P6-010 | Add validation matrix for therapist vs parent flows and entitlement edge cases | TODO | Include offline, stale entitlement cache, partial download, uninstall, and role switch scenarios |
| R-P6-011 | Add game-driven Flutter control-layout schema (editor-configurable) | DONE | Delivered declarative `THERAPLY_MOBILE_CONTROL_SCHEMA` path end-to-end: Unity authoring/validator/export, contract sync into catalog assets, Flutter dynamic renderer for `START_GAME` + `UPDATE_CONFIG`, runtime fallback only when schema missing, and admin-console freshness/status checks for export manifest |

## Notes
- Validation run (2026-02-17, Unity follow-up/modularity): Firebase network automation passed after removing hardcoded validation `gameId` and adding dynamic module/game resolution.
- Firebase validation command used (2026-02-17):
  - `"C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\Users\licen\Projects\theraply-vr-framework\unity-quest-template" -buildTarget Android -executeMethod "TheraplyCore.Editor.Automation.FirebaseNetworkValidation.RunFirebaseNetworkValidation" -logFile "C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\firebase_network_validation_20260217_2138.log"`
- Firebase validation PASS evidence (2026-02-17):
  - `[FirebaseNetworkValidation] PASS: gameId=demo_cube_clicker; session=...; onlineAccepted=32; onlineSynced=32; offlineFailureDelta=1; offlineRetryDelta=32; pendingAfterReconnect=0; reconnectAccepted=390; reconnectSynced=390`
- Validation run (2026-02-17, full command set re-check):
  - `flutter analyze` -> PASS (`No issues found!`).
  - `flutter test` -> PASS (`All tests passed!`).
  - `flutter build apk --debug` -> PASS (`Built build\app\outputs\flutter-apk\app-debug.apk`).
  - `powershell -ExecutionPolicy Bypass -File .\scripts\unity_cli_validate.ps1 -Mode both` -> PASS (Unity compile + android-build).
- Execution order checkpoint (2026-02-17): M1 smoke flow DONE -> M3 real-network validation DONE -> P6 backlog remains TODO.
- Smoke flow implementation (2026-02-17):
  - Mobile selector + `gameId` payload path: `flutter_controller/lib/screens/control_screen.dart`.
  - Smoke module: `unity-quest-template/Assets/_Examples/Scripts/SmokeTestGameModule.cs` (`smoke_test_game`).
  - Runtime explicit startup and result reporting hardening:
    - `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/GameRuntimeService.cs`
    - `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/GameRegistryService.cs`
- Firebase real-network activation (2026-02-17):
  - Default simulate toggle set to real path in `unity-quest-template/Assets/_TheraplyCore/Firebase/FirebaseDataService.cs` (`_simulateFirebase = false`).
  - Scene endpoints wired in `unity-quest-template/Assets/_Examples/Scenes/SessionResilienceTest.unity`.
  - Added backend diagnostics fields/logging for ingest/reconciliation success/failure + reason codes.
- M3 validation run command (2026-02-17):
  - `"C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\Users\licen\Projects\theraply-vr-framework\unity-quest-template" -executeMethod "TheraplyCore.Editor.Automation.FirebaseNetworkValidation.RunFirebaseNetworkValidation" -logFile "C:\Users\licen\Projects\theraply-vr-framework\unity-quest-template\Temp\CliValidation\logs\unity_firebase_network_validation.log"`
  - PASS evidence in log:
    - `[FirebaseNetworkValidation] Online phase: accepted=32, outboxSynced=32, pending=280, inFlight=0.`
    - `[FirebaseNetworkValidation] Offline phase: failureDelta=1, retryDelta=32, pending=284.`
    - `[FirebaseNetworkValidation] Reconnect phase: accepted=316, pending=0, inFlight=0, synced=316.`
    - `[FirebaseNetworkValidation] PASS: session=...`
- M4 validation runs (2026-02-17):
  - `flutter analyze` -> PASS (`No issues found!`).
  - `flutter test` -> PASS (`All tests passed!`).
  - `flutter build apk --debug` -> PASS (`Built build\app\outputs\flutter-apk\app-debug.apk`).
  - Unity compile/build executed via:
    - `powershell -ExecutionPolicy Bypass -File .\scripts\unity_cli_validate.ps1 -Mode both`
  - Unity result: compile PASS + android-build PASS (`TheraplyCliValidation.apk` generated in `unity-quest-template\Temp\CliValidation\build`).
- Common failures observed + fixes (2026-02-17):
  - Batch scene recovery prompt from stale `.utmp` caused apparent hang in long Unity batch runs.
  - Main-thread exceptions in network path (`UnityWebRequest.Create`) were avoided by keeping validation flow on Unity main thread.
  - SQLite init fallback to NDJSON blocked outbox/reconciliation capabilities; NDJSON backend now supports outbox + sequence-index operations needed by resilience validation.
- Firebase toggle reminder:
  - Mock mode: `_simulateFirebase = true`.
  - Real endpoint mode: `_simulateFirebase = false` + configured `_sessionIngestEndpointUrl`/`_sessionReconciliationEndpointUrl`.
- Wand-specific behavior from Focus and Calm is optional for telemetry and not required in P0-P2.
- No game migration starts until P0 is complete.
- Naming update (2026-02-17, R-DOC-004): replaced `MiniGame` runtime nomenclature with `Game` in core contracts/services (`GameContracts`, `GameCommandBus`, `GameRuntimeService`, `GameSessionContext`, etc.), Unity scene component references, and docs (`Game-Contracts.md` + guide updates) to align terminology with therapist-facing product language.
- Validation run (2026-02-17, R-DOC-004): Unity CLI compile executed via `powershell -ExecutionPolicy Bypass -File .\scripts\unity_cli_validate.ps1 -Mode compile` and passed.
- Validation run (2026-02-17, R-DOC-004): `flutter analyze`, `flutter test`, and `flutter build apk --debug` pass.
- Automation hardening update (2026-02-17, P5-001): added `scripts/unity_cli_validate.ps1` (`compile`/`build`/`both`) plus Unity execute-methods in `unity-quest-template/Assets/_TheraplyCore/Editor/Automation/UnityCliValidation.cs` for deterministic CLI compile/build checks.
- Validation run (2026-02-17, P5-001): `flutter analyze`, `flutter test`, and `flutter build apk --debug` pass.
- Validation run (2026-02-17, P5-001): Unity compile/build WAS executed in CLI using `powershell -ExecutionPolicy Bypass -File .\scripts\unity_cli_validate.ps1 -Mode both` (run against detached worktree path `C:\Users\licen\Projects\theraply-vr-framework-cli-validation` because main project path had an open Unity Editor lock); compile and android-build steps passed.
- Validation run (2026-02-17): `flutter analyze`, `flutter test`, and `flutter build apk --debug` pass.
- Validation run (2026-02-17, P1-001): `flutter analyze`, `flutter test`, and `flutter build apk --debug` pass; Unity compile/build not executed in CLI.
- Analyzer warnings in `scanner_screen.dart` and `students_screen.dart` were fixed during this work block.
- Local durability baseline currently stores payload as JSON string and uses append-only NDJSON for critical event types.
- Quest FPS guardrail in persistence path: gameplay thread now only enqueues durable records; disk writes happen in background store worker with bounded queue and fallback mode reporting.
- Snapshot guardrail: checkpoints are enqueued and flushed by background writer task (`GameSessionSnapshotService`), not direct synchronous writes from gameplay callbacks.
- Recovery behavior: boot restore promotes any non-terminal snapshot (`CREATED/IN_PROGRESS/PAUSED/INTERRUPTED`) to runtime state `INTERRUPTED` to avoid implicit resume after crash/restart.
- Therapist reconnect gate: when mobile detects a different non-terminal Quest session id, UI enforces `Resume`/`Start New` decision before control commands proceed.
- Validation run (2026-02-17, P2-001): `flutter analyze`, `flutter test`, and `flutter build apk --debug` pass; Unity compile/build not executed in CLI.
- Outbox safety: durable events are persisted to `session_events` and `sync_outbox` in one SQLite transaction; upload retries use exponential backoff + jitter and run from background cycle (no gameplay-thread blocking).
- Validation run (2026-02-17, P2-002): `flutter analyze`, `flutter test`, and `flutter build apk --debug` pass; Unity compile/build not executed in CLI.
- Ingest idempotency contract: outbox upload now evaluates per-event ingest result; `DUPLICATE` by `eventId` is treated as success (acked locally), while unresolved/missing per-event results are retried with backoff.
- Validation run (2026-02-17, P2-003): `flutter analyze`, `flutter test`, and `flutter build apk --debug` pass; Unity compile/build not executed in CLI.
- Reconciliation contract: `BuildSessionReconciliationReportAsync(sessionId)` now compares local `session_events` sequence index with server index response by `sessionId + sequence`, reporting missing sequence lists in both directions.
- Validation run (2026-02-17, P2-004): `flutter analyze`, `flutter test`, and `flutter build apk --debug` pass; Unity compile/build not executed in CLI.
- Firebase backend path is now production-ready for resilience sync: `SubmitSessionIngestBatchAsync` and `SubmitSessionReconciliationRequestAsync` call configured HTTPS endpoints (with timeout/auth headers/API key support), parse direct/wrapped JSON responses, and preserve simulate mode via `_simulateFirebase`.
- Manual support recovery path: `MANUAL_RESYNC` can target missing-on-server/unsynced session events (or all local events if requested), re-queues selected `eventId`s in `sync_outbox`, forces immediate upload cycle, and publishes `MANUAL_RESYNC_REPORT` with before/after reconciliation counters.
- Validation run (2026-02-17, Unity Editor local/no Flutter, scene `Assets/_Examples/Scenes/SessionResilienceTest.unity`): startup recovery restored snapshot session to `INTERRUPTED`; durable store started with SQLite fallback to NDJSON when WAL init failed; after flush shutdown fix in `FirebaseDataService`, Play Mode stop completed cleanly (`App quitting -> Flushing all data -> All data flushed`) without editor hang.
- Scene wiring update (2026-02-17): critical runtime references in `SessionResilienceTest.unity` were explicitly connected (`TCPServerService`, `FirebaseDataService`, `GameRuntimeService`, `GameContextService`, `GameSessionSnapshotService`, `GameTelemetryService`, `GameClockService`, `GameCommandBus`) to reduce reliance on runtime auto-discovery.
- Guardrail update (2026-02-17, P3-001): null/fallback wrappers added in `FirebaseDataService` and `GameTelemetryService`; critical events no longer hard-drop on durable write failure by default (configurable fallback), and telemetry is buffered/flushed when `FirebaseDataService` dependency is temporarily missing.
- Validation run (2026-02-17, P3-001): `flutter analyze`, `flutter test`, and `flutter build apk --debug` pass; Unity compile/build not executed in CLI (manual Editor validation used in this block).
- Watchdog update (2026-02-17, P3-002): `GameRuntimeService` now runs periodic watchdog health checks and publishes `SESSION_WATCHDOG_HEARTBEAT`; `GameCommandBus` and contracts include heartbeat command mapping/payload; Flutter `ControlScreen` now shows watchdog health/freshness from heartbeat stream.
- Validation run (2026-02-17, P3-002): `flutter analyze`, `flutter test`, and `flutter build apk --debug` pass; Unity compile/build not executed in CLI.
- Crash-context update (2026-02-17, P3-003): `GameRuntimeService` now registers crash hooks (`Application.logMessageReceivedThreaded`, `AppDomain.CurrentDomain.UnhandledException`), queues/processes structured crash reports on main thread, persists NDJSON crash context reports with session/runtime metadata, and forwards structured `error` telemetry with `sessionId` + report identifiers.
- Validation run (2026-02-17, P3-003): `flutter analyze`, `flutter test`, and `flutter build apk --debug` pass; Unity compile/build not executed in CLI.
- Chaos matrix update (2026-02-17, P4-001): added `flutter_controller/test/chaos_fault_matrix_test.dart` with automated fault scenarios for network toggle (in-flight retry + reconnect), app kill (pending command interruption + restart recovery), device reboot (runtime status reattach + control continuity), delayed ACK (stale ACK ignored, retry ACK accepted), and duplicate command fault noise (repeat command remains ACK-isolated under duplicate ACK injection).
- Validation run (2026-02-17, P4-001): `flutter analyze`, `flutter test`, and `flutter build apk --debug` pass; Unity compile/build not executed in CLI.

### R-P4-001 Chaos Test Matrix (Fault -> Expected Behavior -> Assert)

| Fault | Expected behavior | Primary assert in test |
|---|---|---|
| Network toggle | In-flight critical command survives link drop via reconnect + retry | Retry request is observed with new `messageId`, then `ACK` completes send |
| App kill | Pending command does not false-complete; after restart, new command flow is healthy | In-flight send fails after disconnect; fresh `ConnectionService` connects and succeeds with `ACK` |
| Device reboot | Reattached runtime keeps Quest `sessionId`, control resumes without session fork | Post-reconnect runtime status updates `activeSessionId`; `RESUME_GAME` payload uses Quest `sessionId` |
| Delayed ACK | Stale late `ACK` for attempt N does not complete attempt N+1 | Late `ACK` on old `messageId` is ignored; completion occurs only after retry attempt `ACK` |
| Duplicate commands / ACK noise | Duplicate/stale ACK traffic does not corrupt later repeated critical command handling | Duplicate `ACK` for old `messageId` has no effect; next repeated command completes only on its own `ACK` |

### R-P4-002 Save/Resume Regression Suite

| Coverage area | Regression guarantee | Assert style |
|---|---|---|
| Decision gate policy matrix | Different non-terminal remote sessions always force `Resume/Start New`, terminal sessions never do | Unit assertions over `SessionRecoveryPolicy.shouldRequireDecision(...)` across state/runtime combinations |
| Runtime fallback behavior | When session state is unavailable, runtime statuses (`playing/paused/interrupted/sync_pending`) still enforce gate | Matrix assertions for runtime fallback truth table |
| Resume attach continuity | Resume command after remote recovery uses attached Quest `sessionId`, not stale local id | Socket-level framed message assertion for `RESUME_GAME` envelope payload |
| Start-new continuity | `END_SESSION` for remote session is completed before `START_GAME` with fresh local `sessionId` | Sequential protocol assertions over command order + per-command `sessionId` |

- Save/resume regression update (2026-02-17, P4-002): added `flutter_controller/test/save_resume_regression_test.dart` with policy matrix coverage and protocol-level resume/start-new flow checks using framed TCP mock server + ACK verification.
- Validation run (2026-02-17, P4-002): `flutter analyze`, `flutter test`, and `flutter build apk --debug` pass; Unity compile/build not executed in CLI.
- Rollout/SOP update (2026-02-17, P4-003): added `docs/06-Session-Resilience-Rollout-SOP.md` covering release roles, pre-rollout checklist, staged rollout progression (canary/beta/broad), go/no-go metrics (`no-loss sessions`, ACK timeout rate, outbox age, crash trend), incident severity levels, first-15-minute response, and fault-specific runbooks.
- Validation run (2026-02-17, P4-003): `flutter analyze`, `flutter test`, and `flutter build apk --debug` pass; Unity compile/build not executed in CLI.
- Documentation update (2026-02-17): added `docs/01-System-Components-Guide.md` with component-by-component system reference (Unity + Flutter), including purpose, usage, dependencies, and configuration checklists; linked from `README.md` and `docs/README.md`.
- P6 update (2026-02-25, authoring lane): `R-P6-011` is now `DONE` with shared mobile-control schema contract + validators (`docs/40-Mobile-Control-Schema-Contract.md`), Unity flow authoring/export tooling, Flutter dynamic control rendering, contract gate CI, and admin-console manifest freshness/status visibility.
- P6 update (2026-02-25, protocol lane): `R-P6-005` is now `DONE`; content commands (`SYNC_CATALOG`, `INSTALL_GAME`, `UNINSTALL_GAME`) are treated as critical transport commands on both mobile and Unity, and are executed with ACK/NACK retry semantics through `COMMAND_ACK`.
- P6 update (2026-02-25, content lifecycle lane): `R-P6-004` moved to `IN_PROGRESS`; `GameRuntimeService` now persists simulated content lifecycle state to local storage and rehydrates it on runtime boot so install/update/uninstall state survives app reconnect/restart.
- P6 update (2026-02-25, mobile catalog lane): `R-P6-002` is now `DONE`; Flutter catalog/setup screens use runtime availability chips plus Quest-authoritative launch gating (game can start only after headset status sync reports launch-ready state).
- P6 update (2026-02-25, catalog contract lane): `R-P6-001` is now `DONE`; shared game catalog metadata contract (`sceneKey`, `contentVersion`, `entitlementKey`, `deliveryMode`, `parameterSchema`) is parsed/written in Flutter and admin seeding, mirrored in Unity contract types, and validated by authoring gate with backward-compatible fallback rules.
