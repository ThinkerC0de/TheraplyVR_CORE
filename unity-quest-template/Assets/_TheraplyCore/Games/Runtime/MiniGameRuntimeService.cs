using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using TheraplyCore.Firebase;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Network.Connection;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Runtime controller that coordinates active game lifecycle through contract services.
    /// </summary>
    [DisallowMultipleComponent]
    public class MiniGameRuntimeService : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private MiniGameRegistryService _registryService;
        [SerializeField] private MiniGameContextService _contextService;
        [SerializeField] private MiniGameCommandBus _commandBus;
        [SerializeField] private TCPServerService _tcpServerService;
        [SerializeField] private FirebaseDataService _firebaseDataService;

        [Header("Runtime")]
        [SerializeField] private string _defaultGameId = "";
        [SerializeField] private bool _subscribeToStandardCommands = true;
        [SerializeField] private float _syncStatusPollIntervalSeconds = 1f;

        [Header("Watchdog")]
        [SerializeField] private bool _enableSessionWatchdog = true;
        [SerializeField] private float _watchdogHeartbeatIntervalSeconds = 2f;
        [SerializeField] private float _watchdogHungThresholdSeconds = 8f;
        [SerializeField] private bool _watchdogAutoInterruptInProgress = true;
        [SerializeField] private bool _watchdogEmitTelemetry = true;
        [SerializeField] private bool _watchdogLogHeartbeat = false;

        [Header("Crash Context")]
        [SerializeField] private bool _enableStructuredCrashContext = true;
        [SerializeField] private bool _captureUnityExceptionLogs = true;
        [SerializeField] private bool _captureAppDomainUnhandledExceptions = true;
        [SerializeField] private bool _captureUnityErrorLogs = false;
        [SerializeField] private bool _persistCrashReportsLocally = true;
        [SerializeField] private bool _emitCrashReportsToTelemetry = true;
        [SerializeField] private string _crashReportFolder = "session_resilience";
        [SerializeField] private string _crashReportFileName = "crash_reports.ndjson";
        [SerializeField] private int _maxCrashReportsPerFrame = 2;
        [SerializeField] private int _maxPendingCrashSignals = 64;
        [SerializeField] private bool _logCrashCapture = true;

        private readonly Dictionary<string, IMiniGameConfig> _knownConfigs =
            new Dictionary<string, IMiniGameConfig>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<PendingCrashSignal> _pendingCrashSignals = new Queue<PendingCrashSignal>();
        private readonly object _pendingCrashSignalsLock = new object();

        private IMiniGameModule _activeGame;
        private string _activeGameId;
        private MiniGameSessionContext _sessionContext;
        private Coroutine _syncStatusPollRoutine;
        private string _lastRuntimeStatus = string.Empty;
        private bool _syncPending;
        private bool _syncPendingInitialized;
        private Coroutine _watchdogRoutine;
        private DateTime _watchdogLastHealthyUtc = DateTime.MinValue;
        private bool _watchdogHangReported;
        private bool _crashHooksRegistered;
        private string _crashReportPath;
        private string _lastCrashFingerprint = string.Empty;
        private DateTime _lastCrashFingerprintAtUtc = DateTime.MinValue;
        private int _capturedCrashReportCount;

        public IMiniGameModule ActiveGame => _activeGame;
        public string ActiveGameId => _activeGameId;
        public MiniGameState ActiveGameState => _activeGame == null ? MiniGameState.NotInitialized : _activeGame.State;
        public string LastPublishedRuntimeStatus => _lastRuntimeStatus;

        private void Awake()
        {
            if (_registryService == null) _registryService = FindFirstObjectByType<MiniGameRegistryService>();
            if (_contextService == null) _contextService = FindFirstObjectByType<MiniGameContextService>();
            if (_commandBus == null) _commandBus = FindFirstObjectByType<MiniGameCommandBus>();
            if (_tcpServerService == null) _tcpServerService = FindFirstObjectByType<TCPServerService>();
            if (_firebaseDataService == null) _firebaseDataService = FindFirstObjectByType<FirebaseDataService>();
            _sessionContext = ResolveSessionContext();

            if (!string.IsNullOrWhiteSpace(_defaultGameId))
            {
                SetActiveGame(_defaultGameId);
            }
        }

        private void OnEnable()
        {
            if (_subscribeToStandardCommands && _commandBus != null)
            {
                _commandBus.Subscribe<StartGameCommand>(HandleStartCommand);
                _commandBus.Subscribe<PauseGameCommand>(HandlePauseCommand);
                _commandBus.Subscribe<ResumeGameCommand>(HandleResumeCommand);
                _commandBus.Subscribe<StopGameCommand>(HandleStopCommand);
                _commandBus.Subscribe<EndSessionCommand>(HandleEndSessionCommand);
                _commandBus.Subscribe<ManualResyncCommand>(HandleManualResyncCommand);
            }

            if (_sessionContext == null)
            {
                _sessionContext = ResolveSessionContext();
            }

            if (_sessionContext != null)
            {
                _sessionContext.OnSessionStateChanged += HandleSessionStateChanged;
            }

            if (_tcpServerService != null)
            {
                _tcpServerService.OnClientConnected += HandleClientConnected;
                _tcpServerService.OnClientDisconnected += HandleClientDisconnected;
            }

            RegisterCrashHooks();
            StartSyncStatusPolling();
            StartSessionWatchdog();
            PublishRuntimeStatusIfChanged("RUNTIME_ENABLED");
        }

        private void OnDisable()
        {
            if (_subscribeToStandardCommands && _commandBus != null)
            {
                _commandBus.Unsubscribe<StartGameCommand>(HandleStartCommand);
                _commandBus.Unsubscribe<PauseGameCommand>(HandlePauseCommand);
                _commandBus.Unsubscribe<ResumeGameCommand>(HandleResumeCommand);
                _commandBus.Unsubscribe<StopGameCommand>(HandleStopCommand);
                _commandBus.Unsubscribe<EndSessionCommand>(HandleEndSessionCommand);
                _commandBus.Unsubscribe<ManualResyncCommand>(HandleManualResyncCommand);
            }

            if (_sessionContext != null)
            {
                _sessionContext.OnSessionStateChanged -= HandleSessionStateChanged;
            }

            if (_tcpServerService != null)
            {
                _tcpServerService.OnClientConnected -= HandleClientConnected;
                _tcpServerService.OnClientDisconnected -= HandleClientDisconnected;
            }

            UnregisterCrashHooks();
            DrainPendingCrashSignals(_maxPendingCrashSignals);
            StopSyncStatusPolling();
            StopSessionWatchdog();
        }

        private void Update()
        {
            DrainPendingCrashSignals();
        }

        public bool SetActiveGame(string gameId)
        {
            if (_registryService == null)
            {
                Logger.Warning("[MiniGameRuntime] Registry service is missing.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(gameId))
            {
                Logger.Warning("[MiniGameRuntime] Cannot activate game with empty gameId.");
                return false;
            }

            if (!_registryService.TryResolve(gameId, out var module))
            {
                Logger.Warning($"[MiniGameRuntime] Game not found in registry: {gameId}");
                return false;
            }

            _activeGame = module;
            _activeGameId = gameId;
            Logger.Info($"[MiniGameRuntime] Active game set: {gameId}");
            return true;
        }

        public bool InitializeGame(string gameId, IMiniGameConfig config)
        {
            if (config == null)
            {
                Logger.Warning("[MiniGameRuntime] Initialize failed: config is null.");
                return false;
            }

            if (!SetActiveGame(gameId))
            {
                return false;
            }

            if (_contextService == null)
            {
                Logger.Warning("[MiniGameRuntime] Initialize failed: context service is missing.");
                return false;
            }

            _knownConfigs[gameId] = config;
            _activeGame.Initialize(config, _contextService);
            return true;
        }

        public bool UpdateGameConfig(string gameId, IMiniGameConfig config)
        {
            if (config == null)
            {
                Logger.Warning("[MiniGameRuntime] Update config failed: config is null.");
                return false;
            }

            if (!SetActiveGame(gameId))
            {
                return false;
            }

            _knownConfigs[gameId] = config;

            if (_activeGame.State == MiniGameState.NotInitialized)
            {
                if (_contextService == null)
                {
                    Logger.Warning("[MiniGameRuntime] Update config failed: context service is missing.");
                    return false;
                }

                _activeGame.Initialize(config, _contextService);
            }
            else
            {
                _activeGame.UpdateConfig(config);
            }

            return true;
        }

        public bool StartActiveGame()
        {
            if (!EnsureActiveGame())
            {
                return false;
            }

            var shouldEmitSessionStart = _sessionContext != null &&
                                         _sessionContext.SessionState == SessionLifecycleState.CREATED;

            if (_activeGame.State == MiniGameState.NotInitialized)
            {
                if (!_knownConfigs.TryGetValue(_activeGameId, out var cachedConfig))
                {
                    Logger.Warning($"[MiniGameRuntime] Start failed: {_activeGameId} has no config. Call InitializeGame first.");
                    return false;
                }

                _activeGame.Initialize(cachedConfig, _contextService);
            }

            _activeGame.StartGame();
            TryTransitionSessionState(SessionLifecycleState.IN_PROGRESS, "START_GAME");
            TrackCriticalRuntimeEvent("game_start", new Dictionary<string, object>
            {
                { "gameId", _activeGameId ?? string.Empty },
            });

            if (shouldEmitSessionStart)
            {
                TrackCriticalRuntimeEvent("session_start", new Dictionary<string, object>
                {
                    { "gameId", _activeGameId ?? string.Empty },
                    { "reason", "START_GAME" },
                });
            }

            return true;
        }

        public bool PauseActiveGame()
        {
            if (!EnsureActiveGame()) return false;
            _activeGame.PauseGame();
            TryTransitionSessionState(SessionLifecycleState.PAUSED, "PAUSE_GAME");
            return true;
        }

        public bool ResumeActiveGame()
        {
            if (!EnsureActiveGame()) return false;
            _activeGame.ResumeGame();
            TryTransitionSessionState(SessionLifecycleState.IN_PROGRESS, "RESUME_GAME");
            return true;
        }

        public bool StopActiveGame(MiniGameStopReason reason)
        {
            if (!EnsureActiveGame()) return false;
            _activeGame.StopGame(reason);
            TryTransitionSessionState(MapStopReasonToSessionState(reason), $"STOP_GAME:{reason}");
            TrackCriticalRuntimeEvent("game_end", new Dictionary<string, object>
            {
                { "gameId", _activeGameId ?? string.Empty },
                { "reason", reason.ToString() },
            });
            TrackCriticalRuntimeEvent("session_stop", new Dictionary<string, object>
            {
                { "gameId", _activeGameId ?? string.Empty },
                { "reason", reason.ToString() },
            });
            return true;
        }

        private void HandleStartCommand(StartGameCommand command)
        {
            if (!TryResolveCommandGame(command?.gameId))
            {
                throw new InvalidOperationException("START_GAME_NO_ACTIVE_GAME");
            }

            if (!StartActiveGame())
            {
                throw new InvalidOperationException("START_GAME_FAILED");
            }
        }

        private void HandlePauseCommand(PauseGameCommand command)
        {
            if (!TryResolveCommandGame(command?.gameId))
            {
                throw new InvalidOperationException("PAUSE_GAME_NO_ACTIVE_GAME");
            }

            if (!PauseActiveGame())
            {
                throw new InvalidOperationException("PAUSE_GAME_FAILED");
            }
        }

        private void HandleResumeCommand(ResumeGameCommand command)
        {
            if (!TryResolveCommandGame(command?.gameId))
            {
                throw new InvalidOperationException("RESUME_GAME_NO_ACTIVE_GAME");
            }

            if (!ResumeActiveGame())
            {
                throw new InvalidOperationException("RESUME_GAME_FAILED");
            }
        }

        private void HandleStopCommand(StopGameCommand command)
        {
            if (!TryResolveCommandGame(command?.gameId))
            {
                throw new InvalidOperationException("STOP_GAME_NO_ACTIVE_GAME");
            }

            var reason = MiniGameStopReason.TherapistStop;
            if (!string.IsNullOrWhiteSpace(command?.reason) &&
                Enum.TryParse(command.reason, true, out MiniGameStopReason parsedReason))
            {
                reason = parsedReason;
            }

            if (!StopActiveGame(reason))
            {
                throw new InvalidOperationException("STOP_GAME_FAILED");
            }
        }

        private void HandleEndSessionCommand(EndSessionCommand command)
        {
            if (_activeGame != null || EnsureActiveGame())
            {
                if (!StopActiveGame(MiniGameStopReason.TherapistStop))
                {
                    throw new InvalidOperationException("END_SESSION_STOP_FAILED");
                }
                return;
            }

            if (!TryTransitionSessionState(SessionLifecycleState.ABORTED_BY_THERAPIST, "END_SESSION"))
            {
                throw new InvalidOperationException("END_SESSION_TRANSITION_FAILED");
            }

            TrackCriticalRuntimeEvent("session_stop", new Dictionary<string, object>
            {
                { "gameId", _activeGameId ?? string.Empty },
                { "reason", "END_SESSION" },
            });
        }

        private void HandleManualResyncCommand(ManualResyncCommand command)
        {
            _ = HandleManualResyncCommandAsync(command);
        }

        private async Task HandleManualResyncCommandAsync(ManualResyncCommand command)
        {
            if (_firebaseDataService == null)
            {
                _firebaseDataService = FindFirstObjectByType<FirebaseDataService>();
            }

            var requestedSessionId = command?.sessionId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(requestedSessionId))
            {
                requestedSessionId = _sessionContext?.SessionId ?? string.Empty;
            }

            var reportCommand = new ManualResyncReportCommand
            {
                correlationId = string.IsNullOrWhiteSpace(command?.correlationId)
                    ? Guid.NewGuid().ToString()
                    : command.correlationId,
                success = false,
                reasonCode = "UNINITIALIZED",
                sessionId = requestedSessionId ?? string.Empty,
                requestedBy = command?.requestedBy ?? string.Empty,
                requestedReasonCode = command?.reasonCode ?? string.Empty,
                requestedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                includeSyncedEvents = command != null && command.includeSyncedEvents,
                targetedEvents = 0,
                outboxRowsUpdated = 0,
                uploadCycleTriggered = false,
                localEvents = 0,
                beforeReasonCode = string.Empty,
                beforeMissingOnServerCount = 0,
                beforeMissingOnDeviceCount = 0,
                beforeOutboxPending = 0,
                afterReasonCode = string.Empty,
                afterMissingOnServerCount = 0,
                afterMissingOnDeviceCount = 0,
                afterOutboxPending = 0,
                targetedSequencePreview = "[]",
                details = string.Empty,
            };

            if (string.IsNullOrWhiteSpace(requestedSessionId))
            {
                reportCommand.reasonCode = "SESSION_ID_REQUIRED";
                reportCommand.details = "Manual re-sync requires sessionId in command or active session context.";
                await PublishManualResyncReportAsync(reportCommand);
                return;
            }

            if (_firebaseDataService == null)
            {
                reportCommand.reasonCode = "FIREBASE_DATA_SERVICE_UNAVAILABLE";
                reportCommand.details = "FirebaseDataService dependency is missing.";
                await PublishManualResyncReportAsync(reportCommand);
                return;
            }

            try
            {
                var manualReport = await _firebaseDataService.RunManualResyncAsync(
                    requestedSessionId,
                    includeSyncedEvents: command != null && command.includeSyncedEvents,
                    requestedBy: command?.requestedBy,
                    reasonCode: command?.reasonCode);

                reportCommand.success = manualReport.success;
                reportCommand.reasonCode = manualReport.reasonCode ?? string.Empty;
                reportCommand.sessionId = manualReport.sessionId ?? requestedSessionId;
                reportCommand.requestedBy = manualReport.requestedBy ?? string.Empty;
                reportCommand.requestedReasonCode = manualReport.requestedReasonCode ?? string.Empty;
                reportCommand.requestedAtUtc = manualReport.requestedAtUtc ?? reportCommand.requestedAtUtc;
                reportCommand.includeSyncedEvents = manualReport.includeSyncedEvents;
                reportCommand.targetedEvents = manualReport.targetedEvents;
                reportCommand.outboxRowsUpdated = manualReport.outboxRowsUpdated;
                reportCommand.uploadCycleTriggered = manualReport.uploadCycleTriggered;
                reportCommand.localEvents = manualReport.localEvents;
                reportCommand.beforeReasonCode = manualReport.beforeReasonCode ?? string.Empty;
                reportCommand.beforeMissingOnServerCount = manualReport.beforeMissingOnServerCount;
                reportCommand.beforeMissingOnDeviceCount = manualReport.beforeMissingOnDeviceCount;
                reportCommand.beforeOutboxPending = manualReport.beforeOutboxPending;
                reportCommand.afterReasonCode = manualReport.afterReasonCode ?? string.Empty;
                reportCommand.afterMissingOnServerCount = manualReport.afterMissingOnServerCount;
                reportCommand.afterMissingOnDeviceCount = manualReport.afterMissingOnDeviceCount;
                reportCommand.afterOutboxPending = manualReport.afterOutboxPending;
                reportCommand.targetedSequencePreview =
                    BuildSequencePreview(manualReport.targetedSequencePreview, 24);
                reportCommand.details = manualReport.details ?? string.Empty;
            }
            catch (Exception e)
            {
                reportCommand.success = false;
                reportCommand.reasonCode = "MANUAL_RESYNC_EXCEPTION";
                reportCommand.details = e.Message;
            }

            await PublishManualResyncReportAsync(reportCommand);
            RefreshSyncPendingState("MANUAL_RESYNC");
        }

        private bool EnsureActiveGame()
        {
            if (_activeGame != null)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(_defaultGameId))
            {
                return SetActiveGame(_defaultGameId);
            }

            Logger.Warning("[MiniGameRuntime] No active game selected.");
            return false;
        }

        private bool TryResolveCommandGame(string gameId)
        {
            if (!string.IsNullOrWhiteSpace(gameId))
            {
                return SetActiveGame(gameId);
            }

            return EnsureActiveGame();
        }

        private MiniGameSessionContext ResolveSessionContext()
        {
            if (_contextService?.Session is MiniGameSessionContext typedSessionContext)
            {
                return typedSessionContext;
            }

            return FindFirstObjectByType<MiniGameSessionContext>();
        }

        private bool TryTransitionSessionState(SessionLifecycleState targetState, string reasonCode)
        {
            if (_sessionContext == null)
            {
                _sessionContext = ResolveSessionContext();
            }

            if (_sessionContext == null)
            {
                Logger.Warning("[MiniGameRuntime] Session context missing. Cannot update session lifecycle state.");
                return false;
            }

            return _sessionContext.TryTransitionTo(targetState, reasonCode);
        }

        private void HandleSessionStateChanged(
            SessionLifecycleState previousState,
            SessionLifecycleState currentState,
            string reasonCode)
        {
            if (_commandBus == null || _sessionContext == null)
            {
                return;
            }

            var command = new SessionStateUpdateCommand
            {
                sessionId = _sessionContext.SessionId,
                patientId = _sessionContext.PatientId,
                therapistId = _sessionContext.TherapistId,
                state = SessionFsmContract.ToWireState(currentState),
                previousState = SessionFsmContract.ToWireState(previousState),
                reasonCode = reasonCode ?? string.Empty,
                changedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };

            _ = PublishSessionStateUpdateAsync(command);
            PublishRuntimeStatusIfChanged(reasonCode);

            if (IsSessionActiveNonTerminal(currentState))
            {
                _watchdogLastHealthyUtc = DateTime.UtcNow;
            }

            _watchdogHangReported = false;
        }

        private async Task PublishSessionStateUpdateAsync(SessionStateUpdateCommand command)
        {
            try
            {
                await _commandBus.PublishAsync(command);
            }
            catch (Exception e)
            {
                Logger.Warning($"[MiniGameRuntime] Failed to publish {MiniGameCommandIds.SessionStateUpdate}: {e.Message}");
                TrackCriticalRuntimeEvent("error", new Dictionary<string, object>
                {
                    { "source", "SESSION_STATE_UPDATE" },
                    { "message", e.Message },
                });
            }
        }

        private void HandleClientConnected(string _)
        {
            PublishRuntimeStatusIfChanged("TCP_CLIENT_CONNECTED");
        }

        private void HandleClientDisconnected()
        {
            _lastRuntimeStatus = string.Empty;
        }

        private void RegisterCrashHooks()
        {
            if (!_enableStructuredCrashContext || _crashHooksRegistered)
            {
                return;
            }

            if (_captureUnityExceptionLogs || _captureUnityErrorLogs)
            {
                Application.logMessageReceivedThreaded += HandleUnityLogMessageThreaded;
            }

            if (_captureAppDomainUnhandledExceptions)
            {
                AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
            }

            _crashHooksRegistered = true;
        }

        private void UnregisterCrashHooks()
        {
            if (!_crashHooksRegistered)
            {
                return;
            }

            if (_captureUnityExceptionLogs || _captureUnityErrorLogs)
            {
                Application.logMessageReceivedThreaded -= HandleUnityLogMessageThreaded;
            }

            if (_captureAppDomainUnhandledExceptions)
            {
                AppDomain.CurrentDomain.UnhandledException -= HandleUnhandledException;
            }

            _crashHooksRegistered = false;
        }

        private void HandleUnityLogMessageThreaded(string condition, string stackTrace, LogType logType)
        {
            if (!_enableStructuredCrashContext || !ShouldCaptureUnityLogType(logType))
            {
                return;
            }

            EnqueueCrashSignal(
                source: "UNITY_LOG",
                message: condition,
                stackTrace: stackTrace,
                logType: logType,
                isTerminating: false,
                exceptionType: string.Empty);
        }

        private void HandleUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            if (!_enableStructuredCrashContext)
            {
                return;
            }

            var exception = args?.ExceptionObject as Exception;
            var message = exception == null
                ? Convert.ToString(args?.ExceptionObject, CultureInfo.InvariantCulture)
                : exception.Message;

            EnqueueCrashSignal(
                source: "APPDOMAIN_UNHANDLED",
                message: string.IsNullOrWhiteSpace(message) ? "Unhandled exception" : message,
                stackTrace: exception?.StackTrace ?? string.Empty,
                logType: LogType.Exception,
                isTerminating: args != null && args.IsTerminating,
                exceptionType: exception?.GetType().FullName ?? string.Empty);
        }

        private bool ShouldCaptureUnityLogType(LogType logType)
        {
            if (_captureUnityExceptionLogs &&
                (logType == LogType.Exception || logType == LogType.Assert))
            {
                return true;
            }

            return _captureUnityErrorLogs && logType == LogType.Error;
        }

        private void EnqueueCrashSignal(
            string source,
            string message,
            string stackTrace,
            LogType logType,
            bool isTerminating,
            string exceptionType)
        {
            var safeMessage = string.IsNullOrWhiteSpace(message) ? "<empty>" : message.Trim();
            var fingerprint = BuildCrashFingerprint(source, safeMessage, stackTrace, logType, exceptionType);
            var nowUtc = DateTime.UtcNow;

            lock (_pendingCrashSignalsLock)
            {
                if (string.Equals(fingerprint, _lastCrashFingerprint, StringComparison.Ordinal) &&
                    (nowUtc - _lastCrashFingerprintAtUtc).TotalMilliseconds < 500)
                {
                    return;
                }

                _lastCrashFingerprint = fingerprint;
                _lastCrashFingerprintAtUtc = nowUtc;

                var safeMaxSignals = Math.Max(8, _maxPendingCrashSignals);
                while (_pendingCrashSignals.Count >= safeMaxSignals)
                {
                    _pendingCrashSignals.Dequeue();
                }

                _pendingCrashSignals.Enqueue(new PendingCrashSignal
                {
                    source = source ?? "UNKNOWN",
                    message = safeMessage,
                    stackTrace = stackTrace ?? string.Empty,
                    logType = logType.ToString(),
                    exceptionType = exceptionType ?? string.Empty,
                    isTerminating = isTerminating,
                    capturedAtUtc = nowUtc,
                });
            }
        }

        private static string BuildCrashFingerprint(
            string source,
            string message,
            string stackTrace,
            LogType logType,
            string exceptionType)
        {
            var messagePart = message ?? string.Empty;
            if (messagePart.Length > 180)
            {
                messagePart = messagePart.Substring(0, 180);
            }

            var stackPart = stackTrace ?? string.Empty;
            if (stackPart.Length > 180)
            {
                stackPart = stackPart.Substring(0, 180);
            }

            return string.Concat(
                source ?? string.Empty, "|",
                logType.ToString(), "|",
                exceptionType ?? string.Empty, "|",
                messagePart, "|",
                stackPart);
        }

        private void DrainPendingCrashSignals(int maxReports = -1)
        {
            if (!_enableStructuredCrashContext)
            {
                return;
            }

            var maxPerFrame = maxReports > 0 ? maxReports : Math.Max(1, _maxCrashReportsPerFrame);
            for (var i = 0; i < maxPerFrame; i++)
            {
                if (!TryDequeuePendingCrashSignal(out var signal))
                {
                    return;
                }

                ProcessCrashSignal(signal);
            }
        }

        private bool TryDequeuePendingCrashSignal(out PendingCrashSignal signal)
        {
            lock (_pendingCrashSignalsLock)
            {
                if (_pendingCrashSignals.Count == 0)
                {
                    signal = new PendingCrashSignal();
                    return false;
                }

                signal = _pendingCrashSignals.Dequeue();
                return true;
            }
        }

        private void ProcessCrashSignal(PendingCrashSignal signal)
        {
            var report = BuildCrashContextReport(signal);

            if (_persistCrashReportsLocally)
            {
                PersistCrashContextReport(report);
            }

            if (_emitCrashReportsToTelemetry)
            {
                EmitCrashContextTelemetry(report);
            }

            _capturedCrashReportCount++;

            if (_logCrashCapture)
            {
                Logger.Warning(
                    $"[MiniGameRuntime][CrashContext] Captured {report.logType} source={report.source} session={report.sessionId} report={report.reportId}");
            }
        }

        private StructuredCrashContextReport BuildCrashContextReport(PendingCrashSignal signal)
        {
            if (_sessionContext == null)
            {
                _sessionContext = ResolveSessionContext();
            }

            var pendingQueueSize = ResolvePendingQueueSize();
            return new StructuredCrashContextReport
            {
                reportId = Guid.NewGuid().ToString(),
                capturedAtUtc = (signal.capturedAtUtc == default ? DateTime.UtcNow : signal.capturedAtUtc)
                    .ToString("O", CultureInfo.InvariantCulture),
                source = signal.source ?? "UNKNOWN",
                logType = string.IsNullOrWhiteSpace(signal.logType) ? "Exception" : signal.logType,
                exceptionType = signal.exceptionType ?? string.Empty,
                message = signal.message ?? string.Empty,
                stackTrace = signal.stackTrace ?? string.Empty,
                isTerminating = signal.isTerminating,
                sessionId = _sessionContext?.SessionId ?? string.Empty,
                patientId = _sessionContext?.PatientId ?? string.Empty,
                therapistId = _sessionContext?.TherapistId ?? string.Empty,
                sessionState = _sessionContext == null ? string.Empty : SessionFsmContract.ToWireState(_sessionContext.SessionState),
                activeGameId = _activeGameId ?? string.Empty,
                activeGameState = ActiveGameState.ToString(),
                runtimeStatus = ResolveRuntimeStatusForCrashReport(pendingQueueSize),
                pendingQueueSize = pendingQueueSize,
                sceneName = ResolveSceneName(),
                deviceModel = SystemInfo.deviceModel ?? string.Empty,
                deviceName = SystemInfo.deviceName ?? string.Empty,
                deviceUniqueIdentifier = SystemInfo.deviceUniqueIdentifier ?? string.Empty,
                platform = Application.platform.ToString(),
                unityVersion = Application.unityVersion,
                appVersion = Application.version,
            };
        }

        private void PersistCrashContextReport(StructuredCrashContextReport report)
        {
            if (report == null)
            {
                return;
            }

            try
            {
                EnsureCrashReportPath();
                if (string.IsNullOrWhiteSpace(_crashReportPath))
                {
                    return;
                }

                var directory = Path.GetDirectoryName(_crashReportPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var line = JsonUtility.ToJson(report) + Environment.NewLine;
                File.AppendAllText(_crashReportPath, line);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[MiniGameRuntime][CrashContext] Failed to persist crash report: {e.Message}");
            }
        }

        private void EmitCrashContextTelemetry(StructuredCrashContextReport report)
        {
            if (report == null)
            {
                return;
            }

            TrackCriticalRuntimeEvent("error", new Dictionary<string, object>
            {
                { "eventName", "crash_context" },
                { "reportId", report.reportId ?? string.Empty },
                { "source", report.source ?? string.Empty },
                { "logType", report.logType ?? string.Empty },
                { "exceptionType", report.exceptionType ?? string.Empty },
                { "message", TruncateForPayload(report.message, 512) },
                { "stackTrace", TruncateForPayload(report.stackTrace, 2048) },
                { "isTerminating", report.isTerminating },
                { "sessionId", report.sessionId ?? string.Empty },
                { "patientId", report.patientId ?? string.Empty },
                { "therapistId", report.therapistId ?? string.Empty },
                { "sessionState", report.sessionState ?? string.Empty },
                { "activeGameId", report.activeGameId ?? string.Empty },
                { "activeGameState", report.activeGameState ?? string.Empty },
                { "runtimeStatus", report.runtimeStatus ?? string.Empty },
                { "pendingQueueSize", report.pendingQueueSize },
                { "capturedAtUtc", report.capturedAtUtc ?? string.Empty },
                { "sceneName", report.sceneName ?? string.Empty },
                { "platform", report.platform ?? string.Empty },
                { "unityVersion", report.unityVersion ?? string.Empty },
                { "appVersion", report.appVersion ?? string.Empty },
            });
        }

        private void EnsureCrashReportPath()
        {
            if (!string.IsNullOrWhiteSpace(_crashReportPath))
            {
                return;
            }

            var folder = string.IsNullOrWhiteSpace(_crashReportFolder) ? "session_resilience" : _crashReportFolder.Trim();
            var fileName = string.IsNullOrWhiteSpace(_crashReportFileName) ? "crash_reports.ndjson" : _crashReportFileName.Trim();

            _crashReportPath = Path.Combine(Application.persistentDataPath, folder, fileName);
        }

        private string ResolveRuntimeStatusForCrashReport(int pendingQueueSize)
        {
            if (!string.IsNullOrWhiteSpace(_lastRuntimeStatus))
            {
                return _lastRuntimeStatus;
            }

            return ResolveRuntimeStatus(pendingQueueSize);
        }

        private static string ResolveSceneName()
        {
            try
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                return scene.name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string TruncateForPayload(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || maxLength <= 0 || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxLength);
        }

        private void StartSyncStatusPolling()
        {
            StopSyncStatusPolling();

            if (_firebaseDataService == null)
            {
                _firebaseDataService = FindFirstObjectByType<FirebaseDataService>();
            }

            if (_firebaseDataService == null)
            {
                return;
            }

            _syncStatusPollRoutine = StartCoroutine(PollSyncStatus());
        }

        private void StopSyncStatusPolling()
        {
            if (_syncStatusPollRoutine == null)
            {
                return;
            }

            StopCoroutine(_syncStatusPollRoutine);
            _syncStatusPollRoutine = null;
        }

        private void StartSessionWatchdog()
        {
            StopSessionWatchdog();

            if (!_enableSessionWatchdog)
            {
                return;
            }

            _watchdogLastHealthyUtc = DateTime.UtcNow;
            _watchdogHangReported = false;
            _watchdogRoutine = StartCoroutine(PollSessionWatchdog());
        }

        private void StopSessionWatchdog()
        {
            if (_watchdogRoutine == null)
            {
                return;
            }

            StopCoroutine(_watchdogRoutine);
            _watchdogRoutine = null;
        }

        private IEnumerator PollSessionWatchdog()
        {
            var heartbeatIntervalSeconds = Mathf.Max(0.5f, _watchdogHeartbeatIntervalSeconds);
            var wait = new WaitForSeconds(heartbeatIntervalSeconds);

            while (true)
            {
                RunSessionWatchdogCycle(heartbeatIntervalSeconds);
                yield return wait;
            }
        }

        private void RunSessionWatchdogCycle(float heartbeatIntervalSeconds)
        {
            if (_sessionContext == null)
            {
                _sessionContext = ResolveSessionContext();
            }

            if (_sessionContext == null)
            {
                return;
            }

            var sessionState = _sessionContext.SessionState;
            var sessionId = _sessionContext.SessionId ?? string.Empty;
            if (!IsSessionActiveNonTerminal(sessionState) || string.IsNullOrWhiteSpace(sessionId))
            {
                _watchdogLastHealthyUtc = DateTime.UtcNow;
                _watchdogHangReported = false;
                return;
            }

            var nowUtc = DateTime.UtcNow;
            if (_watchdogLastHealthyUtc == DateTime.MinValue)
            {
                _watchdogLastHealthyUtc = nowUtc;
            }

            var hasActiveGame = _activeGame != null;
            var activeGameState = hasActiveGame ? _activeGame.State : MiniGameState.NotInitialized;
            var isHealthy = EvaluateWatchdogHealth(sessionState, hasActiveGame, activeGameState, out var healthCode);

            if (isHealthy)
            {
                if (_watchdogHangReported)
                {
                    Logger.Info(
                        $"[MiniGameRuntime][Watchdog] Recovered: session={sessionId}, state={sessionState}, gameState={activeGameState}, healthCode={healthCode}");
                    TrackWatchdogEvent("watchdog_recovered", healthCode, sessionState, activeGameState, 0f);
                }

                _watchdogLastHealthyUtc = nowUtc;
                _watchdogHangReported = false;
            }
            else
            {
                var unhealthySeconds = (float)(nowUtc - _watchdogLastHealthyUtc).TotalSeconds;
                var thresholdSeconds = Mathf.Max(1f, _watchdogHungThresholdSeconds);
                if (!_watchdogHangReported && unhealthySeconds >= thresholdSeconds)
                {
                    _watchdogHangReported = true;
                    Logger.Warning(
                        $"[MiniGameRuntime][Watchdog] Hung state detected: session={sessionId}, state={sessionState}, gameState={activeGameState}, healthCode={healthCode}, unhealthyForSec={unhealthySeconds:F1}");
                    TrackWatchdogEvent("watchdog_hung_state", healthCode, sessionState, activeGameState, unhealthySeconds);

                    if (_watchdogAutoInterruptInProgress &&
                        sessionState == SessionLifecycleState.IN_PROGRESS &&
                        TryTransitionSessionState(SessionLifecycleState.INTERRUPTED, "WATCHDOG_HUNG_STATE"))
                    {
                        TrackWatchdogEvent(
                            "watchdog_forced_interrupt",
                            "WATCHDOG_HUNG_STATE",
                            SessionLifecycleState.INTERRUPTED,
                            activeGameState,
                            unhealthySeconds);
                    }
                }
            }

            PublishSessionWatchdogHeartbeat(
                heartbeatIntervalSeconds,
                sessionState,
                activeGameState,
                healthCode,
                isHealthy);
        }

        private void TrackWatchdogEvent(
            string eventName,
            string healthCode,
            SessionLifecycleState sessionState,
            MiniGameState activeGameState,
            float unhealthyForSec)
        {
            if (!_watchdogEmitTelemetry)
            {
                return;
            }

            TrackCriticalRuntimeEvent(eventName, new Dictionary<string, object>
            {
                { "sessionId", _sessionContext?.SessionId ?? string.Empty },
                { "state", SessionFsmContract.ToWireState(sessionState) },
                { "healthCode", healthCode ?? string.Empty },
                { "activeGameId", _activeGameId ?? string.Empty },
                { "activeGameState", activeGameState.ToString() },
                { "unhealthyForSec", unhealthyForSec.ToString("F3", CultureInfo.InvariantCulture) },
            });
        }

        private void PublishSessionWatchdogHeartbeat(
            float heartbeatIntervalSeconds,
            SessionLifecycleState sessionState,
            MiniGameState activeGameState,
            string healthCode,
            bool healthy)
        {
            if (_commandBus == null || !HasStatusDeliveryRoute())
            {
                return;
            }

            var pendingQueueSize = ResolvePendingQueueSize();
            var nowUtc = DateTime.UtcNow;
            var nowUnixMs = new DateTimeOffset(nowUtc).ToUnixTimeMilliseconds();
            var lastHealthyUtc = _watchdogLastHealthyUtc == DateTime.MinValue ? nowUtc : _watchdogLastHealthyUtc;
            var lastHealthyUnixMs = new DateTimeOffset(lastHealthyUtc).ToUnixTimeMilliseconds();
            var expectedIntervalMs = Math.Max(200, Mathf.RoundToInt(Mathf.Max(0.2f, heartbeatIntervalSeconds) * 1000f));
            var staleAfterMs = Math.Max(
                expectedIntervalMs * 3,
                Mathf.RoundToInt(Mathf.Max(1f, _watchdogHungThresholdSeconds) * 1000f));

            var command = new SessionWatchdogHeartbeatCommand
            {
                sessionId = _sessionContext?.SessionId ?? string.Empty,
                patientId = _sessionContext?.PatientId ?? string.Empty,
                therapistId = _sessionContext?.TherapistId ?? string.Empty,
                sessionState = _sessionContext == null
                    ? string.Empty
                    : SessionFsmContract.ToWireState(_sessionContext.SessionState),
                runtimeStatus = ResolveRuntimeStatus(pendingQueueSize),
                healthCode = healthCode ?? string.Empty,
                healthy = healthy,
                activeGameId = _activeGameId ?? string.Empty,
                activeGameState = activeGameState.ToString(),
                heartbeatUnixMs = nowUnixMs,
                lastHealthyUnixMs = lastHealthyUnixMs,
                pendingQueueSize = pendingQueueSize,
                expectedIntervalMs = expectedIntervalMs,
                staleAfterMs = staleAfterMs,
            };

            if (_watchdogLogHeartbeat)
            {
                Logger.Debug(
                    $"[MiniGameRuntime][Watchdog] Heartbeat: session={command.sessionId}, state={command.sessionState}, healthy={command.healthy}, healthCode={command.healthCode}");
            }

            _ = PublishSessionWatchdogHeartbeatAsync(command);
        }

        private async Task PublishSessionWatchdogHeartbeatAsync(SessionWatchdogHeartbeatCommand command)
        {
            if (_commandBus == null || command == null)
            {
                return;
            }

            try
            {
                await _commandBus.PublishAsync(command);
            }
            catch (Exception e)
            {
                Logger.Warning(
                    $"[MiniGameRuntime] Failed to publish {MiniGameCommandIds.SessionWatchdogHeartbeat}: {e.Message}");
            }
        }

        private static bool EvaluateWatchdogHealth(
            SessionLifecycleState sessionState,
            bool hasActiveGame,
            MiniGameState activeGameState,
            out string healthCode)
        {
            switch (sessionState)
            {
                case SessionLifecycleState.IN_PROGRESS:
                    if (!hasActiveGame)
                    {
                        healthCode = "IN_PROGRESS_NO_ACTIVE_GAME";
                        return false;
                    }

                    if (activeGameState != MiniGameState.Playing)
                    {
                        healthCode = $"IN_PROGRESS_GAME_STATE_{activeGameState.ToString().ToUpperInvariant()}";
                        return false;
                    }

                    healthCode = "OK";
                    return true;

                case SessionLifecycleState.PAUSED:
                    if (!hasActiveGame)
                    {
                        healthCode = "PAUSED_NO_ACTIVE_GAME";
                        return false;
                    }

                    if (activeGameState != MiniGameState.Paused)
                    {
                        healthCode = $"PAUSED_GAME_STATE_{activeGameState.ToString().ToUpperInvariant()}";
                        return false;
                    }

                    healthCode = "OK";
                    return true;

                case SessionLifecycleState.CREATED:
                case SessionLifecycleState.INTERRUPTED:
                    healthCode = "OK";
                    return true;

                case SessionLifecycleState.COMPLETED:
                case SessionLifecycleState.ABORTED_BY_THERAPIST:
                case SessionLifecycleState.FAILED_TECHNICAL:
                default:
                    healthCode = "TERMINAL";
                    return true;
            }
        }

        private IEnumerator PollSyncStatus()
        {
            var intervalSeconds = Mathf.Max(0.2f, _syncStatusPollIntervalSeconds);
            var wait = new WaitForSeconds(intervalSeconds);

            while (true)
            {
                RefreshSyncPendingState("SYNC_POLL");
                yield return wait;
            }
        }

        private void RefreshSyncPendingState(string reasonCode)
        {
            var pendingQueueSize = ResolvePendingQueueSize();
            var syncPendingNow = pendingQueueSize > 0;

            if (_syncPendingInitialized && _syncPending == syncPendingNow)
            {
                return;
            }

            _syncPendingInitialized = true;
            _syncPending = syncPendingNow;
            PublishRuntimeStatusIfChanged(reasonCode, pendingQueueSize);
        }

        private void PublishRuntimeStatusIfChanged(string reasonCode, int? pendingQueueSizeOverride = null)
        {
            if (_commandBus == null || !HasStatusDeliveryRoute())
            {
                return;
            }

            if (_sessionContext == null)
            {
                _sessionContext = ResolveSessionContext();
            }

            var pendingQueueSize = pendingQueueSizeOverride ?? ResolvePendingQueueSize();
            var nextStatus = ResolveRuntimeStatus(pendingQueueSize);
            if (string.IsNullOrWhiteSpace(nextStatus) ||
                string.Equals(nextStatus, _lastRuntimeStatus, StringComparison.Ordinal))
            {
                return;
            }

            var command = new RuntimeStatusUpdateCommand
            {
                sessionId = _sessionContext?.SessionId ?? string.Empty,
                patientId = _sessionContext?.PatientId ?? string.Empty,
                therapistId = _sessionContext?.TherapistId ?? string.Empty,
                status = nextStatus,
                previousStatus = _lastRuntimeStatus ?? string.Empty,
                reasonCode = reasonCode ?? string.Empty,
                changedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                pendingQueueSize = pendingQueueSize,
            };

            _lastRuntimeStatus = nextStatus;
            _ = PublishRuntimeStatusUpdateAsync(command);
        }

        private async Task PublishRuntimeStatusUpdateAsync(RuntimeStatusUpdateCommand command)
        {
            try
            {
                await _commandBus.PublishAsync(command);
            }
            catch (Exception e)
            {
                Logger.Warning(
                    $"[MiniGameRuntime] Failed to publish {MiniGameCommandIds.RuntimeStatusUpdate}: {e.Message}");
            }
        }

        private async Task PublishManualResyncReportAsync(ManualResyncReportCommand command)
        {
            if (_commandBus == null || command == null)
            {
                return;
            }

            try
            {
                await _commandBus.PublishAsync(command);
            }
            catch (Exception e)
            {
                Logger.Warning(
                    $"[MiniGameRuntime] Failed to publish {MiniGameCommandIds.ManualResyncReport}: {e.Message}");
            }
        }

        private int ResolvePendingQueueSize()
        {
            if (_firebaseDataService == null)
            {
                _firebaseDataService = FindFirstObjectByType<FirebaseDataService>();
            }

            if (_firebaseDataService == null)
            {
                return 0;
            }

            try
            {
                var stats = _firebaseDataService.GetStatistics();
                return Mathf.Max(
                    0,
                    stats.queueSize +
                    stats.durablePendingWrites +
                    stats.durableOutboxPending +
                    stats.durableOutboxInFlight);
            }
            catch (Exception e)
            {
                Logger.Warning($"[MiniGameRuntime] Failed to read queue statistics: {e.Message}");
                return 0;
            }
        }

        private bool HasStatusDeliveryRoute()
        {
            if (_tcpServerService == null)
            {
                _tcpServerService = FindFirstObjectByType<TCPServerService>();
            }

            if (_tcpServerService == null)
            {
                return true;
            }

            return _tcpServerService.HasClient;
        }

        private static bool IsSessionActiveNonTerminal(SessionLifecycleState state)
        {
            return state == SessionLifecycleState.CREATED ||
                   state == SessionLifecycleState.IN_PROGRESS ||
                   state == SessionLifecycleState.PAUSED ||
                   state == SessionLifecycleState.INTERRUPTED;
        }

        private string ResolveRuntimeStatus(int pendingQueueSize)
        {
            if (pendingQueueSize > 0 || _syncPending)
            {
                return RuntimeStatusValues.SyncPending;
            }

            if (_sessionContext == null)
            {
                return RuntimeStatusValues.Connected;
            }

            switch (_sessionContext.SessionState)
            {
                case SessionLifecycleState.IN_PROGRESS:
                    return RuntimeStatusValues.Playing;
                case SessionLifecycleState.PAUSED:
                    return RuntimeStatusValues.Paused;
                case SessionLifecycleState.INTERRUPTED:
                case SessionLifecycleState.FAILED_TECHNICAL:
                    return RuntimeStatusValues.Interrupted;
                case SessionLifecycleState.CREATED:
                case SessionLifecycleState.COMPLETED:
                case SessionLifecycleState.ABORTED_BY_THERAPIST:
                default:
                    return RuntimeStatusValues.Connected;
            }
        }

        private void TrackCriticalRuntimeEvent(string eventName, IReadOnlyDictionary<string, object> payload)
        {
            if (_contextService?.Telemetry == null || string.IsNullOrWhiteSpace(eventName))
            {
                return;
            }

            try
            {
                _contextService.Telemetry.Track(eventName, payload ?? new Dictionary<string, object>());
            }
            catch (Exception e)
            {
                Logger.Warning($"[MiniGameRuntime] Failed to track critical event {eventName}: {e.Message}");
            }
        }

        private static SessionLifecycleState MapStopReasonToSessionState(MiniGameStopReason reason)
        {
            switch (reason)
            {
                case MiniGameStopReason.Completed:
                    return SessionLifecycleState.COMPLETED;
                case MiniGameStopReason.TherapistStop:
                    return SessionLifecycleState.ABORTED_BY_THERAPIST;
                case MiniGameStopReason.Error:
                    return SessionLifecycleState.FAILED_TECHNICAL;
                case MiniGameStopReason.Timeout:
                case MiniGameStopReason.UserExit:
                case MiniGameStopReason.NetworkLoss:
                default:
                    return SessionLifecycleState.INTERRUPTED;
            }
        }

        private static string BuildSequencePreview(IReadOnlyList<long> sequences, int maxItems)
        {
            if (sequences == null || sequences.Count == 0)
            {
                return "[]";
            }

            var safeMax = Math.Max(1, maxItems);
            var count = Math.Min(safeMax, sequences.Count);
            var builder = new StringBuilder();
            builder.Append('[');
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(sequences[i].ToString(CultureInfo.InvariantCulture));
            }

            if (sequences.Count > count)
            {
                builder.Append(",...");
            }

            builder.Append(']');
            return builder.ToString();
        }

        private struct PendingCrashSignal
        {
            public string source;
            public string logType;
            public string exceptionType;
            public string message;
            public string stackTrace;
            public bool isTerminating;
            public DateTime capturedAtUtc;
        }

        [Serializable]
        private sealed class StructuredCrashContextReport
        {
            public string reportId;
            public string capturedAtUtc;
            public string source;
            public string logType;
            public string exceptionType;
            public string message;
            public string stackTrace;
            public bool isTerminating;

            public string sessionId;
            public string patientId;
            public string therapistId;
            public string sessionState;
            public string activeGameId;
            public string activeGameState;
            public string runtimeStatus;
            public int pendingQueueSize;

            public string sceneName;
            public string deviceModel;
            public string deviceName;
            public string deviceUniqueIdentifier;
            public string platform;
            public string unityVersion;
            public string appVersion;
        }
    }
}
