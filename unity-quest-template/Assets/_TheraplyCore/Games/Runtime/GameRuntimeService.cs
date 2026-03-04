using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
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
            public string installedPackageUri;
            public string installedManifestPath;
            public string installedBundlePath;
            public string installedBundleUri;
            public string installedSceneAssetPath;
            public string installedSceneName;
            public string installedBundleSha256;
            public DateTime updatedAtUtc;
        }

        [Serializable]
        private sealed class SimulatedContentStateRecord
        {
            public string gameId;
            public bool owned;
            public string installedVersion;
            public string targetVersion;
            public bool updateRequired;
            public bool updateOptional;
            public string runtimeStatus;
            public string lastError;
            public string installedPackageUri;
            public string installedManifestPath;
            public string installedBundlePath;
            public string installedBundleUri;
            public string installedSceneAssetPath;
            public string installedSceneName;
            public string installedBundleSha256;
            public string updatedAtUtc;
        }

        [Serializable]
        private sealed class SimulatedContentStateStore
        {
            public string schema;
            public string schemaVersion;
            public string generatedAtUtc;
            public List<SimulatedContentStateRecord> states = new List<SimulatedContentStateRecord>();
        }

        [Serializable]
        private sealed class InstalledPackageManifestRecord
        {
            public string schema;
            public string schemaVersion;
            public string packageId;
            public string contentVersion;
            public string artifactType;
            public string deliveryMode;
            public string generatedAtUtc;
            public string packageUri;
            public InstalledPackageAssetBundleRecord assetBundle;
        }

        [Serializable]
        private sealed class InstalledPackageAssetBundleRecord
        {
            public string bundleUri;
            public string bundleFileName;
            public string bundleSha256;
            public long bundleBytes;
            public string unityBuildTarget;
            public string compression;
            public string sceneAssetPath;
            public string sceneName;
            public string loadMode;
        }

        private sealed class PackageProbeOutcome
        {
            public bool success;
            public string method = string.Empty;
            public long statusCode;
            public long contentLength;
            public string eTag = string.Empty;
            public string contentType = string.Empty;
            public string reasonCode = PackageProbeReasonCodes.RequestFailed;
        }

        private static class RuntimeEntitlementReasonCodes
        {
            public const string SnapshotApplied = "ENTITLEMENT_SNAPSHOT_APPLIED";
            public const string SnapshotLegacyFallback = "ENTITLEMENT_SNAPSHOT_LEGACY_FALLBACK";
            public const string SnapshotCountMismatch = "ENTITLEMENT_IDS_COUNT_MISMATCH";
            public const string GameIdRequiredSuffix = "GAME_ID_REQUIRED";
            public const string GameNotEntitledSuffix = "GAME_NOT_ENTITLED";
            public const string ProfileUnknownSuffix = "ENTITLEMENT_PROFILE_UNKNOWN";
        }

        private static class InstallReasonCodes
        {
            public const string ManifestSyncStarted = "INSTALL_MANIFEST_SYNC_STARTED";
            public const string DownloadStarted = "INSTALL_DOWNLOAD_STARTED";
            public const string BundleDownloadStarted = "INSTALL_BUNDLE_DOWNLOAD_STARTED";
            public const string VerifyStarted = "INSTALL_VERIFY_STARTED";
            public const string ActivationStarted = "INSTALL_ACTIVATION_STARTED";
            public const string Completed = "INSTALL_COMPLETED";
            public const string DownloadFailed = "INSTALL_DOWNLOAD_FAILED";
            public const string DownloadTimeout = "INSTALL_DOWNLOAD_TIMEOUT";
            public const string DownloadConnectionError = "INSTALL_DOWNLOAD_CONNECTION_ERROR";
            public const string DownloadHttpError = "INSTALL_DOWNLOAD_HTTP_ERROR";
            public const string PackageUriMissing = "INSTALL_PACKAGE_URI_MISSING";
            public const string PackageUriInvalid = "INSTALL_PACKAGE_URI_INVALID";
            public const string PackageEmpty = "INSTALL_PACKAGE_EMPTY";
            public const string ManifestParseFailed = "INSTALL_MANIFEST_PARSE_FAILED";
            public const string ManifestSchemaUnsupported = "INSTALL_MANIFEST_SCHEMA_UNSUPPORTED";
            public const string ManifestGameMismatch = "INSTALL_MANIFEST_GAME_MISMATCH";
            public const string BundleUriMissing = "INSTALL_BUNDLE_URI_MISSING";
            public const string BundleUriInvalid = "INSTALL_BUNDLE_URI_INVALID";
            public const string BundleEmpty = "INSTALL_BUNDLE_EMPTY";
            public const string BundleChecksumMismatch = "INSTALL_BUNDLE_CHECKSUM_MISMATCH";
            public const string VerifyFailed = "INSTALL_VERIFY_FAILED";
            public const string StorageWriteFailed = "INSTALL_STORAGE_WRITE_FAILED";
            public const string UnexpectedException = "INSTALL_EXCEPTION";
            public const string RecoveredStaleProgress = "INSTALL_RECOVERED_STALE_PROGRESS";
            public const string RecoveredMissingArtifacts = "INSTALL_RECOVERED_MISSING_ARTIFACTS";
            public const string StartBundleLoadFailed = "START_CONTENT_BUNDLE_LOAD_FAILED";
            public const string StartSceneMissing = "START_CONTENT_SCENE_MISSING";
            public const string StartSceneLoadFailed = "START_CONTENT_SCENE_LOAD_FAILED";
        }

        private sealed class RuntimeEntitlementSnapshot
        {
            public bool hasExplicitSnapshot;
            public string profileId = RuntimeEntitlementProfileIds.TherapistFull;
            public string role = "THERAPIST";
            public string planTier = "BASIC";
            public string policyVersion = string.Empty;
            public string sourceTag = string.Empty;
            public string evaluatedAtUtc = string.Empty;
            public int declaredEntitledGameIdsCount;
            public HashSet<string> entitledGameIds =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        [Header("Dependencies")]
        [SerializeField] private GameRegistryService _registryService;
        [SerializeField] private GameContextService _contextService;
        [SerializeField] private GameCommandBus _commandBus;
        [SerializeField] private TCPServerService _tcpServerService;
        [SerializeField] private FirebaseDataService _firebaseDataService;

        [Header("Runtime")]
        [SerializeField] private string _defaultGameId = "";
        [SerializeField] private bool _allowDefaultGameFallback = false;
        [SerializeField] private bool _autoResumeOnControllerReconnect = false;
        [SerializeField] private bool _subscribeToStandardCommands = true;
        [SerializeField] private float _syncStatusPollIntervalSeconds = 1f;
        [SerializeField] private string _defaultMobileDisconnectBehavior =
            MobileDisconnectBehaviorValues.Pause;

        [Header("Content Delivery (Dev Simulator)")]
        [SerializeField] private bool _enableContentDeliverySimulation = true;
        [SerializeField] private bool _publishContentCatalogOnClientConnect = false;
        [SerializeField] private float _simulatedManifestSyncDurationSeconds = 0.35f;
        [SerializeField] private float _simulatedDownloadDurationSeconds = 1.2f;
        [SerializeField] private float _simulatedVerifyDurationSeconds = 0.45f;
        [SerializeField] private float _simulatedActivationDurationSeconds = 0.25f;
        [SerializeField] private float _simulatedRollbackDurationSeconds = 0.3f;
        [SerializeField] private bool _simulateVerifyFailureWhenTargetVersionContainsToken = true;
        [SerializeField] private string _simulatedVerifyFailureToken = "verify_fail";
        [SerializeField] private bool _simulateRollbackAfterVerifyFailure = true;
        [SerializeField] private string _defaultSimulatedContentVersion = "1.0.0";
        [SerializeField] private bool _persistContentDeliverySimulationState = true;
        [SerializeField] private string _contentDeliveryStateFolder = "session_resilience";
        [SerializeField] private string _contentDeliveryStateFileName = "content_delivery_state.json";
        [SerializeField] private List<SimulatedContentCatalogEntry> _simulatedContentCatalog =
            new List<SimulatedContentCatalogEntry>
            {
                new SimulatedContentCatalogEntry
                {
                    gameId = "demo_cube_clicker",
                    owned = false,
                    installedVersion = string.Empty,
                    targetVersion = "1.2.0",
                    updateOptional = false,
                },
            };

        [Header("Content Package Install")]
        [SerializeField] private int _packageInstallTimeoutSeconds = 25;
        [SerializeField] private string _packageInstallFolder = "content_packages";
        [SerializeField] private string _packageInstallFallbackFileName = "package.pkg.json";

        [Header("Installed Content Scene")]
        [SerializeField] private bool _loadInstalledContentSceneOnStart = true;
        [SerializeField] private bool _unloadInstalledContentSceneOnStop = true;

        [Header("Content Package Probe (Board-Safe)")]
        [SerializeField] private bool _enableBoardSafePackageProbe = true;
        [SerializeField] private int _packageProbeTimeoutSeconds = 3;
        [SerializeField] private bool _packageProbePreferHeadRequest = true;
        [SerializeField] private bool _packageProbeFallbackToGet = true;

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
        private readonly Dictionary<string, Coroutine> _packageProbeRoutineByGameId =
            new Dictionary<string, Coroutine>(StringComparer.OrdinalIgnoreCase);
        private Coroutine _simulatedCatalogSyncRoutine;
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
        private bool _hasObservedControllerConnection;
        private int _controllerConnectionEpoch;
        private string _lastControllerClientIp = string.Empty;
        private DateTime _lastControllerDisconnectedAtUtc = DateTime.MinValue;
        private string _mobileDisconnectBehavior = MobileDisconnectBehaviorValues.Pause;
        private bool _controllerDisconnectPausePendingResume;
        private string _lastPublishedDevicePresenceState = string.Empty;
        private string _lastPublishedDevicePresenceReasonCode = string.Empty;
        private DateTime _lastPublishedDevicePresenceAtUtc = DateTime.MinValue;
        private bool _lastAppPaused;
        private bool _lastAppFocused = true;
        private string _contentDeliveryStatePath = string.Empty;
        private RuntimeEntitlementSnapshot _runtimeEntitlementSnapshot =
            new RuntimeEntitlementSnapshot();
        private AssetBundle _activeInstalledContentBundle;
        private string _activeInstalledContentScenePath = string.Empty;
        private string _activeInstalledContentGameId = string.Empty;
        private string _activeInstalledContentBundlePath = string.Empty;
        private bool _deferredStartPending;
        private string _deferredStartGameId = string.Empty;

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
            _mobileDisconnectBehavior = NormalizeMobileDisconnectBehavior(_defaultMobileDisconnectBehavior);
            EnsureContentDeliveryStatePathInitialized();

            if (_allowDefaultGameFallback &&
                !string.IsNullOrWhiteSpace(_defaultGameId))
            {
                SetActiveGame(_defaultGameId);
            }
        }

        private void OnEnable()
        {
            if (_subscribeToStandardCommands && _commandBus != null)
            {
                _commandBus.Subscribe<SessionAttachCommand>(HandleSessionAttachCommand);
                _commandBus.Subscribe<StartGameCommand>(HandleStartCommand);
                _commandBus.Subscribe<PauseGameCommand>(HandlePauseCommand);
                _commandBus.Subscribe<ResumeGameCommand>(HandleResumeCommand);
                _commandBus.Subscribe<StopGameCommand>(HandleStopCommand);
                _commandBus.Subscribe<EndSessionCommand>(HandleEndSessionCommand);
                _commandBus.Subscribe<DynamicUpdateConfigCommand>(HandleUpdateConfigCommand);
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
                _commandBus.Unsubscribe<SessionAttachCommand>(HandleSessionAttachCommand);
                _commandBus.Unsubscribe<StartGameCommand>(HandleStartCommand);
                _commandBus.Unsubscribe<PauseGameCommand>(HandlePauseCommand);
                _commandBus.Unsubscribe<ResumeGameCommand>(HandleResumeCommand);
                _commandBus.Unsubscribe<StopGameCommand>(HandleStopCommand);
                _commandBus.Unsubscribe<EndSessionCommand>(HandleEndSessionCommand);
                _commandBus.Unsubscribe<DynamicUpdateConfigCommand>(HandleUpdateConfigCommand);
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
            StopSimulatedCatalogSyncRoutine();
            StopAllSimulatedInstallRoutines();
            StopAllPackageProbeRoutines();
            StopSyncStatusPolling();
            StopSessionWatchdog();
            TryUnloadInstalledContentScene("RUNTIME_DISABLED", true);
        }

        private void Update()
        {
            DrainPendingCrashSignals();
            ReconcileTerminalGameStateOutsideWatchdog();
        }

        private void OnApplicationPause(bool pause)
        {
            _lastAppPaused = pause;
            PublishDevicePresenceUpdateIfPossible(
                pause ? DevicePresenceStateValues.Background : DevicePresenceStateValues.Foreground,
                pause ? "APP_PAUSED" : "APP_RESUMED");
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            _lastAppFocused = hasFocus;
            PublishDevicePresenceUpdateIfPossible(
                hasFocus ? DevicePresenceStateValues.Foreground : DevicePresenceStateValues.FocusLost,
                hasFocus ? "APP_FOCUS_GAINED" : "APP_FOCUS_LOST");
        }

        private void OnApplicationQuit()
        {
            PublishDevicePresenceUpdateIfPossible(
                DevicePresenceStateValues.Quitting,
                "APP_QUIT",
                force: true);
        }

        private void ReconcileTerminalGameStateOutsideWatchdog()
        {
            if (_activeGame == null)
            {
                return;
            }

            if (_sessionContext == null)
            {
                _sessionContext = ResolveSessionContext();
            }

            if (_sessionContext == null)
            {
                return;
            }

            var sessionState = _sessionContext.SessionState;
            var hasActiveGame = _activeGame != null;
            if (!hasActiveGame)
            {
                return;
            }

            var activeGameState = _activeGame.State;
            ReconcileTerminalGameStateWithSession(ref sessionState, ref hasActiveGame, ref activeGameState);
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

            var requiresInitialization = _activeGame.State == GameContracts.GameState.NotInitialized ||
                                         _activeGame.State == GameContracts.GameState.Completed ||
                                         _activeGame.State == GameContracts.GameState.Failed;

            if (requiresInitialization)
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

            if (!TryLoadInstalledContentSceneForGame(_activeGameId, out var sceneLoadReason, out var sceneLoadPending))
            {
                Logger.Warning(
                    $"[GameRuntime] Start failed: could not activate installed content scene for {_activeGameId}: {sceneLoadReason}");
                return false;
            }

            if (sceneLoadPending)
            {
                BeginDeferredStartAfterSceneLoad(_activeGameId, shouldEmitSessionStart);
                return true;
            }

            return CompleteGameStart(shouldEmitSessionStart);
        }

        private void BeginDeferredStartAfterSceneLoad(string gameId, bool shouldEmitSessionStart)
        {
            if (string.IsNullOrWhiteSpace(gameId))
            {
                return;
            }

            if (_deferredStartPending &&
                string.Equals(_deferredStartGameId, gameId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _deferredStartPending = true;
            _deferredStartGameId = gameId;
            StartCoroutine(DeferredStartAfterSceneLoadCoroutine(gameId, shouldEmitSessionStart));
        }

        private IEnumerator DeferredStartAfterSceneLoadCoroutine(string gameId, bool shouldEmitSessionStart)
        {
            var timeoutSeconds = 5f;
            while (timeoutSeconds > 0f)
            {
                if (!_deferredStartPending ||
                    !string.Equals(_deferredStartGameId, gameId, StringComparison.OrdinalIgnoreCase))
                {
                    yield break;
                }

                if (_activeGame == null ||
                    !string.Equals(_activeGameId, gameId, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var scene = string.IsNullOrWhiteSpace(_activeInstalledContentScenePath)
                    ? new Scene()
                    : SceneManager.GetSceneByPath(_activeInstalledContentScenePath);
                if (scene.IsValid() && scene.isLoaded)
                {
                    break;
                }

                timeoutSeconds -= Mathf.Max(0.01f, Time.unscaledDeltaTime);
                yield return null;
            }

            _deferredStartPending = false;
            _deferredStartGameId = string.Empty;

            if (_activeGame == null ||
                !string.Equals(_activeGameId, gameId, StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            if (!CompleteGameStart(shouldEmitSessionStart))
            {
                Logger.Warning(
                    $"[GameRuntime] Deferred START_GAME failed after content scene wait for game={gameId}.");
            }
        }

        private bool CompleteGameStart(bool shouldEmitSessionStart)
        {
            if (_activeGame == null)
            {
                return false;
            }

            if (_activeGame.State == GameContracts.GameState.Playing ||
                _activeGame.State == GameContracts.GameState.Paused)
            {
                return true;
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

        public bool PauseActiveGame(string reasonCode = "PAUSE_GAME")
        {
            if (!EnsureActiveGame()) return false;
            _activeGame.PauseGame();
            var resolvedReasonCode = string.IsNullOrWhiteSpace(reasonCode)
                ? "PAUSE_GAME"
                : reasonCode.Trim();
            TryTransitionSessionState(GameContracts.SessionLifecycleState.PAUSED, resolvedReasonCode);
            return true;
        }

        public bool ResumeActiveGame(string reasonCode = "RESUME_GAME")
        {
            if (!EnsureActiveGame()) return false;
            _activeGame.ResumeGame();
            var resolvedReasonCode = string.IsNullOrWhiteSpace(reasonCode)
                ? "RESUME_GAME"
                : reasonCode.Trim();
            TryTransitionSessionState(GameContracts.SessionLifecycleState.IN_PROGRESS, resolvedReasonCode);
            return true;
        }

        public bool StopActiveGame(GameContracts.GameStopReason reason)
        {
            if (!EnsureActiveGame()) return false;
            var stoppedGameId = _activeGameId ?? string.Empty;
            _activeGame.StopGame(reason);
            TryTransitionSessionState(MapStopReasonToSessionState(reason), $"STOP_GAME:{reason}");
            TrackCriticalRuntimeEvent("game_end", new Dictionary<string, object>
            {
                { "gameId", stoppedGameId },
                { "reason", reason.ToString() },
            });
            TryReportActiveGameResult(reason);
            if (_unloadInstalledContentSceneOnStop)
            {
                TryUnloadInstalledContentScene($"STOP_GAME:{reason}", false);
            }
            ClearActiveGameSelection($"STOP_GAME:{reason}");
            TrackCriticalRuntimeEvent("session_stop", new Dictionary<string, object>
            {
                { "gameId", stoppedGameId },
                { "reason", reason.ToString() },
            });
            return true;
        }

        private void HandleSessionAttachCommand(SessionAttachCommand command)
        {
            if (_sessionContext == null)
            {
                _sessionContext = ResolveSessionContext();
            }

            if (_sessionContext == null)
            {
                throw new InvalidOperationException("SESSION_ATTACH_CONTEXT_MISSING");
            }

            var requestedSessionId = command == null ? string.Empty : (command.sessionId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(requestedSessionId))
            {
                throw new InvalidOperationException("SESSION_ATTACH_SESSION_ID_REQUIRED");
            }

            var patientId = command == null ? string.Empty : (command.patientId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(patientId))
            {
                patientId = string.IsNullOrWhiteSpace(_sessionContext.PatientId)
                    ? "unknown_patient"
                    : _sessionContext.PatientId;
            }

            var therapistId = command == null ? string.Empty : (command.therapistId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(therapistId))
            {
                therapistId = string.IsNullOrWhiteSpace(_sessionContext.TherapistId)
                    ? "unknown_therapist"
                    : _sessionContext.TherapistId;
            }

            var reasonCode = command == null ? string.Empty : (command.reasonCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(reasonCode))
            {
                reasonCode = "SESSION_ATTACH";
            }

            ApplyRuntimeBackendHostOverride(command);

            var currentSessionId = _sessionContext.SessionId ?? string.Empty;
            var currentState = _sessionContext.SessionState;
            var sameSession = string.Equals(currentSessionId, requestedSessionId, StringComparison.Ordinal);

            if (_activeGame != null && !sameSession)
            {
                TrackCriticalRuntimeEvent("session_attach", new Dictionary<string, object>
                {
                    { "sessionId", currentSessionId },
                    { "requestedSessionId", requestedSessionId },
                    { "activePatientId", _sessionContext.PatientId ?? string.Empty },
                    { "activeTherapistId", _sessionContext.TherapistId ?? string.Empty },
                    { "patientId", patientId },
                    { "therapistId", therapistId },
                    { "reasonCode", reasonCode },
                    { "mobileDisconnectBehavior", _mobileDisconnectBehavior },
                    { "mode", "active_game_conflict_reused_current" },
                });
                Logger.Warning(
                    $"[GameRuntime] SESSION_ATTACH conflict during active game. Keeping current session={currentSessionId}, requested={requestedSessionId}.");
                return;
            }

            if (sameSession)
            {
                if (!IsTerminalSessionState(currentState))
                {
                    var activePatientId = (_sessionContext.PatientId ?? string.Empty).Trim();
                    var activeTherapistId = (_sessionContext.TherapistId ?? string.Empty).Trim();
                    var incomingPatientId = patientId.Trim();
                    var incomingTherapistId = therapistId.Trim();

                    if (!string.Equals(activePatientId, incomingPatientId, StringComparison.Ordinal) ||
                        !string.Equals(activeTherapistId, incomingTherapistId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("SESSION_ATTACH_OWNER_CONFLICT");
                    }
                }

                _sessionContext.UpdateParticipantIds(patientId, therapistId);
                if (IsTerminalSessionState(currentState))
                {
                    _sessionContext.BeginSession(patientId, therapistId, requestedSessionId);
                }

                ApplyRuntimeEntitlementSnapshot(command);
                ApplyMobileDisconnectBehavior(command);

                TrackCriticalRuntimeEvent("session_attach", new Dictionary<string, object>
                {
                    { "sessionId", requestedSessionId },
                    { "patientId", patientId },
                    { "therapistId", therapistId },
                    { "reasonCode", reasonCode },
                    { "mobileDisconnectBehavior", _mobileDisconnectBehavior },
                    { "mode", "same_session" },
                });
                return;
            }

            var restored = _sessionContext.RestoreSession(
                patientId: patientId,
                therapistId: therapistId,
                sessionId: requestedSessionId,
                startedAtUtc: DateTime.UtcNow,
                restoredState: GameContracts.SessionLifecycleState.CREATED,
                reasonCode: reasonCode,
                forceReplaceActive: true);

            if (!restored)
            {
                throw new InvalidOperationException("SESSION_ATTACH_RESTORE_FAILED");
            }

            ApplyRuntimeEntitlementSnapshot(command);
            ApplyMobileDisconnectBehavior(command);

            TrackCriticalRuntimeEvent("session_attach", new Dictionary<string, object>
            {
                { "sessionId", requestedSessionId },
                { "patientId", patientId },
                { "therapistId", therapistId },
                { "reasonCode", reasonCode },
                { "mobileDisconnectBehavior", _mobileDisconnectBehavior },
                { "mode", "restored_created" },
            });
        }

        private void HandleStartCommand(StartGameCommand command)
        {
            if (!TryResolveCommandGame(command?.gameId))
            {
                throw new InvalidOperationException("START_GAME_NO_ACTIVE_GAME");
            }

            if (!TryAuthorizeGameCommandByEntitlement(
                    GameCommandIds.StartGame,
                    _activeGameId,
                    out var entitlementReasonCode))
            {
                throw new InvalidOperationException(entitlementReasonCode);
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
            var currentSessionId = _sessionContext == null ? string.Empty : _sessionContext.SessionId ?? string.Empty;
            var currentSessionState = _sessionContext == null
                ? string.Empty
                : _sessionContext.SessionState.ToString();
            Logger.Info(
                $"[GameRuntime] END_SESSION received: session={currentSessionId}, state={currentSessionState}, activeGame={_activeGameId ?? string.Empty}, reason={command?.reason ?? string.Empty}");

            if (_activeGame != null)
            {
                if (!StopActiveGame(GameContracts.GameStopReason.TherapistStop))
                {
                    throw new InvalidOperationException("END_SESSION_STOP_FAILED");
                }
                return;
            }

            if (!TryTransitionSessionState(GameContracts.SessionLifecycleState.ABORTED_BY_THERAPIST, "END_SESSION"))
            {
                // Session is already terminal (e.g. COMPLETED after auto-complete). Not an error.
                Logger.Warning("[GameRuntime] END_SESSION: session already terminal or context missing.");
                return;
            }

            TrackCriticalRuntimeEvent("session_stop", new Dictionary<string, object>
            {
                { "gameId", _activeGameId ?? string.Empty },
                { "reason", "END_SESSION" },
            });
        }

        private void HandleUpdateConfigCommand(DynamicUpdateConfigCommand command)
        {
            if (!TryResolveCommandGame(command?.gameId))
            {
                throw new InvalidOperationException("UPDATE_CONFIG_NO_ACTIVE_GAME");
            }

            var syntheticStart = new StartGameCommand
            {
                correlationId = command == null ? string.Empty : command.correlationId,
                gameId = command == null ? string.Empty : command.gameId,
                resumeFromSaved = false,
                gameConfigType = command == null ? string.Empty : command.gameConfigType,
                gameConfigVersion = command == null ? 0 : command.gameConfigVersion,
                gameConfigJson = command == null ? string.Empty : command.gameConfigJson,
            };

            if (!TryApplyConfigCommand(
                    syntheticStart,
                    "UPDATE_CONFIG_UNSUPPORTED",
                    "UPDATE_CONFIG_REJECTED",
                    out var reasonCode))
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(reasonCode)
                        ? "UPDATE_CONFIG_REJECTED"
                        : reasonCode);
            }

            TrackCriticalRuntimeEvent("update_config", new Dictionary<string, object>
            {
                { "gameId", _activeGameId ?? string.Empty },
                { "reasonCode", "UPDATE_CONFIG_APPLIED" },
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
            EnsureSimulatedContentStatesInitialized();
            StartSimulatedCatalogSyncRoutine(command == null ? string.Empty : command.correlationId);
        }

        private void HandleInstallGameCommand(InstallGameCommand command)
        {
            EnsureSimulatedContentStatesInitialized();
            var correlationId = command == null ? string.Empty : command.correlationId;

            var requestedGameId = command == null ? string.Empty : command.gameId;
            if (string.IsNullOrWhiteSpace(requestedGameId))
            {
                Logger.Warning("[GameRuntime] INSTALL_GAME ignored: empty gameId.");
                return;
            }

            var normalizedGameId = requestedGameId.Trim();
            if (!TryAuthorizeGameCommandByEntitlement(
                    GameCommandIds.InstallGame,
                    normalizedGameId,
                    out var entitlementReasonCode))
            {
                if (TryGetOrCreateSimulatedContentState(
                        normalizedGameId,
                        command == null ? null : command.targetVersion,
                        out var deniedState))
                {
                    deniedState.runtimeStatus = ContentRuntimeStatusValues.Failed;
                    deniedState.lastError = entitlementReasonCode;
                    deniedState.updatedAtUtc = DateTime.UtcNow;
                    PersistSimulatedContentStates(entitlementReasonCode);
                    _ = PublishGameInstallStatusAsync(
                        deniedState,
                        correlationId,
                        entitlementReasonCode);
                }

                Logger.Warning(
                    $"[GameRuntime] INSTALL_GAME rejected by entitlement gate: game={normalizedGameId}, reason={entitlementReasonCode}");
                return;
            }

            if (!TryGetOrCreateSimulatedContentState(
                    normalizedGameId,
                    command == null ? null : command.targetVersion,
                    out var state))
            {
                return;
            }

            TryStartPackageProbeFromInstallCommand(state.gameId, correlationId, command);
            if (command != null && command.probeOnly)
            {
                Logger.Info($"[GameRuntime] INSTALL_GAME probe-only completed for game={state.gameId}.");
                return;
            }

            state.owned = true;
            state.targetVersion = NormalizeContentVersion(
                command == null ? null : command.targetVersion,
                state.targetVersion);
            var packageUri = command == null ? string.Empty : command.packageUri;
            var previousInstalledVersion = state.installedVersion;
            state.runtimeStatus = ContentRuntimeStatusValues.SyncingManifest;
            state.updateRequired = false;
            state.lastError = string.Empty;
            state.installedPackageUri = NormalizePackageInstallUri(packageUri);
            state.updatedAtUtc = DateTime.UtcNow;
            PersistSimulatedContentStates(InstallReasonCodes.ManifestSyncStarted);
            _ = PublishGameInstallStatusAsync(
                state,
                correlationId,
                InstallReasonCodes.ManifestSyncStarted);

            var shouldSimulateVerifyFailure = ShouldSimulateVerifyFailure(state.targetVersion);
            StartSimulatedInstallRoutine(
                state,
                correlationId,
                packageUri,
                previousInstalledVersion,
                shouldSimulateVerifyFailure);
        }

        private void HandleUninstallGameCommand(UninstallGameCommand command)
        {
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
            if (string.Equals(_activeInstalledContentGameId, normalizedGameId, StringComparison.OrdinalIgnoreCase))
            {
                TryUnloadInstalledContentScene("UNINSTALL_COMPLETED", true);
            }
            RemoveInstalledPackageArtifacts(normalizedGameId);
            state.installedVersion = string.Empty;
            state.runtimeStatus = ContentRuntimeStatusValues.NotInstalled;
            state.updateRequired = false;
            state.lastError = string.Empty;
            ClearInstalledContentMetadata(state);
            state.updatedAtUtc = DateTime.UtcNow;
            PersistSimulatedContentStates("UNINSTALL_COMPLETED");

            _ = PublishGameInstallStatusAsync(
                state,
                command == null ? string.Empty : command.correlationId,
                "UNINSTALL_COMPLETED");
        }

        private void EnsureContentDeliveryStatePathInitialized()
        {
            if (!_persistContentDeliverySimulationState)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_contentDeliveryStatePath))
            {
                return;
            }

            var folder = string.IsNullOrWhiteSpace(_contentDeliveryStateFolder)
                ? "session_resilience"
                : _contentDeliveryStateFolder.Trim();
            var fileName = string.IsNullOrWhiteSpace(_contentDeliveryStateFileName)
                ? "content_delivery_state.json"
                : _contentDeliveryStateFileName.Trim();

            try
            {
                var directory = Path.Combine(Application.persistentDataPath, folder);
                Directory.CreateDirectory(directory);
                _contentDeliveryStatePath = Path.Combine(directory, fileName);
            }
            catch (Exception e)
            {
                _contentDeliveryStatePath = string.Empty;
                Logger.Warning($"[GameRuntime] Failed to initialize content state path: {e.Message}");
            }
        }

        private void ApplyPersistedSimulatedContentStates()
        {
            if (!_persistContentDeliverySimulationState)
            {
                return;
            }

            EnsureContentDeliveryStatePathInitialized();
            if (string.IsNullOrWhiteSpace(_contentDeliveryStatePath) ||
                !File.Exists(_contentDeliveryStatePath))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(_contentDeliveryStatePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                var store = JsonUtility.FromJson<SimulatedContentStateStore>(json);
                if (store == null || store.states == null || store.states.Count == 0)
                {
                    return;
                }

                for (var i = 0; i < store.states.Count; i++)
                {
                    var record = store.states[i];
                    if (record == null || string.IsNullOrWhiteSpace(record.gameId))
                    {
                        continue;
                    }

                    if (!IsCatalogGameId(record.gameId))
                    {
                        continue;
                    }

                    var gameId = record.gameId.Trim();
                    if (!_simulatedContentStateByGameId.TryGetValue(gameId, out var state))
                    {
                        state = new SimulatedContentState
                        {
                            gameId = gameId,
                            owned = false,
                            installedVersion = string.Empty,
                            targetVersion = NormalizeContentVersion(
                                record.targetVersion,
                                _defaultSimulatedContentVersion),
                            updateRequired = false,
                            updateOptional = false,
                            runtimeStatus = ContentRuntimeStatusValues.NotInstalled,
                            lastError = string.Empty,
                            installedPackageUri = string.Empty,
                            installedManifestPath = string.Empty,
                            installedBundlePath = string.Empty,
                            installedBundleUri = string.Empty,
                            installedSceneAssetPath = string.Empty,
                            installedSceneName = string.Empty,
                            installedBundleSha256 = string.Empty,
                            updatedAtUtc = DateTime.UtcNow,
                        };
                        _simulatedContentStateByGameId[gameId] = state;
                    }

                    state.owned = record.owned;
                    state.installedVersion = string.IsNullOrWhiteSpace(record.installedVersion)
                        ? string.Empty
                        : record.installedVersion.Trim();
                    state.targetVersion = NormalizeContentVersion(
                        record.targetVersion,
                        state.targetVersion);
                    var computedUpdateRequired = state.owned &&
                                                !string.IsNullOrWhiteSpace(state.installedVersion) &&
                                                !string.Equals(
                                                    state.installedVersion,
                                                    state.targetVersion,
                                                    StringComparison.OrdinalIgnoreCase);
                    state.updateRequired = record.updateRequired || computedUpdateRequired;
                    state.updateOptional = record.updateOptional;
                    state.lastError = string.IsNullOrWhiteSpace(record.lastError)
                        ? string.Empty
                        : record.lastError.Trim();
                    state.installedPackageUri = string.IsNullOrWhiteSpace(record.installedPackageUri)
                        ? string.Empty
                        : record.installedPackageUri.Trim();
                    state.installedManifestPath = string.IsNullOrWhiteSpace(record.installedManifestPath)
                        ? string.Empty
                        : record.installedManifestPath.Trim();
                    state.installedBundlePath = string.IsNullOrWhiteSpace(record.installedBundlePath)
                        ? string.Empty
                        : record.installedBundlePath.Trim();
                    state.installedBundleUri = string.IsNullOrWhiteSpace(record.installedBundleUri)
                        ? string.Empty
                        : record.installedBundleUri.Trim();
                    state.installedSceneAssetPath = string.IsNullOrWhiteSpace(record.installedSceneAssetPath)
                        ? string.Empty
                        : record.installedSceneAssetPath.Trim();
                    state.installedSceneName = string.IsNullOrWhiteSpace(record.installedSceneName)
                        ? string.Empty
                        : record.installedSceneName.Trim();
                    state.installedBundleSha256 = string.IsNullOrWhiteSpace(record.installedBundleSha256)
                        ? string.Empty
                        : record.installedBundleSha256.Trim();
                    state.runtimeStatus = string.IsNullOrWhiteSpace(record.runtimeStatus)
                        ? ResolveRuntimeStatusForContentState(
                            state.owned,
                            state.installedVersion,
                            state.updateRequired)
                        : record.runtimeStatus.Trim();
                    state.updatedAtUtc = ParseContentStateUpdatedAt(record.updatedAtUtc);
                }
            }
            catch (Exception e)
            {
                Logger.Warning($"[GameRuntime] Failed to load persisted content state: {e.Message}");
            }
        }

        private void PersistSimulatedContentStates(string reasonCode)
        {
            if (!_persistContentDeliverySimulationState)
            {
                return;
            }

            EnsureContentDeliveryStatePathInitialized();
            if (string.IsNullOrWhiteSpace(_contentDeliveryStatePath))
            {
                return;
            }

            try
            {
                var snapshot = new SimulatedContentStateStore
                {
                    schema = "THERAPLY_CONTENT_DELIVERY_STATE",
                    schemaVersion = "2026-02-25",
                    generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                };

                var keys = new List<string>(_simulatedContentStateByGameId.Keys);
                keys.Sort(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < keys.Count; i++)
                {
                    var gameId = keys[i];
                    if (!_simulatedContentStateByGameId.TryGetValue(gameId, out var state) || state == null)
                    {
                        continue;
                    }

                    snapshot.states.Add(new SimulatedContentStateRecord
                    {
                        gameId = gameId,
                        owned = state.owned,
                        installedVersion = state.installedVersion ?? string.Empty,
                        targetVersion = state.targetVersion ?? string.Empty,
                        updateRequired = state.updateRequired,
                        updateOptional = state.updateOptional,
                        runtimeStatus = state.runtimeStatus ?? string.Empty,
                        lastError = state.lastError ?? string.Empty,
                        installedPackageUri = state.installedPackageUri ?? string.Empty,
                        installedManifestPath = state.installedManifestPath ?? string.Empty,
                        installedBundlePath = state.installedBundlePath ?? string.Empty,
                        installedBundleUri = state.installedBundleUri ?? string.Empty,
                        installedSceneAssetPath = state.installedSceneAssetPath ?? string.Empty,
                        installedSceneName = state.installedSceneName ?? string.Empty,
                        installedBundleSha256 = state.installedBundleSha256 ?? string.Empty,
                        updatedAtUtc = (state.updatedAtUtc == DateTime.MinValue
                                ? DateTime.UtcNow
                                : state.updatedAtUtc.ToUniversalTime())
                            .ToString("O", CultureInfo.InvariantCulture),
                    });
                }

                var json = JsonUtility.ToJson(snapshot, true);
                File.WriteAllText(_contentDeliveryStatePath, json);
            }
            catch (Exception e)
            {
                Logger.Warning(
                    $"[GameRuntime] Failed to persist content state (reason={reasonCode ?? string.Empty}): {e.Message}");
            }
        }

        private static DateTime ParseContentStateUpdatedAt(string value)
        {
            if (DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsedUtc))
            {
                return parsedUtc.Kind == DateTimeKind.Utc
                    ? parsedUtc
                    : parsedUtc.ToUniversalTime();
            }

            return DateTime.UtcNow;
        }

        private void EnsureSimulatedContentStatesInitialized()
        {
            if (_simulatedContentStateByGameId.Count > 0)
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
                var installedVersion = string.Empty;
                var updateRequired = false;

                var state = new SimulatedContentState
                {
                    gameId = gameId,
                    owned = false,
                    installedVersion = installedVersion,
                    targetVersion = targetVersion,
                    updateRequired = updateRequired,
                    updateOptional = entry.updateOptional,
                    runtimeStatus = ResolveRuntimeStatusForContentState(
                        entry.owned,
                        installedVersion,
                        updateRequired),
                    lastError = string.Empty,
                    installedPackageUri = string.Empty,
                    installedManifestPath = string.Empty,
                    installedBundlePath = string.Empty,
                    installedBundleUri = string.Empty,
                    installedSceneAssetPath = string.Empty,
                    installedSceneName = string.Empty,
                    installedBundleSha256 = string.Empty,
                    updatedAtUtc = DateTime.UtcNow,
                };

                _simulatedContentStateByGameId[gameId] = state;
            }

            if (_allowDefaultGameFallback &&
                _simulatedContentStateByGameId.Count == 0 &&
                !string.IsNullOrWhiteSpace(_defaultGameId))
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
                    installedPackageUri = string.Empty,
                    installedManifestPath = string.Empty,
                    installedBundlePath = string.Empty,
                    installedBundleUri = string.Empty,
                    installedSceneAssetPath = string.Empty,
                    installedSceneName = string.Empty,
                    installedBundleSha256 = string.Empty,
                    updatedAtUtc = DateTime.UtcNow,
                };
            }

            ApplyPersistedSimulatedContentStates();
            if (ReconcilePersistedSimulatedContentStates())
            {
                PersistSimulatedContentStates(InstallReasonCodes.RecoveredMissingArtifacts);
            }
        }

        private bool ReconcilePersistedSimulatedContentStates()
        {
            if (_simulatedContentStateByGameId.Count == 0)
            {
                return false;
            }

            var hasChanges = false;
            var nowUtc = DateTime.UtcNow;
            foreach (var pair in _simulatedContentStateByGameId)
            {
                var state = pair.Value;
                if (state == null)
                {
                    continue;
                }

                if (IsInstallLifecycleInProgressStatus(state.runtimeStatus))
                {
                    state.runtimeStatus = ResolveRuntimeStatusForContentState(
                        state.owned,
                        state.installedVersion,
                        state.updateRequired);
                    state.lastError = InstallReasonCodes.RecoveredStaleProgress;
                    state.updatedAtUtc = nowUtc;
                    hasChanges = true;
                }

                var installedBundlePath = string.IsNullOrWhiteSpace(state.installedBundlePath)
                    ? string.Empty
                    : state.installedBundlePath.Trim();
                var hasInstalledMarkers =
                    !string.IsNullOrWhiteSpace(state.installedVersion) ||
                    !string.IsNullOrWhiteSpace(state.installedManifestPath) ||
                    !string.IsNullOrWhiteSpace(installedBundlePath);
                if (!hasInstalledMarkers)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(installedBundlePath) && File.Exists(installedBundlePath))
                {
                    continue;
                }

                state.installedVersion = string.Empty;
                state.updateRequired = false;
                state.updateOptional = false;
                state.runtimeStatus = ContentRuntimeStatusValues.NotInstalled;
                state.lastError = InstallReasonCodes.RecoveredMissingArtifacts;
                ClearInstalledContentMetadata(state);
                state.updatedAtUtc = nowUtc;
                hasChanges = true;
            }

            return hasChanges;
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

            if (!IsCatalogGameId(gameId))
            {
                Logger.Warning(
                    $"[GameRuntime] Ignoring content state request for game outside active catalog: {gameId}");
                return false;
            }

            if (_simulatedContentStateByGameId.TryGetValue(gameId, out state))
            {
                if (!string.IsNullOrWhiteSpace(requestedTargetVersion))
                {
                    state.targetVersion = NormalizeContentVersion(requestedTargetVersion, state.targetVersion);
                    state.updatedAtUtc = DateTime.UtcNow;
                    PersistSimulatedContentStates("CONTENT_STATE_TARGET_VERSION_UPDATED");
                }

                return true;
            }

            state = new SimulatedContentState
            {
                gameId = gameId,
                owned = false,
                installedVersion = string.Empty,
                targetVersion = NormalizeContentVersion(
                    requestedTargetVersion,
                    _defaultSimulatedContentVersion),
                updateRequired = false,
                updateOptional = false,
                runtimeStatus = ContentRuntimeStatusValues.NotInstalled,
                lastError = string.Empty,
                installedPackageUri = string.Empty,
                installedManifestPath = string.Empty,
                installedBundlePath = string.Empty,
                installedBundleUri = string.Empty,
                installedSceneAssetPath = string.Empty,
                installedSceneName = string.Empty,
                installedBundleSha256 = string.Empty,
                updatedAtUtc = DateTime.UtcNow,
            };
            _simulatedContentStateByGameId[gameId] = state;
            PersistSimulatedContentStates("CONTENT_STATE_CREATED");
            return true;
        }

        private bool IsCatalogGameId(string gameId)
        {
            var normalized = string.IsNullOrWhiteSpace(gameId)
                ? string.Empty
                : gameId.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            if (_simulatedContentCatalog == null || _simulatedContentCatalog.Count == 0)
            {
                return true;
            }

            for (var i = 0; i < _simulatedContentCatalog.Count; i++)
            {
                var entry = _simulatedContentCatalog[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.gameId))
                {
                    continue;
                }

                if (string.Equals(entry.gameId.Trim(), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void StartSimulatedCatalogSyncRoutine(string correlationId)
        {
            EnsureSimulatedContentStatesInitialized();
            StopSimulatedCatalogSyncRoutine();
            _simulatedCatalogSyncRoutine = StartCoroutine(SimulateCatalogSyncRoutine(correlationId));
        }

        private IEnumerator SimulateCatalogSyncRoutine(string correlationId)
        {
            EnsureSimulatedContentStatesInitialized();
            PublishSimulatedContentCatalogSnapshot(correlationId, "SYNC_CATALOG_COMPLETED");
            _simulatedCatalogSyncRoutine = null;
            yield break;
        }

        private bool ShouldSimulateVerifyFailure(string targetVersion)
        {
            if (!_simulateVerifyFailureWhenTargetVersionContainsToken ||
                string.IsNullOrWhiteSpace(targetVersion))
            {
                return false;
            }

            var token = string.IsNullOrWhiteSpace(_simulatedVerifyFailureToken)
                ? "verify_fail"
                : _simulatedVerifyFailureToken.Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            return targetVersion.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void StartSimulatedInstallRoutine(
            SimulatedContentState state,
            string correlationId,
            string packageUri,
            string previousInstalledVersion,
            bool shouldSimulateVerifyFailure)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.gameId))
            {
                return;
            }

            StopSimulatedInstallRoutine(state.gameId);
            var routine = StartCoroutine(
                SimulateInstallRoutine(
                    state,
                    correlationId,
                    packageUri,
                    previousInstalledVersion,
                    shouldSimulateVerifyFailure));
            _simulatedInstallRoutineByGameId[state.gameId] = routine;
        }

        private IEnumerator SimulateInstallRoutine(
            SimulatedContentState state,
            string correlationId,
            string packageUri,
            string previousInstalledVersion,
            bool shouldSimulateVerifyFailure)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.gameId))
            {
                yield break;
            }

            var gameId = state.gameId;
            var previousManifestPath = state.installedManifestPath ?? string.Empty;
            var previousBundlePath = state.installedBundlePath ?? string.Empty;
            var previousBundleUri = state.installedBundleUri ?? string.Empty;
            var previousSceneAssetPath = state.installedSceneAssetPath ?? string.Empty;
            var previousSceneName = state.installedSceneName ?? string.Empty;
            var previousBundleSha256 = state.installedBundleSha256 ?? string.Empty;
            yield return new WaitForSeconds(Mathf.Max(0.05f, _simulatedManifestSyncDurationSeconds));

            if (!_simulatedContentStateByGameId.ContainsKey(gameId))
            {
                CompleteSimulatedInstallRoutine(gameId);
                yield break;
            }

            state.runtimeStatus = ContentRuntimeStatusValues.Downloading;
            state.lastError = string.Empty;
            state.updatedAtUtc = DateTime.UtcNow;
            PersistSimulatedContentStates(InstallReasonCodes.DownloadStarted);
            _ = PublishGameInstallStatusAsync(
                state,
                correlationId,
                InstallReasonCodes.DownloadStarted);

            if (!_simulatedContentStateByGameId.ContainsKey(gameId))
            {
                CompleteSimulatedInstallRoutine(gameId);
                yield break;
            }

            var normalizedPackageUri = NormalizePackageInstallUri(packageUri);
            if (string.IsNullOrWhiteSpace(normalizedPackageUri))
            {
                MarkInstallFailed(
                    state,
                    correlationId,
                    InstallReasonCodes.PackageUriMissing,
                    previousInstalledVersion,
                    previousManifestPath,
                    previousBundlePath,
                    previousBundleUri,
                    previousSceneAssetPath,
                    previousSceneName,
                    previousBundleSha256);
                CompleteSimulatedInstallRoutine(gameId);
                yield break;
            }

            if (!Uri.TryCreate(normalizedPackageUri, UriKind.Absolute, out var parsedUri) ||
                (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
            {
                MarkInstallFailed(
                    state,
                    correlationId,
                    InstallReasonCodes.PackageUriInvalid,
                    previousInstalledVersion,
                    previousManifestPath,
                    previousBundlePath,
                    previousBundleUri,
                    previousSceneAssetPath,
                    previousSceneName,
                    previousBundleSha256);
                CompleteSimulatedInstallRoutine(gameId);
                yield break;
            }

            byte[] manifestBytes = null;
            var timeoutSeconds = Mathf.Clamp(_packageInstallTimeoutSeconds, 3, 120);
            using (var request = UnityWebRequest.Get(parsedUri.AbsoluteUri))
            {
                request.timeout = timeoutSeconds;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success ||
                    request.responseCode < 200 ||
                    request.responseCode >= 400)
                {
                    MarkInstallFailed(
                        state,
                        correlationId,
                        ResolveInstallDownloadFailureReasonCode(request),
                        previousInstalledVersion,
                        previousManifestPath,
                        previousBundlePath,
                        previousBundleUri,
                        previousSceneAssetPath,
                        previousSceneName,
                        previousBundleSha256);
                    CompleteSimulatedInstallRoutine(gameId);
                    yield break;
                }

                manifestBytes = request.downloadHandler == null
                    ? null
                    : request.downloadHandler.data;
            }

            if (manifestBytes == null || manifestBytes.Length == 0)
            {
                MarkInstallFailed(
                    state,
                    correlationId,
                    InstallReasonCodes.PackageEmpty,
                    previousInstalledVersion,
                    previousManifestPath,
                    previousBundlePath,
                    previousBundleUri,
                    previousSceneAssetPath,
                    previousSceneName,
                    previousBundleSha256);
                CompleteSimulatedInstallRoutine(gameId);
                yield break;
            }

            var manifestJson = Encoding.UTF8.GetString(manifestBytes);
            if (!TryParseInstalledPackageManifest(
                    gameId,
                    manifestJson,
                    out var packageManifest,
                    out var manifestReasonCode))
            {
                MarkInstallFailed(
                    state,
                    correlationId,
                    manifestReasonCode,
                    previousInstalledVersion,
                    previousManifestPath,
                    previousBundlePath,
                    previousBundleUri,
                    previousSceneAssetPath,
                    previousSceneName,
                    previousBundleSha256);
                CompleteSimulatedInstallRoutine(gameId);
                yield break;
            }

            if (!TryResolveBundleInstallUri(
                    packageManifest,
                    parsedUri,
                    out var bundleInstallUri,
                    out var bundleUriReasonCode))
            {
                MarkInstallFailed(
                    state,
                    correlationId,
                    bundleUriReasonCode,
                    previousInstalledVersion,
                    previousManifestPath,
                    previousBundlePath,
                    previousBundleUri,
                    previousSceneAssetPath,
                    previousSceneName,
                    previousBundleSha256);
                CompleteSimulatedInstallRoutine(gameId);
                yield break;
            }

            state.runtimeStatus = ContentRuntimeStatusValues.Downloading;
            state.lastError = string.Empty;
            state.updatedAtUtc = DateTime.UtcNow;
            PersistSimulatedContentStates(InstallReasonCodes.BundleDownloadStarted);
            _ = PublishGameInstallStatusAsync(
                state,
                correlationId,
                InstallReasonCodes.BundleDownloadStarted);

            byte[] bundleBytes = null;
            using (var bundleRequest = UnityWebRequest.Get(bundleInstallUri.AbsoluteUri))
            {
                bundleRequest.timeout = timeoutSeconds;
                yield return bundleRequest.SendWebRequest();

                if (bundleRequest.result != UnityWebRequest.Result.Success ||
                    bundleRequest.responseCode < 200 ||
                    bundleRequest.responseCode >= 400)
                {
                    MarkInstallFailed(
                        state,
                        correlationId,
                        ResolveInstallDownloadFailureReasonCode(bundleRequest),
                        previousInstalledVersion,
                        previousManifestPath,
                        previousBundlePath,
                        previousBundleUri,
                        previousSceneAssetPath,
                        previousSceneName,
                        previousBundleSha256);
                    CompleteSimulatedInstallRoutine(gameId);
                    yield break;
                }

                bundleBytes = bundleRequest.downloadHandler == null
                    ? null
                    : bundleRequest.downloadHandler.data;
            }

            yield return new WaitForSeconds(Mathf.Max(0.05f, _simulatedDownloadDurationSeconds));

            if (!_simulatedContentStateByGameId.ContainsKey(gameId))
            {
                CompleteSimulatedInstallRoutine(gameId);
                yield break;
            }

            if (bundleBytes == null || bundleBytes.Length == 0)
            {
                MarkInstallFailed(
                    state,
                    correlationId,
                    InstallReasonCodes.BundleEmpty,
                    previousInstalledVersion,
                    previousManifestPath,
                    previousBundlePath,
                    previousBundleUri,
                    previousSceneAssetPath,
                    previousSceneName,
                    previousBundleSha256);
                CompleteSimulatedInstallRoutine(gameId);
                yield break;
            }

            state.runtimeStatus = ContentRuntimeStatusValues.Verifying;
            state.lastError = string.Empty;
            state.updatedAtUtc = DateTime.UtcNow;
            PersistSimulatedContentStates(InstallReasonCodes.VerifyStarted);
            _ = PublishGameInstallStatusAsync(
                state,
                correlationId,
                InstallReasonCodes.VerifyStarted);
            yield return new WaitForSeconds(Mathf.Max(0.05f, _simulatedVerifyDurationSeconds));

            if (!_simulatedContentStateByGameId.ContainsKey(gameId))
            {
                CompleteSimulatedInstallRoutine(gameId);
                yield break;
            }

            if (shouldSimulateVerifyFailure)
            {
                MarkInstallFailed(
                    state,
                    correlationId,
                    InstallReasonCodes.VerifyFailed,
                    previousInstalledVersion,
                    previousManifestPath,
                    previousBundlePath,
                    previousBundleUri,
                    previousSceneAssetPath,
                    previousSceneName,
                    previousBundleSha256);
                CompleteSimulatedInstallRoutine(gameId);
                yield break;
            }

            var manifestBundleSha = packageManifest.assetBundle == null
                ? string.Empty
                : (packageManifest.assetBundle.bundleSha256 ?? string.Empty).Trim();
            var downloadedBundleSha = ComputeSha256(bundleBytes);
            if (!string.IsNullOrWhiteSpace(manifestBundleSha) &&
                !string.Equals(manifestBundleSha, downloadedBundleSha, StringComparison.OrdinalIgnoreCase))
            {
                MarkInstallFailed(
                    state,
                    correlationId,
                    InstallReasonCodes.BundleChecksumMismatch,
                    previousInstalledVersion,
                    previousManifestPath,
                    previousBundlePath,
                    previousBundleUri,
                    previousSceneAssetPath,
                    previousSceneName,
                    previousBundleSha256);
                CompleteSimulatedInstallRoutine(gameId);
                yield break;
            }

            if (!TryPersistInstalledPackage(
                    gameId,
                    state.targetVersion,
                    parsedUri,
                    manifestBytes,
                    out var installedManifestPath,
                    out var manifestPersistReasonCode))
            {
                MarkInstallFailed(
                    state,
                    correlationId,
                    manifestPersistReasonCode,
                    previousInstalledVersion,
                    previousManifestPath,
                    previousBundlePath,
                    previousBundleUri,
                    previousSceneAssetPath,
                    previousSceneName,
                    previousBundleSha256);
                CompleteSimulatedInstallRoutine(gameId);
                yield break;
            }

            if (!TryPersistInstalledPackage(
                    gameId,
                    state.targetVersion,
                    bundleInstallUri,
                    bundleBytes,
                    out var installedBundlePath,
                    out var bundlePersistReasonCode))
            {
                MarkInstallFailed(
                    state,
                    correlationId,
                    bundlePersistReasonCode,
                    previousInstalledVersion,
                    previousManifestPath,
                    previousBundlePath,
                    previousBundleUri,
                    previousSceneAssetPath,
                    previousSceneName,
                    previousBundleSha256);
                CompleteSimulatedInstallRoutine(gameId);
                yield break;
            }

            state.runtimeStatus = ContentRuntimeStatusValues.Activating;
            state.lastError = string.Empty;
            state.updatedAtUtc = DateTime.UtcNow;
            PersistSimulatedContentStates(InstallReasonCodes.ActivationStarted);
            _ = PublishGameInstallStatusAsync(
                state,
                correlationId,
                InstallReasonCodes.ActivationStarted);
            yield return new WaitForSeconds(Mathf.Max(0.05f, _simulatedActivationDurationSeconds));

            if (!_simulatedContentStateByGameId.ContainsKey(gameId))
            {
                CompleteSimulatedInstallRoutine(gameId);
                yield break;
            }

            var installedSceneAssetPath = packageManifest.assetBundle == null
                ? string.Empty
                : NormalizePackageInstallUri(packageManifest.assetBundle.sceneAssetPath);
            var installedSceneName = packageManifest.assetBundle == null
                ? string.Empty
                : NormalizePackageInstallUri(packageManifest.assetBundle.sceneName);
            state.installedVersion = NormalizeContentVersion(
                state.targetVersion,
                _defaultSimulatedContentVersion);
            state.updateRequired = false;
            state.runtimeStatus = ContentRuntimeStatusValues.Ready;
            state.lastError = string.Empty;
            state.installedPackageUri = normalizedPackageUri;
            state.installedManifestPath = installedManifestPath;
            state.installedBundlePath = installedBundlePath;
            state.installedBundleUri = bundleInstallUri.AbsoluteUri;
            state.installedSceneAssetPath = installedSceneAssetPath;
            state.installedSceneName = installedSceneName;
            state.installedBundleSha256 = downloadedBundleSha;
            state.updatedAtUtc = DateTime.UtcNow;
            PersistSimulatedContentStates(InstallReasonCodes.Completed);
            _ = PublishGameInstallStatusAsync(
                state,
                correlationId,
                InstallReasonCodes.Completed);

            TrackCriticalRuntimeEvent("content_install_completed", new Dictionary<string, object>
            {
                { "gameId", gameId },
                { "targetVersion", state.targetVersion ?? string.Empty },
                { "packageUri", normalizedPackageUri },
                { "installedManifestPath", installedManifestPath },
                { "installedBundlePath", installedBundlePath },
                { "bundleUri", bundleInstallUri.AbsoluteUri },
                { "bundleSha256", downloadedBundleSha },
                { "sceneAssetPath", installedSceneAssetPath },
                { "sceneName", installedSceneName },
                { "reasonCode", InstallReasonCodes.Completed },
            });
            CompleteSimulatedInstallRoutine(gameId);
        }

        private void MarkInstallFailed(
            SimulatedContentState state,
            string correlationId,
            string reasonCode,
            string previousInstalledVersion,
            string previousManifestPath,
            string previousBundlePath,
            string previousBundleUri,
            string previousSceneAssetPath,
            string previousSceneName,
            string previousBundleSha256)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.gameId))
            {
                return;
            }

            state.installedVersion = string.IsNullOrWhiteSpace(previousInstalledVersion)
                ? string.Empty
                : previousInstalledVersion.Trim();
            state.updateRequired = state.owned &&
                                  !string.IsNullOrWhiteSpace(state.installedVersion) &&
                                  !string.Equals(
                                      state.installedVersion,
                                      state.targetVersion,
                                      StringComparison.OrdinalIgnoreCase);
            state.installedManifestPath = string.IsNullOrWhiteSpace(previousManifestPath)
                ? string.Empty
                : previousManifestPath.Trim();
            state.installedBundlePath = string.IsNullOrWhiteSpace(previousBundlePath)
                ? string.Empty
                : previousBundlePath.Trim();
            state.installedBundleUri = string.IsNullOrWhiteSpace(previousBundleUri)
                ? string.Empty
                : previousBundleUri.Trim();
            state.installedSceneAssetPath = string.IsNullOrWhiteSpace(previousSceneAssetPath)
                ? string.Empty
                : previousSceneAssetPath.Trim();
            state.installedSceneName = string.IsNullOrWhiteSpace(previousSceneName)
                ? string.Empty
                : previousSceneName.Trim();
            state.installedBundleSha256 = string.IsNullOrWhiteSpace(previousBundleSha256)
                ? string.Empty
                : previousBundleSha256.Trim();
            if (string.IsNullOrWhiteSpace(previousInstalledVersion))
            {
                state.installedPackageUri = string.Empty;
            }
            state.runtimeStatus = ContentRuntimeStatusValues.Failed;
            state.lastError = string.IsNullOrWhiteSpace(reasonCode)
                ? InstallReasonCodes.DownloadFailed
                : reasonCode.Trim();
            state.updatedAtUtc = DateTime.UtcNow;
            PersistSimulatedContentStates(state.lastError);
            _ = PublishGameInstallStatusAsync(state, correlationId, state.lastError);
        }

        private bool TryParseInstalledPackageManifest(
            string gameId,
            string manifestJson,
            out InstalledPackageManifestRecord manifest,
            out string reasonCode)
        {
            manifest = null;
            reasonCode = string.Empty;

            if (string.IsNullOrWhiteSpace(manifestJson))
            {
                reasonCode = InstallReasonCodes.ManifestParseFailed;
                return false;
            }

            try
            {
                manifest = JsonUtility.FromJson<InstalledPackageManifestRecord>(manifestJson);
            }
            catch (Exception e)
            {
                Logger.Warning($"[GameRuntime] Failed to parse package manifest: {e.Message}");
                reasonCode = InstallReasonCodes.ManifestParseFailed;
                return false;
            }

            if (manifest == null || string.IsNullOrWhiteSpace(manifest.schema))
            {
                reasonCode = InstallReasonCodes.ManifestParseFailed;
                return false;
            }

            var schema = manifest.schema.Trim();
            if (!string.Equals(schema, "THERAPLY_ASSET_BUNDLE_GAME_PACKAGE", StringComparison.OrdinalIgnoreCase))
            {
                reasonCode = InstallReasonCodes.ManifestSchemaUnsupported;
                return false;
            }

            var manifestGameId = string.IsNullOrWhiteSpace(manifest.packageId)
                ? string.Empty
                : manifest.packageId.Trim();
            var normalizedGameId = string.IsNullOrWhiteSpace(gameId)
                ? string.Empty
                : gameId.Trim();
            if (!string.IsNullOrWhiteSpace(manifestGameId) &&
                !string.Equals(manifestGameId, normalizedGameId, StringComparison.OrdinalIgnoreCase))
            {
                reasonCode = InstallReasonCodes.ManifestGameMismatch;
                return false;
            }

            if (manifest.assetBundle == null)
            {
                reasonCode = InstallReasonCodes.BundleUriMissing;
                return false;
            }

            reasonCode = string.Empty;
            return true;
        }

        private bool TryResolveBundleInstallUri(
            InstalledPackageManifestRecord manifest,
            Uri fallbackManifestUri,
            out Uri bundleUri,
            out string reasonCode)
        {
            bundleUri = null;
            reasonCode = string.Empty;

            var bundleUriRaw = manifest == null || manifest.assetBundle == null
                ? string.Empty
                : NormalizePackageInstallUri(manifest.assetBundle.bundleUri);
            if (string.IsNullOrWhiteSpace(bundleUriRaw) &&
                manifest != null &&
                manifest.assetBundle != null &&
                !string.IsNullOrWhiteSpace(manifest.assetBundle.bundleFileName))
            {
                bundleUriRaw = manifest.assetBundle.bundleFileName.Trim();
            }
            if (string.IsNullOrWhiteSpace(bundleUriRaw))
            {
                reasonCode = InstallReasonCodes.BundleUriMissing;
                return false;
            }

            if (!Uri.TryCreate(bundleUriRaw, UriKind.Absolute, out bundleUri))
            {
                if (fallbackManifestUri != null &&
                    Uri.TryCreate(fallbackManifestUri, bundleUriRaw, out bundleUri))
                {
                    // Relative URI resolved from package manifest URI.
                }
            }

            if (bundleUri == null ||
                (bundleUri.Scheme != Uri.UriSchemeHttp && bundleUri.Scheme != Uri.UriSchemeHttps))
            {
                reasonCode = InstallReasonCodes.BundleUriInvalid;
                bundleUri = null;
                return false;
            }

            reasonCode = string.Empty;
            return true;
        }

        private static string ComputeSha256(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
            {
                return string.Empty;
            }

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(payload);
                var builder = new StringBuilder(hash.Length * 2);
                for (var i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static string ResolveInstallDownloadFailureReasonCode(UnityWebRequest request)
        {
            if (request == null)
            {
                return InstallReasonCodes.DownloadFailed;
            }

            if (request.result == UnityWebRequest.Result.ConnectionError)
            {
                var networkError = request.error ?? string.Empty;
                if (networkError.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return InstallReasonCodes.DownloadTimeout;
                }

                return InstallReasonCodes.DownloadConnectionError;
            }

            if (request.result == UnityWebRequest.Result.ProtocolError)
            {
                if (request.responseCode == 408 || request.responseCode == 504)
                {
                    return InstallReasonCodes.DownloadTimeout;
                }

                return InstallReasonCodes.DownloadHttpError;
            }

            return InstallReasonCodes.DownloadFailed;
        }

        private bool TryPersistInstalledPackage(
            string gameId,
            string targetVersion,
            Uri packageUri,
            byte[] downloadedBytes,
            out string installedPath,
            out string reasonCode)
        {
            installedPath = string.Empty;
            reasonCode = string.Empty;

            if (downloadedBytes == null || downloadedBytes.Length == 0)
            {
                reasonCode = InstallReasonCodes.PackageEmpty;
                return false;
            }

            try
            {
                installedPath = ResolvePackageInstallPath(gameId, targetVersion, packageUri);
                if (string.IsNullOrWhiteSpace(installedPath))
                {
                    reasonCode = InstallReasonCodes.StorageWriteFailed;
                    return false;
                }

                var directory = Path.GetDirectoryName(installedPath);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    reasonCode = InstallReasonCodes.StorageWriteFailed;
                    return false;
                }

                Directory.CreateDirectory(directory);
                File.WriteAllBytes(installedPath, downloadedBytes);
                return true;
            }
            catch (Exception e)
            {
                Logger.Warning(
                    $"[GameRuntime] INSTALL_GAME persist failed: game={gameId ?? string.Empty}, reason={e.Message}");
                reasonCode = InstallReasonCodes.StorageWriteFailed;
                return false;
            }
        }

        private void RemoveInstalledPackageArtifacts(string gameId)
        {
            var normalizedGameId = string.IsNullOrWhiteSpace(gameId)
                ? string.Empty
                : gameId.Trim();
            if (string.IsNullOrWhiteSpace(normalizedGameId))
            {
                return;
            }

            var installRoot = string.IsNullOrWhiteSpace(_packageInstallFolder)
                ? "content_packages"
                : _packageInstallFolder.Trim();
            var gameFolder = Path.Combine(
                Application.persistentDataPath,
                installRoot,
                SanitizePathSegment(normalizedGameId, "unknown_game"));

            if (!Directory.Exists(gameFolder))
            {
                return;
            }

            try
            {
                Directory.Delete(gameFolder, true);
            }
            catch (Exception e)
            {
                Logger.Warning(
                    $"[GameRuntime] Failed to remove installed package artifacts for {normalizedGameId}: {e.Message}");
            }
        }

        private static void ClearInstalledContentMetadata(SimulatedContentState state)
        {
            if (state == null)
            {
                return;
            }

            state.installedPackageUri = string.Empty;
            state.installedManifestPath = string.Empty;
            state.installedBundlePath = string.Empty;
            state.installedBundleUri = string.Empty;
            state.installedSceneAssetPath = string.Empty;
            state.installedSceneName = string.Empty;
            state.installedBundleSha256 = string.Empty;
        }

        private bool TryLoadInstalledContentSceneForGame(
            string gameId,
            out string reasonCode,
            out bool sceneLoadPending)
        {
            sceneLoadPending = false;
            reasonCode = "START_CONTENT_SCENE_SKIPPED";
            if (!_loadInstalledContentSceneOnStart)
            {
                return true;
            }

            var normalizedGameId = string.IsNullOrWhiteSpace(gameId)
                ? string.Empty
                : gameId.Trim();
            if (string.IsNullOrWhiteSpace(normalizedGameId))
            {
                reasonCode = "START_CONTENT_GAME_ID_REQUIRED";
                return true;
            }

            EnsureSimulatedContentStatesInitialized();
            if (!_simulatedContentStateByGameId.TryGetValue(normalizedGameId, out var state) ||
                state == null)
            {
                return true;
            }

            var installedBundlePath = string.IsNullOrWhiteSpace(state.installedBundlePath)
                ? string.Empty
                : state.installedBundlePath.Trim();
            if (string.IsNullOrWhiteSpace(installedBundlePath))
            {
                return true;
            }

            if (!File.Exists(installedBundlePath))
            {
                reasonCode = InstallReasonCodes.StartBundleLoadFailed;
                return false;
            }

            var preferredSceneAssetPath = string.IsNullOrWhiteSpace(state.installedSceneAssetPath)
                ? string.Empty
                : state.installedSceneAssetPath.Trim();
            var preferredSceneName = string.IsNullOrWhiteSpace(state.installedSceneName)
                ? string.Empty
                : state.installedSceneName.Trim();

            var activeSceneByPath = string.IsNullOrWhiteSpace(_activeInstalledContentScenePath)
                ? new Scene()
                : SceneManager.GetSceneByPath(_activeInstalledContentScenePath);
            if (!string.IsNullOrWhiteSpace(_activeInstalledContentGameId) &&
                string.Equals(_activeInstalledContentGameId, normalizedGameId, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(_activeInstalledContentBundlePath) &&
                string.Equals(_activeInstalledContentBundlePath, installedBundlePath, StringComparison.OrdinalIgnoreCase) &&
                activeSceneByPath.IsValid() &&
                activeSceneByPath.isLoaded)
            {
                reasonCode = "START_CONTENT_SCENE_ALREADY_LOADED";
                return true;
            }

            TryUnloadInstalledContentScene("START_CONTENT_REPLACE", false);

            var bundle = AssetBundle.LoadFromFile(installedBundlePath);
            if (bundle == null)
            {
                reasonCode = InstallReasonCodes.StartBundleLoadFailed;
                return false;
            }

            if (!TryResolveInstalledScenePathFromBundle(
                    bundle,
                    preferredSceneAssetPath,
                    preferredSceneName,
                    out var resolvedScenePath))
            {
                bundle.Unload(true);
                reasonCode = InstallReasonCodes.StartSceneMissing;
                return false;
            }

            var loadOperation = SceneManager.LoadSceneAsync(resolvedScenePath, LoadSceneMode.Additive);
            if (loadOperation == null)
            {
                bundle.Unload(true);
                reasonCode = InstallReasonCodes.StartSceneLoadFailed;
                return false;
            }

            _activeInstalledContentBundle = bundle;
            _activeInstalledContentScenePath = resolvedScenePath;
            _activeInstalledContentGameId = normalizedGameId;
            _activeInstalledContentBundlePath = installedBundlePath;
            sceneLoadPending = !loadOperation.isDone;
            loadOperation.completed += _ =>
            {
                TrackCriticalRuntimeEvent("content_scene_load_completed", new Dictionary<string, object>
                {
                    { "gameId", normalizedGameId },
                    { "scenePath", resolvedScenePath },
                    { "bundlePath", installedBundlePath },
                    { "reasonCode", "START_CONTENT_SCENE_LOAD_COMPLETED" },
                });
            };

            TrackCriticalRuntimeEvent("content_scene_load_requested", new Dictionary<string, object>
            {
                { "gameId", normalizedGameId },
                { "scenePath", resolvedScenePath },
                { "bundlePath", installedBundlePath },
                { "reasonCode", "START_CONTENT_SCENE_LOAD_REQUESTED" },
            });
            reasonCode = sceneLoadPending
                ? "START_CONTENT_SCENE_LOAD_REQUESTED"
                : "START_CONTENT_SCENE_LOAD_COMPLETED";
            return true;
        }

        private static bool TryResolveInstalledScenePathFromBundle(
            AssetBundle bundle,
            string preferredSceneAssetPath,
            string preferredSceneName,
            out string resolvedScenePath)
        {
            resolvedScenePath = string.Empty;
            if (bundle == null)
            {
                return false;
            }

            var scenePaths = bundle.GetAllScenePaths();
            if (scenePaths == null || scenePaths.Length == 0)
            {
                return false;
            }

            var preferredPath = string.IsNullOrWhiteSpace(preferredSceneAssetPath)
                ? string.Empty
                : preferredSceneAssetPath.Trim();
            if (!string.IsNullOrWhiteSpace(preferredPath))
            {
                for (var i = 0; i < scenePaths.Length; i++)
                {
                    if (string.Equals(scenePaths[i], preferredPath, StringComparison.OrdinalIgnoreCase))
                    {
                        resolvedScenePath = scenePaths[i];
                        return true;
                    }
                }
            }

            var preferredName = string.IsNullOrWhiteSpace(preferredSceneName)
                ? string.Empty
                : preferredSceneName.Trim();
            if (!string.IsNullOrWhiteSpace(preferredName))
            {
                for (var i = 0; i < scenePaths.Length; i++)
                {
                    var candidateName = Path.GetFileNameWithoutExtension(scenePaths[i] ?? string.Empty);
                    if (string.Equals(candidateName, preferredName, StringComparison.OrdinalIgnoreCase))
                    {
                        resolvedScenePath = scenePaths[i];
                        return true;
                    }
                }
            }

            resolvedScenePath = scenePaths[0] ?? string.Empty;
            return !string.IsNullOrWhiteSpace(resolvedScenePath);
        }

        private bool TryUnloadInstalledContentScene(string reasonCode, bool forceBundleUnload)
        {
            if (string.IsNullOrWhiteSpace(_activeInstalledContentScenePath) &&
                _activeInstalledContentBundle == null)
            {
                return true;
            }

            var scenePath = _activeInstalledContentScenePath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(scenePath))
            {
                var loadedScene = SceneManager.GetSceneByPath(scenePath);
                if (loadedScene.IsValid() && loadedScene.isLoaded)
                {
                    var unloadOperation = SceneManager.UnloadSceneAsync(loadedScene);
                    if (unloadOperation == null)
                    {
                        Logger.Warning(
                            $"[GameRuntime] Failed to request unload for installed content scene: {scenePath}");
                    }
                }
            }

            if (_activeInstalledContentBundle != null)
            {
                try
                {
                    _activeInstalledContentBundle.Unload(forceBundleUnload);
                }
                catch (Exception e)
                {
                    Logger.Warning(
                        $"[GameRuntime] Installed content bundle unload failed: {e.Message}");
                }
            }

            TrackCriticalRuntimeEvent("content_scene_unload_requested", new Dictionary<string, object>
            {
                { "gameId", _activeInstalledContentGameId ?? string.Empty },
                { "scenePath", scenePath },
                { "bundlePath", _activeInstalledContentBundlePath ?? string.Empty },
                { "reasonCode", string.IsNullOrWhiteSpace(reasonCode) ? "CONTENT_SCENE_UNLOAD" : reasonCode },
            });

            _activeInstalledContentBundle = null;
            _activeInstalledContentScenePath = string.Empty;
            _activeInstalledContentGameId = string.Empty;
            _activeInstalledContentBundlePath = string.Empty;
            return true;
        }

        private string ResolvePackageInstallPath(string gameId, string targetVersion, Uri packageUri)
        {
            var installRoot = string.IsNullOrWhiteSpace(_packageInstallFolder)
                ? "content_packages"
                : _packageInstallFolder.Trim();
            var gameSegment = SanitizePathSegment(gameId, "unknown_game");
            var versionSegment = SanitizePathSegment(
                NormalizeContentVersion(targetVersion, _defaultSimulatedContentVersion),
                "1_0_0");
            var fileName = ResolvePackageInstallFileName(packageUri);
            return Path.Combine(
                Application.persistentDataPath,
                installRoot,
                gameSegment,
                versionSegment,
                fileName);
        }

        private string ResolvePackageInstallFileName(Uri packageUri)
        {
            var fallbackName = string.IsNullOrWhiteSpace(_packageInstallFallbackFileName)
                ? "package.pkg.json"
                : _packageInstallFallbackFileName.Trim();
            var candidate = packageUri == null
                ? string.Empty
                : Path.GetFileName(packageUri.LocalPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return SanitizePathSegment(fallbackName, "package.pkg.json");
            }

            return SanitizePathSegment(candidate, "package.pkg.json");
        }

        private static string SanitizePathSegment(string value, string fallback)
        {
            var resolved = string.IsNullOrWhiteSpace(value)
                ? fallback
                : value.Trim();
            if (string.IsNullOrWhiteSpace(resolved))
            {
                return string.IsNullOrWhiteSpace(fallback) ? "segment" : fallback.Trim();
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(resolved.Length);
            for (var i = 0; i < resolved.Length; i++)
            {
                var current = resolved[i];
                builder.Append(Array.IndexOf(invalidChars, current) >= 0 ? '_' : current);
            }

            var sanitized = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(sanitized)
                ? (string.IsNullOrWhiteSpace(fallback) ? "segment" : fallback.Trim())
                : sanitized;
        }

        private static string NormalizePackageInstallUri(string packageUri)
        {
            return string.IsNullOrWhiteSpace(packageUri)
                ? string.Empty
                : packageUri.Trim();
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

            CompleteSimulatedInstallRoutine(gameId);
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

        private void StopSimulatedCatalogSyncRoutine()
        {
            if (_simulatedCatalogSyncRoutine == null)
            {
                return;
            }

            StopCoroutine(_simulatedCatalogSyncRoutine);
            _simulatedCatalogSyncRoutine = null;
        }

        private static bool IsInstallLifecycleInProgressStatus(string runtimeStatus)
        {
            if (string.IsNullOrWhiteSpace(runtimeStatus))
            {
                return false;
            }

            return string.Equals(
                       runtimeStatus,
                       ContentRuntimeStatusValues.SyncingManifest,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       runtimeStatus,
                       ContentRuntimeStatusValues.Downloading,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       runtimeStatus,
                       ContentRuntimeStatusValues.Verifying,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       runtimeStatus,
                       ContentRuntimeStatusValues.Activating,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       runtimeStatus,
                       ContentRuntimeStatusValues.RollingBack,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       runtimeStatus,
                       ContentRuntimeStatusValues.Installing,
                       StringComparison.OrdinalIgnoreCase);
        }

        private void CompleteSimulatedInstallRoutine(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId))
            {
                return;
            }

            _simulatedInstallRoutineByGameId.Remove(gameId);
        }

        private void TryStartPackageProbeFromInstallCommand(
            string gameId,
            string correlationId,
            InstallGameCommand command)
        {
            var normalizedGameId = string.IsNullOrWhiteSpace(gameId)
                ? string.Empty
                : gameId.Trim();
            if (string.IsNullOrWhiteSpace(normalizedGameId))
            {
                return;
            }

            var requestPackageProbe = command != null && command.requestPackageProbe;
            var probeOnly = command != null && command.probeOnly;
            var packageUri = command == null ? string.Empty : command.packageUri;

            if (!requestPackageProbe)
            {
                if (probeOnly)
                {
                    EmitPackageProbeResult(
                        normalizedGameId,
                        correlationId,
                        packageUri,
                        success: false,
                        method: string.Empty,
                        statusCode: 0,
                        contentLength: 0,
                        eTag: string.Empty,
                        contentType: string.Empty,
                        reasonCode: PackageProbeReasonCodes.RequestNotEnabled,
                        probeOnly: true);
                }

                return;
            }

            if (!_enableBoardSafePackageProbe)
            {
                EmitPackageProbeResult(
                    normalizedGameId,
                    correlationId,
                    packageUri,
                    success: false,
                    method: string.Empty,
                    statusCode: 0,
                    contentLength: 0,
                    eTag: string.Empty,
                    contentType: string.Empty,
                    reasonCode: PackageProbeReasonCodes.Disabled,
                    probeOnly: probeOnly);
                return;
            }

            StartPackageProbeRoutine(normalizedGameId, correlationId, packageUri, probeOnly);
        }

        private void StartPackageProbeRoutine(
            string gameId,
            string correlationId,
            string packageUri,
            bool probeOnly)
        {
            if (string.IsNullOrWhiteSpace(gameId))
            {
                return;
            }

            StopPackageProbeRoutine(gameId);
            var routine = StartCoroutine(
                RunPackageProbeRoutine(
                    gameId.Trim(),
                    correlationId,
                    packageUri,
                    probeOnly));
            _packageProbeRoutineByGameId[gameId.Trim()] = routine;
        }

        private IEnumerator RunPackageProbeRoutine(
            string gameId,
            string correlationId,
            string packageUri,
            bool probeOnly)
        {
            var normalizedUri = string.IsNullOrWhiteSpace(packageUri)
                ? string.Empty
                : packageUri.Trim();
            if (string.IsNullOrWhiteSpace(normalizedUri))
            {
                EmitPackageProbeResult(
                    gameId,
                    correlationId,
                    normalizedUri,
                    success: false,
                    method: string.Empty,
                    statusCode: 0,
                    contentLength: 0,
                    eTag: string.Empty,
                    contentType: string.Empty,
                    reasonCode: PackageProbeReasonCodes.PackageUriMissing,
                    probeOnly: probeOnly);
                CompletePackageProbeRoutine(gameId);
                yield break;
            }

            if (!Uri.TryCreate(normalizedUri, UriKind.Absolute, out var parsedUri) ||
                (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
            {
                EmitPackageProbeResult(
                    gameId,
                    correlationId,
                    normalizedUri,
                    success: false,
                    method: string.Empty,
                    statusCode: 0,
                    contentLength: 0,
                    eTag: string.Empty,
                    contentType: string.Empty,
                    reasonCode: PackageProbeReasonCodes.PackageUriInvalid,
                    probeOnly: probeOnly);
                CompletePackageProbeRoutine(gameId);
                yield break;
            }

            normalizedUri = parsedUri.AbsoluteUri;
            var timeoutSeconds = Mathf.Clamp(_packageProbeTimeoutSeconds, 1, 20);
            var preferredMethod = _packageProbePreferHeadRequest
                ? UnityWebRequest.kHttpVerbHEAD
                : UnityWebRequest.kHttpVerbGET;
            var outcome = new PackageProbeOutcome();
            yield return ExecutePackageProbeRequest(
                parsedUri,
                preferredMethod,
                timeoutSeconds,
                outcome);

            if (ShouldFallbackPackageProbeToGet(outcome))
            {
                yield return ExecutePackageProbeRequest(
                    parsedUri,
                    UnityWebRequest.kHttpVerbGET,
                    timeoutSeconds,
                    outcome);
            }

            EmitPackageProbeResult(
                gameId,
                correlationId,
                normalizedUri,
                outcome.success,
                outcome.method,
                outcome.statusCode,
                outcome.contentLength,
                outcome.eTag,
                outcome.contentType,
                outcome.reasonCode,
                probeOnly);
            CompletePackageProbeRoutine(gameId);
        }

        private IEnumerator ExecutePackageProbeRequest(
            Uri packageUri,
            string method,
            int timeoutSeconds,
            PackageProbeOutcome outcome)
        {
            if (packageUri == null || outcome == null)
            {
                yield break;
            }

            if (string.Equals(method, UnityWebRequest.kHttpVerbHEAD, StringComparison.OrdinalIgnoreCase))
            {
                using (var request = new UnityWebRequest(packageUri.AbsoluteUri, UnityWebRequest.kHttpVerbHEAD))
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.timeout = timeoutSeconds;
                    yield return request.SendWebRequest();
                    PopulatePackageProbeOutcomeFromRequest(request, UnityWebRequest.kHttpVerbHEAD, outcome);
                }
                yield break;
            }

            using (var request = UnityWebRequest.Get(packageUri.AbsoluteUri))
            {
                request.timeout = timeoutSeconds;
                request.SetRequestHeader("Range", "bytes=0-0");
                yield return request.SendWebRequest();
                PopulatePackageProbeOutcomeFromRequest(request, UnityWebRequest.kHttpVerbGET, outcome);
            }
        }

        private bool ShouldFallbackPackageProbeToGet(PackageProbeOutcome outcome)
        {
            if (!_packageProbeFallbackToGet || outcome == null)
            {
                return false;
            }

            if (!string.Equals(
                    outcome.method,
                    UnityWebRequest.kHttpVerbHEAD,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (outcome.success)
            {
                return false;
            }

            return outcome.statusCode == 0 || outcome.statusCode == 405 || outcome.statusCode == 501;
        }

        private void PopulatePackageProbeOutcomeFromRequest(
            UnityWebRequest request,
            string method,
            PackageProbeOutcome outcome)
        {
            if (outcome == null)
            {
                return;
            }

            outcome.method = method ?? string.Empty;
            if (request == null)
            {
                outcome.success = false;
                outcome.statusCode = 0;
                outcome.contentLength = 0;
                outcome.eTag = string.Empty;
                outcome.contentType = string.Empty;
                outcome.reasonCode = PackageProbeReasonCodes.RequestFailed;
                return;
            }

            var statusCode = request.responseCode;
            outcome.statusCode = statusCode;
            outcome.contentLength = ParsePackageProbeContentLength(
                request.GetResponseHeader("Content-Length"),
                request.downloadedBytes);
            outcome.eTag = request.GetResponseHeader("ETag") ?? string.Empty;
            outcome.contentType = request.GetResponseHeader("Content-Type") ?? string.Empty;
            outcome.success = statusCode >= 200 && statusCode < 400;
            outcome.reasonCode = ResolvePackageProbeReasonCode(request, statusCode);
        }

        private static long ParsePackageProbeContentLength(string headerValue, ulong downloadedBytes)
        {
            if (!string.IsNullOrWhiteSpace(headerValue) &&
                long.TryParse(
                    headerValue.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedHeader) &&
                parsedHeader >= 0)
            {
                return parsedHeader;
            }

            return downloadedBytes > long.MaxValue
                ? long.MaxValue
                : (long)downloadedBytes;
        }

        private static string ResolvePackageProbeReasonCode(UnityWebRequest request, long statusCode)
        {
            if (request == null)
            {
                return PackageProbeReasonCodes.RequestFailed;
            }

            if (statusCode >= 200 && statusCode < 400)
            {
                return PackageProbeReasonCodes.Ok;
            }

            if (request.result == UnityWebRequest.Result.ConnectionError)
            {
                var networkError = request.error ?? string.Empty;
                if (networkError.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return PackageProbeReasonCodes.Timeout;
                }

                return PackageProbeReasonCodes.ConnectionError;
            }

            if (request.result == UnityWebRequest.Result.ProtocolError)
            {
                if (statusCode == 408 || statusCode == 504)
                {
                    return PackageProbeReasonCodes.Timeout;
                }

                return PackageProbeReasonCodes.ProtocolError;
            }

            if (request.result == UnityWebRequest.Result.DataProcessingError)
            {
                return PackageProbeReasonCodes.DataProcessingError;
            }

            return PackageProbeReasonCodes.RequestFailed;
        }

        private void EmitPackageProbeResult(
            string gameId,
            string correlationId,
            string packageUri,
            bool success,
            string method,
            long statusCode,
            long contentLength,
            string eTag,
            string contentType,
            string reasonCode,
            bool probeOnly)
        {
            var normalizedGameId = string.IsNullOrWhiteSpace(gameId)
                ? string.Empty
                : gameId.Trim();
            if (string.IsNullOrWhiteSpace(normalizedGameId))
            {
                return;
            }

            var normalizedReasonCode = string.IsNullOrWhiteSpace(reasonCode)
                ? PackageProbeReasonCodes.RequestFailed
                : reasonCode.Trim();
            var command = new PackageProbeResultCommand
            {
                correlationId = string.IsNullOrWhiteSpace(correlationId)
                    ? Guid.NewGuid().ToString()
                    : correlationId,
                gameId = normalizedGameId,
                packageUri = string.IsNullOrWhiteSpace(packageUri) ? string.Empty : packageUri.Trim(),
                success = success,
                method = string.IsNullOrWhiteSpace(method) ? string.Empty : method.Trim().ToUpperInvariant(),
                statusCode = statusCode,
                contentLength = contentLength,
                eTag = string.IsNullOrWhiteSpace(eTag) ? string.Empty : eTag.Trim(),
                contentType = string.IsNullOrWhiteSpace(contentType) ? string.Empty : contentType.Trim(),
                reasonCode = normalizedReasonCode,
                probedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                probeOnly = probeOnly,
            };

            _ = PublishPackageProbeResultAsync(command);
            TrackCriticalRuntimeEvent("package_probe_result", new Dictionary<string, object>
            {
                { "gameId", command.gameId },
                { "packageUri", command.packageUri },
                { "success", command.success },
                { "method", command.method },
                { "statusCode", command.statusCode },
                { "contentLength", command.contentLength },
                { "eTag", command.eTag },
                { "contentType", command.contentType },
                { "reasonCode", command.reasonCode },
                { "probeOnly", command.probeOnly },
                { "probedAtUtc", command.probedAtUtc },
            });
        }

        private void StopPackageProbeRoutine(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId) ||
                !_packageProbeRoutineByGameId.TryGetValue(gameId, out var routine))
            {
                return;
            }

            if (routine != null)
            {
                StopCoroutine(routine);
            }

            CompletePackageProbeRoutine(gameId);
        }

        private void StopAllPackageProbeRoutines()
        {
            if (_packageProbeRoutineByGameId.Count == 0)
            {
                return;
            }

            var gameIds = new List<string>(_packageProbeRoutineByGameId.Keys);
            for (var i = 0; i < gameIds.Count; i++)
            {
                StopPackageProbeRoutine(gameIds[i]);
            }
        }

        private void CompletePackageProbeRoutine(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId))
            {
                return;
            }

            _packageProbeRoutineByGameId.Remove(gameId);
        }

        private void PublishSimulatedContentCatalogSnapshot(string correlationId, string reasonCode)
        {
            EnsureSimulatedContentStatesInitialized();
            foreach (var pair in _simulatedContentStateByGameId)
            {
                var state = pair.Value;
                if (state == null || !IsCatalogGameId(state.gameId))
                {
                    continue;
                }

                _ = PublishGameInstallStatusAsync(state, correlationId, reasonCode);
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

            if (_allowDefaultGameFallback &&
                !string.IsNullOrWhiteSpace(_defaultGameId))
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

        private void ApplyRuntimeEntitlementSnapshot(SessionAttachCommand command)
        {
            var profileRaw = command == null ? string.Empty : command.entitlementProfile;
            var roleRaw = command == null ? string.Empty : command.entitlementRole;
            var planTierRaw = command == null ? string.Empty : command.entitlementPlanTier;
            var policyVersionRaw = command == null ? string.Empty : command.entitlementPolicyVersion;
            var sourceTagRaw = command == null ? string.Empty : command.entitlementSourceTag;
            var evaluatedAtUtcRaw = command == null ? string.Empty : command.entitlementEvaluatedAtUtc;
            var entitledGameIdsCsv = command == null ? string.Empty : command.entitledGameIdsCsv;
            var declaredEntitledGameIdsCount = Math.Max(0, command == null ? 0 : command.entitledGameIdsCount);

            var hasExplicitSnapshot =
                !string.IsNullOrWhiteSpace(profileRaw) ||
                !string.IsNullOrWhiteSpace(roleRaw) ||
                !string.IsNullOrWhiteSpace(planTierRaw) ||
                !string.IsNullOrWhiteSpace(policyVersionRaw) ||
                !string.IsNullOrWhiteSpace(sourceTagRaw) ||
                !string.IsNullOrWhiteSpace(evaluatedAtUtcRaw) ||
                !string.IsNullOrWhiteSpace(entitledGameIdsCsv) ||
                declaredEntitledGameIdsCount > 0;

            var parsedEntitledGameIds = ParseEntitledGameIdsCsv(entitledGameIdsCsv);
            _runtimeEntitlementSnapshot = new RuntimeEntitlementSnapshot
            {
                hasExplicitSnapshot = hasExplicitSnapshot,
                profileId = NormalizeEntitlementProfileId(
                    profileRaw,
                    roleRaw,
                    hasExplicitSnapshot,
                    parsedEntitledGameIds.Count),
                role = NormalizeOrFallback(roleRaw, "THERAPIST"),
                planTier = NormalizeOrFallback(planTierRaw, "BASIC"),
                policyVersion = NormalizeOrFallback(policyVersionRaw, string.Empty),
                sourceTag = NormalizeOrFallback(sourceTagRaw, string.Empty),
                evaluatedAtUtc = NormalizeOrFallback(evaluatedAtUtcRaw, string.Empty),
                declaredEntitledGameIdsCount = declaredEntitledGameIdsCount,
                entitledGameIds = parsedEntitledGameIds,
            };

            TrackCriticalRuntimeEvent("runtime_entitlement_snapshot", new Dictionary<string, object>
            {
                { "reasonCode", hasExplicitSnapshot
                    ? RuntimeEntitlementReasonCodes.SnapshotApplied
                    : RuntimeEntitlementReasonCodes.SnapshotLegacyFallback },
                { "profileId", _runtimeEntitlementSnapshot.profileId },
                { "role", _runtimeEntitlementSnapshot.role },
                { "planTier", _runtimeEntitlementSnapshot.planTier },
                { "policyVersion", _runtimeEntitlementSnapshot.policyVersion },
                { "sourceTag", _runtimeEntitlementSnapshot.sourceTag },
                { "evaluatedAtUtc", _runtimeEntitlementSnapshot.evaluatedAtUtc },
                { "entitledGameIdsCount", _runtimeEntitlementSnapshot.entitledGameIds.Count },
                { "declaredEntitledGameIdsCount", _runtimeEntitlementSnapshot.declaredEntitledGameIdsCount },
                { "hasExplicitSnapshot", _runtimeEntitlementSnapshot.hasExplicitSnapshot },
            });

            if (_runtimeEntitlementSnapshot.declaredEntitledGameIdsCount > 0 &&
                _runtimeEntitlementSnapshot.declaredEntitledGameIdsCount !=
                _runtimeEntitlementSnapshot.entitledGameIds.Count)
            {
                TrackCriticalRuntimeEvent("runtime_entitlement_snapshot", new Dictionary<string, object>
                {
                    { "reasonCode", RuntimeEntitlementReasonCodes.SnapshotCountMismatch },
                    { "profileId", _runtimeEntitlementSnapshot.profileId },
                    { "role", _runtimeEntitlementSnapshot.role },
                    { "declaredEntitledGameIdsCount", _runtimeEntitlementSnapshot.declaredEntitledGameIdsCount },
                    { "entitledGameIdsCount", _runtimeEntitlementSnapshot.entitledGameIds.Count },
                });
            }
        }

        private bool TryAuthorizeGameCommandByEntitlement(
            string commandId,
            string gameId,
            out string reasonCode)
        {
            reasonCode = string.Empty;

            var normalizedCommandId = string.IsNullOrWhiteSpace(commandId)
                ? "COMMAND"
                : commandId.Trim().ToUpperInvariant();
            var normalizedGameId = string.IsNullOrWhiteSpace(gameId)
                ? string.Empty
                : gameId.Trim();
            if (string.IsNullOrWhiteSpace(normalizedGameId))
            {
                reasonCode = BuildCommandEntitlementReasonCode(
                    normalizedCommandId,
                    RuntimeEntitlementReasonCodes.GameIdRequiredSuffix);
                TrackRuntimeEntitlementDecision(
                    normalizedCommandId,
                    normalizedGameId,
                    allowed: false,
                    reasonCode: reasonCode);
                return false;
            }

            if (_runtimeEntitlementSnapshot == null)
            {
                _runtimeEntitlementSnapshot = new RuntimeEntitlementSnapshot();
            }

            var snapshot = _runtimeEntitlementSnapshot;
            var entitledGameIds = snapshot.entitledGameIds ??
                                  new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hasEntitledGameList = entitledGameIds.Count > 0;
            if (hasEntitledGameList && entitledGameIds.Contains(normalizedGameId))
            {
                TrackRuntimeEntitlementDecision(
                    normalizedCommandId,
                    normalizedGameId,
                    allowed: true,
                    reasonCode: RuntimeEntitlementReasonCodes.SnapshotApplied);
                return true;
            }

            var normalizedProfile = NormalizeEntitlementProfileId(
                snapshot.profileId,
                snapshot.role,
                snapshot.hasExplicitSnapshot,
                entitledGameIds.Count);
            if (string.Equals(
                    normalizedProfile,
                    RuntimeEntitlementProfileIds.TherapistFull,
                    StringComparison.Ordinal))
            {
                if (!hasEntitledGameList)
                {
                    TrackRuntimeEntitlementDecision(
                        normalizedCommandId,
                        normalizedGameId,
                        allowed: true,
                        reasonCode: snapshot.hasExplicitSnapshot
                            ? RuntimeEntitlementReasonCodes.SnapshotApplied
                            : RuntimeEntitlementReasonCodes.SnapshotLegacyFallback);
                    return true;
                }

                reasonCode = BuildCommandEntitlementReasonCode(
                    normalizedCommandId,
                    RuntimeEntitlementReasonCodes.GameNotEntitledSuffix);
                TrackRuntimeEntitlementDecision(
                    normalizedCommandId,
                    normalizedGameId,
                    allowed: false,
                    reasonCode: reasonCode);
                return false;
            }

            if (string.Equals(
                    normalizedProfile,
                    RuntimeEntitlementProfileIds.ParentPurchasedPacks,
                    StringComparison.Ordinal))
            {
                reasonCode = BuildCommandEntitlementReasonCode(
                    normalizedCommandId,
                    RuntimeEntitlementReasonCodes.GameNotEntitledSuffix);
                TrackRuntimeEntitlementDecision(
                    normalizedCommandId,
                    normalizedGameId,
                    allowed: false,
                    reasonCode: reasonCode);
                return false;
            }

            reasonCode = BuildCommandEntitlementReasonCode(
                normalizedCommandId,
                RuntimeEntitlementReasonCodes.ProfileUnknownSuffix);
            TrackRuntimeEntitlementDecision(
                normalizedCommandId,
                normalizedGameId,
                allowed: false,
                reasonCode: reasonCode);
            return false;
        }

        private void TrackRuntimeEntitlementDecision(
            string commandId,
            string gameId,
            bool allowed,
            string reasonCode)
        {
            if (_runtimeEntitlementSnapshot == null)
            {
                _runtimeEntitlementSnapshot = new RuntimeEntitlementSnapshot();
            }

            TrackCriticalRuntimeEvent("runtime_entitlement_gate", new Dictionary<string, object>
            {
                { "commandId", commandId ?? string.Empty },
                { "gameId", gameId ?? string.Empty },
                { "allowed", allowed },
                { "reasonCode", reasonCode ?? string.Empty },
                { "profileId", _runtimeEntitlementSnapshot.profileId ?? string.Empty },
                { "role", _runtimeEntitlementSnapshot.role ?? string.Empty },
                { "planTier", _runtimeEntitlementSnapshot.planTier ?? string.Empty },
                { "policyVersion", _runtimeEntitlementSnapshot.policyVersion ?? string.Empty },
                { "sourceTag", _runtimeEntitlementSnapshot.sourceTag ?? string.Empty },
                { "hasExplicitSnapshot", _runtimeEntitlementSnapshot.hasExplicitSnapshot },
                { "entitledGameIdsCount", _runtimeEntitlementSnapshot.entitledGameIds == null
                    ? 0
                    : _runtimeEntitlementSnapshot.entitledGameIds.Count },
                { "declaredEntitledGameIdsCount", _runtimeEntitlementSnapshot.declaredEntitledGameIdsCount },
            });
        }

        private static string BuildCommandEntitlementReasonCode(string commandId, string reasonSuffix)
        {
            var normalizedCommand = string.IsNullOrWhiteSpace(commandId)
                ? "COMMAND"
                : commandId.Trim().ToUpperInvariant();
            var normalizedSuffix = string.IsNullOrWhiteSpace(reasonSuffix)
                ? RuntimeEntitlementReasonCodes.GameNotEntitledSuffix
                : reasonSuffix.Trim().ToUpperInvariant();
            return $"{normalizedCommand}_{normalizedSuffix}";
        }

        private static string NormalizeEntitlementProfileId(
            string profileId,
            string role,
            bool hasExplicitSnapshot,
            int entitledGameIdsCount)
        {
            var normalizedProfile = NormalizeOrFallback(profileId, string.Empty).ToUpperInvariant();
            if (string.Equals(normalizedProfile, RuntimeEntitlementProfileIds.TherapistFull, StringComparison.Ordinal) ||
                string.Equals(normalizedProfile, RuntimeEntitlementProfileIds.ParentPurchasedPacks, StringComparison.Ordinal) ||
                string.Equals(normalizedProfile, RuntimeEntitlementProfileIds.Unknown, StringComparison.Ordinal))
            {
                return normalizedProfile;
            }

            var normalizedRole = NormalizeOrFallback(role, string.Empty).ToUpperInvariant();
            if (string.Equals(normalizedRole, "PARENT", StringComparison.Ordinal))
            {
                return RuntimeEntitlementProfileIds.ParentPurchasedPacks;
            }

            if (string.Equals(normalizedRole, "THERAPIST", StringComparison.Ordinal))
            {
                return RuntimeEntitlementProfileIds.TherapistFull;
            }

            if (!hasExplicitSnapshot && entitledGameIdsCount <= 0)
            {
                return RuntimeEntitlementProfileIds.TherapistFull;
            }

            return RuntimeEntitlementProfileIds.Unknown;
        }

        private static HashSet<string> ParseEntitledGameIdsCsv(string entitledGameIdsCsv)
        {
            var parsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(entitledGameIdsCsv))
            {
                return parsed;
            }

            var values = entitledGameIdsCsv.Split(',');
            for (var i = 0; i < values.Length; i++)
            {
                var normalized = values[i] == null ? string.Empty : values[i].Trim();
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    parsed.Add(normalized);
                }
            }

            return parsed;
        }

        private static string NormalizeOrFallback(string value, string fallback)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }

            return fallback ?? string.Empty;
        }

        private void TryApplyStartCommandConfig(StartGameCommand command)
        {
            if (!TryApplyConfigCommand(
                    command,
                    "START_CONFIG_PROVIDER_UNSUPPORTED",
                    "START_CONFIG_REJECTED",
                    out var reasonCode))
            {
                if (!string.IsNullOrWhiteSpace(reasonCode))
                {
                    Logger.Warning(
                        $"[GameRuntime] Ignored START_GAME config for {_activeGameId}: {reasonCode}");
                }
            }
        }

        private bool TryApplyConfigCommand(
            StartGameCommand command,
            string unsupportedReasonCode,
            string rejectedReasonCode,
            out string reasonCode)
        {
            reasonCode = string.Empty;

            if (command == null || _activeGame == null || string.IsNullOrWhiteSpace(_activeGameId))
            {
                reasonCode = "UPDATE_CONFIG_INVALID";
                return false;
            }

            if (!(_activeGame is GameContracts.IStartCommandConfigProvider configProvider))
            {
                reasonCode = string.IsNullOrWhiteSpace(unsupportedReasonCode)
                    ? "UPDATE_CONFIG_UNSUPPORTED"
                    : unsupportedReasonCode;
                return false;
            }

            _knownConfigs.TryGetValue(_activeGameId, out var previousConfig);

            if (!configProvider.TryCreateConfigFromStartCommand(
                    command,
                    previousConfig,
                    out var resolvedConfig,
                    out reasonCode))
            {
                reasonCode = string.IsNullOrWhiteSpace(reasonCode)
                    ? (string.IsNullOrWhiteSpace(rejectedReasonCode) ? "UPDATE_CONFIG_REJECTED" : rejectedReasonCode)
                    : reasonCode;
                return false;
            }

            if (resolvedConfig == null)
            {
                reasonCode = "UPDATE_CONFIG_NULL_CONFIG";
                return false;
            }

            if (!UpdateGameConfig(_activeGameId, resolvedConfig))
            {
                reasonCode = "UPDATE_CONFIG_FAILED";
                return false;
            }

            reasonCode = string.Empty;
            return true;
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

            var sessionId = _sessionContext.SessionId ?? string.Empty;
            var patientId = _sessionContext.PatientId ?? string.Empty;
            var therapistId = _sessionContext.TherapistId ?? string.Empty;
            var ownerKey = BuildOwnerKey(therapistId, patientId);
            var sessionKey = BuildSessionKey(ownerKey, sessionId);

            var command = new SessionStateUpdateCommand
            {
                sessionId = sessionId,
                studentId = patientId,
                patientId = patientId,
                therapistId = therapistId,
                ownerKey = ownerKey,
                sessionKey = sessionKey,
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
            else
            {
                // Terminal state reached: immediately begin a fresh CREATED session so the
                // next game start has a valid CREATED → IN_PROGRESS transition path and
                // Flutter sees the new session via SESSION_STATE_UPDATE(CREATED).
                _sessionContext.BeginSession(_sessionContext.PatientId, _sessionContext.TherapistId);
            }

            if (_controllerDisconnectPausePendingResume &&
                currentState != GameContracts.SessionLifecycleState.PAUSED)
            {
                _controllerDisconnectPausePendingResume = false;
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

        private void HandleClientConnected(string clientIp)
        {
            var normalizedClientIp = string.IsNullOrWhiteSpace(clientIp)
                ? string.Empty
                : clientIp.Trim();

            _controllerConnectionEpoch++;

            var eventName = _hasObservedControllerConnection
                ? "controller_reconnected"
                : "controller_connected";

            TrackControllerConnectionEvent(
                eventName,
                "TCP_CLIENT_CONNECTED",
                normalizedClientIp);

            _hasObservedControllerConnection = true;
            _lastControllerClientIp = normalizedClientIp;
            _lastControllerDisconnectedAtUtc = DateTime.MinValue;
            PublishDevicePresenceUpdateIfPossible(
                DevicePresenceStateValues.Connected,
                "TCP_CLIENT_CONNECTED",
                force: true);
            if (_autoResumeOnControllerReconnect)
            {
                TryResumeAfterControlRecovered(
                    reasonCode: "TCP_CLIENT_CONNECTED",
                    trigger: "controller_reconnected");
            }
            PublishRuntimeStatusIfChanged("TCP_CLIENT_CONNECTED");
            if (_publishContentCatalogOnClientConnect)
            {
                PublishSimulatedContentCatalogSnapshot(string.Empty, "TCP_CLIENT_CONNECTED");
            }
        }

        private void HandleClientDisconnected()
        {
            _lastControllerDisconnectedAtUtc = DateTime.UtcNow;
            TrackControllerConnectionEvent(
                "controller_disconnected",
                "TCP_CLIENT_DISCONNECTED",
                _lastControllerClientIp);
            ApplyControllerDisconnectBehavior();
            _lastPublishedDevicePresenceState = string.Empty;
            _lastPublishedDevicePresenceReasonCode = string.Empty;
            _lastPublishedDevicePresenceAtUtc = DateTime.MinValue;
            _lastRuntimeStatus = string.Empty;
        }

        private void ApplyRuntimeBackendHostOverride(SessionAttachCommand command)
        {
            var backendHost = command == null
                ? string.Empty
                : command.backendHost ?? string.Empty;
            var backendPort = command == null ? 0 : command.backendPort;
            var backendHostSource = command == null
                ? string.Empty
                : command.backendHostSource ?? string.Empty;

            if (string.IsNullOrWhiteSpace(backendHost))
            {
                TrackCriticalRuntimeEvent("runtime_backend_host_override", new Dictionary<string, object>
                {
                    { "reasonCode", "BACKEND_HOST_OVERRIDE_MISSING" },
                    { "host", string.Empty },
                    { "port", backendPort },
                    { "source", backendHostSource },
                });
                return;
            }

            if (_firebaseDataService == null)
            {
                _firebaseDataService = FindFirstObjectByType<FirebaseDataService>();
            }

            if (_firebaseDataService == null)
            {
                TrackCriticalRuntimeEvent("runtime_backend_host_override", new Dictionary<string, object>
                {
                    { "reasonCode", "FIREBASE_DATA_SERVICE_UNAVAILABLE" },
                    { "host", backendHost },
                    { "port", backendPort },
                    { "source", backendHostSource },
                });
                return;
            }

            var configured = _firebaseDataService.TryConfigureRuntimeBackendHostOverride(
                backendHost,
                backendPort,
                backendHostSource,
                out var reasonCode);

            TrackCriticalRuntimeEvent("runtime_backend_host_override", new Dictionary<string, object>
            {
                { "reasonCode", string.IsNullOrWhiteSpace(reasonCode) ? "BACKEND_HOST_OVERRIDE_UNKNOWN" : reasonCode },
                { "configured", configured },
                { "host", backendHost },
                { "port", backendPort },
                { "source", backendHostSource },
            });
        }

        private void ApplyMobileDisconnectBehavior(SessionAttachCommand command)
        {
            var requestedBehavior = command == null
                ? string.Empty
                : command.mobileDisconnectBehavior ?? string.Empty;
            var fallbackBehavior = string.IsNullOrWhiteSpace(_defaultMobileDisconnectBehavior)
                ? MobileDisconnectBehaviorValues.Pause
                : _defaultMobileDisconnectBehavior;
            var resolvedBehavior = NormalizeMobileDisconnectBehavior(
                string.IsNullOrWhiteSpace(requestedBehavior)
                    ? fallbackBehavior
                    : requestedBehavior);
            var previousBehavior = _mobileDisconnectBehavior;
            _mobileDisconnectBehavior = resolvedBehavior;

            TrackCriticalRuntimeEvent("mobile_disconnect_policy_updated", new Dictionary<string, object>
            {
                { "previousBehavior", previousBehavior ?? string.Empty },
                { "behavior", _mobileDisconnectBehavior },
                { "source", "SESSION_ATTACH" },
                { "sessionId", _sessionContext == null ? string.Empty : _sessionContext.SessionId ?? string.Empty },
            });

            if (_controllerDisconnectPausePendingResume &&
                string.Equals(
                    _mobileDisconnectBehavior,
                    MobileDisconnectBehaviorValues.Continue,
                    StringComparison.Ordinal))
            {
                TryResumeAfterControlRecovered(
                    reasonCode: "SESSION_ATTACH",
                    trigger: "session_attach_policy_continue");
            }
        }

        private void ApplyControllerDisconnectBehavior()
        {
            if (_sessionContext == null)
            {
                _sessionContext = ResolveSessionContext();
            }

            var sessionState = _sessionContext == null
                ? GameContracts.SessionLifecycleState.CREATED
                : _sessionContext.SessionState;
            var activeGameState = _activeGame == null
                ? GameContracts.GameState.NotInitialized
                : _activeGame.State;
            var behavior = NormalizeMobileDisconnectBehavior(_mobileDisconnectBehavior);
            var appliedPause = false;
            var skipReason = string.Empty;

            if (string.Equals(behavior, MobileDisconnectBehaviorValues.Continue, StringComparison.Ordinal))
            {
                skipReason = "CONTINUE_POLICY";
                _controllerDisconnectPausePendingResume = false;
            }
            else if (_activeGame == null)
            {
                skipReason = "NO_ACTIVE_GAME";
                _controllerDisconnectPausePendingResume = false;
            }
            else if (activeGameState != GameContracts.GameState.Playing)
            {
                skipReason = $"ACTIVE_GAME_STATE_{activeGameState.ToString().ToUpperInvariant()}";
                _controllerDisconnectPausePendingResume = false;
            }
            else if (sessionState != GameContracts.SessionLifecycleState.IN_PROGRESS)
            {
                skipReason = $"SESSION_STATE_{sessionState.ToString().ToUpperInvariant()}";
                _controllerDisconnectPausePendingResume = false;
            }
            else
            {
                appliedPause = PauseActiveGame("TCP_CLIENT_DISCONNECTED");
                _controllerDisconnectPausePendingResume = appliedPause;
                if (!appliedPause)
                {
                    skipReason = "PAUSE_REJECTED";
                }
            }

            TrackCriticalRuntimeEvent("mobile_disconnect_policy_applied", new Dictionary<string, object>
            {
                { "behavior", behavior },
                { "appliedPause", appliedPause },
                { "skipReason", skipReason },
                { "sessionState", sessionState.ToString() },
                { "activeGameId", _activeGameId ?? string.Empty },
                { "activeGameState", activeGameState.ToString() },
                { "pendingResume", _controllerDisconnectPausePendingResume },
            });
        }

        private void TryResumeAfterControlRecovered(string reasonCode, string trigger)
        {
            if (!_controllerDisconnectPausePendingResume)
            {
                return;
            }

            if (_sessionContext == null)
            {
                _sessionContext = ResolveSessionContext();
            }

            var sessionState = _sessionContext == null
                ? GameContracts.SessionLifecycleState.CREATED
                : _sessionContext.SessionState;
            var activeGameState = _activeGame == null
                ? GameContracts.GameState.NotInitialized
                : _activeGame.State;
            var skipReason = string.Empty;

            if (_activeGame == null)
            {
                skipReason = "NO_ACTIVE_GAME";
                _controllerDisconnectPausePendingResume = false;
            }
            else if (sessionState != GameContracts.SessionLifecycleState.PAUSED)
            {
                skipReason = $"SESSION_STATE_{sessionState.ToString().ToUpperInvariant()}";
                _controllerDisconnectPausePendingResume = false;
            }
            else if (activeGameState != GameContracts.GameState.Paused)
            {
                skipReason = $"ACTIVE_GAME_STATE_{activeGameState.ToString().ToUpperInvariant()}";
                _controllerDisconnectPausePendingResume = false;
            }

            var resumed = false;
            if (string.IsNullOrWhiteSpace(skipReason))
            {
                resumed = ResumeActiveGame(reasonCode);
                if (resumed)
                {
                    _controllerDisconnectPausePendingResume = false;
                }
                else
                {
                    skipReason = "RESUME_REJECTED";
                }
            }

            TrackCriticalRuntimeEvent("mobile_disconnect_policy_resume", new Dictionary<string, object>
            {
                { "trigger", trigger ?? string.Empty },
                { "reasonCode", string.IsNullOrWhiteSpace(reasonCode) ? "TCP_CLIENT_CONNECTED" : reasonCode },
                { "resumed", resumed },
                { "skipReason", skipReason },
                { "sessionState", sessionState.ToString() },
                { "activeGameId", _activeGameId ?? string.Empty },
                { "activeGameState", activeGameState.ToString() },
                { "pendingResume", _controllerDisconnectPausePendingResume },
            });
        }

        private static string NormalizeMobileDisconnectBehavior(string behavior)
        {
            var normalized = (behavior ?? string.Empty).Trim().ToLowerInvariant();
            return string.Equals(normalized, MobileDisconnectBehaviorValues.Continue, StringComparison.Ordinal)
                ? MobileDisconnectBehaviorValues.Continue
                : MobileDisconnectBehaviorValues.Pause;
        }

        private void TrackControllerConnectionEvent(string eventName, string reasonCode, string clientIp)
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                return;
            }

            if (_sessionContext == null)
            {
                _sessionContext = ResolveSessionContext();
            }

            var payload = new Dictionary<string, object>
            {
                { "reasonCode", reasonCode ?? string.Empty },
                { "clientIp", clientIp ?? string.Empty },
                { "activeGameId", _activeGameId ?? string.Empty },
                { "hasActiveGame", _activeGame != null },
                { "sessionState", _sessionContext != null ? _sessionContext.SessionState.ToString() : string.Empty },
                { "connectionEpoch", _controllerConnectionEpoch },
                { "mobileDisconnectBehavior", _mobileDisconnectBehavior },
                { "disconnectPausePendingResume", _controllerDisconnectPausePendingResume },
            };

            if ((string.Equals(eventName, "controller_connected", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(eventName, "controller_reconnected", StringComparison.OrdinalIgnoreCase)) &&
                _lastControllerDisconnectedAtUtc != DateTime.MinValue)
            {
                var downtimeMs = Math.Max(
                    0L,
                    (long)(DateTime.UtcNow - _lastControllerDisconnectedAtUtc).TotalMilliseconds);
                payload["downtimeMs"] = downtimeMs;
            }

            TrackCriticalRuntimeEvent(eventName, payload);
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
            ReconcileTerminalGameStateWithSession(ref sessionState, ref hasActiveGame, ref activeGameState);
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

        private void ReconcileTerminalGameStateWithSession(
            ref GameContracts.SessionLifecycleState sessionState,
            ref bool hasActiveGame,
            ref GameContracts.GameState activeGameState)
        {
            if (!hasActiveGame)
            {
                return;
            }

            if (!TryResolveAutoTerminalSync(
                    sessionState,
                    activeGameState,
                    out var stopReason,
                    out var targetSessionState,
                    out var reasonCode))
            {
                return;
            }

            if (!TryTransitionSessionState(targetSessionState, reasonCode))
            {
                return;
            }

            var stoppedGameId = _activeGameId ?? string.Empty;
            TrackCriticalRuntimeEvent("game_end", new Dictionary<string, object>
            {
                { "gameId", stoppedGameId },
                { "reason", stopReason.ToString() },
            });
            TryReportActiveGameResult(stopReason);
            if (_unloadInstalledContentSceneOnStop)
            {
                TryUnloadInstalledContentScene(reasonCode, false);
            }
            ClearActiveGameSelection(reasonCode);
            TrackCriticalRuntimeEvent("session_stop", new Dictionary<string, object>
            {
                { "gameId", stoppedGameId },
                { "reason", reasonCode },
            });

            sessionState = _sessionContext == null
                ? targetSessionState
                : _sessionContext.SessionState;
            hasActiveGame = _activeGame != null;
            activeGameState = hasActiveGame ? _activeGame.State : GameContracts.GameState.NotInitialized;
        }

        private static bool TryResolveAutoTerminalSync(
            GameContracts.SessionLifecycleState sessionState,
            GameContracts.GameState activeGameState,
            out GameContracts.GameStopReason stopReason,
            out GameContracts.SessionLifecycleState targetSessionState,
            out string reasonCode)
        {
            stopReason = GameContracts.GameStopReason.Error;
            targetSessionState = GameContracts.SessionLifecycleState.FAILED_TECHNICAL;
            reasonCode = string.Empty;

            if (activeGameState == GameContracts.GameState.Completed &&
                sessionState == GameContracts.SessionLifecycleState.IN_PROGRESS)
            {
                stopReason = GameContracts.GameStopReason.Completed;
                targetSessionState = GameContracts.SessionLifecycleState.COMPLETED;
                reasonCode = "ACTIVE_GAME_COMPLETED";
                return true;
            }

            if (activeGameState == GameContracts.GameState.Failed &&
                (sessionState == GameContracts.SessionLifecycleState.IN_PROGRESS ||
                 sessionState == GameContracts.SessionLifecycleState.PAUSED))
            {
                stopReason = GameContracts.GameStopReason.Error;
                targetSessionState = GameContracts.SessionLifecycleState.FAILED_TECHNICAL;
                reasonCode = "ACTIVE_GAME_FAILED";
                return true;
            }

            return false;
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
            var sessionId = _sessionContext?.SessionId ?? string.Empty;
            var patientId = _sessionContext?.PatientId ?? string.Empty;
            var therapistId = _sessionContext?.TherapistId ?? string.Empty;
            var ownerKey = BuildOwnerKey(therapistId, patientId);
            var sessionKey = BuildSessionKey(ownerKey, sessionId);
            var expectedIntervalMs = Math.Max(200, Mathf.RoundToInt(Mathf.Max(0.2f, heartbeatIntervalSeconds) * 1000f));
            var staleAfterMs = Math.Max(
                expectedIntervalMs * 3,
                Mathf.RoundToInt(Mathf.Max(1f, _watchdogHungThresholdSeconds) * 1000f));

            var command = new SessionWatchdogHeartbeatCommand
            {
                sessionId = sessionId,
                studentId = patientId,
                patientId = patientId,
                therapistId = therapistId,
                ownerKey = ownerKey,
                sessionKey = sessionKey,
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

            var sessionId = _sessionContext?.SessionId ?? string.Empty;
            var patientId = _sessionContext?.PatientId ?? string.Empty;
            var therapistId = _sessionContext?.TherapistId ?? string.Empty;
            var ownerKey = BuildOwnerKey(therapistId, patientId);
            var sessionKey = BuildSessionKey(ownerKey, sessionId);

            var command = new RuntimeStatusUpdateCommand
            {
                sessionId = sessionId,
                studentId = patientId,
                patientId = patientId,
                therapistId = therapistId,
                ownerKey = ownerKey,
                sessionKey = sessionKey,
                status = nextStatus,
                previousStatus = _lastRuntimeStatus ?? string.Empty,
                reasonCode = reasonCode ?? string.Empty,
                changedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                pendingQueueSize = pendingQueueSize,
            };

            _lastRuntimeStatus = nextStatus;
            _ = PublishRuntimeStatusUpdateAsync(command);
        }

        private void PublishDevicePresenceUpdateIfPossible(
            string presenceState,
            string reasonCode,
            bool force = false)
        {
            if (_commandBus == null || !HasStatusDeliveryRoute())
            {
                return;
            }

            var normalizedState = presenceState ?? string.Empty;
            var normalizedReason = reasonCode ?? string.Empty;
            var nowUtc = DateTime.UtcNow;
            var sessionId = _sessionContext?.SessionId ?? string.Empty;
            var patientId = _sessionContext?.PatientId ?? string.Empty;
            var therapistId = _sessionContext?.TherapistId ?? string.Empty;
            var ownerKey = BuildOwnerKey(therapistId, patientId);
            var sessionKey = BuildSessionKey(ownerKey, sessionId);

            if (!force &&
                string.Equals(_lastPublishedDevicePresenceState, normalizedState, StringComparison.Ordinal) &&
                string.Equals(_lastPublishedDevicePresenceReasonCode, normalizedReason, StringComparison.Ordinal) &&
                _lastPublishedDevicePresenceAtUtc != DateTime.MinValue &&
                (nowUtc - _lastPublishedDevicePresenceAtUtc).TotalMilliseconds < 750)
            {
                return;
            }

            var command = new DevicePresenceUpdateCommand
            {
                sessionId = sessionId,
                studentId = patientId,
                patientId = patientId,
                therapistId = therapistId,
                ownerKey = ownerKey,
                sessionKey = sessionKey,
                presenceState = normalizedState,
                reasonCode = normalizedReason,
                changedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                appPaused = _lastAppPaused,
                appFocused = _lastAppFocused,
                hasTcpClient = _tcpServerService != null && _tcpServerService.HasClient,
                activeGameId = _activeGameId ?? string.Empty,
                activeGameState = _activeGame == null ? GameContracts.GameState.NotInitialized.ToString() : _activeGame.State.ToString(),
            };

            _lastPublishedDevicePresenceState = normalizedState;
            _lastPublishedDevicePresenceReasonCode = normalizedReason;
            _lastPublishedDevicePresenceAtUtc = nowUtc;

            _ = PublishDevicePresenceUpdateAsync(command);
            TrackCriticalRuntimeEvent("device_presence_update", new Dictionary<string, object>
            {
                { "presenceState", normalizedState },
                { "reasonCode", normalizedReason },
                { "appPaused", _lastAppPaused },
                { "appFocused", _lastAppFocused },
                { "hasTcpClient", command.hasTcpClient },
                { "activeGameId", _activeGameId ?? string.Empty },
                { "activeGameState", command.activeGameState ?? string.Empty },
            });
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

        private async Task PublishDevicePresenceUpdateAsync(DevicePresenceUpdateCommand command)
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
                    $"[GameRuntime] Failed to publish {GameCommandIds.DevicePresenceUpdate}: {e.Message}");
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

        private async Task PublishPackageProbeResultAsync(PackageProbeResultCommand command)
        {
            if (_commandBus == null || command == null || string.IsNullOrWhiteSpace(command.gameId))
            {
                return;
            }

            if (!HasStatusDeliveryRoute())
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
                    $"[GameRuntime] Failed to publish {GameCommandIds.PackageProbeResult} ({command.gameId}, reason={command.reasonCode}): {e.Message}");
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
                    stats.durableOutboxInFlight +
                    stats.durableOutboxFailed);
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

        private static bool IsTerminalSessionState(GameContracts.SessionLifecycleState state)
        {
            return state == GameContracts.SessionLifecycleState.COMPLETED ||
                   state == GameContracts.SessionLifecycleState.ABORTED_BY_THERAPIST ||
                   state == GameContracts.SessionLifecycleState.FAILED_TECHNICAL;
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

        private void ClearActiveGameSelection(string reasonCode)
        {
            if (_activeGame == null && string.IsNullOrWhiteSpace(_activeGameId))
            {
                return;
            }

            Logger.Info(
                $"[GameRuntime] Active game cleared: {_activeGameId ?? string.Empty} (reason={reasonCode ?? "unspecified"})");
            _activeGame = null;
            _activeGameId = string.Empty;
            _deferredStartPending = false;
            _deferredStartGameId = string.Empty;
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

        private static string BuildOwnerKey(string therapistId, string studentId)
        {
            var normalizedTherapistId = string.IsNullOrWhiteSpace(therapistId)
                ? string.Empty
                : therapistId.Trim();
            var normalizedStudentId = string.IsNullOrWhiteSpace(studentId)
                ? string.Empty
                : studentId.Trim();
            if (string.IsNullOrWhiteSpace(normalizedTherapistId) ||
                string.IsNullOrWhiteSpace(normalizedStudentId))
            {
                return string.Empty;
            }

            return $"{normalizedTherapistId}|{normalizedStudentId}";
        }

        private static string BuildSessionKey(string ownerKey, string sessionId)
        {
            var normalizedOwnerKey = string.IsNullOrWhiteSpace(ownerKey)
                ? string.Empty
                : ownerKey.Trim();
            var normalizedSessionId = string.IsNullOrWhiteSpace(sessionId)
                ? string.Empty
                : sessionId.Trim();
            if (string.IsNullOrWhiteSpace(normalizedOwnerKey) ||
                string.IsNullOrWhiteSpace(normalizedSessionId))
            {
                return string.Empty;
            }

            return $"{normalizedOwnerKey}|{normalizedSessionId}";
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
