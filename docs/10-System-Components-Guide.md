# System Components Guide (Unity + Flutter)

## Purpose
This document describes each main component in the current system:

- what it is,
- what it is used for,
- how to use it,
- how to configure it (Unity Inspector / Flutter app / Android bridge).

Scope is the current implementation in:

- `unity-quest-template/Assets/_TheraplyCore`
- `flutter_controller/lib`
- `flutter_controller/android/app/src/main`

## System Topology

### Runtime sides
- Quest (Unity): session source of truth, command execution, durability, sync, watchdog, crash context, media source.
- Therapist phone (Flutter): discovery, control commands, ACK/retry, operator UI, recovery decisions, media receiver.
- Backend (Firebase + HTTP endpoints): auth + ingest/reconciliation endpoints for resilient event sync.

### Main channels
- UDP discovery:
  - Quest broadcast: `8767` (`UDPDiscoveryService`)
  - Flutter listener: `8767` (`DiscoveryService`)
- TCP control/signaling:
  - Quest server: `8080` (`TCPServerService`)
  - Flutter client: `ConnectionService`
  - Framing: `[4-byte big-endian length][JSON]`
- WebRTC media:
  - signaling over TCP commands (`WEBRTC_OFFER`, `WEBRTC_ANSWER`, `WEBRTC_ICE_CANDIDATE`)
  - media over peer connection (video + optional bidirectional audio)

### Durable files (Quest)
Under `Application.persistentDataPath/session_resilience`:

- `snapshot.json` (session snapshot)
- `events.ndjson` (NDJSON fallback/mirror)
- `session_events.db` (SQLite WAL durable store + outbox)
- `crash_reports.ndjson` (structured crash context)

## Unity Components

Legend:
- `Required`: expected in production runtime scene.
- `Optional`: useful but not mandatory.
- `Legacy`: older path kept for compatibility; prefer contract runtime services.

## Contracts Layer

### `GameContracts.cs` (`unity-quest-template/Assets/_TheraplyCore/Games/Contracts/GameContracts.cs`) - Required
- Role: canonical interfaces and enums for mini-game runtime.
- Core types:
  - `IMiniGameModule`, `IMiniGameConfig`, `IMiniGameResult`
  - `IMiniGameContext`, `ISessionContext`, `ICommandBus`, `ITelemetryService`, `IGameClock`, `IGameFeedback`
  - `SessionLifecycleState`, `SessionFsmContract`, `MiniGameState`, `MiniGameStopReason`
- Use:
  - all new mini-games implement `IMiniGameModule` (usually via `MiniGameModuleBase`)
  - all session transitions should use `SessionFsmContract`
- Configuration:
  - no Inspector configuration; this is the contract layer.

### `GameCommands.cs` (`unity-quest-template/Assets/_TheraplyCore/Games/Contracts/GameCommands.cs`) - Required
- Role: wire command IDs and typed payload contracts.
- Contains:
  - command IDs (`START_GAME`, `PAUSE_GAME`, `RESUME_GAME`, `STOP_GAME`, `END_SESSION`, `COMMAND_ACK`, `RUNTIME_STATUS_UPDATE`, `SESSION_WATCHDOG_HEARTBEAT`, `MANUAL_RESYNC`, `MANUAL_RESYNC_REPORT`)
  - critical envelope + ACK payload contracts
  - runtime status values (`connected`, `playing`, `paused`, `interrupted`, `sync_pending`)
- Use:
  - Unity and Flutter must match these IDs exactly.
- Configuration:
  - no Inspector configuration.

### `GameRegistry.cs` (`unity-quest-template/Assets/_TheraplyCore/Games/Contracts/GameRegistry.cs`) - Required
- Role: `IGameRegistry` abstraction (`TryResolve(gameId, out module)`).
- Use: implemented by `MiniGameRegistryService`.
- Configuration: none.

## Runtime Orchestration Layer

### `MiniGameSessionContext.cs` (`unity-quest-template/Assets/_TheraplyCore/Games/Runtime/MiniGameSessionContext.cs`) - Required
- Role: session identity + lifecycle state machine holder.
- Main responsibilities:
  - session creation (`BeginSession`)
  - session restore (`RestoreSession`)
  - guarded transitions (`TryTransitionTo`)
  - active session lock (prevents hidden duplicate active sessions)
  - optional auto-start defer when non-terminal snapshot exists
- Inspector configuration:
  - `_patientId`, `_therapistId`, `_sessionId`
  - `_autoStartSessionOnAwake`
  - `_deferAutoStartWhenRecoverySnapshotExists`
  - `_recoverySnapshotFolder`, `_recoverySnapshotFileName`
  - `_sessionState` (default bootstrap state)
- Use:
  - place once in scene
  - other runtime services read/write session state through this component.

### `MiniGameSessionSnapshotService.cs` (`unity-quest-template/Assets/_TheraplyCore/Games/Runtime/MiniGameSessionSnapshotService.cs`) - Required
- Role: asynchronous snapshot writer + auto-restore.
- Main behavior:
  - periodic snapshots (`_snapshotIntervalSeconds`)
  - boundary snapshots on session/state changes
  - restore on start (`TryRestoreLatestSnapshot`)
  - optional restore-to-`INTERRUPTED` policy (`_recoverToInterruptedOnStart`)
  - atomic write via temp file swap (`snapshot.json.tmp` -> `snapshot.json`)
- Inspector configuration:
  - `_autoRestoreOnStart`
  - `_recoverToInterruptedOnStart`
  - `_recoveryReasonCode`
  - `_snapshotOnSessionChanged`
  - `_snapshotOnStateBoundaries`
  - `_snapshotIntervalSeconds`
  - `_snapshotFolder`, `_snapshotFileName`
  - `_maxPendingSnapshots`, `_logSnapshots`
- Use:
  - assign `_sessionContext` and `_runtimeService`
  - keep enabled in all production scenes.

### `MiniGameRegistryService.cs` (`unity-quest-template/Assets/_TheraplyCore/Games/Runtime/MiniGameRegistryService.cs`) - Required
- Role: runtime map `gameId -> IMiniGameModule`.
- Main behavior:
  - builds registry from serialized `_entries`
  - validates that `moduleBehaviour` implements `IMiniGameModule`
- Inspector configuration:
  - `_entries` list (`gameId`, `moduleBehaviour`)
  - `_logMappings`
- Use:
  - add each game module here
  - keep `gameId` unique.

### `MiniGameContextService.cs` (`unity-quest-template/Assets/_TheraplyCore/Games/Runtime/MiniGameContextService.cs`) - Required
- Role: composition root implementing `IMiniGameContext`.
- Exposes:
  - `Session`, `CommandBus`, `Telemetry`, `Clock`, `Feedback`
- Inspector configuration:
  - `_sessionContext`
  - `_commandBus`
  - `_telemetryService`
  - `_clockService`
  - `_feedbackService`
  - `_snapshotService`
- Use:
  - central context dependency passed into modules in `Initialize`.

### `MiniGameCommandBus.cs` (`unity-quest-template/Assets/_TheraplyCore/Games/Runtime/MiniGameCommandBus.cs`) - Required
- Role: typed command bus over TCP messages.
- Main behavior:
  - subscribes handlers by typed command
  - serializes outgoing typed commands to wire messages
  - deserializes incoming payloads
  - validates critical envelope and active session lock
  - emits `COMMAND_ACK` (`ACK`/`NACK`) with reason codes
- Critical validation includes:
  - `commandId`/`messageId` consistency
  - issued/expiry timestamps
  - session lock conflict detection
- Inspector configuration:
  - `_tcpServerService` (host path)
  - `_tcpConnectionService` (client-mode path)
  - `_sessionContext`
  - `_logInbound`, `_logOutbound`, `_logUnmappedIncoming`
- Use:
  - this is the command transport for `MiniGameRuntimeService`.

### `MiniGameRuntimeService.cs` (`unity-quest-template/Assets/_TheraplyCore/Games/Runtime/MiniGameRuntimeService.cs`) - Required
- Role: top-level runtime orchestrator.
- Main behavior:
  - command handlers for `START_GAME`, `PAUSE_GAME`, `RESUME_GAME`, `STOP_GAME`, `END_SESSION`, `MANUAL_RESYNC`
  - game lifecycle transitions + critical telemetry (`session_start`, `game_start`, `game_end`, `session_stop`, `error`)
  - publishes `SESSION_STATE_UPDATE`
  - publishes `RUNTIME_STATUS_UPDATE`
  - session watchdog:
    - heartbeat command: `SESSION_WATCHDOG_HEARTBEAT`
    - hung-state detection and optional auto interrupt
  - structured crash context:
    - hooks Unity/AppDomain exceptions
    - persists NDJSON reports
    - enriches telemetry with session and runtime context
- Inspector configuration:
  - dependencies: `_registryService`, `_contextService`, `_commandBus`, `_tcpServerService`, `_firebaseDataService`
  - runtime: `_defaultGameId`, `_subscribeToStandardCommands`, `_syncStatusPollIntervalSeconds`
  - watchdog: `_enableSessionWatchdog`, `_watchdogHeartbeatIntervalSeconds`, `_watchdogHungThresholdSeconds`, `_watchdogAutoInterruptInProgress`, `_watchdogEmitTelemetry`, `_watchdogLogHeartbeat`
  - crash context: `_enableStructuredCrashContext`, `_captureUnityExceptionLogs`, `_captureAppDomainUnhandledExceptions`, `_captureUnityErrorLogs`, `_persistCrashReportsLocally`, `_emitCrashReportsToTelemetry`, `_crashReportFolder`, `_crashReportFileName`, `_maxCrashReportsPerFrame`, `_maxPendingCrashSignals`, `_logCrashCapture`
- Use:
  - keep exactly one active instance per runtime scene.

### `MiniGameTelemetryService.cs` (`unity-quest-template/Assets/_TheraplyCore/Games/Runtime/MiniGameTelemetryService.cs`) - Required
- Role: telemetry adapter (`ITelemetryService`) to `FirebaseDataService`.
- Main behavior:
  - enriches payload with session metadata
  - queues `GameDataPoint` into durability/sync pipeline
  - fallback in-memory buffer when Firebase service unavailable
- Inspector configuration:
  - `_firebaseDataService`
  - `_sessionContext`
  - `_enrichPayloadWithSessionMetadata`
  - `_logTelemetry`
  - `_maxPendingTelemetryFallback`
  - `_logTelemetryFallback`
- Use:
  - runtime and modules call `Context.Telemetry.Track(...)`.

### `MiniGameClockService.cs` (`unity-quest-template/Assets/_TheraplyCore/Games/Runtime/MiniGameClockService.cs`) - Required
- Role: `IGameClock` implementation.
- Behavior:
  - by default returns elapsed time from session start UTC
  - fallback to realtime clock if session sync disabled
- Inspector configuration:
  - `_sessionContext`
  - `_syncToSessionStart`
- Use:
  - read through `Context.Clock.ElapsedSeconds`.

### `MiniGameFeedbackService.cs` (`unity-quest-template/Assets/_TheraplyCore/Games/Runtime/MiniGameFeedbackService.cs`) - Optional
- Role: `IGameFeedback` adapter (audio + hint logging + haptic stub).
- Inspector configuration:
  - `_audioSource`
  - `_audioEntries` (`id`, `clip`)
  - `_logMissingAudioIds`
  - `_logHints`
- Use:
  - module calls `Context.Feedback.PlaySfx("id")`, `ShowHint("key")`, `HapticPulse(...)`.

### `MiniGameModuleBase.cs` (`unity-quest-template/Assets/_TheraplyCore/Games/Runtime/MiniGameModuleBase.cs`) - Required for new games
- Role: default `IMiniGameModule` base with lifecycle timing and telemetry hooks.
- Use:
  - inherit and implement `GameId`
  - override lifecycle methods only if needed
  - call `TrackEvent` for module-specific metrics.
- Configuration:
  - no Inspector fields in base class.

## Network and Streaming Layer

### `UDPDiscoveryService.cs` (`unity-quest-template/Assets/_TheraplyCore/Network/Discovery/UDPDiscoveryService.cs`) - Required
- Role: Quest presence broadcast + optional receive path.
- Behavior:
  - broadcasts device info JSON periodically
  - can pause/resume broadcast while TCP client connected
  - resolves local IP off main thread
- Inspector configuration:
  - `_discoveryPort` (default `8767`)
  - `_broadcastInterval` (default `0.5`)
  - `_deviceTimeout`
  - `_studentId`, `_customDeviceName`
  - `_logBroadcasts`, `_logReceives`
- Use:
  - attach to network root object
  - reference it from `TCPServerService`.

### `TCPServerService.cs` (`unity-quest-template/Assets/_TheraplyCore/Network/Connection/TCPServerService.cs`) - Required
- Role: Quest TCP host for control messages.
- Behavior:
  - accepts single active client
  - JSON over length-prefixed framing
  - pauses discovery on connect, resumes on disconnect
  - exposes events for connect/disconnect/message
- Inspector configuration:
  - `_serverPort` (default `8080`)
  - `_sendBufferSize`, `_receiveBufferSize`
  - `_discoveryService`
  - `_mediaStreamService`
  - `_logConnections`, `_logMessages`
- Use:
  - required transport for command bus and WebRTC signaling.

### `TCPConnectionService.cs` (`unity-quest-template/Assets/_TheraplyCore/Network/Connection/TCPConnectionService.cs`) - Optional / client mode
- Role: Unity TCP client service (not main Quest-host path).
- Use:
  - keep only if you need Unity client mode integration.
- Inspector configuration:
  - `_controlPort`, `_connectionTimeout`
  - `_sendBufferSize`, `_receiveBufferSize`
  - `_logConnections`, `_logMessages`

### `TCPMessageHelper.cs` (`unity-quest-template/Assets/_TheraplyCore/Network/Connection/TCPMessageHelper.cs`) - Optional utility
- Role: explicit send/receive helper for the length-prefix protocol.
- Use:
  - utility API for protocol consistency in custom code.
- Configuration:
  - no Inspector fields.

### `MediaStreamService.cs` (`unity-quest-template/Assets/_TheraplyCore/Streaming/MediaStreamService.cs`) - Required for preview/talkback
- Role: WebRTC media source (Quest video + optional audio send/receive).
- Behavior:
  - creates render pipeline from XR camera
  - starts peer connection on client connect
  - generates ICE candidates and accepts remote answer/candidates
  - optional therapist voice playback and talkback receive
- Inspector configuration:
  - camera: `_sourceCamera`, `_autoDetectCamera`
  - audio: `_sendQuestAudio`, `_sourceAudioListener`, `_receiveTherapistVoice`
  - stream: `_streamWidth`, `_streamHeight`, `_targetFps`, `_targetBitrate`
  - WebRTC: `_stunServers`
  - debug: `_logStats`, `_logVerbose`
- Use:
  - assign Quest XR camera explicitly when possible.

### `WebRTCServerSignaling.cs` (`unity-quest-template/Assets/_TheraplyCore/Streaming/WebRTCServerSignaling.cs`) - Required for preview/talkback
- Role: WebRTC signaling over TCP command channel.
- Behavior:
  - on client connect:
    - starts media stream
    - creates and sends `WEBRTC_OFFER`
  - handles incoming `WEBRTC_ANSWER`
  - handles bidirectional `WEBRTC_ICE_CANDIDATE`
- Inspector configuration:
  - `_mediaStreamService`
  - `_tcpServer`
  - `_logSignaling`

## Persistence and Sync Layer

### `FirebaseDataService.cs` (`unity-quest-template/Assets/_TheraplyCore/Firebase/FirebaseDataService.cs`) - Required
- Role: asynchronous data queue + durable persistence + outbox sync + reconciliation + manual resync.
- Main behavior:
  - in-memory write queue and batch flushing
  - local durability before network for critical events
  - durable backend:
    - preferred SQLite WAL (`session_events.db`)
    - NDJSON fallback/mirror (`events.ndjson`)
  - outbox uploader with retry/backoff/jitter
  - backend ingest + reconciliation HTTP calls
  - support manual re-sync by session
- Inspector configuration:
  - batching:
    - `_batchSize`, `_batchInterval`
  - queue:
    - `_maxQueueSize`, `_dropOldestOnFull`
  - backend:
    - `_simulateFirebase`
    - `_sessionIngestEndpointUrl`
    - `_sessionReconciliationEndpointUrl`
    - `_firebaseAuthBearerToken`
    - `_firebaseApiKey`
    - `_firebaseRequestTimeoutSeconds`
    - `_logFirebaseBackendPayloads`
  - durability:
    - `_persistCriticalSessionEventsLocally`
    - `_persistAllEventsToDurableStore`
    - `_preferSqliteWalStore`
    - `_mirrorDurableEventsToNdjson`
    - `_localDurableFolder`, `_localDurableFileName`, `_sqliteStoreFileName`
    - `_maxDurableStoreQueueSize`
    - `_allowVolatileQueueFallbackWhenDurableWriteFails`
    - `_sessionContext`
  - outbox sync:
    - `_enableOutboxSync`
    - `_outboxSyncIntervalSeconds`
    - `_outboxBatchSize`
    - `_outboxBackoffBaseSeconds`, `_outboxBackoffMaxSeconds`, `_outboxBackoffJitter`
    - `_logOutboxSync`, `_outboxWorkerId`
- Use:
  - required for resilience guarantees in session roadmap.

### `SessionEventStore.cs` (`unity-quest-template/Assets/_TheraplyCore/Firebase/SessionEventStore.cs`) - Required (internal backend)
- Role: durable event write worker and outbox operations.
- Behavior:
  - background writer queue
  - checksum generation
  - backend modes:
    - disabled
    - NDJSON
    - SQLite WAL (+ optional NDJSON mirror)
  - outbox operations:
    - claim
    - mark synced
    - reschedule retries
    - force pending for manual resync
    - read sequence index for reconciliation
- Configuration:
  - configured indirectly by `FirebaseDataService` Inspector fields.

## Observability

### `Logger.cs` (`unity-quest-template/Assets/_TheraplyCore/Logging/Logger.cs`) - Optional
- Role: structured log utility with severity and optional remote queue.
- Use:
  - runtime/system logging helper
  - supports min log level and error/fatal remote flush placeholders.
- Configuration:
  - static runtime methods (`SetMinLevel`, `SetRemoteLogging`, `SetStackTraces`).

## Legacy / Compatibility Components

### `IGameModule.cs` + `BaseGame.cs` (`unity-quest-template/Assets/_TheraplyCore/Games/IGameModule.cs`, `unity-quest-template/Assets/_TheraplyCore/Games/BaseGame.cs`) - Legacy
- Role: older game API path.
- Status:
  - still present and usable for legacy modules
  - new modules should target contracts (`IMiniGameModule` + `MiniGameModuleBase`).

### `NetworkCommand.cs` (`unity-quest-template/Assets/_TheraplyCore/Network/NetworkCommand.cs`) - Legacy bridge
- Role: ScriptableObject command events for designer-driven workflows.
- Use:
  - create assets via `Create > Theraply > Network Command`
  - subscribe to `OnReceived` in components.

### `ConnectionStateManager.cs` + `ReliableCommandService.cs` (`unity-quest-template/Assets/_TheraplyCore/Connection/ConnectionStateManager.cs`, `unity-quest-template/Assets/_TheraplyCore/Connection/ReliableCommandService.cs`) - Legacy/alternative
- Role:
  - connection state machine with reconnect
  - older ACK/retry command service
- Status:
  - retained for compatibility
  - current resilience path uses Flutter critical envelopes + `MiniGameCommandBus` ACK/NACK.

## Flutter Components

## App Entry and Screens

### `main.dart` (`flutter_controller/lib/main.dart`) - Required
- Role: app bootstrap.
- Behavior:
  - `FirebaseService.initialize()`
  - runs `TheraplyControllerApp`
  - starts at `LoginScreen`
- Configuration:
  - app theme and title.

### `login_screen.dart` (`flutter_controller/lib/screens/login_screen.dart`) - Required
- Role: therapist sign-in UI.
- Behavior:
  - email/password login using Firebase Auth
  - navigates to `StudentsScreen` on success
- Configuration:
  - test defaults are prefilled in controllers.

### `students_screen.dart` (`flutter_controller/lib/screens/students_screen.dart`) - Required
- Role: student CRUD and selection.
- Behavior:
  - stream list by current therapist
  - add/edit/delete students
  - opens `ScannerScreen` for selected student
- Configuration:
  - no environment config; uses `StudentService`.

### `scanner_screen.dart` (`flutter_controller/lib/screens/scanner_screen.dart`) - Required
- Role: UDP device scanner UI.
- Behavior:
  - starts `DiscoveryService`
  - tracks last seen devices
  - removes stale devices (>15 seconds)
  - opens `ControlScreen` with selected device
- Configuration:
  - discovery behavior is in `DiscoveryService`.

### `control_screen.dart` (`flutter_controller/lib/screens/control_screen.dart`) - Required
- Role: primary therapist control panel.
- Behavior:
  - TCP connect/reconnect
  - sends control commands
  - critical commands through ACK/retry envelopes
  - receives and renders:
    - `SESSION_STATE_UPDATE`
    - `RUNTIME_STATUS_UPDATE`
    - `SESSION_WATCHDOG_HEARTBEAT`
    - `MANUAL_RESYNC_REPORT`
  - session decision gate (`Resume` vs `Start New`)
  - manual support re-sync trigger
  - starts/stops foreground service and wakelock during control
- Configuration:
  - command payloads assembled in `_buildCriticalPayload`
  - local session IDs are generated as `mobile-...`.

### `media_stream_widget.dart` (`flutter_controller/lib/widgets/media_stream_widget.dart`) - Required for preview/talkback
- Role: WebRTC video renderer with push-to-talk button.
- Behavior:
  - initializes `RTCVideoRenderer`
  - starts `WebRTCMediaService`
  - clears stream on disconnect
  - hold-to-talk toggles microphone track enable/disable
- Configuration:
  - needs active `ConnectionService`.

## Services

### `firebase_service.dart` (`flutter_controller/lib/services/firebase_service.dart`) - Required
- Role: Firebase init/auth access wrapper.
- Configuration:
  - depends on valid Android Firebase setup (`google-services.json` and Gradle plugin config).

### `student_service.dart` (`flutter_controller/lib/services/student_service.dart`) - Required
- Role: Firestore operations for `students` collection scoped by therapist ID.

### `discovery_service.dart` (`flutter_controller/lib/services/discovery_service.dart`) - Required
- Role: UDP scan listener on port `8767`.
- Behavior:
  - start/stop scan
  - pause/resume emitting devices (used when TCP is connected)
- Configuration:
  - currently fixed to bind `8767` in code.

### `connection_service.dart` (`flutter_controller/lib/services/connection_service.dart`) - Required
- Role: TCP command transport + critical ACK/retry logic.
- Behavior:
  - connect/reconnect and length-prefixed receive parser
  - base64 payload encoding for Unity compatibility
  - critical send:
    - wraps in `CriticalCommandEnvelope`
    - waits for `COMMAND_ACK`
    - retry with backoff
  - buffers WebRTC signaling messages until media service subscribes
- Configuration:
  - reconnect defaults: max attempts 8, linear backoff from 300 ms
  - critical defaults: ack timeout 3s, retries 3.

### `webrtc_media_service.dart` (`flutter_controller/lib/services/webrtc_media_service.dart`) - Required for preview/talkback
- Role: signaling and peer connection management.
- Behavior:
  - handles offer/answer
  - applies ICE candidates (including buffered early candidates)
  - emits remote stream
  - manages local microphone track for talkback (PTT)
- Configuration:
  - STUN server list currently includes Google STUN.

### `foreground_service_bridge.dart` (`flutter_controller/lib/services/foreground_service_bridge.dart`) - Required on Android for background stability
- Role: method-channel bridge to Android foreground service.
- Channel:
  - `theraply/foreground_service`
  - methods: `start`, `stop`.

### `video_stream_receiver.dart` (`flutter_controller/lib/services/video_stream_receiver.dart`) - Optional/legacy
- Role: UDP fragmented frame reassembler.
- Status:
  - not used by current `ControlScreen` (current media path is WebRTC).

## Models and Wire Contracts

### `critical_command_envelope.dart` (`flutter_controller/lib/models/critical_command_envelope.dart`) - Required
- Role: critical command envelope + ACK parser.
- Key IDs:
  - `START_GAME`, `PAUSE_GAME`, `RESUME_GAME`, `STOP_GAME`, `END_SESSION`
  - `COMMAND_ACK` with `ACK` / `NACK`.

### `session_fsm_contract.dart` (`flutter_controller/lib/models/session_fsm_contract.dart`) - Required
- Role: mirrored session FSM and `SESSION_STATE_UPDATE` parser.

### `runtime_status_signal.dart` (`flutter_controller/lib/models/runtime_status_signal.dart`) - Required
- Role: runtime status and watchdog heartbeat models/parsers.

### `manual_resync_report_signal.dart` (`flutter_controller/lib/models/manual_resync_report_signal.dart`) - Required
- Role: manual resync response model/parser.

### `session_recovery_policy.dart` (`flutter_controller/lib/models/session_recovery_policy.dart`) - Required
- Role: `Resume vs Start New` decision logic.

### `device_info.dart`, `student.dart`, `game_status.dart` (`flutter_controller/lib/models/device_info.dart`, `flutter_controller/lib/models/student.dart`, `flutter_controller/lib/models/game_status.dart`) - Required/basic
- Role:
  - device discovery payload model
  - student entity model
  - simple game status model.

## Android Bridge Components (Flutter app)

### `MainActivity.kt` (`flutter_controller/android/app/src/main/kotlin/com/yourcompany/flutter_controller/MainActivity.kt`) - Required
- Role: method channel handler; starts/stops foreground service.

### `ConnectionForegroundService.kt` (`flutter_controller/android/app/src/main/kotlin/com/yourcompany/flutter_controller/ConnectionForegroundService.kt`) - Required
- Role: Android foreground notification service to keep control session stable in background.
- Key behavior:
  - `ACTION_START` -> `startForeground(...)`
  - `ACTION_STOP` or task removed -> stop service.

### `AndroidManifest.xml` (`flutter_controller/android/app/src/main/AndroidManifest.xml`) - Required
- Must include:
  - internet/network/audio permissions
  - foreground service permissions
  - service declaration for `ConnectionForegroundService`.

## Unity Configuration Checklist

For production scene, ensure these references are wired:

- `UDPDiscoveryService`
  - discovery port `8767`
- `TCPServerService`
  - port `8080`
  - `_discoveryService` -> `UDPDiscoveryService`
  - `_mediaStreamService` -> `MediaStreamService`
- `MediaStreamService`
  - `_sourceCamera` assigned to XR camera
  - stream baseline `1280x720@30`
- `WebRTCServerSignaling`
  - `_mediaStreamService` -> `MediaStreamService`
  - `_tcpServer` -> `TCPServerService`
- `MiniGameSessionContext`
  - auto-start/defer strategy according to rollout policy
- `MiniGameSessionSnapshotService`
  - `_sessionContext`, `_runtimeService`
- `MiniGameRegistryService`
  - all `gameId -> moduleBehaviour` entries
- `MiniGameCommandBus`
  - `_tcpServerService`, `_sessionContext`
- `MiniGameContextService`
  - all context dependencies assigned
- `MiniGameRuntimeService`
  - `_registryService`, `_contextService`, `_commandBus`, `_tcpServerService`, `_firebaseDataService`
- `MiniGameTelemetryService`
  - `_firebaseDataService`, `_sessionContext`
- `FirebaseDataService`
  - backend endpoint/auth values
  - durability flags
  - outbox retry policy values

If any dependency is intentionally auto-discovered, still prefer explicit Inspector assignment in production scenes.

## Flutter Configuration Checklist

- Firebase:
  - `google-services.json` in `flutter_controller/android/app/`
  - Firebase Auth + Firestore enabled
- Ports/protocol:
  - UDP discovery listener on `8767`
  - TCP control to Quest `8080`
  - length-prefixed JSON framing
  - payload base64-encoded JSON for Unity compatibility
- Session resilience:
  - keep critical commands through `sendCriticalCommand`
  - do not bypass decision gate logic in `SessionRecoveryPolicy`
- Android:
  - method channel name remains `theraply/foreground_service`
  - manifest service entry and foreground permissions remain present

## Command and Signal Map

Command flow used today:

- Flutter -> Unity critical commands:
  - `START_GAME`, `PAUSE_GAME`, `RESUME_GAME`, `STOP_GAME`, `END_SESSION`
  - wrapped in `CriticalCommandEnvelope`
  - response required: `COMMAND_ACK`
- Flutter -> Unity support:
  - `MANUAL_RESYNC`
- Unity -> Flutter status:
  - `SESSION_STATE_UPDATE`
  - `RUNTIME_STATUS_UPDATE`
  - `SESSION_WATCHDOG_HEARTBEAT`
  - `MANUAL_RESYNC_REPORT`
- WebRTC signaling over TCP:
  - `WEBRTC_OFFER`, `WEBRTC_ANSWER`, `WEBRTC_ICE_CANDIDATE`

## Testing and Validation Components

Flutter test suites that protect resilience behavior:

- `flutter_controller/test/chaos_fault_matrix_test.dart`
- `flutter_controller/test/save_resume_regression_test.dart`
- `flutter_controller/test/end_session_reliability_test.dart`
- `flutter_controller/test/reconnect_path_test.dart`
- `flutter_controller/test/runtime_status_signal_test.dart`
- `flutter_controller/test/session_recovery_policy_test.dart`
- `flutter_controller/test/manual_resync_report_signal_test.dart`

These tests are part of the safety net for session resilience hardening.

