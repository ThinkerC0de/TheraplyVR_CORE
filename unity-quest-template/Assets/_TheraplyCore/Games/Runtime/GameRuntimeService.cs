using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using TheraplyCore.Firebase;
using GameContracts = TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Network.Connection;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Runtime controller that coordinates active game lifecycle through contract services.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameRuntimeService : MonoBehaviour
    {
        [Serializable]
        private sealed class SimulatedContentCatalogEntry
        {
            public string gameId;
            public bool owned = true;
            public string installedVersion = string.Empty;
            public string targetVersion = "1.0.0";
            public bool updateOptional;
        }

        private sealed class SimulatedContentState
        {
            public string gameId;
            public bool owned;
            public string installedVersion;
            public string targetVersion;
            public bool updateRequired;
            public bool updateOptional;
            public string runtimeStatus;
            public string lastError;
            public DateTime updatedAtUtc;
        }

        [Header("Dependencies")]
        [SerializeField] private GameRegistryService _registryService;
        [SerializeField] private GameContextService _contextService;
        [SerializeField] private GameCommandBus _commandBus;
        [SerializeField] private TCPServerService _tcpServerService;
        [SerializeField] private FirebaseDataService _firebaseDataService;

        [Header("Runtime")]
        [SerializeField] private string _defaultGameId = "";
        [SerializeField] private bool _subscribeToStandardCommands = true;
        [SerializeField] private float _syncStatusPollIntervalSeconds = 1f;

        [Header("Content Delivery (Dev Simulator)")]
        [SerializeField] private bool _enableContentDeliverySimulation = true;
        [SerializeField] private bool _publishContentCatalogOnClientConnect = true;
        [SerializeField] private float _simulatedInstallDurationSeconds = 1.2f;
        [SerializeField] private string _defaultSimulatedContentVersion = "1.0.0";
        [SerializeField] private List<SimulatedContentCatalogEntry> _simulatedContentCatalog =
            new List<SimulatedContentCatalogEntry>
            {
                new SimulatedContentCatalogEntry
                {
                    gameId = "smoke_test_game",
                    owned = true,
                    installedVersion = "1.0.0",
                    targetVersion = "1.0.0",
                    updateOptional = false,
                },
                new SimulatedContentCatalogEntry
                {
                    gameId = "demo_cube_clicker",
                    owned = true,
                    installedVersion = "1.1.0",
                    targetVersion = "1.2.0",
                    updateOptional = false,
                },
                new SimulatedContentCatalogEntry
                {
                    gameId = "pulse_target_tap",
                    owned = true,
                    installedVersion = "",
                    targetVersion = "1.0.0",
                    updateOptional = false,
                },
            };

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

        private readonly Dictionary<string, GameContracts.IGameConfig> _knownConfigs =
            new Dictionary<string, GameContracts.IGameConfig>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SimulatedContentState> _simulatedContentStateByGameId =
            new Dictionary<string, SimulatedContentState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Coroutine> _simulatedInstallRoutineByGameId =
            new Dictionary<string, Coroutine>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<PendingCrashSignal> _pendingCrashSignals = new Queue<PendingCrashSignal>();
        private readonly object _pendingCrashSignalsLock = new object();

        private GameContracts.IGameModule _activeGame;
        private string _activeGameId;
        private GameSessionContext _sessionContext;
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

        public GameContracts.IGameModule ActiveGame => _activeGame;
        public string ActiveGameId => _activeGameId;
        public GameContracts.GameState ActiveGameState => _activeGame == null ? GameContracts.GameState.NotInitialized : _activeGame.State;
        public string LastPublishedRuntimeStatus => _lastRuntimeStatus;

        private void Awake()
        {
            if (_registryService == null) _registryService = FindFirstObjectByType<GameRegistryService>();
            if (_contextService == null) _contextService = FindFirstObjectByType<GameContextService>();
            if (_commandBus == null) _commandBus = FindFirstObjectByType<GameCommandBus>();
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
                _commandBus.Subscribe<SyncCatalogCommand>(HandleSyncCatalogCommand);
                _commandBus.Subscribe<InstallGameCommand>(HandleInstallGameCommand);
                _commandBus.Subscribe<UninstallGameCommand>(HandleUninstallGameCommand);
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
            EnsureSimulatedContentStatesInitialized();
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
                _commandBus.Unsubscribe<SyncCatalogCommand>(HandleSyncCatalogCommand);
                _commandBus.Unsubscribe<InstallGameCommand>(HandleInstallGameCommand);
                _commandBus.Unsubscribe<UninstallGameCommand>(HandleUninstallGameCommand);
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
            StopAllSimulatedInstallRoutines();
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
                Logger.Warning("[GameRuntime] Registry service is missing.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(gameId))
            {
                Logger.Warning("[GameRuntime] Cannot activate game with empty gameId.");
                return false;
            }

            if (!_registryService.TryResolve(gameId, out var module))
            {
                Logger.Warning($"[GameRuntime] Game not found in registry: {gameId}");
                return false;
            }

            _activeGame = module;
            _activeGameId = gameId;
            Logger.Info($"[GameRuntime] Active game set: {gameId}");
            return true;
        }

        public bool InitializeGame(string gameId, GameContracts.IGameConfig config)
        {
            if (config == null)
            {
                Logger.Warning("[GameRuntime] Initialize failed: config is null.");
                return false;
            }

            if (!SetActiveGame(gameId))
            {
                return false;
            }

            if (_contextService == null)
            {
                Logger.Warning("[GameRuntime] Initialize failed: context service is missing.");
                return false;
            }

            _knownConfigs[gameId] = config;
            _activeGame.Initialize(config, _contextService);
            return true;
        }

        public bool UpdateGameConfig(string gameId, GameContracts.IGameConfig config)
        {
            if (config == null)
            {
                Logger.Warning("[GameRuntime] Update config failed: config is null.");
                return false;
            }

            if (!SetActiveGame(gameId))
            {
                return false;
            }

            _knownConfigs[gameId] = config;

            if (_activeGame.State == GameContracts.GameState.NotInitialized)
            {
                if (_contextService == null)
                {
                    Logger.Warning("[GameRuntime] Update config failed: context service is missing.");
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
                                         _sessionContext.SessionState == GameContracts.SessionLifecycleState.CREATED;

            if (_activeGame.State == GameContracts.GameState.NotInitialized)
            {
                if (_contextService == null)
                {
                    Logger.Warning("[GameRuntime] Start failed: context service is missing.");
                    return false;
                }

                if (!TryResolveStartupConfig(out var startupConfig))
                {
                    Logger.Warning(
                        $"[GameRuntime] Start failed: {_activeGameId} has no config. Call InitializeGame first or implement IDefaultGameConfigProvider.");
                    return false;
                }

                _knownConfigs[_activeGameId] = startupConfig;
                _activeGame.Initialize(startupConfig, _contextService);
            }

            _activeGame.StartGame();
            TryTransitionSessionState(GameContracts.SessionLifecycleState.IN_PROGRESS, "START_GAME");
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
            TryTransitionSessionState(GameContracts.SessionLifecycleState.PAUSED, "PAUSE_GAME");
            return true;
        }

        public bool ResumeActiveGame()
        {
            if (!EnsureActiveGame()) return false;
            _activeGame.ResumeGame();
            TryTransitionSessionState(GameContracts.SessionLifecycleState.IN_PROGRESS, "RESUME_GAME");
            return true;
        }

        public bool StopActiveGame(GameContracts.GameStopReason reason)
        {
            if (!EnsureActiveGame()) return false;
            _activeGame.StopGame(reason);
            TryTransitionSessionState(MapStopReasonToSessionState(reason), $"STOP_GAME:{reason}");
            TrackCriticalRuntimeEvent("game_end", new Dictionary<string, object>
            {
                { "gameId", _activeGameId ?? string.Empty },
                { "reason", reason.ToString() },
            });
            TryReportActiveGameResult(reason);
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

            TryApplyStartCommandConfig(command);

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

            if (_activeGame != null &&
                _activeGame.State == GameContracts.GameState.NotInitialized &&
                command != null &&
                command.resumeFromSaved)
            {
                var syntheticStart = new StartGameCommand
                {
                    correlationId = command.correlationId,
                    gameId = command.gameId,
                    resumeFromSaved = true,
                    gameConfigType = string.Empty,
                    gameConfigVersion = 0,
                    gameConfigJson = string.Empty,
                };

                TryApplyStartCommandConfig(syntheticStart);

                if (!StartActiveGame())
                {
                    throw new InvalidOperationException("RESUME_GAME_START_FROM_SAVE_FAILED");
                }

                return;
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

            var reason = GameContracts.GameStopReason.TherapistStop;
            if (!string.IsNullOrWhiteSpace(command?.reason) &&
                Enum.TryParse(command.reason, true, out GameContracts.GameStopReason parsedReason))
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
                if (!StopActiveGame(GameContracts.GameStopReason.TherapistStop))
                {
                    throw new InvalidOperationException("END_SESSION_STOP_FAILED");
                }
                return;
            }

            if (!TryTransitionSessionState(GameContracts.SessionLifecycleState.ABORTED_BY_THERAPIST, "END_SESSION"))
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

        private void HandleSyncCatalogCommand(SyncCatalogCommand command)
        {
            if (!_enableContentDeliverySimulation)
            {
                return;
            }

            EnsureSimulatedContentStatesInitialized();
            PublishSimulatedContentCatalogSnapshot(
                command == null ? string.Empty : command.correlationId,
                "SYNC_CATALOG");
        }

        private void HandleInstallGameCommand(InstallGameCommand command)
        {
            if (!_enableContentDeliverySimulation)
            {
                return;
            }

            EnsureSimulatedContentStatesInitialized();

            var requestedGameId = command == null ? string.Empty : command.gameId;
            if (string.IsNullOrWhiteSpace(requestedGameId))
            {
                Logger.Warning("[GameRuntime] INSTALL_GAME ignored: empty gameId.");
                return;
            }

            var normalizedGameId = requestedGameId.Trim();
            if (!TryGetOrCreateSimulatedContentState(
                    normalizedGameId,
                    command == null ? null : command.targetVersion,
                    out var state))
            {
                return;
            }

            if (!state.owned)
            {
                state.runtimeStatus = ContentRuntimeStatusValues.Failed;
                state.lastError = "NOT_OWNED";
                state.updatedAtUtc = DateTime.UtcNow;
                _ = PublishGameInstallStatusAsync(
                    state,
                    command == null ? string.Empty : command.correlationId,
                    "INSTALL_NOT_OWNED");
                return;
            }

            state.targetVersion = NormalizeContentVersion(
                command == null ? null : command.targetVersion,
                state.targetVersion);
            state.runtimeStatus = ContentRuntimeStatusValues.Installing;
            state.updateRequired = false;
            state.lastError = string.Empty;
            state.updatedAtUtc = DateTime.UtcNow;
            _ = PublishGameInstallStatusAsync(
                state,
                command == null ? string.Empty : command.correlationId,
                "INSTALL_STARTED");

            StartSimulatedInstallRoutine(
                state,
                command == null ? string.Empty : command.correlationId);
        }

        private void HandleUninstallGameCommand(UninstallGameCommand command)
        {
            if (!_enableContentDeliverySimulation)
            {
                return;
            }

            EnsureSimulatedContentStatesInitialized();

            var requestedGameId = command == null ? string.Empty : command.gameId;
            if (string.IsNullOrWhiteSpace(requestedGameId))
            {
                Logger.Warning("[GameRuntime] UNINSTALL_GAME ignored: empty gameId.");
                return;
            }

            var normalizedGameId = requestedGameId.Trim();
            if (!TryGetOrCreateSimulatedContentState(normalizedGameId, null, out var state))
            {
                return;
            }

            StopSimulatedInstallRoutine(normalizedGameId);
            state.installedVersion = string.Empty;
            state.runtimeStatus = ContentRuntimeStatusValues.NotInstalled;
            state.updateRequired = false;
            state.lastError = string.Empty;
            state.updatedAtUtc = DateTime.UtcNow;

            _ = PublishGameInstallStatusAsync(
                state,
                command == null ? string.Empty : command.correlationId,
                "UNINSTALL_COMPLETED");
        }

        private void EnsureSimulatedContentStatesInitialized()
        {
            if (!_enableContentDeliverySimulation || _simulatedContentStateByGameId.Count > 0)
            {
                return;
            }

            if (_simulatedContentCatalog == null)
            {
                _simulatedContentCatalog = new List<SimulatedContentCatalogEntry>();
            }

            for (var i = 0; i < _simulatedContentCatalog.Count; i++)
            {
                var entry = _simulatedContentCatalog[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.gameId))
                {
                    continue;
                }

                var gameId = entry.gameId.Trim();
                if (_simulatedContentStateByGameId.ContainsKey(gameId))
                {
                    continue;
                }

                var targetVersion = NormalizeContentVersion(entry.targetVersion, _defaultSimulatedContentVersion);
                var installedVersion = string.IsNullOrWhiteSpace(entry.installedVersion)
                    ? string.Empty
                    : entry.installedVersion.Trim();
                var updateRequired = entry.owned &&
                                     !string.IsNullOrWhiteSpace(installedVersion) &&
                                     !string.Equals(
                                         installedVersion,
                                         targetVersion,
                                         StringComparison.OrdinalIgnoreCase);

                var state = new SimulatedContentState
                {
                    gameId = gameId,
                    owned = entry.owned,
                    installedVersion = installedVersion,
                    targetVersion = targetVersion,
                    updateRequired = updateRequired,
                    updateOptional = entry.updateOptional,
                    runtimeStatus = ResolveRuntimeStatusForContentState(
                        entry.owned,
                        installedVersion,
                        updateRequired),
                    lastError = string.Empty,
                    updatedAtUtc = DateTime.UtcNow,
                };

                _simulatedContentStateByGameId[gameId] = state;
            }

            if (_simulatedContentStateByGameId.Count == 0 && !string.IsNullOrWhiteSpace(_defaultGameId))
            {
                var gameId = _defaultGameId.Trim();
                _simulatedContentStateByGameId[gameId] = new SimulatedContentState
                {
                    gameId = gameId,
                    owned = true,
                    installedVersion = NormalizeContentVersion(
                        _defaultSimulatedContentVersion,
                        "1.0.0"),
                    targetVersion = NormalizeContentVersion(
                        _defaultSimulatedContentVersion,
                        "1.0.0"),
                    updateRequired = false,
                    updateOptional = false,
                    runtimeStatus = ContentRuntimeStatusValues.Ready,
                    lastError = string.Empty,
                    updatedAtUtc = DateTime.UtcNow,
                };
            }
        }

        private bool TryGetOrCreateSimulatedContentState(
            string gameId,
            string requestedTargetVersion,
            out SimulatedContentState state)
        {
            state = null;
            if (string.IsNullOrWhiteSpace(gameId))
            {
                return false;
            }

            if (_simulatedContentStateByGameId.TryGetValue(gameId, out state))
            {
                if (!string.IsNullOrWhiteSpace(requestedTargetVersion))
                {
                    state.targetVersion = NormalizeContentVersion(requestedTargetVersion, state.targetVersion);
                }

                return true;
            }

            state = new SimulatedContentState
            {
                gameId = gameId,
                owned = true,
                installedVersion = string.Empty,
                targetVersion = NormalizeContentVersion(
                    requestedTargetVersion,
                    _defaultSimulatedContentVersion),
                updateRequired = false,
                updateOptional = false,
                runtimeStatus = ContentRuntimeStatusValues.NotInstalled,
                lastError = string.Empty,
                updatedAtUtc = DateTime.UtcNow,
            };
            _simulatedContentStateByGameId[gameId] = state;
            return true;
        }

        private void StartSimulatedInstallRoutine(SimulatedContentState state, string correlationId)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.gameId))
            {
                return;
            }

            StopSimulatedInstallRoutine(state.gameId);
            var routine = StartCoroutine(SimulateInstallRoutine(state, correlationId));
            _simulatedInstallRoutineByGameId[state.gameId] = routine;
        }

        private IEnumerator SimulateInstallRoutine(SimulatedContentState state, string correlationId)
        {
            var waitSeconds = Mathf.Max(0.1f, _simulatedInstallDurationSeconds);
            yield return new WaitForSeconds(waitSeconds);

            if (state == null || string.IsNullOrWhiteSpace(state.gameId))
            {
                yield break;
            }

            state.installedVersion = NormalizeContentVersion(
                state.targetVersion,
                _defaultSimulatedContentVersion);
            state.updateRequired = false;
            state.runtimeStatus = ContentRuntimeStatusValues.Ready;
            state.lastError = string.Empty;
            state.updatedAtUtc = DateTime.UtcNow;
            _ = PublishGameInstallStatusAsync(state, correlationId, "INSTALL_COMPLETED");
            _simulatedInstallRoutineByGameId.Remove(state.gameId);
        }

        private void StopSimulatedInstallRoutine(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId) ||
                !_simulatedInstallRoutineByGameId.TryGetValue(gameId, out var routine))
            {
                return;
            }

            if (routine != null)
            {
                StopCoroutine(routine);
            }

            _simulatedInstallRoutineByGameId.Remove(gameId);
        }

        private void StopAllSimulatedInstallRoutines()
        {
            if (_simulatedInstallRoutineByGameId.Count == 0)
            {
                return;
            }

            var gameIds = new List<string>(_simulatedInstallRoutineByGameId.Keys);
            for (var i = 0; i < gameIds.Count; i++)
            {
                StopSimulatedInstallRoutine(gameIds[i]);
            }
        }

        private void PublishSimulatedContentCatalogSnapshot(string correlationId, string reasonCode)
        {
            if (!_enableContentDeliverySimulation)
            {
                return;
            }

            EnsureSimulatedContentStatesInitialized();
            foreach (var pair in _simulatedContentStateByGameId)
            {
                _ = PublishGameInstallStatusAsync(pair.Value, correlationId, reasonCode);
            }
        }

        private static string ResolveRuntimeStatusForContentState(
            bool owned,
            string installedVersion,
            bool updateRequired)
        {
            if (!owned)
            {
                return ContentRuntimeStatusValues.NotInstalled;
            }

            if (string.IsNullOrWhiteSpace(installedVersion))
            {
                return ContentRuntimeStatusValues.NotInstalled;
            }

            if (updateRequired)
            {
                return ContentRuntimeStatusValues.UpdateRequired;
            }

            return ContentRuntimeStatusValues.Ready;
        }

        private static string NormalizeContentVersion(string version, string fallbackVersion)
        {
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version.Trim();
            }

            if (!string.IsNullOrWhiteSpace(fallbackVersion))
            {
                return fallbackVersion.Trim();
            }

            return "1.0.0";
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

            Logger.Warning("[GameRuntime] No active game selected.");
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

        private void TryApplyStartCommandConfig(StartGameCommand command)
        {
            if (command == null || _activeGame == null || string.IsNullOrWhiteSpace(_activeGameId))
            {
                return;
            }

            if (!(_activeGame is GameContracts.IStartCommandConfigProvider configProvider))
            {
                return;
            }

            _knownConfigs.TryGetValue(_activeGameId, out var previousConfig);

            if (!configProvider.TryCreateConfigFromStartCommand(
                    command,
                    previousConfig,
                    out var resolvedConfig,
                    out var reasonCode))
            {
                if (!string.IsNullOrWhiteSpace(reasonCode))
                {
                    Logger.Warning(
                        $"[GameRuntime] Ignored START_GAME config for {_activeGameId}: {reasonCode}");
                }
                return;
            }

            if (resolvedConfig == null)
            {
                Logger.Warning($"[GameRuntime] START_GAME config provider returned null for {_activeGameId}.");
                return;
            }

            if (!UpdateGameConfig(_activeGameId, resolvedConfig))
            {
                Logger.Warning($"[GameRuntime] Failed to apply START_GAME config for {_activeGameId}.");
            }
        }

        private bool TryResolveStartupConfig(out GameContracts.IGameConfig config)
        {
            config = null;

            if (string.IsNullOrWhiteSpace(_activeGameId))
            {
                return false;
            }

            if (_knownConfigs.TryGetValue(_activeGameId, out var cachedConfig) && cachedConfig != null)
            {
                config = cachedConfig;
                return true;
            }

            if (!(_activeGame is GameContracts.IDefaultGameConfigProvider defaultConfigProvider))
            {
                return false;
            }

            var defaultConfig = defaultConfigProvider.CreateDefaultConfig();
            if (defaultConfig == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(defaultConfig.GameId) &&
                !string.Equals(defaultConfig.GameId, _activeGameId, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warning(
                    $"[GameRuntime] Default config gameId mismatch. active={_activeGameId}, config={defaultConfig.GameId}. Using active game id.");
            }

            config = defaultConfig;
            return true;
        }

        private GameSessionContext ResolveSessionContext()
        {
            if (_contextService?.Session is GameSessionContext typedSessionContext)
            {
                return typedSessionContext;
            }

            return FindFirstObjectByType<GameSessionContext>();
        }

        private bool TryTransitionSessionState(GameContracts.SessionLifecycleState targetState, string reasonCode)
        {
            if (_sessionContext == null)
            {
                _sessionContext = ResolveSessionContext();
            }

            if (_sessionContext == null)
            {
                Logger.Warning("[GameRuntime] Session context missing. Cannot update session lifecycle state.");
                return false;
            }

            return _sessionContext.TryTransitionTo(targetState, reasonCode);
        }

        private void HandleSessionStateChanged(
            GameContracts.SessionLifecycleState previousState,
            GameContracts.SessionLifecycleState currentState,
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
                state = GameContracts.SessionFsmContract.ToWireState(currentState),
                previousState = GameContracts.SessionFsmContract.ToWireState(previousState),
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
                Logger.Warning($"[GameRuntime] Failed to publish {GameCommandIds.SessionStateUpdate}: {e.Message}");
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
            if (_publishContentCatalogOnClientConnect)
            {
                PublishSimulatedContentCatalogSnapshot(string.Empty, "TCP_CLIENT_CONNECTED");
            }
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
                    $"[GameRuntime][CrashContext] Captured {report.logType} source={report.source} session={report.sessionId} report={report.reportId}");
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
                sessionState = _sessionContext == null ? string.Empty : GameContracts.SessionFsmContract.ToWireState(_sessionContext.SessionState),
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
                UnityEngine.Debug.LogWarning($"[GameRuntime][CrashContext] Failed to persist crash report: {e.Message}");
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
            var activeGameState = hasActiveGame ? _activeGame.State : GameContracts.GameState.NotInitialized;
            var isHealthy = EvaluateWatchdogHealth(sessionState, hasActiveGame, activeGameState, out var healthCode);

            if (isHealthy)
            {
                if (_watchdogHangReported)
                {
                    Logger.Info(
                        $"[GameRuntime][Watchdog] Recovered: session={sessionId}, state={sessionState}, gameState={activeGameState}, healthCode={healthCode}");
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
                        $"[GameRuntime][Watchdog] Hung state detected: session={sessionId}, state={sessionState}, gameState={activeGameState}, healthCode={healthCode}, unhealthyForSec={unhealthySeconds:F1}");
                    TrackWatchdogEvent("watchdog_hung_state", healthCode, sessionState, activeGameState, unhealthySeconds);

                    if (_watchdogAutoInterruptInProgress &&
                        sessionState == GameContracts.SessionLifecycleState.IN_PROGRESS &&
                        TryTransitionSessionState(GameContracts.SessionLifecycleState.INTERRUPTED, "WATCHDOG_HUNG_STATE"))
                    {
                        TrackWatchdogEvent(
                            "watchdog_forced_interrupt",
                            "WATCHDOG_HUNG_STATE",
                            GameContracts.SessionLifecycleState.INTERRUPTED,
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
            GameContracts.SessionLifecycleState sessionState,
            GameContracts.GameState activeGameState,
            float unhealthyForSec)
        {
            if (!_watchdogEmitTelemetry)
            {
                return;
            }

            TrackCriticalRuntimeEvent(eventName, new Dictionary<string, object>
            {
                { "sessionId", _sessionContext?.SessionId ?? string.Empty },
                { "state", GameContracts.SessionFsmContract.ToWireState(sessionState) },
                { "healthCode", healthCode ?? string.Empty },
                { "activeGameId", _activeGameId ?? string.Empty },
                { "activeGameState", activeGameState.ToString() },
                { "unhealthyForSec", unhealthyForSec.ToString("F3", CultureInfo.InvariantCulture) },
            });
        }

        private void PublishSessionWatchdogHeartbeat(
            float heartbeatIntervalSeconds,
            GameContracts.SessionLifecycleState sessionState,
            GameContracts.GameState activeGameState,
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
                    : GameContracts.SessionFsmContract.ToWireState(_sessionContext.SessionState),
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
                    $"[GameRuntime][Watchdog] Heartbeat: session={command.sessionId}, state={command.sessionState}, healthy={command.healthy}, healthCode={command.healthCode}");
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
                    $"[GameRuntime] Failed to publish {GameCommandIds.SessionWatchdogHeartbeat}: {e.Message}");
            }
        }

        private static bool EvaluateWatchdogHealth(
            GameContracts.SessionLifecycleState sessionState,
            bool hasActiveGame,
            GameContracts.GameState activeGameState,
            out string healthCode)
        {
            switch (sessionState)
            {
                case GameContracts.SessionLifecycleState.IN_PROGRESS:
                    if (!hasActiveGame)
                    {
                        healthCode = "IN_PROGRESS_NO_ACTIVE_GAME";
                        return false;
                    }

                    if (activeGameState != GameContracts.GameState.Playing)
                    {
                        healthCode = $"IN_PROGRESS_GAME_STATE_{activeGameState.ToString().ToUpperInvariant()}";
                        return false;
                    }

                    healthCode = "OK";
                    return true;

                case GameContracts.SessionLifecycleState.PAUSED:
                    if (!hasActiveGame)
                    {
                        healthCode = "PAUSED_NO_ACTIVE_GAME";
                        return false;
                    }

                    if (activeGameState != GameContracts.GameState.Paused)
                    {
                        healthCode = $"PAUSED_GAME_STATE_{activeGameState.ToString().ToUpperInvariant()}";
                        return false;
                    }

                    healthCode = "OK";
                    return true;

                case GameContracts.SessionLifecycleState.CREATED:
                case GameContracts.SessionLifecycleState.INTERRUPTED:
                    healthCode = "OK";
                    return true;

                case GameContracts.SessionLifecycleState.COMPLETED:
                case GameContracts.SessionLifecycleState.ABORTED_BY_THERAPIST:
                case GameContracts.SessionLifecycleState.FAILED_TECHNICAL:
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
                    $"[GameRuntime] Failed to publish {GameCommandIds.RuntimeStatusUpdate}: {e.Message}");
            }
        }

        private async Task PublishGameInstallStatusAsync(
            SimulatedContentState state,
            string correlationId,
            string reasonCode)
        {
            if (_commandBus == null || state == null || string.IsNullOrWhiteSpace(state.gameId))
            {
                return;
            }

            if (!HasStatusDeliveryRoute())
            {
                return;
            }

            var command = new GameInstallStatusCommand
            {
                correlationId = string.IsNullOrWhiteSpace(correlationId)
                    ? Guid.NewGuid().ToString()
                    : correlationId,
                gameId = state.gameId,
                owned = state.owned,
                installedVersion = string.IsNullOrWhiteSpace(state.installedVersion)
                    ? string.Empty
                    : state.installedVersion,
                targetVersion = string.IsNullOrWhiteSpace(state.targetVersion)
                    ? _defaultSimulatedContentVersion
                    : state.targetVersion,
                updateRequired = state.updateRequired,
                updateOptional = state.updateOptional,
                runtimeStatus = string.IsNullOrWhiteSpace(state.runtimeStatus)
                    ? ContentRuntimeStatusValues.NotInstalled
                    : state.runtimeStatus,
                lastError = state.lastError ?? string.Empty,
                updatedAtUtc = (state.updatedAtUtc == DateTime.MinValue
                        ? DateTime.UtcNow
                        : state.updatedAtUtc.ToUniversalTime())
                    .ToString("O", CultureInfo.InvariantCulture),
            };

            try
            {
                await _commandBus.PublishAsync(command);
            }
            catch (Exception e)
            {
                Logger.Warning(
                    $"[GameRuntime] Failed to publish {GameCommandIds.GameInstallStatus} ({state.gameId}, reason={reasonCode}): {e.Message}");
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
                    $"[GameRuntime] Failed to publish {GameCommandIds.ManualResyncReport}: {e.Message}");
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
                Logger.Warning($"[GameRuntime] Failed to read queue statistics: {e.Message}");
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

        private static bool IsSessionActiveNonTerminal(GameContracts.SessionLifecycleState state)
        {
            return state == GameContracts.SessionLifecycleState.CREATED ||
                   state == GameContracts.SessionLifecycleState.IN_PROGRESS ||
                   state == GameContracts.SessionLifecycleState.PAUSED ||
                   state == GameContracts.SessionLifecycleState.INTERRUPTED;
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
                case GameContracts.SessionLifecycleState.IN_PROGRESS:
                    return RuntimeStatusValues.Playing;
                case GameContracts.SessionLifecycleState.PAUSED:
                    return RuntimeStatusValues.Paused;
                case GameContracts.SessionLifecycleState.INTERRUPTED:
                case GameContracts.SessionLifecycleState.FAILED_TECHNICAL:
                    return RuntimeStatusValues.Interrupted;
                case GameContracts.SessionLifecycleState.CREATED:
                case GameContracts.SessionLifecycleState.COMPLETED:
                case GameContracts.SessionLifecycleState.ABORTED_BY_THERAPIST:
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
                Logger.Warning($"[GameRuntime] Failed to track critical event {eventName}: {e.Message}");
            }
        }

        private void TryReportActiveGameResult(GameContracts.GameStopReason stopReason)
        {
            if (_activeGame == null)
            {
                return;
            }

            try
            {
                var result = _activeGame.BuildResult();
                if (result == null)
                {
                    return;
                }

                var metricCount = result.Metrics == null ? 0 : result.Metrics.Count;
                Logger.Info(
                    $"[GameRuntime] Result: gameId={result.GameId}, completed={result.Completed}, durationSec={result.DurationSec:F3}, stopReason={stopReason}, metricCount={metricCount}");

                TrackCriticalRuntimeEvent("game_result", new Dictionary<string, object>
                {
                    { "gameId", string.IsNullOrWhiteSpace(result.GameId) ? _activeGameId ?? string.Empty : result.GameId },
                    { "completed", result.Completed },
                    { "durationSec", result.DurationSec.ToString("F3", CultureInfo.InvariantCulture) },
                    { "stopReason", stopReason.ToString() },
                    { "metricCount", metricCount },
                });
            }
            catch (Exception e)
            {
                Logger.Warning($"[GameRuntime] Failed to build/report game result: {e.Message}");
            }
        }

        private static GameContracts.SessionLifecycleState MapStopReasonToSessionState(GameContracts.GameStopReason reason)
        {
            switch (reason)
            {
                case GameContracts.GameStopReason.Completed:
                    return GameContracts.SessionLifecycleState.COMPLETED;
                case GameContracts.GameStopReason.TherapistStop:
                    return GameContracts.SessionLifecycleState.ABORTED_BY_THERAPIST;
                case GameContracts.GameStopReason.Error:
                    return GameContracts.SessionLifecycleState.FAILED_TECHNICAL;
                case GameContracts.GameStopReason.Timeout:
                case GameContracts.GameStopReason.UserExit:
                case GameContracts.GameStopReason.NetworkLoss:
                default:
                    return GameContracts.SessionLifecycleState.INTERRUPTED;
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
