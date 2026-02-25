import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_controller/models/content_delivery_contract.dart';
import 'package:flutter_controller/models/critical_command_envelope.dart';
import 'package:flutter_controller/models/device_info.dart';
import 'package:flutter_controller/models/entitlement_access.dart';
import 'package:flutter_controller/models/game_catalog_entry.dart';
import 'package:flutter_controller/models/mobile_control_schema.dart';
import 'package:flutter_controller/models/ops_error_catalog.dart';
import 'package:flutter_controller/models/parent_progress_snapshot.dart';
import 'package:flutter_controller/models/runtime_status_signal.dart';
import 'package:flutter_controller/models/session_attach_failure_policy.dart';
import 'package:flutter_controller/models/session_fsm_contract.dart';
import 'package:flutter_controller/models/session_recovery_manager.dart';
import 'package:flutter_controller/models/session_ownership.dart';
import 'package:flutter_controller/models/session_recovery_policy.dart';
import 'package:flutter_controller/models/student.dart';
import 'package:flutter_controller/models/student_reward_unlock.dart';
import 'package:flutter_controller/models/therapist_session_settings.dart';
import 'package:flutter_controller/services/connection_service.dart';
import 'package:flutter_controller/services/discovery_service.dart';
import 'package:flutter_controller/services/entitlement_service.dart';
import 'package:flutter_controller/services/firebase_service.dart';
import 'package:flutter_controller/services/foreground_service_bridge.dart';
import 'package:flutter_controller/services/game_catalog_service.dart';
import 'package:flutter_controller/services/operator_incident_popup_queue.dart';
import 'package:flutter_controller/services/parent_progress_service.dart';
import 'package:flutter_controller/services/session_journal_service.dart';
import 'package:flutter_controller/services/student_reward_service.dart';
import 'package:flutter_controller/services/therapist_session_settings_service.dart';
import 'package:flutter_controller/models/therapy_session_record.dart';
import 'package:flutter_controller/widgets/media_stream_widget.dart';
import 'package:flutter_controller/widgets/mobile_control_renderer.dart';

enum _SessionGateAction { keepCurrent, resume, startNew }

enum _WorkflowStep { gameCatalog, gameSetup }

enum _CatalogFilterTab { installed, store }

enum _ExitChoice { keepUnfinished, endSession, cancel }

class ControlScreen extends StatefulWidget {
  final DeviceInfo device;
  final Student student;
  final DiscoveryService discoveryService;

  const ControlScreen({
    super.key,
    required this.device,
    required this.student,
    required this.discoveryService,
  });

  @override
  State<ControlScreen> createState() => _ControlScreenState();
}

class _ControlScreenState extends State<ControlScreen>
    with WidgetsBindingObserver {
  static final bool _contentDeliveryEnabled = true;
  static final bool _serverAuthoritativeHandoffGate = true;
  static const bool _showCatalogRescueTerminateButton = false;
  static const List<_GameCatalogEntry> _fallbackGameCatalog =
      <_GameCatalogEntry>[
    _GameCatalogEntry(
      gameId: 'demo_cube_clicker',
      title: 'Demo Cube Clicker',
      description:
          'Wersja pogladowa: klikaj poruszajace sie cubey, mierz czas i best score.',
      targetContentVersion: '1.2.0',
      packageUri: '',
      thumbnailUrl: '',
      supportsSaveResume: true,
      availableForPurchase: false,
      requiresExplicitLicense: false,
      runtimeLaunchEnabled: true,
      sortOrder: 10,
      previewLines: <String>[
        'Poziom basic: dowolny kolor.',
        'Poziom alternation: kolory na zmiane.',
        'Poziom random target: aktywny kolor celu zmienia sie dynamicznie.',
      ],
    ),
    _GameCatalogEntry(
      gameId: 'pulse_target_tap',
      title: 'Pulse Targets',
      description:
          'Sekwencja celow z adaptacja trudnosci i etykietowaniem task run.',
      targetContentVersion: '1.0.0',
      packageUri: '',
      thumbnailUrl: '',
      supportsSaveResume: false,
      availableForPurchase: false,
      requiresExplicitLicense: false,
      runtimeLaunchEnabled: true,
      sortOrder: 20,
      previewLines: <String>[
        'Klikaj aktywne cele zgodnie z sekwencja.',
        'Adaptive difficulty dopasowuje tempo i skale.',
        'Generuje TASK_OUTCOME_SUMMARY + TASK_LABEL_GENERATED.',
      ],
    ),
    _GameCatalogEntry(
      gameId: 'puzzle_paths',
      title: 'Puzzle Paths',
      description:
          'Placeholder katalogowy pod sesje puzzli. Runtime launch jeszcze nieaktywny.',
      targetContentVersion: '0.9.0',
      packageUri: 'https://cdn.theraply.local/content/puzzle_paths_0_9_0',
      thumbnailUrl: '',
      supportsSaveResume: false,
      availableForPurchase: true,
      requiresExplicitLicense: true,
      runtimeLaunchEnabled: false,
      sortOrder: 30,
      previewLines: <String>[
        'Tryb demonstracyjny: karta katalogu + status instalacji.',
        'Docelowo: panel dynamiczny z kontrolkami z Unity schema.',
      ],
    ),
    _GameCatalogEntry(
      gameId: 'memory_orchard',
      title: 'Memory Orchard',
      description:
          'Placeholder katalogowy pod gre memory. Runtime launch jeszcze nieaktywny.',
      targetContentVersion: '0.9.0',
      packageUri: 'https://cdn.theraply.local/content/memory_orchard_0_9_0',
      thumbnailUrl: '',
      supportsSaveResume: false,
      availableForPurchase: true,
      requiresExplicitLicense: true,
      runtimeLaunchEnabled: false,
      sortOrder: 40,
      previewLines: <String>[
        'Kontrolki i konfiguracja beda ladowane po schema.',
      ],
    ),
    _GameCatalogEntry(
      gameId: 'sunflower_defense',
      title: 'Sunflower Defense',
      description:
          'Placeholder katalogowy pod obrone slonecznikow. Runtime launch jeszcze nieaktywny.',
      targetContentVersion: '0.9.0',
      packageUri: 'https://cdn.theraply.local/content/sunflower_defense_0_9_0',
      thumbnailUrl: '',
      supportsSaveResume: false,
      availableForPurchase: true,
      requiresExplicitLicense: true,
      runtimeLaunchEnabled: false,
      sortOrder: 50,
      previewLines: <String>[
        'Sciezka pod test pipeline instalacji i statusow.',
      ],
    ),
    _GameCatalogEntry(
      gameId: 'coding_master',
      title: 'Coding Master',
      description:
          'Placeholder katalogowy pod mistrza kodowania. Runtime launch jeszcze nieaktywny.',
      targetContentVersion: '0.9.0',
      packageUri: 'https://cdn.theraply.local/content/coding_master_0_9_0',
      thumbnailUrl: '',
      supportsSaveResume: false,
      availableForPurchase: true,
      requiresExplicitLicense: true,
      runtimeLaunchEnabled: false,
      sortOrder: 60,
      previewLines: <String>[
        'Karta katalogu gotowa pod future dynamic control schema.',
      ],
    ),
    _GameCatalogEntry(
      gameId: 'bilateral_markers',
      title: 'Bilateral Markers',
      description:
          'Placeholder katalogowy pod zaznacz oburecz. Runtime launch jeszcze nieaktywny.',
      targetContentVersion: '0.9.0',
      packageUri: 'https://cdn.theraply.local/content/bilateral_markers_0_9_0',
      thumbnailUrl: '',
      supportsSaveResume: false,
      availableForPurchase: true,
      requiresExplicitLicense: true,
      runtimeLaunchEnabled: false,
      sortOrder: 70,
      previewLines: <String>[
        'Na ten etap: store/install/status UX + gotowosc pod schema.',
      ],
    ),
  ];

  static const Set<String> _gameScopedCriticalCommands = <String>{
    CriticalCommandIds.startGame,
    CriticalCommandIds.pauseGame,
    CriticalCommandIds.resumeGame,
    CriticalCommandIds.stopGame,
  };

  static const String _demoCubeGameId = 'demo_cube_clicker';
  static const String _pulseTargetGameId = 'pulse_target_tap';
  static const String _updateConfigCommandId = 'UPDATE_CONFIG';
  static const String _interruptedAutoCloseReasonCode =
      'INTERRUPTED_AUTO_CLOSED_TIMEOUT';
  static const Duration _recentlyEndedSessionTtl = Duration(seconds: 20);
  static const Duration _discoveryCandidateFreshTtl = Duration(seconds: 12);
  static const Duration _connectionLivenessPollInterval = Duration(seconds: 2);
  static const Duration _connectionSignalGracePeriod = Duration(seconds: 8);
  static const Duration _connectionSignalFallbackTimeout =
      Duration(seconds: 12);
  static final Map<String, DateTime> _recentlyEndedSessionIds =
      <String, DateTime>{};
  static const List<String> _demoLevelModes = <String>[
    'basic',
    'alternate_colors',
    'random_target_color',
  ];

  final ConnectionService _connection = ConnectionService();
  late final OperatorIncidentPopupQueue _incidentPopupQueue;
  final Set<String> _simulatedOwnedGameIds = <String>{};
  List<_GameCatalogEntry> _remoteGameCatalog = const <_GameCatalogEntry>[];
  _CatalogFilterTab _catalogFilterTab = _CatalogFilterTab.installed;

  bool _isConnected = false;
  bool _requiresSessionDecision = false;
  bool _isSessionDecisionDialogOpen = false;
  bool _isPrimaryActionInFlight = false;
  bool _allowSystemPop = false;
  bool _contentSyncInFlight = false;
  bool _autoReconnectLoopActive = false;
  bool _autoReconnectEnabled = true;
  bool _isVideoPreviewExpanded = false;
  bool _optimisticRuntimeActive = false;
  bool _optimisticRuntimePaused = false;
  bool _sessionAttachInFlight = false;
  bool _sessionAttachReady = false;
  bool _hasConnectedAtLeastOnce = false;
  TherapistSessionSettings _therapistSessionSettings =
      TherapistSessionSettings.defaults();

  SessionLifecycleState? _sessionLifecycleState;
  TherapistRuntimeStatus? _runtimeStatus;
  final Map<String, PurchasedContentState> _contentStatesByGameId =
      <String, PurchasedContentState>{};
  final Set<String> _contentActionsInFlight = <String>{};

  StreamSubscription<bool>? _connectionSubscription;
  StreamSubscription<Map<String, dynamic>>? _messageSubscription;
  StreamSubscription<DeviceInfo>? _discoverySubscription;
  StreamSubscription<List<GameCatalogEntry>>? _gameCatalogSubscription;
  Timer? _connectionLivenessTimer;

  late String _activeSessionId;
  late String _selectedGameId;
  String? _remoteSessionIdPendingDecision;
  String? _remoteActiveGameId;
  String? _lastSessionStateUpdateSessionId;
  String? _lastRuntimeStatusSessionId;
  TherapySessionRecord? _latestPersistedSession;
  ParentProgressSnapshot _parentProgressSnapshot = ParentProgressSnapshot.empty;
  List<StudentRewardUnlock> _recentRewardUnlocks =
      const <StudentRewardUnlock>[];
  bool _parentInsightsLoading = false;
  bool _persistedSessionRefreshInFlight = false;
  bool _interruptedSessionAutoCloseInFlight = false;
  bool _ownershipConflictRecoveryInFlight = false;
  DeviceInfo? _latestDiscoveryReconnectCandidate;
  DateTime? _latestDiscoveryReconnectSeenAt;
  DateTime? _connectedAtUtc;
  DateTime? _lastConnectionLostAtUtc;
  DateTime? _lastRuntimeSignalAtUtc;
  DevicePresenceUpdateSignal? _lastDevicePresenceSignal;
  int _lastWatchdogStaleAfterMs = 6000;
  bool _livenessRecoveryInFlight = false;
  String? _disconnectReasonOverride;
  final TextEditingController _timelineNoteController = TextEditingController();
  bool _timelineNoteInFlight = false;
  String? _deferredHandoffSessionId;
  DateTime? _deferredHandoffMarkedAtUtc;
  String? _deferredHandoffReasonCode;

  _WorkflowStep _workflowStep = _WorkflowStep.gameCatalog;

  int _demoCubeCount = 12;
  double _demoCubeSpeed = 0.7;
  String _demoLevelMode = _demoLevelModes.first;
  int _pulseTargetCount = 8;
  double _pulseTargetSpeed = 0.7;
  double _pulseTargetScale = 0.3;
  final Map<String, dynamic> _dynamicControlValuesByControlId =
      <String, dynamic>{};
  String _dynamicControlValuesGameId = '';

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _incidentPopupQueue = OperatorIncidentPopupQueue(
      queueName: 'control_screen',
      languageResolver: () => _therapistSessionSettings.operatorUiLanguage,
    );

    _connection.setDiscoveryService(widget.discoveryService);
    _startGameCatalogSubscription();
    _activeSessionId = _buildLocalSessionId();
    _selectedGameId = _resolveInitialGameId();
    _bootstrapLocalContentStates();

    _setupConnectionListeners();
    _setupDiscoveryListener();
    _startConnectionLivenessWatchdog();
    unawaited(ForegroundServiceBridge.start());
    unawaited(_loadTherapistSessionSettings());
    unawaited(_refreshPersistedSessionSnapshot(triggerPrompt: true));
    unawaited(_refreshParentInsights());
    unawaited(_connect());
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      unawaited(_recoverConnectionAfterResume());
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _autoReconnectEnabled = false;
    _timelineNoteController.dispose();
    _connectionSubscription?.cancel();
    _messageSubscription?.cancel();
    _discoverySubscription?.cancel();
    _gameCatalogSubscription?.cancel();
    _connectionLivenessTimer?.cancel();
    unawaited(ForegroundServiceBridge.stop());
    _connection.dispose();
    super.dispose();
  }

  void _setupConnectionListeners() {
    _connectionSubscription = _connection.connectionStatus.listen((connected) {
      if (!mounted) {
        return;
      }

      final wasConnected = _isConnected;
      setState(() {
        _isConnected = connected;
        if (!connected) {
          _sessionAttachReady = false;
          _lastDevicePresenceSignal = null;
        }
      });

      if (connected) {
        _connectedAtUtc = DateTime.now().toUtc();
        _lastRuntimeSignalAtUtc = null;
        _disconnectReasonOverride = null;
        _livenessRecoveryInFlight = false;

        final attachReason =
            _hasConnectedAtLeastOnce ? 'AUTO_RECONNECT' : 'INITIAL_CONNECT';
        _hasConnectedAtLeastOnce = true;

        if (_contentDeliveryEnabled) {
          unawaited(_syncContentCatalog(silent: true));
        }
        unawaited(_loadTherapistSessionSettings());
        unawaited(_refreshPersistedSessionSnapshot(triggerPrompt: true));
        if (!wasConnected) {
          final connectionEventType = attachReason == 'INITIAL_CONNECT'
              ? 'CONTROLLER_CONNECTED'
              : 'CONTROLLER_RECONNECTED';
          unawaited(
            _recordConnectionLifecycleEvent(
              eventType: connectionEventType,
              reasonCode: attachReason,
            ),
          );
          unawaited(
            _ensureSessionAttached(
              reasonCode: attachReason,
              force: true,
            ),
          );
        }
      } else {
        _connectedAtUtc = null;
        if (wasConnected) {
          _lastConnectionLostAtUtc = DateTime.now().toUtc();
        }
        _lastRuntimeSignalAtUtc = null;
        _livenessRecoveryInFlight = false;
        if (wasConnected) {
          final disconnectReason = _disconnectReasonOverride ?? 'TCP_LINK_LOST';
          _disconnectReasonOverride = null;
          unawaited(
            _recordConnectionLifecycleEvent(
              eventType: 'CONTROLLER_DISCONNECTED',
              reasonCode: disconnectReason,
            ),
          );
        } else {
          _disconnectReasonOverride = null;
        }
        _startAutoReconnectLoop(reason: 'connection_lost');
      }
    });

    _messageSubscription = _connection.messages.listen((message) {
      final previousSessionState = _sessionLifecycleState;
      final sessionUpdate = _filterSessionUpdateByOwnership(
        SessionStateUpdateSignal.tryFromNetworkMessage(message),
      );
      final runtimeUpdate = _filterRuntimeUpdateByOwnership(
        RuntimeStatusUpdateSignal.tryFromNetworkMessage(message),
      );
      final devicePresenceUpdate = _filterDevicePresenceByOwnership(
        DevicePresenceUpdateSignal.tryFromNetworkMessage(message),
      );
      final watchdogHeartbeat = _filterWatchdogHeartbeatByOwnership(
        SessionWatchdogHeartbeatSignal.tryFromNetworkMessage(message),
      );
      final watchdogSessionState = SessionLifecycleState.tryParse(
        watchdogHeartbeat?.sessionState,
      );
      final contentStatusSignal =
          ContentInstallStatusSignal.tryFromNetworkMessage(message);

      if (mounted && devicePresenceUpdate != null) {
        final previousSignal = _lastDevicePresenceSignal;
        _lastRuntimeSignalAtUtc = DateTime.now().toUtc();

        setState(() {
          _lastDevicePresenceSignal = devicePresenceUpdate;
        });

        _handleDevicePresenceFeedback(
          previousSignal: previousSignal,
          signal: devicePresenceUpdate,
        );
      }

      if (mounted &&
          (sessionUpdate != null ||
              runtimeUpdate != null ||
              watchdogHeartbeat != null)) {
        final shouldAdoptCreatedSessionRollover = sessionUpdate != null &&
            sessionUpdate.state == SessionLifecycleState.created &&
            _shouldAdoptCreatedSessionRollover(
              sessionUpdate.sessionId,
              activeSessionStateOverride: previousSessionState,
            );
        _lastRuntimeSignalAtUtc = DateTime.now().toUtc();
        if (watchdogHeartbeat != null && watchdogHeartbeat.staleAfterMs > 0) {
          _lastWatchdogStaleAfterMs = watchdogHeartbeat.staleAfterMs;
        }
        setState(() {
          if (sessionUpdate != null) {
            if (shouldAdoptCreatedSessionRollover) {
              final previousActiveSessionId = _activeSessionId;
              _activeSessionId = sessionUpdate.sessionId.trim();
              _sessionAttachReady = true;
              debugPrint(
                '[ControlScreen][Ownership] Accepted rollover CREATED signal: '
                'activeSession=$previousActiveSessionId '
                'incomingSession=$_activeSessionId',
              );
            }
            _lastSessionStateUpdateSessionId = sessionUpdate.sessionId;
            _sessionLifecycleState = sessionUpdate.state;
            if (SessionRecoveryPolicy.isTerminalState(sessionUpdate.state)) {
              _optimisticRuntimeActive = false;
              _optimisticRuntimePaused = false;
              _remoteActiveGameId = null;
            } else if (sessionUpdate.state ==
                SessionLifecycleState.inProgress) {
              _optimisticRuntimeActive = true;
              _optimisticRuntimePaused = false;
            } else if (sessionUpdate.state == SessionLifecycleState.paused) {
              _optimisticRuntimeActive = true;
              _optimisticRuntimePaused = true;
            } else if (sessionUpdate.state ==
                SessionLifecycleState.interrupted) {
              _optimisticRuntimeActive = true;
            }
          }

          if (runtimeUpdate != null) {
            _lastRuntimeStatusSessionId = runtimeUpdate.sessionId;
            _runtimeStatus = runtimeUpdate.status;
            switch (runtimeUpdate.status) {
              case TherapistRuntimeStatus.playing:
                _optimisticRuntimeActive = true;
                _optimisticRuntimePaused = false;
                break;
              case TherapistRuntimeStatus.paused:
                _optimisticRuntimeActive = true;
                _optimisticRuntimePaused = true;
                break;
              case TherapistRuntimeStatus.interrupted:
                _optimisticRuntimeActive = true;
                break;
              case TherapistRuntimeStatus.syncPending:
                // Sync backlog is not evidence of an unfinished in-progress game.
                break;
              case TherapistRuntimeStatus.connected:
                _optimisticRuntimePaused = false;
                break;
            }
          }

          if (watchdogHeartbeat != null) {
            final activeGameId = watchdogHeartbeat.activeGameId.trim();
            if (activeGameId.isNotEmpty) {
              _remoteActiveGameId = activeGameId;
              _optimisticRuntimeActive = true;
            } else {
              _remoteActiveGameId = null;
              if (sessionUpdate == null && runtimeUpdate == null) {
                _optimisticRuntimeActive = false;
                _optimisticRuntimePaused = false;
              }
            }

            final activeGameState =
                watchdogHeartbeat.activeGameState.trim().toUpperCase();
            if (activeGameState == 'PAUSED') {
              _optimisticRuntimePaused = true;
            } else if (activeGameState == 'PLAYING' ||
                activeGameState == 'RUNNING' ||
                activeGameState == 'IN_PROGRESS') {
              _optimisticRuntimePaused = false;
            }

            if (watchdogSessionState != null &&
                SessionRecoveryPolicy.isTerminalState(watchdogSessionState)) {
              _optimisticRuntimeActive = false;
              _optimisticRuntimePaused = false;
              _remoteActiveGameId = null;
            }
          }
        });

        if (sessionUpdate != null &&
            SessionRecoveryPolicy.isTerminalState(sessionUpdate.state)) {
          _markSessionAsRecentlyEnded(sessionUpdate.sessionId);
        }
        if (watchdogHeartbeat != null &&
            watchdogSessionState != null &&
            SessionRecoveryPolicy.isTerminalState(watchdogSessionState)) {
          _markSessionAsRecentlyEnded(watchdogHeartbeat.sessionId);
        }
        if (sessionUpdate != null) {
          unawaited(_persistRuntimeSessionState(sessionUpdate));
        }

        _handlePotentialSessionDecisionGate(
          sessionUpdate,
          runtimeUpdate,
          watchdogHeartbeat,
        );
        _handleSessionTerminalStateFeedback(
          previousState: previousSessionState,
          sessionUpdate: sessionUpdate,
          runtimeUpdate: runtimeUpdate,
        );
      }

      if (mounted && contentStatusSignal != null) {
        _applyContentInstallStatusSignal(contentStatusSignal);
      }
    });
  }

  void _startConnectionLivenessWatchdog() {
    _connectionLivenessTimer?.cancel();
    _connectionLivenessTimer = Timer.periodic(
      _connectionLivenessPollInterval,
      (_) => unawaited(_evaluateConnectionLiveness()),
    );
  }

  Future<void> _evaluateConnectionLiveness() async {
    if (!mounted ||
        !_autoReconnectEnabled ||
        !_isConnected ||
        _allowSystemPop ||
        _autoReconnectLoopActive ||
        _sessionAttachInFlight ||
        _livenessRecoveryInFlight) {
      return;
    }

    if (!_sessionAttachReady) {
      return;
    }

    final connectedAtUtc = _connectedAtUtc;
    if (connectedAtUtc == null) {
      return;
    }

    final nowUtc = DateTime.now().toUtc();
    if (nowUtc.difference(connectedAtUtc) < _connectionSignalGracePeriod) {
      return;
    }

    final staleAfterMs = _lastWatchdogStaleAfterMs > 0
        ? _lastWatchdogStaleAfterMs
        : _connectionSignalFallbackTimeout.inMilliseconds;
    final adaptiveTimeout = Duration(milliseconds: staleAfterMs + 2500);
    final timeout = adaptiveTimeout < _connectionSignalFallbackTimeout
        ? _connectionSignalFallbackTimeout
        : adaptiveTimeout;

    final lastSignalAtUtc = _lastRuntimeSignalAtUtc;
    if (lastSignalAtUtc == null) {
      if (nowUtc.difference(connectedAtUtc) >= timeout) {
        await _forceLivenessReconnect('NO_RUNTIME_SIGNAL_TIMEOUT');
      }
      return;
    }

    if (nowUtc.difference(lastSignalAtUtc) >= timeout) {
      await _forceLivenessReconnect('RUNTIME_SIGNAL_STALE');
    }
  }

  Future<void> _forceLivenessReconnect(String reasonCode) async {
    if (!mounted ||
        !_isConnected ||
        _allowSystemPop ||
        _livenessRecoveryInFlight) {
      return;
    }

    _livenessRecoveryInFlight = true;
    _disconnectReasonOverride = reasonCode;
    debugPrint(
      '[ControlScreen] Liveness watchdog forcing disconnect ($reasonCode)',
    );

    try {
      await _connection.disconnect();
    } finally {
      _livenessRecoveryInFlight = false;
    }
  }

  void _setupDiscoveryListener() {
    _discoverySubscription = widget.discoveryService.devices.listen((device) {
      if (!_matchesReconnectCandidate(device)) {
        return;
      }

      _latestDiscoveryReconnectCandidate = device;
      _latestDiscoveryReconnectSeenAt = DateTime.now().toUtc();

      if (!mounted || _allowSystemPop || _connection.isConnected) {
        return;
      }

      if (!_autoReconnectLoopActive) {
        _startAutoReconnectLoop(reason: 'discovery_candidate');
      }
    });
  }

  bool _matchesReconnectCandidate(DeviceInfo device) {
    if (device.controlPort != widget.device.controlPort) {
      return false;
    }

    if (device.deviceId == widget.device.deviceId) {
      return true;
    }

    if (device.studentId != null && device.studentId == widget.student.id) {
      return true;
    }

    if (device.deviceName == widget.device.deviceName) {
      return true;
    }

    return false;
  }

  DeviceInfo? _resolveFreshDiscoveryReconnectCandidate() {
    final candidate = _latestDiscoveryReconnectCandidate;
    final seenAt = _latestDiscoveryReconnectSeenAt;
    if (candidate == null || seenAt == null) {
      return null;
    }

    final age = DateTime.now().toUtc().difference(seenAt);
    if (age > _discoveryCandidateFreshTtl) {
      return null;
    }

    return candidate;
  }

  Future<bool> _tryReconnectViaDiscoveryCandidate() async {
    final candidate = _resolveFreshDiscoveryReconnectCandidate();
    if (candidate == null) {
      return false;
    }

    final candidateIp = candidate.ip.trim();
    if (candidateIp.isEmpty) {
      return false;
    }

    final connected = await _connection.connect(
      candidateIp,
      candidate.controlPort,
    );
    if (connected) {
      debugPrint(
        '[ControlScreen] Reconnected via discovery candidate '
        '${candidate.deviceName} @ ${candidate.ip}:${candidate.controlPort}',
      );
    }
    return connected;
  }

  Future<void> _connect() async {
    final success = await _connection.connect(
      widget.device.ip,
      widget.device.controlPort,
    );

    if (!success && mounted) {
      _autoReconnectEnabled = false;
      _enqueueIncidentAlert(
        title: 'Failed to connect to device',
        message:
            'Could not connect to ${widget.device.deviceName} (${widget.device.ip}:${widget.device.controlPort}).',
        reasonCode: 'DISCONNECTED',
      );
      _allowSystemPop = true;
      Navigator.pop(context);
    }
  }

  Future<void> _recoverConnectionAfterResume() async {
    if (!mounted || _connection.isConnected) {
      return;
    }
    _startAutoReconnectLoop(reason: 'app_resumed');
  }

  void _startAutoReconnectLoop({required String reason}) {
    if (!mounted ||
        !_autoReconnectEnabled ||
        _allowSystemPop ||
        _connection.isConnected ||
        _autoReconnectLoopActive) {
      return;
    }

    _autoReconnectLoopActive = true;
    unawaited(_runAutoReconnectLoop(reason: reason));
  }

  Future<void> _runAutoReconnectLoop({required String reason}) async {
    while (mounted &&
        _autoReconnectEnabled &&
        !_allowSystemPop &&
        !_connection.isConnected) {
      var ok = await _connection.reconnect(
        maxAttempts: 4,
        baseDelay: const Duration(milliseconds: 350),
      );

      if (!ok) {
        ok = await _tryReconnectViaDiscoveryCandidate();
      }

      if (!mounted || !_autoReconnectEnabled || _allowSystemPop) {
        break;
      }

      if (ok) {
        if (mounted && _sessionAttachReady) {
          setState(() {
            _sessionAttachReady = false;
          });
        }
        // Keep _isConnected transitions sourced from connectionStatus stream.
        // Otherwise listener-level "wasConnected" detection can be bypassed and
        // SESSION_ATTACH bootstrap may be skipped after reconnect.
        break;
      }

      if (mounted) {
        setState(() {
          _isConnected = false;
        });
      }

      await Future<void>.delayed(const Duration(seconds: 2));
    }

    _autoReconnectLoopActive = false;
    debugPrint('[ControlScreen] Auto reconnect loop stopped ($reason)');
  }

  String _buildLocalSessionId() {
    return 'mobile-${widget.student.id}-${DateTime.now().toUtc().millisecondsSinceEpoch}';
  }

  String _resolveAttachTargetSessionId() {
    final runtimeSessionId = _lastSessionStateUpdateSessionId?.trim() ?? '';
    if (runtimeSessionId.isNotEmpty) {
      return runtimeSessionId;
    }

    final runtimeStatusSessionId = _lastRuntimeStatusSessionId?.trim() ?? '';
    if (runtimeStatusSessionId.isNotEmpty) {
      return runtimeStatusSessionId;
    }

    final pendingDecisionId = _remoteSessionIdPendingDecision?.trim() ?? '';
    if (pendingDecisionId.isNotEmpty) {
      return pendingDecisionId;
    }

    final persisted = _latestPersistedSession;
    if (persisted != null && persisted.requiresHandoffDecision) {
      final persistedSessionId = persisted.sessionId.trim();
      if (persistedSessionId.isNotEmpty &&
          !_wasSessionRecentlyEnded(persistedSessionId)) {
        return persistedSessionId;
      }
    }

    final activeSessionId = _activeSessionId.trim();
    if (activeSessionId.isNotEmpty) {
      return activeSessionId;
    }

    final generatedSessionId = _buildLocalSessionId();
    _activeSessionId = generatedSessionId;
    return generatedSessionId;
  }

  Future<void> _ensureSessionAttached({
    required String reasonCode,
    bool force = false,
    String? sessionIdOverride,
  }) async {
    if (!mounted || !_isConnected || _allowSystemPop) {
      _logAttachDecision(
        decision: 'SKIP_ATTACH',
        reasonCode: reasonCode,
        sessionId: sessionIdOverride,
      );
      return;
    }
    if (_sessionAttachInFlight) {
      _logAttachDecision(
        decision: 'SKIP_ATTACH_IN_FLIGHT',
        reasonCode: reasonCode,
        sessionId: sessionIdOverride,
      );
      return;
    }
    if (_sessionAttachReady && !force) {
      _logAttachDecision(
        decision: 'SKIP_ALREADY_READY',
        reasonCode: reasonCode,
        sessionId: sessionIdOverride,
      );
      return;
    }

    final targetSessionId =
        (sessionIdOverride ?? _resolveAttachTargetSessionId()).trim();
    if (targetSessionId.isEmpty) {
      _logAttachDecision(
        decision: 'SKIP_EMPTY_SESSION_ID',
        reasonCode: reasonCode,
      );
      return;
    }

    final therapistId = _resolveActorTherapistId();
    final ownerKey = _resolveOwnerKey(therapistIdOverride: therapistId);
    final sessionKey = _resolveSessionKey(
      targetSessionId,
      therapistIdOverride: therapistId,
    );
    _sessionAttachInFlight = true;
    var retryAttachWithEndSessionOverride = false;
    if (mounted) {
      setState(() {});
    }

    try {
      _logAttachDecision(
        decision: 'SEND_SESSION_ATTACH',
        reasonCode: reasonCode,
        commandId: CriticalCommandIds.sessionAttach,
        sessionId: targetSessionId,
      );
      await _connection.sendCriticalCommand(
        commandId: CriticalCommandIds.sessionAttach,
        sessionId: targetSessionId,
        payload: <String, dynamic>{
          'sessionId': targetSessionId,
          'studentId': widget.student.id,
          'patientId': widget.student.id,
          'therapistId': therapistId,
          'ownerKey': ownerKey,
          'sessionKey': sessionKey,
          'reasonCode': reasonCode,
          'origin': 'mobile_controller',
        },
        expiresAtUtc: DateTime.now().toUtc().add(const Duration(seconds: 30)),
      );

      if (!mounted) {
        return;
      }

      setState(() {
        _sessionAttachReady = true;
        _activeSessionId = targetSessionId;
      });
      _logAttachDecision(
        decision: 'SESSION_ATTACH_ACK',
        reasonCode: reasonCode,
        commandId: CriticalCommandIds.sessionAttach,
        sessionId: targetSessionId,
      );

      await _recordConnectionLifecycleEvent(
        eventType: 'SESSION_ATTACH_ACK',
        reasonCode: reasonCode,
        sessionIdOverride: targetSessionId,
      );
    } catch (e) {
      if (!mounted) {
        return;
      }

      final failureReasonCode =
          OpsErrorCatalog.tryExtractReasonCode(e) ?? reasonCode;
      final attachReasonTag = OpsErrorCatalog.buildReasonTag(failureReasonCode);
      final errorSummary = OpsErrorCatalog.buildOperatorSummary(
        error: e,
        fallbackReasonCode: reasonCode,
      );

      setState(() {
        _sessionAttachReady = false;
      });
      _logAttachDecision(
        decision: 'SESSION_ATTACH_FAILED',
        reasonCode: reasonCode,
        commandId: CriticalCommandIds.sessionAttach,
        sessionId: targetSessionId,
      );

      if (failureReasonCode == 'SESSION_OWNERSHIP_CONFLICT' &&
          reasonCode != 'END_SESSION_OVERRIDE') {
        final recovered = await _tryRecoverSessionOwnershipConflict(
          targetSessionId: targetSessionId,
        );
        if (recovered) {
          retryAttachWithEndSessionOverride = true;
          _logAttachDecision(
            decision: 'SESSION_ATTACH_RECOVERY_OWNERSHIP_OVERRIDE',
            reasonCode: 'END_SESSION_OVERRIDE',
            commandId: CriticalCommandIds.sessionAttach,
            sessionId: targetSessionId,
          );
        }
      }

      if (retryAttachWithEndSessionOverride) {
        return;
      }

      final shouldShowAttachFailurePopup =
          SessionAttachFailurePolicy.shouldShowPopup(
        attachReasonCode: reasonCode,
        failureReasonCode: failureReasonCode,
        isGameSetupWorkflow: _workflowStep == _WorkflowStep.gameSetup,
      );

      if (shouldShowAttachFailurePopup) {
        _enqueueIncidentAlert(
          title: 'Session attach failed',
          message: 'Session attach failed [$attachReasonTag]: $errorSummary',
          reasonCode: failureReasonCode,
          severity: OperatorIncidentSeverity.error,
        );
      } else {
        _logAttachDecision(
          decision: 'SESSION_ATTACH_FAILED_TRANSIENT_SUPPRESSED',
          reasonCode: failureReasonCode,
          commandId: CriticalCommandIds.sessionAttach,
          sessionId: targetSessionId,
        );
      }
    } finally {
      _sessionAttachInFlight = false;
      if (mounted) {
        setState(() {});
      }
      if (retryAttachWithEndSessionOverride && mounted) {
        await _ensureSessionAttached(
          reasonCode: 'END_SESSION_OVERRIDE',
          force: true,
          sessionIdOverride: targetSessionId,
        );
      }
    }
  }

  Future<bool> _tryRecoverSessionOwnershipConflict({
    required String targetSessionId,
  }) async {
    if (!_isConnected || _ownershipConflictRecoveryInFlight) {
      return false;
    }

    _ownershipConflictRecoveryInFlight = true;
    try {
      final ended = await _sendEndSessionWithConfirmation(
        reasonCode: 'END_SESSION_OVERRIDE',
        extraPayload: const <String, dynamic>{
          'reason': 'OwnershipConflictRecovery',
          'reasonCode': 'END_SESSION_OVERRIDE',
        },
      );
      if (!ended) {
        return false;
      }

      if (!mounted) {
        return false;
      }

      setState(() {
        _sessionAttachReady = false;
        _activeSessionId = targetSessionId;
        _requiresSessionDecision = false;
        _remoteSessionIdPendingDecision = null;
      });
      return true;
    } finally {
      _ownershipConflictRecoveryInFlight = false;
    }
  }

  Future<void> _recordConnectionLifecycleEvent({
    required String eventType,
    required String reasonCode,
    String? sessionIdOverride,
  }) async {
    final sessionId =
        (sessionIdOverride ?? _resolveAttachTargetSessionId()).trim();
    if (sessionId.isEmpty) {
      return;
    }

    final therapistId = _resolveActorTherapistId();
    final latestPersistedId = _latestPersistedSession?.sessionId.trim() ?? '';
    final requiresHandoff =
        _latestPersistedSession?.requiresHandoffDecision ?? false;
    final shouldSeedCreatedState = (eventType == 'CONTROLLER_CONNECTED' ||
            eventType == 'CONTROLLER_RECONNECTED') &&
        sessionId.startsWith('mobile-') &&
        !requiresHandoff &&
        latestPersistedId != sessionId;

    try {
      if (shouldSeedCreatedState) {
        await SessionJournalService.upsertSessionState(
          sessionId: sessionId,
          studentId: widget.student.id,
          therapistId: therapistId,
          state: SessionLifecycleState.created,
          latestGameId: _selectedGameId,
          reasonCode: reasonCode,
          metadata: <String, dynamic>{
            'origin': 'mobile_connection',
            'eventType': eventType,
          },
        );
      }

      await SessionJournalService.appendSessionEvent(
        sessionId: sessionId,
        studentId: widget.student.id,
        therapistId: therapistId,
        eventType: eventType,
        gameId: _selectedGameId,
        details: <String, dynamic>{
          'reasonCode': reasonCode,
          'transportConnected': _isConnected,
        },
      );
    } catch (e) {
      debugPrint(
        '[ControlScreen] Connection lifecycle event persist failed: event=$eventType, reason=$reasonCode, error=$e',
      );
    }
  }

  void _pruneRecentlyEndedSessions() {
    final nowUtc = DateTime.now().toUtc();
    final staleIds = <String>[];
    _recentlyEndedSessionIds.forEach((sessionId, endedAtUtc) {
      if (nowUtc.difference(endedAtUtc) > _recentlyEndedSessionTtl) {
        staleIds.add(sessionId);
      }
    });
    for (final sessionId in staleIds) {
      _recentlyEndedSessionIds.remove(sessionId);
    }
  }

  void _markSessionAsRecentlyEnded(String? sessionId) {
    final normalizedSessionId = sessionId?.trim() ?? '';
    if (normalizedSessionId.isEmpty) {
      return;
    }

    _pruneRecentlyEndedSessions();
    _recentlyEndedSessionIds[normalizedSessionId] = DateTime.now().toUtc();
  }

  bool _wasSessionRecentlyEnded(String sessionId) {
    _pruneRecentlyEndedSessions();
    return _recentlyEndedSessionIds.containsKey(sessionId);
  }

  bool _isRuntimeStatusNonTerminal(TherapistRuntimeStatus? status) {
    switch (status) {
      case TherapistRuntimeStatus.playing:
      case TherapistRuntimeStatus.paused:
      case TherapistRuntimeStatus.interrupted:
        return true;
      case TherapistRuntimeStatus.syncPending:
      case TherapistRuntimeStatus.connected:
      case null:
        return false;
    }
  }

  bool get _isStableActiveSessionContext {
    return _workflowStep == _WorkflowStep.gameSetup ||
        _isGameRuntimeActive ||
        _isPrimaryActionInFlight;
  }

  bool _isSessionDecisionAllowedByContext() {
    if (_allowSystemPop) {
      return false;
    }

    if (_isStableActiveSessionContext) {
      return false;
    }

    if (!_isConnected) {
      return !_hasConnectedAtLeastOnce;
    }

    if (_sessionAttachInFlight || !_sessionAttachReady) {
      return true;
    }

    if (_workflowStep != _WorkflowStep.gameCatalog) {
      return false;
    }

    return true;
  }

  bool get _hasDeferredHandoff {
    return (_deferredHandoffSessionId?.trim().isNotEmpty ?? false);
  }

  String get _deferredHandoffSessionIdValue {
    return _deferredHandoffSessionId?.trim() ?? '';
  }

  void _markDeferredHandoff({
    required String sessionId,
    required String reasonCode,
    required String source,
  }) {
    final normalizedSessionId = sessionId.trim();
    if (normalizedSessionId.isEmpty ||
        _wasSessionRecentlyEnded(normalizedSessionId)) {
      return;
    }

    final normalizedReasonCode = reasonCode.trim();
    final previousSessionId = _deferredHandoffSessionId?.trim() ?? '';
    final previousReasonCode = _deferredHandoffReasonCode?.trim() ?? '';
    final shouldUpdate = previousSessionId != normalizedSessionId ||
        previousReasonCode != normalizedReasonCode;
    if (!shouldUpdate) {
      return;
    }

    if (mounted) {
      setState(() {
        _deferredHandoffSessionId = normalizedSessionId;
        _deferredHandoffReasonCode = normalizedReasonCode;
        _deferredHandoffMarkedAtUtc = DateTime.now().toUtc();
      });
    } else {
      _deferredHandoffSessionId = normalizedSessionId;
      _deferredHandoffReasonCode = normalizedReasonCode;
      _deferredHandoffMarkedAtUtc = DateTime.now().toUtc();
    }

    _logSessionDecision(
      source: source,
      decision: 'DEFERRED_BADGE_SET',
      sessionId: normalizedSessionId,
      reason: normalizedReasonCode,
    );
    unawaited(
      _recordDeferredHandoffTimelineEvent(
        sessionId: normalizedSessionId,
        eventType: 'SESSION_HANDOFF_DEFERRED',
        reasonCode: normalizedReasonCode,
      ),
    );
  }

  void _clearDeferredHandoff({
    String? sessionId,
    required String source,
    required String reasonCode,
  }) {
    final currentSessionId = _deferredHandoffSessionId?.trim() ?? '';
    if (currentSessionId.isEmpty) {
      return;
    }

    final targetSessionId = sessionId?.trim() ?? '';
    if (targetSessionId.isNotEmpty && targetSessionId != currentSessionId) {
      return;
    }

    if (mounted) {
      setState(() {
        _deferredHandoffSessionId = null;
        _deferredHandoffReasonCode = null;
        _deferredHandoffMarkedAtUtc = null;
      });
    } else {
      _deferredHandoffSessionId = null;
      _deferredHandoffReasonCode = null;
      _deferredHandoffMarkedAtUtc = null;
    }

    _logSessionDecision(
      source: source,
      decision: 'DEFERRED_BADGE_CLEARED',
      sessionId: currentSessionId,
      reason: reasonCode,
    );
    unawaited(
      _recordDeferredHandoffTimelineEvent(
        sessionId: currentSessionId,
        eventType: 'SESSION_HANDOFF_DEFERRED_CLEARED',
        reasonCode: reasonCode,
      ),
    );
  }

  Future<void> _recordDeferredHandoffTimelineEvent({
    required String sessionId,
    required String eventType,
    required String reasonCode,
  }) async {
    try {
      await SessionJournalService.appendSessionEvent(
        sessionId: sessionId,
        studentId: widget.student.id,
        therapistId: _resolveActorTherapistId(),
        eventType: eventType,
        gameId: _selectedGameId,
        details: <String, dynamic>{
          'reasonCode': reasonCode,
          'workflowStep': _workflowStep.name,
          'connected': _isConnected,
          'attachReady': _sessionAttachReady,
        },
      );
    } catch (e) {
      debugPrint(
        '[ControlScreen] Deferred handoff timeline event failed: '
        'session=$sessionId event=$eventType reason=$reasonCode error=$e',
      );
    }
  }

  Future<void> _reviewDeferredHandoff() async {
    final sessionId = _deferredHandoffSessionIdValue;
    if (sessionId.isEmpty) {
      return;
    }

    if (!_isSessionDecisionAllowedByContext()) {
      _logSessionDecision(
        source: 'deferred_badge',
        decision: 'REVIEW_BLOCKED',
        sessionId: sessionId,
        reason: 'CONTEXT_NOT_ENTRY_OR_RECONNECT',
      );
      if (!mounted) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Handoff is still deferred while active game flow is in progress.',
          ),
          backgroundColor: Colors.orange,
        ),
      );
      return;
    }

    if (mounted) {
      setState(() {
        _remoteSessionIdPendingDecision = sessionId;
        _requiresSessionDecision = true;
      });
    } else {
      _remoteSessionIdPendingDecision = sessionId;
      _requiresSessionDecision = true;
    }

    _clearDeferredHandoff(
      sessionId: sessionId,
      source: 'deferred_badge',
      reasonCode: 'THERAPIST_REVIEW_REQUESTED',
    );
    _logSessionDecision(
      source: 'deferred_badge',
      decision: 'REVIEW_OPEN_DIALOG',
      sessionId: sessionId,
      reason: 'THERAPIST_REVIEW_REQUESTED',
    );
    _promptSessionDecisionIfNeeded();
  }

  String _describeDeferredHandoffReason(String reasonCode) {
    final normalized = reasonCode.trim();
    switch (normalized) {
      case 'CONTEXT_NOT_ENTRY_OR_RECONNECT':
        return 'active workflow context';
      case 'THERAPIST_KEEP_CURRENT':
        return 'therapist selected Keep current';
      case 'RECOVERY_OVER_WINDOW':
        return 'recovery window exceeded';
      default:
        return normalized.isEmpty ? 'deferred state' : normalized;
    }
  }

  SessionRecoveryEvaluation _evaluateRecoveryWindowState({
    required bool remoteSessionNeedsDecision,
  }) {
    return SessionRecoveryManager.evaluate(
      remoteSessionNeedsDecision: remoteSessionNeedsDecision,
      nowUtc: DateTime.now().toUtc(),
      lastConnectionLostAtUtc: _lastConnectionLostAtUtc,
      sessionRecoveryWindowMinutes:
          _therapistSessionSettings.sessionRecoveryWindowMinutes,
      requireResumeConfirmationAfterRecoveryWindow: _therapistSessionSettings
          .requireResumeConfirmationAfterRecoveryWindow,
    );
  }

  String _recoveryStateReasonCode(SessionRecoveryWindowState state) {
    switch (state) {
      case SessionRecoveryWindowState.interruptedRecoveringUnderWindow:
        return 'RECOVERY_UNDER_WINDOW';
      case SessionRecoveryWindowState
            .interruptedOverWindowNeedsTherapistDecision:
        return 'RECOVERY_OVER_WINDOW';
      case SessionRecoveryWindowState.notApplicable:
        return 'RECOVERY_NOT_APPLICABLE';
    }
  }

  Future<void> _attemptUnderWindowRecovery(
    String remoteSessionId, {
    required String source,
  }) async {
    _logSessionDecision(
      source: source,
      decision: 'AUTO_ATTACH_UNDER_WINDOW',
      sessionId: remoteSessionId,
      reason: 'RECOVERY_UNDER_WINDOW',
    );

    if (mounted) {
      setState(() {
        _requiresSessionDecision = false;
        _remoteSessionIdPendingDecision = null;
        _activeSessionId = remoteSessionId;
      });
    } else {
      _requiresSessionDecision = false;
      _remoteSessionIdPendingDecision = null;
      _activeSessionId = remoteSessionId;
    }
    _clearDeferredHandoff(
      sessionId: remoteSessionId,
      source: source,
      reasonCode: 'RECOVERY_UNDER_WINDOW',
    );

    if (!_isConnected) {
      return;
    }

    await _ensureSessionAttached(
      reasonCode: 'RECOVERY_UNDER_WINDOW',
      force: true,
      sessionIdOverride: remoteSessionId,
    );
    if (!mounted || !_sessionAttachReady) {
      return;
    }

    setState(() {
      _lastConnectionLostAtUtc = null;
    });
  }

  void _logSessionDecision({
    required String source,
    required String decision,
    String? commandId,
    String? sessionId,
    String? reason,
  }) {
    debugPrint(
      '[ControlScreen][SessionGate] source=$source decision=$decision '
      'command=${commandId ?? ''} session=${sessionId ?? ''} '
      'reason=${reason ?? ''} '
      'connected=$_isConnected attachReady=$_sessionAttachReady '
      'attachInFlight=$_sessionAttachInFlight workflow=${_workflowStep.name} '
      'runtimeActive=$_isGameRuntimeActive requiresDecision=$_requiresSessionDecision',
    );
  }

  void _logAttachDecision({
    required String decision,
    required String reasonCode,
    String? commandId,
    String? sessionId,
  }) {
    debugPrint(
      '[ControlScreen][AttachDecision] decision=$decision reason=$reasonCode '
      'command=${commandId ?? ''} session=${sessionId ?? ''} '
      'connected=$_isConnected attachReady=$_sessionAttachReady '
      'attachInFlight=$_sessionAttachInFlight',
    );
  }

  void _enqueueIncidentAlert({
    required String title,
    required String message,
    String reasonCode = '',
    OperatorIncidentSeverity severity = OperatorIncidentSeverity.error,
    String source = 'control_screen',
    Map<String, dynamic> contextData = const <String, dynamic>{},
  }) {
    if (!mounted) {
      return;
    }

    final normalizedReasonCode = reasonCode.trim().toUpperCase();
    final fallbackReasonCode = normalizedReasonCode.isNotEmpty
        ? normalizedReasonCode
        : (OpsErrorCatalog.tryExtractReasonCode(message) ?? 'UNKNOWN');

    _incidentPopupQueue.enqueue(
      context,
      OperatorIncidentAlert(
        occurredAtUtc: DateTime.now().toUtc(),
        source: source,
        title: title,
        message: message,
        reasonCode: fallbackReasonCode,
        severity: severity,
        contextData: _buildIncidentContextData(contextData),
      ),
    );
  }

  Map<String, dynamic> _buildIncidentContextData(
    Map<String, dynamic> extras,
  ) {
    final context = <String, dynamic>{
      'screen': 'control_screen',
      'studentId': widget.student.id,
      'studentTherapistId': widget.student.therapistId,
      'deviceId': widget.device.deviceId,
      'deviceName': widget.device.deviceName,
      'deviceIp': widget.device.ip,
      'controlPort': widget.device.controlPort,
      'sessionId': _activeSessionId,
      'selectedGameId': _selectedGameId,
      'connected': _isConnected,
      'sessionAttachReady': _sessionAttachReady,
      'sessionAttachInFlight': _sessionAttachInFlight,
      'requiresSessionDecision': _requiresSessionDecision,
      'workflowStep': _workflowStep.name,
      'runtimeStatus': _runtimeStatus?.wireValue ?? '',
      'sessionState': _sessionLifecycleState?.wireValue ?? '',
      'pendingDecisionSessionId': _remoteSessionIdPendingDecision ?? '',
      'deferredHandoffSessionId': _deferredHandoffSessionId ?? '',
      'deferredHandoffReasonCode': _deferredHandoffReasonCode ?? '',
      'operatorUiLanguage':
          _therapistSessionSettings.operatorUiLanguage.wireValue,
    };
    if (extras.isNotEmpty) {
      context.addAll(extras);
    }
    return context;
  }

  String _resolveActorTherapistId() {
    final currentUserId = FirebaseService.currentUser?.uid.trim() ?? '';
    if (currentUserId.isNotEmpty) {
      return currentUserId;
    }
    final studentOwner = widget.student.therapistId.trim();
    if (studentOwner.isNotEmpty) {
      return studentOwner;
    }
    return 'unknown_therapist';
  }

  Future<void> _loadTherapistSessionSettings() async {
    final settings =
        await TherapistSessionSettingsService.fetchCurrentTherapistSettings();
    if (!mounted) {
      return;
    }

    setState(() {
      _therapistSessionSettings = settings;
    });
  }

  String _resolveOwnerKey({String? therapistIdOverride}) {
    return SessionOwnership.ownerKey(
      therapistId: therapistIdOverride ?? _resolveActorTherapistId(),
      studentId: widget.student.id,
    );
  }

  String _resolveSessionKey(
    String sessionId, {
    String? therapistIdOverride,
  }) {
    return SessionOwnership.sessionKey(
      therapistId: therapistIdOverride ?? _resolveActorTherapistId(),
      studentId: widget.student.id,
      sessionId: sessionId,
    );
  }

  bool _shouldAdoptCreatedSessionRollover(
    String incomingSessionId, {
    SessionLifecycleState? activeSessionStateOverride,
  }) {
    return SessionRecoveryPolicy.shouldAdoptCreatedRolloverSession(
      sessionAttachReady: _sessionAttachReady,
      requiresSessionDecision: _requiresSessionDecision,
      activeSessionId: _activeSessionId,
      incomingSessionId: incomingSessionId,
      activeSessionState: activeSessionStateOverride ?? _sessionLifecycleState,
      activeSessionRecentlyEnded: _wasSessionRecentlyEnded(_activeSessionId),
    );
  }

  bool _isSignalOwnershipAccepted({
    required String source,
    required String sessionId,
    required String therapistId,
    required String studentId,
    required String patientId,
    required String ownerKey,
    required String sessionKey,
    bool allowCreatedSessionRollover = false,
  }) {
    final expectedTherapistId = _resolveActorTherapistId().trim();
    final expectedStudentId = widget.student.id.trim();
    final incomingTherapistId = therapistId.trim();
    final incomingStudentId = SessionOwnership.resolveStudentId(
      studentId: studentId,
      patientId: patientId,
    );
    final normalizedSessionId = sessionId.trim();
    final incomingOwnerKeyRaw = ownerKey.trim();
    final incomingSessionKeyRaw = sessionKey.trim();
    final expectedOwnerKey = SessionOwnership.ownerKey(
      therapistId: expectedTherapistId,
      studentId: expectedStudentId,
    );

    if (incomingTherapistId.isEmpty || incomingStudentId.isEmpty) {
      final shouldRejectForMissingOwnership =
          normalizedSessionId.isNotEmpty && _sessionAttachReady;
      if (shouldRejectForMissingOwnership) {
        debugPrint(
          '[ControlScreen][Ownership] Rejecting $source signal due missing '
          'ownership metadata: session=$normalizedSessionId '
          'incomingTherapist=$incomingTherapistId '
          'incomingStudent=$incomingStudentId',
        );
        return false;
      }
      return true;
    }

    final incomingOwnerKey = incomingOwnerKeyRaw.isNotEmpty
        ? incomingOwnerKeyRaw
        : SessionOwnership.ownerKey(
            therapistId: incomingTherapistId,
            studentId: incomingStudentId,
          );
    if (incomingOwnerKey.isEmpty || expectedOwnerKey.isEmpty) {
      if (normalizedSessionId.isNotEmpty && _sessionAttachReady) {
        debugPrint(
          '[ControlScreen][Ownership] Rejecting $source signal due missing '
          'ownerKey: session=$normalizedSessionId '
          'incomingOwner=$incomingOwnerKey expectedOwner=$expectedOwnerKey',
        );
        return false;
      }
      return true;
    }

    if (incomingOwnerKey != expectedOwnerKey) {
      debugPrint(
        '[ControlScreen][Ownership] Rejecting $source signal due owner '
        'mismatch: session=$normalizedSessionId '
        'expected=$expectedOwnerKey incoming=$incomingOwnerKey',
      );
      return false;
    }

    final shouldValidateSessionKey = _sessionAttachReady;
    if (shouldValidateSessionKey && normalizedSessionId.isNotEmpty) {
      final activeSessionId = _activeSessionId.trim();
      if (activeSessionId.isNotEmpty) {
        final expectedSessionKey = SessionOwnership.sessionKey(
          therapistId: expectedTherapistId,
          studentId: expectedStudentId,
          sessionId: activeSessionId,
        );
        final incomingSessionKey = incomingSessionKeyRaw.isNotEmpty
            ? incomingSessionKeyRaw
            : SessionOwnership.sessionKey(
                therapistId: incomingTherapistId,
                studentId: incomingStudentId,
                sessionId: normalizedSessionId,
              );
        if (incomingSessionKey.isEmpty || expectedSessionKey.isEmpty) {
          debugPrint(
            '[ControlScreen][Ownership] Rejecting $source signal due missing '
            'sessionKey: activeSession=$activeSessionId '
            'incomingSession=$normalizedSessionId',
          );
          return false;
        }

        if (incomingSessionKey != expectedSessionKey) {
          if (allowCreatedSessionRollover &&
              _shouldAdoptCreatedSessionRollover(normalizedSessionId)) {
            debugPrint(
              '[ControlScreen][Ownership] Accepting $source signal due '
              'terminal rollover: activeSession=$activeSessionId '
              'incomingSession=$normalizedSessionId',
            );
            return true;
          }
          debugPrint(
            '[ControlScreen][Ownership] Rejecting $source signal due session '
            'mismatch: expectedSessionKey=$expectedSessionKey '
            'incomingSessionKey=$incomingSessionKey',
          );
          return false;
        }
      }
    }

    return true;
  }

  SessionStateUpdateSignal? _filterSessionUpdateByOwnership(
    SessionStateUpdateSignal? signal,
  ) {
    if (signal == null) {
      return null;
    }

    if (!_isSignalOwnershipAccepted(
      source: SessionSignalCommandIds.sessionStateUpdate,
      sessionId: signal.sessionId,
      therapistId: signal.therapistId,
      studentId: signal.studentId,
      patientId: signal.patientId,
      ownerKey: signal.ownerKey,
      sessionKey: signal.sessionKey,
      allowCreatedSessionRollover:
          signal.state == SessionLifecycleState.created,
    )) {
      return null;
    }

    return signal;
  }

  RuntimeStatusUpdateSignal? _filterRuntimeUpdateByOwnership(
    RuntimeStatusUpdateSignal? signal,
  ) {
    if (signal == null) {
      return null;
    }

    if (!_isSignalOwnershipAccepted(
      source: RuntimeStatusSignalCommandIds.runtimeStatusUpdate,
      sessionId: signal.sessionId,
      therapistId: signal.therapistId,
      studentId: signal.studentId,
      patientId: signal.patientId,
      ownerKey: signal.ownerKey,
      sessionKey: signal.sessionKey,
    )) {
      return null;
    }

    return signal;
  }

  DevicePresenceUpdateSignal? _filterDevicePresenceByOwnership(
    DevicePresenceUpdateSignal? signal,
  ) {
    if (signal == null) {
      return null;
    }

    if (!_isSignalOwnershipAccepted(
      source: RuntimeStatusSignalCommandIds.devicePresenceUpdate,
      sessionId: signal.sessionId,
      therapistId: signal.therapistId,
      studentId: signal.studentId,
      patientId: signal.patientId,
      ownerKey: signal.ownerKey,
      sessionKey: signal.sessionKey,
    )) {
      return null;
    }

    return signal;
  }

  SessionWatchdogHeartbeatSignal? _filterWatchdogHeartbeatByOwnership(
    SessionWatchdogHeartbeatSignal? signal,
  ) {
    if (signal == null) {
      return null;
    }

    if (!_isSignalOwnershipAccepted(
      source: RuntimeStatusSignalCommandIds.sessionWatchdogHeartbeat,
      sessionId: signal.sessionId,
      therapistId: signal.therapistId,
      studentId: signal.studentId,
      patientId: signal.patientId,
      ownerKey: signal.ownerKey,
      sessionKey: signal.sessionKey,
    )) {
      return null;
    }

    return signal;
  }

  bool _isKnownGameId(String gameId) {
    final normalizedGameId = gameId.trim();
    if (normalizedGameId.isEmpty) {
      return false;
    }
    for (final entry in _effectiveGameCatalog) {
      if (entry.gameId == normalizedGameId) {
        return true;
      }
    }
    return false;
  }

  Map<String, dynamic> _buildSelectedGameConfigSnapshot() {
    final schemaSnapshot = _buildSchemaDrivenConfigSnapshot();
    if (schemaSnapshot != null) {
      return schemaSnapshot;
    }

    if (_isDemoCubeGameSelected) {
      return <String, dynamic>{
        'gameConfigType': 'demo_cube_config_v1',
        'gameConfigVersion': 1,
        'cubeCount': _demoCubeCount,
        'cubeSpeed': double.parse(_demoCubeSpeed.toStringAsFixed(2)),
        'levelMode': _demoLevelMode,
      };
    }

    if (_isPulseTargetGameSelected) {
      final adaptiveDifficultySensitivity = double.parse(
        _therapistSessionSettings.adaptiveDifficultySensitivity
            .toStringAsFixed(2),
      );
      return <String, dynamic>{
        'gameConfigType': 'pulse_targets_config_v1',
        'gameConfigVersion': 1,
        'targetCount': _pulseTargetCount,
        'targetSpeed': double.parse(_pulseTargetSpeed.toStringAsFixed(2)),
        'targetScale': double.parse(_pulseTargetScale.toStringAsFixed(2)),
        'adaptiveDifficultyEnabled':
            _therapistSessionSettings.adaptiveDifficultyEnabled,
        'adaptiveDifficultySensitivity': adaptiveDifficultySensitivity,
        'adaptiveDifficultyLevel': _resolveAdaptiveDifficultyLevel(),
        'labelPipelineEnabled': _therapistSessionSettings.labelPipelineEnabled,
      };
    }

    return <String, dynamic>{};
  }

  Map<String, dynamic>? _buildSchemaDrivenConfigSnapshot() {
    final schema = _selectedGameEntry.mobileControlSchema;
    if (schema == null) {
      return null;
    }

    final gameConfig = _buildSchemaDrivenGameConfigPayload(
      schema,
      emitForUpdateConfig: false,
    );

    return <String, dynamic>{
      'gameConfigType': _resolveSchemaGameConfigType(schema),
      'gameConfigVersion': _resolveSchemaGameConfigVersion(schema),
      ...gameConfig,
    };
  }

  Map<String, dynamic> _buildSchemaDrivenGameConfigPayload(
    MobileControlSchema schema, {
    required bool emitForUpdateConfig,
  }) {
    _ensureDynamicControlValuesForSelectedSchema();

    final payload = <String, dynamic>{};
    for (final control in schema.controls) {
      if (control.isButton) {
        continue;
      }

      final shouldEmit = emitForUpdateConfig
          ? control.binding.emitOnUpdateConfig
          : control.binding.emitOnStartGame;
      if (!shouldEmit) {
        continue;
      }

      final bindingPath = control.binding.path.trim();
      if (bindingPath.isEmpty) {
        continue;
      }

      var value = _dynamicControlValuesByControlId[control.controlId];
      value ??= _resolveSchemaControlDefaultValue(control);
      payload[bindingPath] =
          _coerceSchemaValue(control.binding.valueType, value);
    }

    if (schema.payload.includeVersionInGameConfig &&
        schema.payload.gameConfigVersion > 0) {
      payload['version'] = schema.payload.gameConfigVersion;
    }

    return payload;
  }

  String _resolveSchemaGameConfigType(MobileControlSchema schema) {
    final configured = schema.payload.gameConfigType.trim();
    if (configured.isNotEmpty) {
      return configured;
    }

    return '${_selectedGameId}_config';
  }

  int _resolveSchemaGameConfigVersion(MobileControlSchema schema) {
    final version = schema.payload.gameConfigVersion;
    return version > 0 ? version : 1;
  }

  void _ensureDynamicControlValuesForSelectedSchema() {
    final schema = _selectedGameEntry.mobileControlSchema;
    if (schema == null) {
      _dynamicControlValuesByControlId.clear();
      _dynamicControlValuesGameId = '';
      return;
    }

    final normalizedGameId = _selectedGameId.trim();
    if (_dynamicControlValuesGameId != normalizedGameId) {
      _dynamicControlValuesByControlId.clear();
      _dynamicControlValuesGameId = normalizedGameId;
    }

    final knownControlIds = <String>{};
    for (final control in schema.controls) {
      knownControlIds.add(control.controlId);
      _dynamicControlValuesByControlId.putIfAbsent(
        control.controlId,
        () => _resolveSchemaControlDefaultValue(control),
      );
    }

    final staleKeys = _dynamicControlValuesByControlId.keys
        .where((key) => !knownControlIds.contains(key))
        .toList(growable: false);
    for (final key in staleKeys) {
      _dynamicControlValuesByControlId.remove(key);
    }
  }

  dynamic _resolveSchemaControlDefaultValue(MobileControlDefinition control) {
    final raw = control.defaultValue;
    return _coerceSchemaValue(control.binding.valueType, raw);
  }

  dynamic _coerceSchemaValue(String valueType, dynamic raw) {
    switch (valueType) {
      case MobileControlValueTypes.integer:
        if (raw is int) {
          return raw;
        }
        if (raw is num) {
          return raw.round();
        }
        return int.tryParse(raw.toString().trim()) ?? 0;
      case MobileControlValueTypes.decimal:
        if (raw is double) {
          return raw;
        }
        if (raw is num) {
          return raw.toDouble();
        }
        return double.tryParse(raw.toString().trim()) ?? 0.0;
      case MobileControlValueTypes.boolean:
        if (raw is bool) {
          return raw;
        }
        final normalized = raw.toString().trim().toLowerCase();
        return normalized == 'true' || normalized == '1' || normalized == 'yes';
      case MobileControlValueTypes.text:
      default:
        return raw.toString();
    }
  }

  Future<void> _refreshPersistedSessionSnapshot({
    required bool triggerPrompt,
  }) async {
    if (_persistedSessionRefreshInFlight) {
      return;
    }

    _persistedSessionRefreshInFlight = true;
    try {
      final latest = await SessionJournalService.fetchLatestForStudent(
        studentId: widget.student.id,
        therapistId: _resolveActorTherapistId(),
      );
      if (!mounted) {
        return;
      }

      setState(() {
        _latestPersistedSession = latest;
      });
      if (_isParentRole) {
        unawaited(_refreshParentInsights());
      }

      if (triggerPrompt) {
        final handledByAutoClose = _applyInterruptedSessionAutoClose();
        if (!handledByAutoClose) {
          _applyPersistedSessionDecisionGate();
        }
      }
    } catch (e) {
      debugPrint(
        '[ControlScreen] Persisted session snapshot refresh failed: $e',
      );
    } finally {
      _persistedSessionRefreshInFlight = false;
    }
  }

  Future<void> _refreshParentInsights() async {
    if (!_isParentRole || _parentInsightsLoading) {
      return;
    }

    _parentInsightsLoading = true;
    try {
      final progressSnapshot = _isProgressInsightsAllowed
          ? await ParentProgressService.fetchSnapshot(
              studentId: widget.student.id,
              therapistId: _resolveActorTherapistId(),
            )
          : ParentProgressSnapshot.empty;
      final rewards = _isRewardsUnlocksAllowed
          ? await StudentRewardService.fetchRecentRewardsForStudent(
              studentId: widget.student.id,
            )
          : const <StudentRewardUnlock>[];

      if (!mounted) {
        return;
      }
      setState(() {
        _parentProgressSnapshot = progressSnapshot;
        _recentRewardUnlocks = rewards;
      });
    } catch (e) {
      debugPrint('[ControlScreen] Parent insights refresh failed: $e');
    } finally {
      _parentInsightsLoading = false;
    }
  }

  Future<void> _unlockRewardForCompletedSession({
    required String sessionId,
    required String gameId,
    required String reasonCode,
  }) async {
    if (!_isRewardsUnlocksAllowed) {
      return;
    }

    try {
      final result = await StudentRewardService.unlockForCompletedSession(
        studentId: widget.student.id,
        therapistId: _resolveActorTherapistId(),
        sessionId: sessionId,
        gameId: gameId,
        reasonCode: reasonCode,
      );
      if (result == null) {
        return;
      }

      if (result.unlockedNow) {
        await SessionJournalService.appendSessionEvent(
          sessionId: sessionId,
          studentId: widget.student.id,
          therapistId: _resolveActorTherapistId(),
          eventType: 'REWARD_UNLOCKED',
          gameId: gameId,
          details: <String, dynamic>{
            'rewardCode': result.reward.rewardCode,
            'rewardTitle': result.reward.rewardTitle,
            'reasonCode': reasonCode,
            'unlockId': result.reward.unlockId,
          },
        );
      }

      await _refreshParentInsights();

      if (result.unlockedNow && mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('Reward unlocked: ${result.reward.rewardTitle}'),
            duration: const Duration(seconds: 2),
          ),
        );
      }
    } catch (e) {
      debugPrint(
        '[ControlScreen] Reward unlock persist failed: '
        'session=$sessionId game=$gameId error=$e',
      );
    }
  }

  bool _applyPersistedSessionDecisionGate() {
    final persisted = _latestPersistedSession;
    if (persisted == null) {
      return false;
    }

    final persistedSessionId = persisted.sessionId.trim();
    if (persistedSessionId.isEmpty) {
      return false;
    }

    if (_applyInterruptedSessionAutoClose()) {
      return true;
    }

    if (persisted.requiresHandoffDecision &&
        persistedSessionId != _activeSessionId &&
        !_wasSessionRecentlyEnded(persistedSessionId)) {
      if (!_isSessionDecisionAllowedByContext()) {
        _markDeferredHandoff(
          sessionId: persistedSessionId,
          reasonCode: 'CONTEXT_NOT_ENTRY_OR_RECONNECT',
          source: 'persisted',
        );
        _logSessionDecision(
          source: 'persisted',
          decision: 'SUPPRESS',
          sessionId: persistedSessionId,
          reason: 'CONTEXT_NOT_ENTRY_OR_RECONNECT',
        );
        if (_requiresSessionDecision &&
            _remoteSessionIdPendingDecision == persistedSessionId) {
          setState(() {
            _requiresSessionDecision = false;
            _remoteSessionIdPendingDecision = null;
          });
        }
        return false;
      }

      final recoveryEvaluation = _evaluateRecoveryWindowState(
        remoteSessionNeedsDecision: true,
      );
      if (recoveryEvaluation.shouldAutoRecoverSilently) {
        _clearDeferredHandoff(
          sessionId: persistedSessionId,
          source: 'persisted',
          reasonCode: 'RECOVERY_UNDER_WINDOW',
        );
        unawaited(
          _attemptUnderWindowRecovery(
            persistedSessionId,
            source: 'persisted',
          ),
        );
        return true;
      }

      if (!mounted) {
        return false;
      }

      if (_isKnownGameId(persisted.latestGameId) &&
          persisted.latestGameId != _selectedGameId) {
        setState(() {
          _selectedGameId = persisted.latestGameId;
        });
      }
      _clearDeferredHandoff(
        sessionId: persistedSessionId,
        source: 'persisted',
        reasonCode: 'REVIEW_DIALOG_OPENED',
      );

      setState(() {
        _remoteSessionIdPendingDecision = persistedSessionId;
        _requiresSessionDecision = true;
      });
      _logSessionDecision(
        source: 'persisted',
        decision: 'SHOW_DIALOG',
        sessionId: persistedSessionId,
        reason: _recoveryStateReasonCode(recoveryEvaluation.state),
      );
      _promptSessionDecisionIfNeeded();
      return true;
    }

    if (persistedSessionId == _activeSessionId) {
      _clearDeferredHandoff(
        sessionId: persistedSessionId,
        source: 'persisted',
        reasonCode: 'ACTIVE_SESSION_MATCHED',
      );
    }

    if (!persisted.requiresHandoffDecision) {
      _clearDeferredHandoff(
        sessionId: persistedSessionId,
        source: 'persisted',
        reasonCode: 'PERSISTED_GATE_CLEARED',
      );
    }

    if (!persisted.requiresHandoffDecision &&
        _requiresSessionDecision &&
        _remoteSessionIdPendingDecision == persistedSessionId) {
      setState(() {
        _requiresSessionDecision = false;
        _remoteSessionIdPendingDecision = null;
      });
      _logSessionDecision(
        source: 'persisted',
        decision: 'CLEAR_GATE',
        sessionId: persistedSessionId,
        reason: 'PERSISTED_GATE_CLEARED',
      );
      unawaited(
        _ensureSessionAttached(
          reasonCode: 'HANDOFF_GATE_CLEARED',
          force: true,
        ),
      );
      return true;
    }

    return false;
  }

  bool _applyInterruptedSessionAutoClose() {
    final persisted = _latestPersistedSession;
    if (persisted == null || _interruptedSessionAutoCloseInFlight) {
      return false;
    }

    final sessionId = persisted.sessionId.trim();
    if (sessionId.isEmpty || _wasSessionRecentlyEnded(sessionId)) {
      return false;
    }

    final evaluation = SessionRecoveryManager.evaluateInterruptedAutoClose(
      sessionState: persisted.state,
      nowUtc: DateTime.now().toUtc(),
      interruptedAtUtc: persisted.interruptedAtUtc,
      interruptedSessionAutoCloseHours:
          _therapistSessionSettings.interruptedSessionAutoCloseHours,
      autoCloseInterruptedSessionsEnabled:
          _therapistSessionSettings.autoCloseInterruptedSessionsEnabled,
    );
    if (!evaluation.shouldAutoClose) {
      return false;
    }

    _interruptedSessionAutoCloseInFlight = true;
    _logSessionDecision(
      source: 'persisted',
      decision: 'AUTO_CLOSE_INTERRUPTED',
      sessionId: sessionId,
      reason: _interruptedAutoCloseReasonCode,
    );
    unawaited(_autoCloseInterruptedSession(persisted, evaluation));
    return true;
  }

  Future<void> _autoCloseInterruptedSession(
    TherapySessionRecord persisted,
    InterruptedSessionAutoCloseEvaluation evaluation,
  ) async {
    final sessionId = persisted.sessionId.trim();
    if (sessionId.isEmpty) {
      _interruptedSessionAutoCloseInFlight = false;
      return;
    }

    final therapistId = _resolveActorTherapistId();
    final gameId = persisted.latestGameId.trim().isNotEmpty
        ? persisted.latestGameId.trim()
        : _selectedGameId;
    final interruptedAtUtc = persisted.interruptedAtUtc;
    final interruptedAgeMinutes = evaluation.interruptedAge?.inMinutes ?? 0;
    final autoCloseWindowHours = evaluation.autoCloseWindowHours;

    try {
      await SessionJournalService.upsertSessionState(
        sessionId: sessionId,
        studentId: widget.student.id,
        therapistId: therapistId,
        state: SessionLifecycleState.failedTechnical,
        latestGameId: gameId,
        reasonCode: _interruptedAutoCloseReasonCode,
        metadata: <String, dynamic>{
          'origin': 'mobile_auto_close_policy',
          'sourceState': SessionLifecycleState.interrupted.wireValue,
          'autoCloseWindowHours': autoCloseWindowHours,
          'interruptedAtUtc': interruptedAtUtc?.toIso8601String() ?? '',
          'interruptedAgeMinutes': interruptedAgeMinutes,
          'autoCloseState': evaluation.state.name,
        },
      );

      await SessionJournalService.appendSessionEvent(
        sessionId: sessionId,
        studentId: widget.student.id,
        therapistId: therapistId,
        eventType: 'INTERRUPTED_SESSION_AUTO_CLOSED',
        gameId: gameId,
        details: <String, dynamic>{
          'reasonCode': _interruptedAutoCloseReasonCode,
          'autoCloseWindowHours': autoCloseWindowHours,
          'interruptedAtUtc': interruptedAtUtc?.toIso8601String() ?? '',
          'interruptedAgeMinutes': interruptedAgeMinutes,
          'autoCloseState': evaluation.state.name,
        },
      );

      _markSessionAsRecentlyEnded(sessionId);
      _clearDeferredHandoff(
        sessionId: sessionId,
        source: 'persisted',
        reasonCode: _interruptedAutoCloseReasonCode,
      );
      if (mounted) {
        setState(() {
          if (_remoteSessionIdPendingDecision == sessionId) {
            _requiresSessionDecision = false;
            _remoteSessionIdPendingDecision = null;
          }
          if (_activeSessionId.trim() == sessionId) {
            _activeSessionId = _buildLocalSessionId();
            _sessionAttachReady = false;
          }
        });
      }
      _logSessionDecision(
        source: 'persisted',
        decision: 'AUTO_CLOSE_COMPLETED',
        sessionId: sessionId,
        reason: _interruptedAutoCloseReasonCode,
      );
    } catch (e) {
      debugPrint(
        '[ControlScreen] Interrupted session auto-close failed: '
        'session=$sessionId, error=$e',
      );
    } finally {
      _interruptedSessionAutoCloseInFlight = false;
      await _refreshPersistedSessionSnapshot(triggerPrompt: true);
    }
  }

  Future<void> _persistRuntimeSessionState(
    SessionStateUpdateSignal sessionUpdate,
  ) async {
    final sessionId = sessionUpdate.sessionId.trim();
    if (sessionId.isEmpty) {
      return;
    }

    final activeSessionId = _activeSessionId.trim();
    final shouldIgnoreForeignSessionSignal = _sessionAttachReady &&
        activeSessionId.isNotEmpty &&
        sessionId != activeSessionId &&
        !_requiresSessionDecision;
    if (shouldIgnoreForeignSessionSignal) {
      debugPrint(
        '[ControlScreen] Ignoring runtime session state from foreign session '
        'while attached: incoming=$sessionId active=$activeSessionId '
        'state=${sessionUpdate.state.wireValue}',
      );
      return;
    }

    try {
      await SessionJournalService.upsertSessionState(
        sessionId: sessionId,
        studentId: widget.student.id,
        therapistId: _resolveActorTherapistId(),
        state: sessionUpdate.state,
        latestGameId: _selectedGameId,
        reasonCode: sessionUpdate.reasonCode,
        metadata: <String, dynamic>{
          'origin': 'runtime_signal',
          'previousState': sessionUpdate.previousState?.wireValue ?? '',
        },
      );

      await SessionJournalService.appendSessionEvent(
        sessionId: sessionId,
        studentId: widget.student.id,
        therapistId: _resolveActorTherapistId(),
        eventType: 'RUNTIME_SESSION_STATE_UPDATE',
        gameId: _selectedGameId,
        details: <String, dynamic>{
          'state': sessionUpdate.state.wireValue,
          'previousState': sessionUpdate.previousState?.wireValue ?? '',
          'reasonCode': sessionUpdate.reasonCode,
        },
      );

      await _refreshPersistedSessionSnapshot(triggerPrompt: true);
    } catch (e) {
      debugPrint(
        '[ControlScreen] Persist runtime session state failed: session=$sessionId, error=$e',
      );
    }
  }

  Future<void> _persistCommandSideEffects(
    String command, {
    String? sessionIdOverride,
    Map<String, dynamic>? extraPayload,
  }) async {
    final sessionId = (sessionIdOverride ?? _activeSessionId).trim();
    if (sessionId.isEmpty) {
      return;
    }

    final therapistId = _resolveActorTherapistId();
    final selectedGameId = _selectedGameId;

    try {
      if (command == CriticalCommandIds.startGame ||
          command == CriticalCommandIds.resumeGame) {
        await SessionJournalService.upsertSessionState(
          sessionId: sessionId,
          studentId: widget.student.id,
          therapistId: therapistId,
          state: SessionLifecycleState.inProgress,
          latestGameId: selectedGameId,
          reasonCode: command,
          metadata: <String, dynamic>{
            'origin': 'mobile_command',
            'resumeFromSaved': false,
          },
        );
        await SessionJournalService.appendSessionEvent(
          sessionId: sessionId,
          studentId: widget.student.id,
          therapistId: therapistId,
          eventType: command == CriticalCommandIds.resumeGame
              ? 'GAME_RESUMED'
              : 'GAME_STARTED',
          gameId: selectedGameId,
          details: <String, dynamic>{
            'resumeFromSaved': false,
            'config': _buildSelectedGameConfigSnapshot(),
          },
        );
      } else if (command == CriticalCommandIds.pauseGame) {
        await SessionJournalService.upsertSessionState(
          sessionId: sessionId,
          studentId: widget.student.id,
          therapistId: therapistId,
          state: SessionLifecycleState.paused,
          latestGameId: selectedGameId,
          reasonCode: command,
          metadata: const <String, dynamic>{'origin': 'mobile_command'},
        );
        await SessionJournalService.appendSessionEvent(
          sessionId: sessionId,
          studentId: widget.student.id,
          therapistId: therapistId,
          eventType: 'GAME_PAUSED',
          gameId: selectedGameId,
        );
      } else if (command == CriticalCommandIds.stopGame) {
        await SessionJournalService.upsertSessionState(
          sessionId: sessionId,
          studentId: widget.student.id,
          therapistId: therapistId,
          state: SessionLifecycleState.interrupted,
          latestGameId: selectedGameId,
          reasonCode: extraPayload?['reason'] as String? ?? 'USER_EXIT',
          metadata: const <String, dynamic>{'origin': 'mobile_command'},
        );
        await SessionJournalService.appendSessionEvent(
          sessionId: sessionId,
          studentId: widget.student.id,
          therapistId: therapistId,
          eventType: 'GAME_ENDED',
          gameId: selectedGameId,
          details: <String, dynamic>{
            'reason': extraPayload?['reason'] as String? ?? 'USER_EXIT',
          },
        );
      } else if (command == CriticalCommandIds.endSession) {
        await SessionJournalService.markSessionCompletedByTherapist(
          sessionId: sessionId,
          studentId: widget.student.id,
          therapistId: therapistId,
          latestGameId: selectedGameId,
          reasonCode: 'THERAPIST_CONFIRMED_END',
          metadata: const <String, dynamic>{'origin': 'mobile_command'},
        );
        await SessionJournalService.appendSessionEvent(
          sessionId: sessionId,
          studentId: widget.student.id,
          therapistId: therapistId,
          eventType: 'SESSION_ENDED',
          gameId: selectedGameId,
          details: const <String, dynamic>{
            'reason': 'THERAPIST_CONFIRMED_END',
          },
        );
        await _unlockRewardForCompletedSession(
          sessionId: sessionId,
          gameId: selectedGameId,
          reasonCode: 'THERAPIST_CONFIRMED_END',
        );
      }
    } catch (e) {
      debugPrint(
        '[ControlScreen] Persist command side effects failed: command=$command, error=$e',
      );
    } finally {
      await _refreshPersistedSessionSnapshot(triggerPrompt: true);
    }
  }

  Future<void> _persistStartNewDecisionOutcome(String remoteSessionId) async {
    final normalizedSessionId = remoteSessionId.trim();
    if (normalizedSessionId.isEmpty) {
      return;
    }

    final therapistId = _resolveActorTherapistId();
    try {
      await SessionJournalService.markSessionCompletedByTherapist(
        sessionId: normalizedSessionId,
        studentId: widget.student.id,
        therapistId: therapistId,
        latestGameId: _selectedGameId,
        reasonCode: 'THERAPIST_START_NEW_DECISION',
        metadata: const <String, dynamic>{'origin': 'mobile_decision_gate'},
      );
      await SessionJournalService.appendSessionEvent(
        sessionId: normalizedSessionId,
        studentId: widget.student.id,
        therapistId: therapistId,
        eventType: 'SESSION_ENDED_BY_DECISION',
        gameId: _selectedGameId,
        details: const <String, dynamic>{
          'decision': 'START_NEW',
          'reason': 'THERAPIST_START_NEW_DECISION',
        },
      );
    } catch (e) {
      debugPrint(
        '[ControlScreen] Persist start-new decision outcome failed: session=$normalizedSessionId, error=$e',
      );
    } finally {
      await _refreshPersistedSessionSnapshot(triggerPrompt: true);
    }
  }

  List<_GameCatalogEntry> get _effectiveGameCatalog {
    if (_remoteGameCatalog.isNotEmpty) {
      return _remoteGameCatalog;
    }
    return _fallbackGameCatalog;
  }

  void _startGameCatalogSubscription() {
    _gameCatalogSubscription?.cancel();
    _gameCatalogSubscription = GameCatalogService.watchActiveCatalog().listen(
      (entries) {
        if (!mounted) {
          return;
        }

        final mapped = entries
            .map(_GameCatalogEntry.fromRemoteEntry)
            .where((entry) => entry.gameId.isNotEmpty)
            .toList(growable: false);
        setState(() {
          _remoteGameCatalog = mapped;
          _bootstrapLocalContentStates();
          if (!_effectiveGameCatalog
              .any((entry) => entry.gameId == _selectedGameId)) {
            _selectedGameId = _resolveInitialGameId();
          }
        });
      },
      onError: (Object error) {
        debugPrint('[ControlScreen] game_catalog stream failed: $error');
      },
    );
  }

  bool _isGameOwnedByEntitlement(
    _GameCatalogEntry entry, {
    DateTime? atUtc,
  }) {
    final normalizedGameId = entry.gameId.trim();
    if (normalizedGameId.isEmpty) {
      return false;
    }

    if (_simulatedOwnedGameIds.contains(normalizedGameId)) {
      return true;
    }

    final access = EntitlementService.activeAccess;
    final nowUtc = atUtc ?? DateTime.now().toUtc();
    if (access == null) {
      return false;
    }

    if (entry.requiresExplicitLicense) {
      if (!access.hasAppAccess(nowUtc) ||
          !access.isGameAllowedByPlan(normalizedGameId)) {
        return false;
      }

      final gameGrant = access.gameLicenses[normalizedGameId];
      return gameGrant != null && gameGrant.isActiveAt(nowUtc);
    }

    return EntitlementService.canLaunchGame(normalizedGameId, atUtc: nowUtc);
  }

  String _resolveInitialGameId() {
    final catalog = _effectiveGameCatalog;
    if (catalog.isEmpty) {
      return _demoCubeGameId;
    }

    final nowUtc = DateTime.now().toUtc();
    for (final entry in catalog) {
      if (_isGameOwnedByEntitlement(entry, atUtc: nowUtc)) {
        return entry.gameId;
      }
    }

    return catalog.first.gameId;
  }

  List<_GameCatalogEntry> get _entitledGameCatalog {
    return _effectiveGameCatalog;
  }

  _GameCatalogEntry get _selectedGameEntry {
    final catalog = _effectiveGameCatalog;
    if (catalog.isNotEmpty) {
      for (final entry in catalog) {
        if (entry.gameId == _selectedGameId) {
          return entry;
        }
      }

      final fallback = catalog.first;
      if (_selectedGameId != fallback.gameId) {
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (!mounted || _selectedGameId == fallback.gameId) {
            return;
          }
          setState(() {
            _selectedGameId = fallback.gameId;
          });
        });
      }
      return fallback;
    }

    return _fallbackGameCatalog.first;
  }

  SubscriptionPlanTier get _activePlanTier {
    return EntitlementService.activeAccess?.planProfile.tier ??
        SubscriptionPlanTier.unknown;
  }

  EntitlementRole get _activeRole {
    return EntitlementService.activeAccess?.role ?? EntitlementRole.unknown;
  }

  bool get _isParentRole => _activeRole == EntitlementRole.parent;

  bool get _isFreePlan => _activePlanTier == SubscriptionPlanTier.free;

  bool get _hasRemainingDemoSessions {
    return EntitlementService.hasRemainingDemoSessions();
  }

  bool get _isVrSessionControlAllowed {
    return EntitlementService.isFeatureEnabled(
      EntitlementFeatureKeys.vrSessionControl,
    );
  }

  bool get _isParentGuidedStartAllowed {
    return EntitlementService.isFeatureEnabled(
      EntitlementFeatureKeys.parentGuidedStart,
    );
  }

  bool get _isProgressInsightsAllowed {
    return EntitlementService.isFeatureEnabled(
      EntitlementFeatureKeys.progressInsights,
    );
  }

  bool get _isRewardsUnlocksAllowed {
    return EntitlementService.isFeatureEnabled(
      EntitlementFeatureKeys.rewardsUnlocks,
    );
  }

  bool get _isPlanBlockingLaunch {
    if (!_isVrSessionControlAllowed) {
      return true;
    }
    if (_isParentRole && !_isParentGuidedStartAllowed) {
      return true;
    }
    if (_isFreePlan && !_hasRemainingDemoSessions) {
      return true;
    }
    return false;
  }

  String? get _planGateBannerText {
    if (!_isVrSessionControlAllowed) {
      return 'Current plan does not allow starting VR sessions.';
    }
    if (_isParentRole && !_isParentGuidedStartAllowed) {
      return 'Current plan does not allow guided parent start.';
    }
    if (_isFreePlan && !_hasRemainingDemoSessions) {
      return 'Free plan demo limit reached. Upgrade plan or reset demo quota.';
    }
    return null;
  }

  bool get _isDemoCubeGameSelected => _selectedGameId == _demoCubeGameId;
  bool get _isPulseTargetGameSelected => _selectedGameId == _pulseTargetGameId;

  bool get _isGameRuntimeActive {
    final activeGameId = _remoteActiveGameId?.trim() ?? '';
    final inferredFromHeartbeat = activeGameId.isNotEmpty;

    return _runtimeStatus == TherapistRuntimeStatus.playing ||
        _runtimeStatus == TherapistRuntimeStatus.paused ||
        _sessionLifecycleState == SessionLifecycleState.inProgress ||
        _sessionLifecycleState == SessionLifecycleState.paused ||
        inferredFromHeartbeat ||
        _optimisticRuntimeActive;
  }

  bool get _isGameRuntimePaused {
    return _runtimeStatus == TherapistRuntimeStatus.paused ||
        _sessionLifecycleState == SessionLifecycleState.paused ||
        _optimisticRuntimePaused;
  }

  bool get _isHeadsetPresenceBlocking {
    if (!_isConnected) {
      return false;
    }

    final state = _lastDevicePresenceSignal?.presenceState;
    return state == DevicePresenceState.background ||
        state == DevicePresenceState.focusLost ||
        state == DevicePresenceState.quitting;
  }

  String? get _headsetPresenceBannerText {
    if (!_isHeadsetPresenceBlocking) {
      return null;
    }

    final signal = _lastDevicePresenceSignal;
    if (signal == null) {
      return null;
    }

    switch (signal.presenceState) {
      case DevicePresenceState.background:
        return 'Headset app is in background/menu (${_describePresenceReason(signal.reasonCode)}). Ask student to return to VR app.';
      case DevicePresenceState.focusLost:
        return 'Headset lost app focus (${_describePresenceReason(signal.reasonCode)}). Controls stay locked until focus returns.';
      case DevicePresenceState.quitting:
        return 'Headset app is closing (${_describePresenceReason(signal.reasonCode)}). Wait for reconnect or reopen app.';
      case DevicePresenceState.connected:
      case DevicePresenceState.foreground:
        return null;
    }
  }

  String _describePresenceReason(String reasonCode) {
    final normalized = reasonCode.trim().toUpperCase();
    if (normalized.isEmpty) {
      return '${OpsErrorCatalog.buildReasonTag(normalized)} unknown reason';
    }

    final reasonTag = OpsErrorCatalog.buildReasonTag(normalized);
    final detail = switch (normalized) {
      'APP_PAUSED' => 'app paused',
      'APP_RESUMED' => 'app resumed',
      'APP_FOCUS_LOST' => 'focus lost',
      'APP_FOCUS_GAINED' => 'focus regained',
      'APP_QUIT' => 'app quit',
      'TCP_CLIENT_CONNECTED' => 'transport connected',
      _ => normalized,
    };
    return '$reasonTag $detail';
  }

  void _handleDevicePresenceFeedback({
    required DevicePresenceUpdateSignal? previousSignal,
    required DevicePresenceUpdateSignal signal,
  }) {
    if (!mounted) {
      return;
    }

    final didStateChange = previousSignal == null ||
        previousSignal.presenceState != signal.presenceState;
    final didReasonChange = previousSignal == null ||
        previousSignal.reasonCode != signal.reasonCode;
    if (!didStateChange && !didReasonChange) {
      return;
    }

    final messenger = ScaffoldMessenger.of(context);
    final reason = _describePresenceReason(signal.reasonCode);

    final isBlockingState =
        signal.presenceState == DevicePresenceState.background ||
            signal.presenceState == DevicePresenceState.focusLost ||
            signal.presenceState == DevicePresenceState.quitting;

    if (isBlockingState) {
      final message = switch (signal.presenceState) {
        DevicePresenceState.background =>
          'Headset opened system menu / app is in background ($reason).',
        DevicePresenceState.focusLost =>
          'Headset focus lost ($reason). Controls are temporarily locked.',
        DevicePresenceState.quitting =>
          'Headset app is closing ($reason). Waiting for reconnect.',
        DevicePresenceState.connected => '',
        DevicePresenceState.foreground => '',
      };

      if (message.isNotEmpty) {
        messenger.showSnackBar(
          SnackBar(
            content: Text(message),
            backgroundColor: Colors.orange.shade800,
            duration: const Duration(seconds: 2),
          ),
        );
      }
      return;
    }

    final wasBlocking = previousSignal != null &&
        (previousSignal.presenceState == DevicePresenceState.background ||
            previousSignal.presenceState == DevicePresenceState.focusLost ||
            previousSignal.presenceState == DevicePresenceState.quitting);
    if (wasBlocking) {
      messenger.showSnackBar(
        const SnackBar(
          content:
              Text('Headset returned to active app. Controls are unlocked.'),
          duration: Duration(seconds: 2),
        ),
      );
    }
  }

  bool get _isSetupLockedByRuntime => _isGameRuntimeActive;

  PurchasedContentState get _selectedContentState {
    return _contentStateForGame(_selectedGameId);
  }

  bool get _isSelectedGameLaunchable {
    final selectedEntry = _selectedGameEntry;
    return selectedEntry.runtimeLaunchEnabled &&
        _isLaunchableContentState(_selectedContentState);
  }

  void _bootstrapLocalContentStates() {
    final nowUtc = DateTime.now().toUtc();
    final catalog = _effectiveGameCatalog;
    final knownGameIds = <String>{};
    for (final entry in catalog) {
      knownGameIds.add(entry.gameId);
      final isOwnedByEntitlement =
          _isGameOwnedByEntitlement(entry, atUtc: nowUtc);
      final existing = _contentStatesByGameId[entry.gameId];

      final defaultInstalledVersion =
          entry.requiresExplicitLicense ? null : entry.targetContentVersion;
      final defaultRuntimeStatus = isOwnedByEntitlement
          ? (entry.requiresExplicitLicense
              ? ContentRuntimeStatus.notInstalled
              : ContentRuntimeStatus.ready)
          : ContentRuntimeStatus.notInstalled;
      if (existing == null) {
        _contentStatesByGameId[entry.gameId] = PurchasedContentState(
          gameId: entry.gameId,
          owned: isOwnedByEntitlement,
          installedVersion: defaultInstalledVersion,
          targetVersion: entry.targetContentVersion,
          updateRequired: false,
          updateOptional: false,
          runtimeStatus: defaultRuntimeStatus,
          lastError: null,
          updatedAtUtc: nowUtc,
        );
        continue;
      }

      _contentStatesByGameId[entry.gameId] = existing.copyWith(
        owned: isOwnedByEntitlement,
        targetVersion: entry.targetContentVersion,
        installedVersion:
            isOwnedByEntitlement ? existing.installedVersion : null,
        runtimeStatus: isOwnedByEntitlement
            ? existing.runtimeStatus
            : ContentRuntimeStatus.notInstalled,
        updateRequired: isOwnedByEntitlement ? existing.updateRequired : false,
        lastError: isOwnedByEntitlement ? existing.lastError : null,
        updatedAtUtc: nowUtc,
      );
    }

    _contentStatesByGameId.removeWhere(
      (gameId, _) => !knownGameIds.contains(gameId),
    );
  }

  PurchasedContentState _contentStateForGame(String gameId) {
    final existing = _contentStatesByGameId[gameId];
    if (existing != null) {
      return existing;
    }

    final fallbackEntry =
        _effectiveGameCatalog.where((entry) => entry.gameId == gameId);
    if (fallbackEntry.isNotEmpty) {
      final entry = fallbackEntry.first;
      final isOwnedByEntitlement = _isGameOwnedByEntitlement(
        entry,
        atUtc: DateTime.now().toUtc(),
      );
      return PurchasedContentState(
        gameId: gameId,
        owned: isOwnedByEntitlement,
        installedVersion:
            entry.requiresExplicitLicense ? null : entry.targetContentVersion,
        targetVersion: entry.targetContentVersion,
        updateRequired: false,
        updateOptional: false,
        runtimeStatus: isOwnedByEntitlement
            ? (entry.requiresExplicitLicense
                ? ContentRuntimeStatus.notInstalled
                : ContentRuntimeStatus.ready)
            : ContentRuntimeStatus.notInstalled,
        lastError: null,
        updatedAtUtc: DateTime.now().toUtc(),
      );
    }

    return PurchasedContentState(
      gameId: gameId,
      owned: false,
      installedVersion: null,
      targetVersion: '0.0.0',
      updateRequired: false,
      updateOptional: false,
      runtimeStatus: ContentRuntimeStatus.notInstalled,
      lastError: null,
      updatedAtUtc: DateTime.now().toUtc(),
    );
  }

  bool _isLaunchableContentState(PurchasedContentState state) {
    if (!_contentDeliveryEnabled) {
      return state.owned;
    }
    return state.owned &&
        state.runtimeStatus == ContentRuntimeStatus.ready &&
        !state.updateRequired;
  }

  void _applyContentInstallStatusSignal(ContentInstallStatusSignal signal) {
    final state = signal.state;
    if (state.gameId.trim().isEmpty) {
      return;
    }

    setState(() {
      _contentStatesByGameId[state.gameId] = state;
      _contentActionsInFlight.remove(state.gameId);
      if (_contentActionsInFlight.isEmpty) {
        _contentSyncInFlight = false;
      }
    });
  }

  Future<void> _syncContentCatalog({bool silent = false}) async {
    if (!_contentDeliveryEnabled) {
      return;
    }

    if (!_isConnected || _contentSyncInFlight) {
      return;
    }

    setState(() {
      _contentSyncInFlight = true;
    });

    try {
      await _connection.sendCommand(
        ContentDeliveryCommandIds.syncCatalog,
        ContentDeliveryRequests.buildSyncCatalogRequest(
          actorId: _resolveActorTherapistId(),
          role: (EntitlementService.activeAccess?.role ??
                  EntitlementRole.therapist)
              .wireValue,
        ),
      );

      if (!mounted || silent) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Catalog sync requested'),
          duration: Duration(seconds: 1),
        ),
      );
    } catch (e) {
      if (!mounted || silent) {
        return;
      }

      _enqueueIncidentAlert(
        title: 'Catalog sync failed',
        message: 'Catalog sync failed: $e',
        reasonCode: OpsErrorCatalog.tryExtractReasonCode(e) ?? 'UNSPECIFIED',
        severity: OperatorIncidentSeverity.error,
      );
    } finally {
      if (mounted) {
        setState(() {
          _contentSyncInFlight = false;
        });
      }
    }
  }

  Future<void> _requestInstallOrUpdate(PurchasedContentState state) async {
    if (!_contentDeliveryEnabled) {
      return;
    }

    if (!_isConnected || _contentActionsInFlight.contains(state.gameId)) {
      return;
    }

    final nextStatus = ContentDeliveryTransitionRule.nextStatus(
      current: state.runtimeStatus,
      action: ContentDeliveryAction.requestInstallOrUpdate,
    );
    String packageUri = '';
    for (final entry in _effectiveGameCatalog) {
      if (entry.gameId == state.gameId) {
        packageUri = entry.packageUri;
        break;
      }
    }

    setState(() {
      _contentActionsInFlight.add(state.gameId);
      _contentStatesByGameId[state.gameId] = state.copyWith(
        runtimeStatus: nextStatus,
        updateRequired: false,
        lastError: null,
        updatedAtUtc: DateTime.now().toUtc(),
      );
    });

    try {
      await _connection.sendCommand(
        ContentDeliveryCommandIds.installGame,
        ContentDeliveryRequests.buildInstallRequest(
          actorId: _resolveActorTherapistId(),
          gameId: state.gameId,
          targetVersion: state.targetVersion,
          packageUri: packageUri,
        ),
      );

      if (!mounted) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Install/update requested for ${state.gameId}'),
          duration: const Duration(seconds: 1),
        ),
      );
    } catch (e) {
      if (!mounted) {
        return;
      }

      setState(() {
        _contentStatesByGameId[state.gameId] = state.copyWith(
          runtimeStatus: ContentRuntimeStatus.failed,
          lastError: e.toString(),
          updatedAtUtc: DateTime.now().toUtc(),
        );
        _contentActionsInFlight.remove(state.gameId);
      });

      _enqueueIncidentAlert(
        title: 'Install/update failed',
        message: 'Install/update failed for ${state.gameId}: $e',
        reasonCode: OpsErrorCatalog.tryExtractReasonCode(e) ?? 'UNSPECIFIED',
        severity: OperatorIncidentSeverity.error,
      );
    }
  }

  Future<void> _requestUninstall(PurchasedContentState state) async {
    if (!_contentDeliveryEnabled) {
      return;
    }

    if (!_isConnected || _contentActionsInFlight.contains(state.gameId)) {
      return;
    }

    setState(() {
      _contentActionsInFlight.add(state.gameId);
    });

    try {
      await _connection.sendCommand(
        ContentDeliveryCommandIds.uninstallGame,
        ContentDeliveryRequests.buildUninstallRequest(
          actorId: _resolveActorTherapistId(),
          gameId: state.gameId,
        ),
      );

      if (!mounted) {
        return;
      }

      setState(() {
        _contentStatesByGameId[state.gameId] = state.copyWith(
          installedVersion: null,
          runtimeStatus: ContentRuntimeStatus.notInstalled,
          updateRequired: false,
          updateOptional: false,
          lastError: null,
          updatedAtUtc: DateTime.now().toUtc(),
        );
      });

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Uninstall requested for ${state.gameId}'),
          duration: const Duration(seconds: 1),
        ),
      );
    } catch (e) {
      if (!mounted) {
        return;
      }

      setState(() {
        _contentStatesByGameId[state.gameId] = state.copyWith(
          runtimeStatus: ContentRuntimeStatus.failed,
          lastError: e.toString(),
          updatedAtUtc: DateTime.now().toUtc(),
        );
      });

      _enqueueIncidentAlert(
        title: 'Uninstall failed',
        message: 'Uninstall failed for ${state.gameId}: $e',
        reasonCode: OpsErrorCatalog.tryExtractReasonCode(e) ?? 'UNSPECIFIED',
        severity: OperatorIncidentSeverity.error,
      );
    } finally {
      if (mounted) {
        setState(() {
          _contentActionsInFlight.remove(state.gameId);
        });
      }
    }
  }

  void _handlePotentialSessionDecisionGate(
    SessionStateUpdateSignal? sessionUpdate,
    RuntimeStatusUpdateSignal? runtimeUpdate,
    SessionWatchdogHeartbeatSignal? watchdogHeartbeat,
  ) {
    final handledByPersisted = _applyPersistedSessionDecisionGate();
    if (handledByPersisted) {
      return;
    }

    if (_serverAuthoritativeHandoffGate) {
      return;
    }

    final remoteSessionId = _resolveRemoteSessionId(
        sessionUpdate, runtimeUpdate, watchdogHeartbeat);
    if (remoteSessionId == null || remoteSessionId.isEmpty) {
      return;
    }

    final heartbeatState = SessionLifecycleState.tryParse(
      watchdogHeartbeat?.sessionState,
    );
    SessionLifecycleState? remoteState = sessionUpdate?.state ?? heartbeatState;
    if (remoteState == null &&
        _lastSessionStateUpdateSessionId == remoteSessionId) {
      remoteState = _sessionLifecycleState;
    }

    TherapistRuntimeStatus? remoteRuntime =
        runtimeUpdate?.status ?? watchdogHeartbeat?.runtimeStatus;
    if (remoteRuntime == null &&
        _lastRuntimeStatusSessionId == remoteSessionId) {
      remoteRuntime = _runtimeStatus;
    }

    final hasStrongNonTerminalState = remoteState != null &&
        SessionRecoveryPolicy.shouldGateBySessionState(remoteState);
    final hasStrongNonTerminalRuntime =
        _isRuntimeStatusNonTerminal(remoteRuntime);

    if (_wasSessionRecentlyEnded(remoteSessionId) &&
        !hasStrongNonTerminalState &&
        !hasStrongNonTerminalRuntime) {
      _clearDeferredHandoff(
        sessionId: remoteSessionId,
        source: 'runtime',
        reasonCode: 'SESSION_RECENTLY_TERMINAL',
      );
      if (_requiresSessionDecision && mounted) {
        setState(() {
          _requiresSessionDecision = false;
          _remoteSessionIdPendingDecision = null;
        });
      }
      return;
    }
    if (_wasSessionRecentlyEnded(remoteSessionId) &&
        (hasStrongNonTerminalState || hasStrongNonTerminalRuntime)) {
      _recentlyEndedSessionIds.remove(remoteSessionId);
    }

    final isRemoteTerminal = remoteState != null &&
        SessionRecoveryPolicy.isTerminalState(remoteState);
    if (isRemoteTerminal) {
      _markSessionAsRecentlyEnded(remoteSessionId);
      _clearDeferredHandoff(
        sessionId: remoteSessionId,
        source: 'runtime',
        reasonCode: 'REMOTE_STATE_TERMINAL',
      );
      if (_requiresSessionDecision && mounted) {
        setState(() {
          _requiresSessionDecision = false;
          _remoteSessionIdPendingDecision = null;
        });
      }
      return;
    }

    final needsDecision = SessionRecoveryPolicy.shouldRequireDecision(
      localSessionId: _activeSessionId,
      remoteSessionId: remoteSessionId,
      remoteState: remoteState,
      remoteRuntimeStatus: remoteRuntime,
    );

    if (needsDecision) {
      if (!_isSessionDecisionAllowedByContext()) {
        _markDeferredHandoff(
          sessionId: remoteSessionId,
          reasonCode: 'CONTEXT_NOT_ENTRY_OR_RECONNECT',
          source: 'runtime',
        );
        _logSessionDecision(
          source: 'runtime',
          decision: 'SUPPRESS',
          sessionId: remoteSessionId,
          reason: 'CONTEXT_NOT_ENTRY_OR_RECONNECT',
        );
        return;
      }

      final recoveryEvaluation = _evaluateRecoveryWindowState(
        remoteSessionNeedsDecision: true,
      );
      if (recoveryEvaluation.shouldAutoRecoverSilently) {
        _clearDeferredHandoff(
          sessionId: remoteSessionId,
          source: 'runtime',
          reasonCode: 'RECOVERY_UNDER_WINDOW',
        );
        unawaited(
          _attemptUnderWindowRecovery(
            remoteSessionId,
            source: 'runtime',
          ),
        );
        return;
      }
      _clearDeferredHandoff(
        sessionId: remoteSessionId,
        source: 'runtime',
        reasonCode: 'REVIEW_DIALOG_OPENED',
      );

      if (mounted) {
        setState(() {
          _remoteSessionIdPendingDecision = remoteSessionId;
          _requiresSessionDecision = true;
        });
      } else {
        _remoteSessionIdPendingDecision = remoteSessionId;
        _requiresSessionDecision = true;
      }
      _logSessionDecision(
        source: 'runtime',
        decision: 'SHOW_DIALOG',
        sessionId: remoteSessionId,
        reason: _recoveryStateReasonCode(recoveryEvaluation.state),
      );
      _promptSessionDecisionIfNeeded();
      return;
    }

    final shouldUpdateActiveSession =
        remoteState == SessionLifecycleState.created &&
            remoteSessionId != _activeSessionId;
    final shouldClearDecisionState = _requiresSessionDecision;
    _clearDeferredHandoff(
      sessionId: remoteSessionId,
      source: 'runtime',
      reasonCode: 'RUNTIME_GATE_CLEARED',
    );

    if (!shouldUpdateActiveSession && !shouldClearDecisionState) {
      return;
    }

    setState(() {
      if (shouldUpdateActiveSession) {
        _activeSessionId = remoteSessionId;
      }
      if (shouldClearDecisionState) {
        _requiresSessionDecision = false;
        _remoteSessionIdPendingDecision = null;
      }
    });
  }

  void _handleSessionTerminalStateFeedback({
    required SessionLifecycleState? previousState,
    required SessionStateUpdateSignal? sessionUpdate,
    required RuntimeStatusUpdateSignal? runtimeUpdate,
  }) {
    if (!mounted || sessionUpdate == null) {
      return;
    }

    final state = sessionUpdate.state;
    final isTerminal = SessionRecoveryPolicy.isTerminalState(state);
    final wasTerminal = previousState != null &&
        SessionRecoveryPolicy.isTerminalState(previousState);

    if (!isTerminal || wasTerminal) {
      return;
    }

    final runtime = runtimeUpdate?.status;
    if (state == SessionLifecycleState.completed) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Game completed. You can adjust settings and start a new round.',
          ),
          duration: Duration(seconds: 2),
        ),
      );
    } else {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            'Session ended (${state.wireValue}${runtime == null ? '' : ', runtime=${runtime.wireValue}'}).',
          ),
          duration: const Duration(seconds: 2),
        ),
      );
    }
  }

  String? _resolveRemoteSessionId(
    SessionStateUpdateSignal? sessionUpdate,
    RuntimeStatusUpdateSignal? runtimeUpdate,
    SessionWatchdogHeartbeatSignal? watchdogHeartbeat,
  ) {
    final fromSessionState = sessionUpdate?.sessionId ?? '';
    if (fromSessionState.isNotEmpty) {
      return fromSessionState;
    }

    final fromRuntimeStatus = runtimeUpdate?.sessionId ?? '';
    if (fromRuntimeStatus.isNotEmpty) {
      return fromRuntimeStatus;
    }

    final fromWatchdogHeartbeat = watchdogHeartbeat?.sessionId ?? '';
    if (fromWatchdogHeartbeat.isNotEmpty) {
      return fromWatchdogHeartbeat;
    }

    return null;
  }

  String _resolveSessionIdForCriticalCommand(String command) {
    final activeSessionId = _activeSessionId.trim();
    final pendingDecisionSessionId = _requiresSessionDecision
        ? (_remoteSessionIdPendingDecision?.trim() ?? '')
        : '';
    final lastSessionSignalId = _lastSessionStateUpdateSessionId?.trim() ?? '';
    final lastRuntimeSignalId = _lastRuntimeStatusSessionId?.trim() ?? '';
    final persistedSessionId = _latestPersistedSession?.sessionId.trim() ?? '';

    if (command == CriticalCommandIds.endSession) {
      for (final candidate in <String>[
        lastSessionSignalId,
        lastRuntimeSignalId,
        pendingDecisionSessionId,
        activeSessionId,
      ]) {
        if (candidate.isNotEmpty) {
          return candidate;
        }
      }

      if (persistedSessionId.isNotEmpty) {
        return persistedSessionId;
      }
      return activeSessionId;
    }

    if (_gameScopedCriticalCommands.contains(command)) {
      for (final candidate in <String>[
        lastSessionSignalId,
        lastRuntimeSignalId,
        activeSessionId,
      ]) {
        if (candidate.isNotEmpty) {
          return candidate;
        }
      }
    }

    return activeSessionId;
  }

  void _promptSessionDecisionIfNeeded() {
    if (!mounted || !_requiresSessionDecision || _isSessionDecisionDialogOpen) {
      return;
    }

    if (!_isSessionDecisionAllowedByContext()) {
      final deferredSessionId = _remoteSessionIdPendingDecision?.trim() ?? '';
      if (deferredSessionId.isNotEmpty) {
        _markDeferredHandoff(
          sessionId: deferredSessionId,
          reasonCode: 'CONTEXT_NOT_ENTRY_OR_RECONNECT',
          source: 'prompt',
        );
      }
      _logSessionDecision(
        source: 'prompt',
        decision: 'SKIP_DIALOG',
        sessionId: _remoteSessionIdPendingDecision,
        reason: 'CONTEXT_NOT_ENTRY_OR_RECONNECT',
      );
      setState(() {
        _requiresSessionDecision = false;
        _remoteSessionIdPendingDecision = null;
      });
      return;
    }

    _logSessionDecision(
      source: 'prompt',
      decision: 'OPEN_DIALOG',
      sessionId: _remoteSessionIdPendingDecision,
      reason: 'PENDING_DECISION',
    );
    _isSessionDecisionDialogOpen = true;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      unawaited(_showSessionDecisionDialog());
    });
  }

  Future<void> _showSessionDecisionDialog() async {
    if (!mounted) {
      _isSessionDecisionDialogOpen = false;
      return;
    }

    final remoteSessionId = _remoteSessionIdPendingDecision;
    if (remoteSessionId == null ||
        remoteSessionId.isEmpty ||
        !_requiresSessionDecision) {
      _isSessionDecisionDialogOpen = false;
      return;
    }

    if (!_isSessionDecisionAllowedByContext()) {
      _markDeferredHandoff(
        sessionId: remoteSessionId,
        reasonCode: 'CONTEXT_NOT_ENTRY_OR_RECONNECT',
        source: 'dialog',
      );
      _logSessionDecision(
        source: 'dialog',
        decision: 'SKIP_DIALOG',
        sessionId: remoteSessionId,
        reason: 'CONTEXT_CHANGED',
      );
      setState(() {
        _requiresSessionDecision = false;
        _remoteSessionIdPendingDecision = null;
      });
      _isSessionDecisionDialogOpen = false;
      return;
    }

    final recoveryWindowMinutes =
        _therapistSessionSettings.sessionRecoveryWindowMinutes;
    final action = await showDialog<_SessionGateAction>(
      context: context,
      barrierDismissible: false,
      builder: (context) {
        return PopScope(
          canPop: false,
          child: AlertDialog(
            title: const Text('Session handoff needed'),
            content: Text(
              'The headset reports another unfinished session and the '
              'recovery window ($recoveryWindowMinutes min) has passed.\n\n'
              'Choose whether to continue it, start a new session, '
              'or keep current context for now.',
            ),
            actions: [
              TextButton(
                onPressed: () =>
                    Navigator.of(context).pop(_SessionGateAction.keepCurrent),
                child: const Text('Keep current'),
              ),
              TextButton(
                onPressed: () =>
                    Navigator.of(context).pop(_SessionGateAction.resume),
                child: const Text('Continue unfinished'),
              ),
              ElevatedButton(
                onPressed: () =>
                    Navigator.of(context).pop(_SessionGateAction.startNew),
                child: const Text('Start new session'),
              ),
            ],
          ),
        );
      },
    );

    if (!mounted) {
      _isSessionDecisionDialogOpen = false;
      return;
    }

    switch (action) {
      case _SessionGateAction.keepCurrent:
        await _handleKeepCurrentDecision(remoteSessionId);
        break;
      case _SessionGateAction.resume:
        await _handleResumeDecision(remoteSessionId);
        break;
      case _SessionGateAction.startNew:
        await _handleStartNewDecision(remoteSessionId);
        break;
      case null:
        await _handleKeepCurrentDecision(remoteSessionId);
        break;
    }

    _isSessionDecisionDialogOpen = false;
    if (_requiresSessionDecision) {
      _promptSessionDecisionIfNeeded();
    }
  }

  Future<void> _handleKeepCurrentDecision(String remoteSessionId) async {
    _logSessionDecision(
      source: 'dialog',
      decision: 'KEEP_CURRENT',
      sessionId: remoteSessionId,
      reason: 'THERAPIST_KEEP_CURRENT',
    );

    if (!mounted) {
      return;
    }

    setState(() {
      _requiresSessionDecision = false;
      _remoteSessionIdPendingDecision = null;
      _lastConnectionLostAtUtc = null;
    });
    _markDeferredHandoff(
      sessionId: remoteSessionId,
      reasonCode: 'THERAPIST_KEEP_CURRENT',
      source: 'dialog',
    );

    if (_isConnected && !_sessionAttachReady) {
      await _ensureSessionAttached(
        reasonCode: 'HANDOFF_KEEP_CURRENT',
        force: true,
        sessionIdOverride: _activeSessionId,
      );
    }

    if (!mounted) {
      return;
    }

    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(
        content: Text(
          'Keeping current session context. You can reconnect later to hand off.',
        ),
        duration: Duration(seconds: 2),
      ),
    );
  }

  Future<void> _handleResumeDecision(String remoteSessionId) async {
    _logSessionDecision(
      source: 'dialog',
      decision: 'RESUME_REMOTE',
      sessionId: remoteSessionId,
      reason: 'THERAPIST_CONTINUE_UNFINISHED',
    );
    final resolvedGameId = _resolveRemoteGameIdForResume();

    setState(() {
      _activeSessionId = remoteSessionId;
      _requiresSessionDecision = false;
      _remoteSessionIdPendingDecision = null;
      _lastConnectionLostAtUtc = null;
      _sessionAttachReady = false;
      _workflowStep = _WorkflowStep.gameSetup;
      _isVideoPreviewExpanded = true;
      if (resolvedGameId != null) {
        _selectedGameId = resolvedGameId;
      }
    });
    _clearDeferredHandoff(
      sessionId: remoteSessionId,
      source: 'dialog',
      reasonCode: 'THERAPIST_CONTINUE_UNFINISHED',
    );
    await _ensureSessionAttached(
      reasonCode: 'HANDOFF_RESUME',
      force: true,
    );
    if (mounted && _sessionAttachReady) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Session continued. Use Start/Resume/Pause controls to proceed.',
          ),
        ),
      );
    }
  }

  String? _resolveRemoteGameIdForResume() {
    final remoteGameId = _remoteActiveGameId?.trim() ?? '';
    if (remoteGameId.isEmpty) {
      return null;
    }

    for (final entry in _effectiveGameCatalog) {
      if (entry.gameId == remoteGameId) {
        return remoteGameId;
      }
    }

    return null;
  }

  Future<void> _handleStartNewDecision(String remoteSessionId) async {
    _logSessionDecision(
      source: 'dialog',
      decision: 'START_NEW_SELECTED',
      sessionId: remoteSessionId,
      reason: 'THERAPIST_START_NEW_REQUESTED',
    );
    final confirmed = await showDialog<bool>(
          context: context,
          barrierDismissible: false,
          builder: (context) {
            return PopScope(
              canPop: false,
              child: AlertDialog(
                title: const Text('Start a new session?'),
                content: const Text(
                  'This sends END_SESSION for the unfinished headset session '
                  'and creates a new one. Continue?',
                ),
                actions: [
                  TextButton(
                    onPressed: () => Navigator.of(context).pop(false),
                    child: const Text('Cancel'),
                  ),
                  ElevatedButton(
                    onPressed: () => Navigator.of(context).pop(true),
                    child: const Text('Start new'),
                  ),
                ],
              ),
            );
          },
        ) ??
        false;

    if (!confirmed || !mounted) {
      _logSessionDecision(
        source: 'dialog',
        decision: 'START_NEW_CANCELLED',
        sessionId: remoteSessionId,
        reason: 'THERAPIST_CANCELLED_CONFIRMATION',
      );
      return;
    }

    try {
      await _connection.sendCriticalCommand(
        commandId: CriticalCommandIds.endSession,
        sessionId: remoteSessionId,
        payload: _buildCriticalPayload(
          CriticalCommandIds.endSession,
          sessionId: remoteSessionId,
        ),
        expiresAtUtc: DateTime.now().toUtc().add(const Duration(seconds: 30)),
      );
      _markSessionAsRecentlyEnded(remoteSessionId);
      await _persistStartNewDecisionOutcome(remoteSessionId);

      if (!mounted) {
        return;
      }

      setState(() {
        _activeSessionId = _buildLocalSessionId();
        _requiresSessionDecision = false;
        _remoteSessionIdPendingDecision = null;
        _lastConnectionLostAtUtc = null;
        _sessionAttachReady = false;
        _workflowStep = _WorkflowStep.gameCatalog;
        _isVideoPreviewExpanded = false;
      });
      _clearDeferredHandoff(
        sessionId: remoteSessionId,
        source: 'dialog',
        reasonCode: 'END_SESSION_AND_ATTACH_OK',
      );
      await _ensureSessionAttached(
        reasonCode: 'HANDOFF_START_NEW',
        force: true,
      );
      if (!mounted || !_sessionAttachReady) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Previous session ended. You can start a new one.'),
        ),
      );
      _logSessionDecision(
        source: 'dialog',
        decision: 'START_NEW_COMPLETED',
        sessionId: remoteSessionId,
        reason: 'END_SESSION_AND_ATTACH_OK',
      );
    } catch (e) {
      if (!mounted) {
        return;
      }

      final errorSummary = OpsErrorCatalog.buildOperatorSummary(
        error: e,
      );
      _logSessionDecision(
        source: 'dialog',
        decision: 'START_NEW_FAILED',
        sessionId: remoteSessionId,
        reason: 'END_SESSION_FAILED',
      );
      _enqueueIncidentAlert(
        title: 'Could not end previous session',
        message: 'Could not end previous session: $errorSummary',
        reasonCode: OpsErrorCatalog.tryExtractReasonCode(e) ??
            'END_SESSION_STOP_FAILED',
        severity: OperatorIncidentSeverity.error,
      );
    }
  }

  Future<bool> _sendCommand(
    String command, {
    Map<String, dynamic>? extraPayload,
    bool showSuccessSnack = true,
  }) async {
    if (_gameScopedCriticalCommands.contains(command) &&
        _selectedGameId.trim().isEmpty) {
      if (!mounted) {
        return false;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Select a game before sending this command.'),
          backgroundColor: Colors.orange,
        ),
      );
      return false;
    }

    if (_requiresSessionDecision && command != CriticalCommandIds.endSession) {
      _logSessionDecision(
        source: 'command',
        decision: 'BLOCK_COMMAND',
        commandId: command,
        sessionId: _remoteSessionIdPendingDecision,
        reason: 'PENDING_HANDOFF_DECISION',
      );
      if (!mounted) {
        return false;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Choose Resume or Start New before sending commands.'),
          backgroundColor: Colors.orange,
        ),
      );
      _promptSessionDecisionIfNeeded();
      return false;
    }

    if (CriticalCommandIds.isCritical(command) &&
        command != CriticalCommandIds.sessionAttach &&
        command != CriticalCommandIds.endSession &&
        !_sessionAttachReady) {
      _logAttachDecision(
        decision: 'REQUIRE_ATTACH_PRECONDITION',
        reasonCode: 'COMMAND_PRECONDITION',
        commandId: command,
      );
      await _ensureSessionAttached(
        reasonCode: 'COMMAND_PRECONDITION',
        force: true,
      );
      if (!_sessionAttachReady) {
        if (!mounted) {
          return false;
        }
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('Waiting for session sync. Try again in a moment.'),
            backgroundColor: Colors.orange,
          ),
        );
        return false;
      }
    } else if (command == CriticalCommandIds.endSession &&
        !_sessionAttachReady) {
      _logAttachDecision(
        decision: 'BYPASS_ATTACH_PRECONDITION',
        reasonCode: 'END_SESSION_OVERRIDE',
        commandId: command,
      );
    } else if (CriticalCommandIds.isCritical(command) &&
        command != CriticalCommandIds.sessionAttach) {
      _logAttachDecision(
        decision: 'PRECONDITION_ALREADY_SATISFIED',
        reasonCode: 'COMMAND_PRECONDITION',
        commandId: command,
      );
    }

    final commandSessionId = CriticalCommandIds.isCritical(command)
        ? _resolveSessionIdForCriticalCommand(command)
        : _activeSessionId;
    if (CriticalCommandIds.isCritical(command) &&
        commandSessionId.trim().isEmpty) {
      if (!mounted) {
        return false;
      }

      _enqueueIncidentAlert(
        title: 'Critical command blocked',
        message: 'Cannot resolve active session id for this command.',
        reasonCode: 'SESSION_ID_REQUIRED',
        severity: OperatorIncidentSeverity.error,
      );
      return false;
    }

    try {
      if (CriticalCommandIds.isCritical(command)) {
        await _connection.sendCriticalCommand(
          commandId: command,
          sessionId: commandSessionId,
          payload: _buildCriticalPayload(
            command,
            sessionId: commandSessionId,
            extraPayload: extraPayload,
          ),
          expiresAtUtc: DateTime.now().toUtc().add(const Duration(seconds: 30)),
        );
      } else {
        await _connection.sendCommand(command, null);
      }

      if (mounted) {
        setState(() {
          if (command == CriticalCommandIds.startGame ||
              command == CriticalCommandIds.resumeGame) {
            _optimisticRuntimeActive = true;
            _optimisticRuntimePaused = false;
          } else if (command == CriticalCommandIds.pauseGame) {
            _optimisticRuntimeActive = true;
            _optimisticRuntimePaused = true;
          } else if (command == CriticalCommandIds.stopGame ||
              command == CriticalCommandIds.endSession) {
            _optimisticRuntimeActive = false;
            _optimisticRuntimePaused = false;
            _remoteActiveGameId = null;
          }
        });
      }

      if (command == CriticalCommandIds.startGame ||
          command == CriticalCommandIds.pauseGame ||
          command == CriticalCommandIds.resumeGame ||
          command == CriticalCommandIds.stopGame ||
          command == CriticalCommandIds.endSession) {
        unawaited(
          _persistCommandSideEffects(
            command,
            sessionIdOverride: commandSessionId,
            extraPayload: extraPayload,
          ),
        );
      }

      if (!mounted || !showSuccessSnack) {
        return true;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Sent: $command'),
          duration: const Duration(seconds: 1),
        ),
      );
      return true;
    } catch (e) {
      if (!mounted) {
        return false;
      }

      final isCritical = CriticalCommandIds.isCritical(command);
      final errorSummary = OpsErrorCatalog.buildOperatorSummary(
        error: e,
      );
      final message = isCritical
          ? 'Failed: $command - $errorSummary'
          : 'Failed: $command ($e)';

      _enqueueIncidentAlert(
        title: 'Command failed: $command',
        message: message,
        reasonCode: OpsErrorCatalog.tryExtractReasonCode(e) ??
            (isCritical ? 'UNSPECIFIED' : 'UNKNOWN'),
        severity: OperatorIncidentSeverity.error,
      );
      return false;
    }
  }

  Map<String, dynamic> _buildCriticalPayload(
    String command, {
    required String sessionId,
    Map<String, dynamic>? extraPayload,
  }) {
    final therapistId = _resolveActorTherapistId();
    final ownerKey = _resolveOwnerKey(therapistIdOverride: therapistId);
    final sessionKey = _resolveSessionKey(
      sessionId,
      therapistIdOverride: therapistId,
    );
    final payload = <String, dynamic>{
      'sessionId': sessionId,
      'studentId': widget.student.id,
      'patientId': widget.student.id,
      'therapistId': therapistId,
      'ownerKey': ownerKey,
      'sessionKey': sessionKey,
    };

    if (_gameScopedCriticalCommands.contains(command)) {
      payload['gameId'] = _selectedGameId;
      payload['resumeFromSaved'] = false;
    }

    if (command == CriticalCommandIds.startGame) {
      final schema = _selectedGameEntry.mobileControlSchema;
      if (schema != null) {
        final schemaGameConfig = _buildSchemaDrivenGameConfigPayload(
          schema,
          emitForUpdateConfig: false,
        );
        payload['gameConfigType'] = _resolveSchemaGameConfigType(schema);
        payload['gameConfigVersion'] = _resolveSchemaGameConfigVersion(schema);
        payload['gameConfigJson'] = jsonEncode(schemaGameConfig);
      } else if (_isDemoCubeGameSelected) {
        payload['gameConfigType'] = 'demo_cube_config_v1';
        payload['gameConfigVersion'] = 1;
        payload['gameConfigJson'] = jsonEncode(<String, dynamic>{
          'cubeCount': _demoCubeCount,
          'cubeSpeed': double.parse(_demoCubeSpeed.toStringAsFixed(2)),
          'levelMode': _demoLevelMode,
          'version': 1,
        });
      } else if (_isPulseTargetGameSelected) {
        final adaptiveDifficultySensitivity = double.parse(
          _therapistSessionSettings.adaptiveDifficultySensitivity
              .toStringAsFixed(2),
        );
        payload['gameConfigType'] = 'pulse_targets_config_v1';
        payload['gameConfigVersion'] = 1;
        payload['gameConfigJson'] = jsonEncode(<String, dynamic>{
          'targetCount': _pulseTargetCount,
          'targetSpeed': double.parse(_pulseTargetSpeed.toStringAsFixed(2)),
          'targetScale': double.parse(_pulseTargetScale.toStringAsFixed(2)),
          'adaptiveDifficultyEnabled':
              _therapistSessionSettings.adaptiveDifficultyEnabled,
          'adaptiveDifficultySensitivity': adaptiveDifficultySensitivity,
          'adaptiveDifficultyLevel': _resolveAdaptiveDifficultyLevel(),
          'labelPipelineEnabled':
              _therapistSessionSettings.labelPipelineEnabled,
          'version': 1,
        });
      }
    }

    if (command == CriticalCommandIds.stopGame) {
      payload['reason'] = 'UserExit';
    } else if (command == CriticalCommandIds.endSession) {
      payload['reason'] = 'TherapistEndedSession';
    }

    if (extraPayload != null && extraPayload.isNotEmpty) {
      payload.addAll(extraPayload);
    }

    return payload;
  }

  Future<void> _handleSchemaButtonControl(
    MobileControlDefinition control,
  ) async {
    final normalizedCommand = control.buttonCommandId.trim().toUpperCase();
    if (normalizedCommand == _updateConfigCommandId) {
      await _sendUpdateConfigFromSchema();
      return;
    }

    if (!mounted) {
      return;
    }

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          'Unsupported schema command: ${control.buttonCommandId.trim()}',
        ),
        backgroundColor: Colors.orange.shade700,
      ),
    );
  }

  Future<bool> _sendUpdateConfigFromSchema() async {
    final schema = _selectedGameEntry.mobileControlSchema;
    if (schema == null) {
      return false;
    }

    if (!_isConnected) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('Headset is offline. Cannot send UPDATE_CONFIG.'),
            backgroundColor: Colors.orange,
          ),
        );
      }
      return false;
    }

    final sessionId = _activeSessionId;
    final therapistId = _resolveActorTherapistId();
    final ownerKey = _resolveOwnerKey(therapistIdOverride: therapistId);
    final sessionKey = _resolveSessionKey(
      sessionId,
      therapistIdOverride: therapistId,
    );
    final gameConfig = _buildSchemaDrivenGameConfigPayload(
      schema,
      emitForUpdateConfig: true,
    );

    final payload = <String, dynamic>{
      'sessionId': sessionId,
      'studentId': widget.student.id,
      'patientId': widget.student.id,
      'therapistId': therapistId,
      'ownerKey': ownerKey,
      'sessionKey': sessionKey,
      'gameId': _selectedGameId,
      'gameConfigType': _resolveSchemaGameConfigType(schema),
      'gameConfigVersion': _resolveSchemaGameConfigVersion(schema),
      'gameConfigJson': jsonEncode(gameConfig),
    };

    try {
      await _connection.sendCommand(_updateConfigCommandId, payload);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('Configuration update sent.'),
            duration: Duration(seconds: 1),
          ),
        );
      }
      return true;
    } catch (e) {
      if (mounted) {
        _enqueueIncidentAlert(
          title: 'UPDATE_CONFIG failed',
          message: 'Failed to send UPDATE_CONFIG: $e',
          reasonCode: OpsErrorCatalog.tryExtractReasonCode(e) ??
              'UPDATE_CONFIG_SEND_FAILED',
          severity: OperatorIncidentSeverity.error,
        );
      }
      return false;
    }
  }

  int _resolveAdaptiveDifficultyLevel() {
    final normalized =
        _therapistSessionSettings.adaptiveDifficultySensitivity.clamp(0.0, 1.0);
    final scaled = 1 + (normalized * 4).round();
    if (scaled < 1) {
      return 1;
    }
    if (scaled > 5) {
      return 5;
    }
    return scaled;
  }

  Future<void> _runPrimaryAction(Future<void> Function() action) async {
    if (_isPrimaryActionInFlight) {
      return;
    }

    setState(() {
      _isPrimaryActionInFlight = true;
    });

    try {
      await action();
    } finally {
      if (mounted) {
        setState(() {
          _isPrimaryActionInFlight = false;
        });
      }
    }
  }

  Future<void> _startParentGuidedSession() async {
    if (!_isParentRole) {
      return;
    }

    final planBanner = _planGateBannerText;
    if (_isPlanBlockingLaunch || planBanner != null) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(planBanner ?? 'Current plan blocks guided start.'),
          backgroundColor: Colors.orange,
        ),
      );
      return;
    }

    if (!mounted || !_isConnected || !_sessionAttachReady) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Wait for headset connection and session sync first.'),
          backgroundColor: Colors.orange,
        ),
      );
      return;
    }

    if (_isHeadsetPresenceBlocking) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Headset is not in active VR app yet.'),
          backgroundColor: Colors.orange,
        ),
      );
      return;
    }

    if (_workflowStep != _WorkflowStep.gameSetup) {
      setState(() {
        _workflowStep = _WorkflowStep.gameSetup;
        _isVideoPreviewExpanded = true;
      });
    }

    await _startFromSetup();
  }

  Future<void> _startFromSetup() async {
    if (_isGameRuntimeActive) {
      if (!mounted) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Gra jest aktywna. Najpierw zatrzymaj lub zakoncz sesje, aby zmienic ustawienia.',
          ),
          backgroundColor: Colors.orange,
        ),
      );
      return;
    }

    final selectedContentState = _selectedContentState;
    if (!_isLaunchableContentState(selectedContentState)) {
      if (!mounted) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            'Game not ready: ${selectedContentState.runtimeStatus.wireValue}. Install/update from catalog first.',
          ),
          backgroundColor: Colors.orange,
        ),
      );
      return;
    }

    await _runPrimaryAction(() async {
      await _sendCommand(
        CriticalCommandIds.startGame,
        extraPayload: const <String, dynamic>{
          'resumeFromSaved': false,
        },
      );
    });
  }

  Future<void> _restartFromSetup() async {
    if (!_isGameRuntimeActive) {
      if (!mounted) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Restart is available only while a game is active.',
          ),
          backgroundColor: Colors.orange,
        ),
      );
      return;
    }

    final confirmed = await showDialog<bool>(
          context: context,
          builder: (context) {
            return AlertDialog(
              title: const Text('Restart game?'),
              content: const Text(
                'Current round will be stopped and started from the beginning. Continue?',
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.of(context).pop(false),
                  child: const Text('Cancel'),
                ),
                ElevatedButton(
                  onPressed: () => Navigator.of(context).pop(true),
                  child: const Text('Restart'),
                ),
              ],
            );
          },
        ) ??
        false;

    if (!confirmed) {
      return;
    }

    await _runPrimaryAction(() async {
      final stopSent = await _sendCommand(
        CriticalCommandIds.stopGame,
        showSuccessSnack: false,
      );
      if (!stopSent) {
        return;
      }

      await _sendCommand(
        CriticalCommandIds.startGame,
        extraPayload: const <String, dynamic>{
          'resumeFromSaved': false,
        },
      );
    });
  }

  Future<void> _returnToGameCatalog() async {
    var shouldStopRunningGame = false;
    if (_isGameRuntimeActive) {
      final decision = await showDialog<bool>(
            context: context,
            barrierDismissible: false,
            builder: (context) {
              return PopScope(
                canPop: false,
                child: AlertDialog(
                  title: const Text('Return to game catalog?'),
                  content: const Text(
                    'This will stop the current game and return to the game catalog.',
                  ),
                  actions: [
                    TextButton(
                      onPressed: () => Navigator.of(context).pop(false),
                      child: const Text('Cancel'),
                    ),
                    ElevatedButton(
                      onPressed: () => Navigator.of(context).pop(true),
                      child: const Text('Return'),
                    ),
                  ],
                ),
              );
            },
          ) ??
          false;

      if (!decision) {
        return;
      }

      shouldStopRunningGame = true;
    }

    await _runPrimaryAction(() async {
      if (shouldStopRunningGame) {
        await _sendCommand(
          CriticalCommandIds.stopGame,
          showSuccessSnack: false,
        );
      }

      if (!mounted) {
        return;
      }

      setState(() {
        _workflowStep = _WorkflowStep.gameCatalog;
        _isVideoPreviewExpanded = false;
      });
    });
  }

  Future<bool> _sendEndSessionWithConfirmation({
    String reasonCode = 'THERAPIST_CONFIRMED_END',
    Map<String, dynamic>? extraPayload,
  }) async {
    if (!_isConnected) {
      if (!mounted) {
        return false;
      }
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content:
              Text('Device disconnected. Reconnect before ending session.'),
          backgroundColor: Colors.orange,
        ),
      );
      return false;
    }

    final sessionIdToEnd = _resolveSessionIdForCriticalCommand(
      CriticalCommandIds.endSession,
    );
    _logAttachDecision(
      decision: 'END_SESSION_REQUEST',
      reasonCode: reasonCode,
      commandId: CriticalCommandIds.endSession,
      sessionId: sessionIdToEnd,
    );
    final ended = await _sendCommand(
      CriticalCommandIds.endSession,
      extraPayload: extraPayload,
      showSuccessSnack: false,
    );
    if (!ended) {
      return false;
    }

    _markSessionAsRecentlyEnded(sessionIdToEnd);
    _clearDeferredHandoff(
      sessionId: sessionIdToEnd,
      source: 'command',
      reasonCode: reasonCode,
    );
    _markSessionAsRecentlyEnded(_lastSessionStateUpdateSessionId);
    _markSessionAsRecentlyEnded(_lastRuntimeStatusSessionId);
    _markSessionAsRecentlyEnded(_remoteSessionIdPendingDecision);
    _markSessionAsRecentlyEnded(_latestPersistedSession?.sessionId);

    if (mounted) {
      setState(() {
        _requiresSessionDecision = false;
        _remoteSessionIdPendingDecision = null;
      });
    }
    return true;
  }

  Future<bool> _handleSystemBackPressed() async {
    if (_allowSystemPop) {
      return true;
    }

    final choice = await showDialog<_ExitChoice>(
      context: context,
      barrierDismissible: false,
      builder: (context) {
        return PopScope(
          canPop: false,
          child: AlertDialog(
            title: const Text('Close controller?'),
            content: const Text(
              'Do you want to end the session now, or leave it unfinished and exit?',
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.of(context).pop(_ExitChoice.cancel),
                child: const Text('Cancel'),
              ),
              TextButton(
                onPressed: () =>
                    Navigator.of(context).pop(_ExitChoice.keepUnfinished),
                child: const Text('Leave unfinished'),
              ),
              ElevatedButton(
                onPressed: () =>
                    Navigator.of(context).pop(_ExitChoice.endSession),
                child: const Text('End session'),
              ),
            ],
          ),
        );
      },
    );

    if (choice == null || choice == _ExitChoice.cancel) {
      return false;
    }

    if (choice == _ExitChoice.endSession) {
      final ended = await _sendEndSessionWithConfirmation();
      if (!ended) {
        return false;
      }
    }

    await _disconnectAndPop(
      returnToStudentSelection: choice == _ExitChoice.endSession,
    );
    return false;
  }

  Future<void> _disconnectAndPop(
      {bool returnToStudentSelection = false}) async {
    _autoReconnectEnabled = false;
    _allowSystemPop = true;
    unawaited(ForegroundServiceBridge.stop());
    await _connection.disconnect();

    if (!mounted) {
      return;
    }

    Navigator.pop(context, returnToStudentSelection);
  }

  @override
  Widget build(BuildContext context) {
    final isCatalogScreen = _workflowStep == _WorkflowStep.gameCatalog;

    return PopScope(
      canPop: false,
      onPopInvokedWithResult: (didPop, _) {
        if (didPop) {
          return;
        }

        unawaited(_handleSystemBackPressed());
      },
      child: Scaffold(
        appBar: AppBar(
          automaticallyImplyLeading: false,
          leading: isCatalogScreen
              ? IconButton(
                  onPressed: _isPrimaryActionInFlight
                      ? null
                      : () => unawaited(_disconnectAndPop()),
                  tooltip: 'Back to student selection',
                  icon: const Icon(Icons.arrow_back),
                )
              : IconButton(
                  onPressed: _isPrimaryActionInFlight
                      ? null
                      : () => unawaited(_returnToGameCatalog()),
                  tooltip: 'Back to game catalog',
                  icon: const Icon(Icons.arrow_back),
                ),
          title: Text(
            isCatalogScreen
                ? '${widget.student.firstName}: game catalog'
                : '${_selectedGameEntry.title}: session',
          ),
          actions: [
            if (_hasDeferredHandoff) _buildDeferredHandoffBadgeAction(),
            Padding(
              padding: const EdgeInsets.only(right: 12),
              child: Center(
                child: Container(
                  padding:
                      const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
                  decoration: BoxDecoration(
                    color: _isConnected
                        ? Colors.green.shade50
                        : Colors.red.shade50,
                    borderRadius: BorderRadius.circular(999),
                    border: Border.all(
                      color: _isConnected
                          ? Colors.green.shade200
                          : Colors.red.shade200,
                    ),
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Icon(
                        _isConnected ? Icons.wifi : Icons.wifi_off,
                        color: _isConnected
                            ? Colors.green.shade700
                            : Colors.red.shade700,
                        size: 16,
                      ),
                      const SizedBox(width: 6),
                      Text(
                        _isConnected ? 'Connected' : 'Reconnecting',
                        style: TextStyle(
                          color: _isConnected
                              ? Colors.green.shade800
                              : Colors.red.shade800,
                          fontSize: 12,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ],
        ),
        body: SafeArea(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(12, 8, 12, 12),
            child: isCatalogScreen
                ? _buildGameCatalogStep()
                : _buildGameSetupStep(),
          ),
        ),
      ),
    );
  }

  Widget _buildVideoPreviewPanel({required String subtitle}) {
    return Card(
      margin: EdgeInsets.zero,
      child: Column(
        children: [
          ListTile(
            contentPadding: const EdgeInsets.symmetric(
              horizontal: 12,
              vertical: 2,
            ),
            title: const Text(
              'VR preview',
              style: TextStyle(fontWeight: FontWeight.w700),
            ),
            subtitle: Text(
              subtitle,
              style: TextStyle(
                color: Colors.grey[700],
                fontSize: 12,
              ),
            ),
            trailing: IconButton(
              icon: Icon(
                _isVideoPreviewExpanded ? Icons.expand_less : Icons.expand_more,
              ),
              onPressed: () {
                setState(() {
                  _isVideoPreviewExpanded = !_isVideoPreviewExpanded;
                });
              },
            ),
          ),
          Offstage(
            offstage: !_isVideoPreviewExpanded,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(12, 0, 12, 12),
              child: AspectRatio(
                aspectRatio: 16 / 9,
                child: MediaStreamWidget(
                  connection: _connection,
                  deviceIP: widget.device.ip,
                  port: widget.device.videoPort,
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildStateBanner({
    required IconData icon,
    required Color color,
    required String text,
  }) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.1),
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: color.withValues(alpha: 0.25)),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, size: 16, color: color),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              text,
              style: TextStyle(
                color: color,
                fontSize: 12,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildDeferredHandoffBadgeAction() {
    final sessionId = _deferredHandoffSessionIdValue;
    final hasDeferred = sessionId.isNotEmpty;
    final accent = Colors.orange.shade800;

    return IconButton(
      tooltip: hasDeferred
          ? 'Deferred handoff pending. Tap to review.'
          : 'No deferred handoff',
      onPressed: hasDeferred ? () => unawaited(_reviewDeferredHandoff()) : null,
      icon: Stack(
        clipBehavior: Clip.none,
        children: [
          const Icon(Icons.assignment_late_outlined),
          if (hasDeferred)
            Positioned(
              right: -1,
              top: -1,
              child: Container(
                width: 11,
                height: 11,
                decoration: BoxDecoration(
                  color: accent,
                  shape: BoxShape.circle,
                  border: Border.all(color: Colors.white, width: 1),
                ),
              ),
            ),
        ],
      ),
    );
  }

  Widget _buildDeferredHandoffBanner() {
    final sessionId = _deferredHandoffSessionIdValue;
    final shortSessionId = sessionId.length > 16
        ? sessionId.substring(sessionId.length - 16)
        : sessionId;
    final reasonLabel =
        _describeDeferredHandoffReason(_deferredHandoffReasonCode ?? '');
    final deferredAt = _deferredHandoffMarkedAtUtc;
    final deferredAtLabel = _formatTimelineTimestamp(deferredAt);

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
      decoration: BoxDecoration(
        color: Colors.orange.shade50,
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: Colors.orange.shade200),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(
            Icons.assignment_late_outlined,
            size: 16,
            color: Colors.orange.shade800,
          ),
          const SizedBox(width: 8),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Deferred handoff pending',
                  style: TextStyle(
                    color: Colors.orange.shade900,
                    fontSize: 12,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  'Unfinished session [$shortSessionId] was deferred due to '
                  '$reasonLabel at $deferredAtLabel.',
                  style: TextStyle(
                    color: Colors.orange.shade900,
                    fontSize: 12,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: 8),
          OutlinedButton(
            onPressed: () => unawaited(_reviewDeferredHandoff()),
            style: OutlinedButton.styleFrom(
              foregroundColor: Colors.orange.shade900,
              side: BorderSide(color: Colors.orange.shade300),
              visualDensity: VisualDensity.compact,
            ),
            child: const Text('Review'),
          ),
        ],
      ),
    );
  }

  String _resolveTimelineSessionId() {
    final activeSessionId = _activeSessionId.trim();
    final lastSessionSignalId = _lastSessionStateUpdateSessionId?.trim() ?? '';
    final lastRuntimeSignalId = _lastRuntimeStatusSessionId?.trim() ?? '';
    final pendingDecisionSessionId =
        _remoteSessionIdPendingDecision?.trim() ?? '';
    final persistedSessionId = _latestPersistedSession?.sessionId.trim() ?? '';

    for (final candidate in <String>[
      lastSessionSignalId,
      lastRuntimeSignalId,
      pendingDecisionSessionId,
      persistedSessionId,
      activeSessionId,
    ]) {
      if (candidate.isNotEmpty) {
        return candidate;
      }
    }

    return '';
  }

  Future<void> _appendTimelineNote({
    required String noteText,
    required bool fromQuickTemplate,
  }) async {
    final normalizedNoteText = noteText.trim();
    if (normalizedNoteText.isEmpty || _timelineNoteInFlight) {
      return;
    }

    final sessionId = _resolveTimelineSessionId();
    if (sessionId.isEmpty) {
      if (!mounted) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Session context is not ready yet. Wait for sync before adding notes.',
          ),
          backgroundColor: Colors.orange,
        ),
      );
      return;
    }

    if (mounted) {
      setState(() {
        _timelineNoteInFlight = true;
      });
    } else {
      _timelineNoteInFlight = true;
    }

    try {
      await SessionJournalService.appendTherapistTimelineNote(
        sessionId: sessionId,
        studentId: widget.student.id,
        therapistId: _resolveActorTherapistId(),
        noteText: normalizedNoteText,
        gameId: _selectedGameId,
        fromQuickTemplate: fromQuickTemplate,
      );
      await _refreshPersistedSessionSnapshot(triggerPrompt: false);
      if (!mounted) {
        return;
      }

      if (!fromQuickTemplate) {
        _timelineNoteController.clear();
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Timeline note saved.'),
          duration: Duration(seconds: 1),
        ),
      );
    } catch (e) {
      if (!mounted) {
        return;
      }

      _enqueueIncidentAlert(
        title: 'Could not save timeline note',
        message: 'Could not save timeline note: $e',
        reasonCode: OpsErrorCatalog.tryExtractReasonCode(e) ?? 'UNSPECIFIED',
        severity: OperatorIncidentSeverity.error,
      );
    } finally {
      if (mounted) {
        setState(() {
          _timelineNoteInFlight = false;
        });
      } else {
        _timelineNoteInFlight = false;
      }
    }
  }

  String _formatTimelineTimestamp(DateTime? eventAtUtc) {
    if (eventAtUtc == null) {
      return '--:--:--';
    }

    final local = eventAtUtc.toLocal();
    final nowLocal = DateTime.now();
    final hour = local.hour.toString().padLeft(2, '0');
    final minute = local.minute.toString().padLeft(2, '0');
    final second = local.second.toString().padLeft(2, '0');
    final month = local.month.toString().padLeft(2, '0');
    final day = local.day.toString().padLeft(2, '0');
    final isSameDay = local.year == nowLocal.year &&
        local.month == nowLocal.month &&
        local.day == nowLocal.day;

    return isSameDay
        ? '$hour:$minute:$second'
        : '$month-$day $hour:$minute:$second';
  }

  String _formatTimelineEventType(String eventType) {
    switch (eventType.trim()) {
      case SessionJournalService.therapistTimelineNoteEventType:
        return 'Therapist note';
      case 'CONTROLLER_CONNECTED':
        return 'Controller connected';
      case 'CONTROLLER_RECONNECTED':
        return 'Controller reconnected';
      case 'CONTROLLER_DISCONNECTED':
        return 'Controller disconnected';
      case 'SESSION_ATTACH_ACK':
        return 'Session attached';
      case 'RUNTIME_SESSION_STATE_UPDATE':
        return 'Session state update';
      case 'GAME_STARTED':
        return 'Game started';
      case 'GAME_RESUMED':
        return 'Game resumed';
      case 'GAME_PAUSED':
        return 'Game paused';
      case 'GAME_ENDED':
        return 'Game ended';
      case 'SESSION_ENDED':
        return 'Session ended';
      case 'SESSION_ENDED_BY_DECISION':
        return 'Session ended by decision';
      case 'INTERRUPTED_SESSION_AUTO_CLOSED':
        return 'Interrupted session auto-closed';
      case 'SESSION_HANDOFF_DEFERRED':
        return 'Handoff deferred';
      case 'SESSION_HANDOFF_DEFERRED_CLEARED':
        return 'Handoff defer cleared';
      default:
        final compact = eventType.trim();
        if (compact.isEmpty) {
          return 'Unknown event';
        }
        final words = compact
            .split('_')
            .where((entry) => entry.trim().isNotEmpty)
            .map((entry) {
          final lower = entry.toLowerCase();
          return '${lower[0].toUpperCase()}${lower.substring(1)}';
        });
        return words.join(' ');
    }
  }

  String _formatTimelineEventSummary(SessionTimelineEvent event) {
    if (event.isTherapistNote) {
      return event.noteText;
    }

    final details = event.details;
    final fragments = <String>[];
    for (final key in <String>['state', 'reasonCode', 'reason', 'decision']) {
      final value = details[key];
      final text = value?.toString().trim() ?? '';
      if (text.isEmpty) {
        continue;
      }
      if (key == 'reasonCode') {
        final knownTag = OpsErrorCatalog.tryBuildKnownReasonTag(text);
        final reasonSummary = knownTag ?? text;
        fragments.add('$key=$reasonSummary');
        continue;
      }
      fragments.add('$key=$text');
    }

    if (fragments.isEmpty) {
      return '';
    }

    return fragments.join(' | ');
  }

  Widget _buildTimelineEventTile(SessionTimelineEvent event) {
    final title = _formatTimelineEventType(event.eventType);
    final summary = _formatTimelineEventSummary(event);
    final timestamp = _formatTimelineTimestamp(event.eventAtUtc);
    final metaFragments = <String>[timestamp];
    if (event.gameId.trim().isNotEmpty) {
      metaFragments.add('game=${event.gameId.trim()}');
    }
    final metaLine = metaFragments.join(' | ');

    final isNote = event.isTherapistNote;
    final markerColor = isNote ? Colors.indigo.shade700 : Colors.blueGrey;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(8),
        border: Border.all(
          color: markerColor.withValues(alpha: 0.25),
        ),
        color: markerColor.withValues(alpha: 0.06),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(
                isNote ? Icons.sticky_note_2_outlined : Icons.history,
                size: 14,
                color: markerColor,
              ),
              const SizedBox(width: 6),
              Expanded(
                child: Text(
                  title,
                  style: TextStyle(
                    color: markerColor,
                    fontSize: 12,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ),
            ],
          ),
          if (summary.isNotEmpty) ...[
            const SizedBox(height: 4),
            Text(
              summary,
              style: const TextStyle(
                fontSize: 12,
                fontWeight: FontWeight.w600,
              ),
            ),
          ],
          const SizedBox(height: 4),
          Text(
            metaLine,
            style: TextStyle(
              color: Colors.grey.shade700,
              fontSize: 11,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildTherapistTimelinePanel() {
    final sessionId = _resolveTimelineSessionId();
    final hasSessionId = sessionId.isNotEmpty;
    final templates = _therapistSessionSettings.timelineQuickNoteTemplates
        .map((entry) => entry.trim())
        .where((entry) => entry.isNotEmpty)
        .toList(growable: false);

    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.grey.shade100,
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(Icons.timeline, size: 18),
              const SizedBox(width: 8),
              const Expanded(
                child: Text(
                  'Session timeline',
                  style: TextStyle(
                    fontWeight: FontWeight.w700,
                    fontSize: 13,
                  ),
                ),
              ),
              if (hasSessionId)
                Text(
                  'session: ${sessionId.length > 16 ? sessionId.substring(sessionId.length - 16) : sessionId}',
                  style: TextStyle(
                    color: Colors.grey.shade700,
                    fontSize: 11,
                    fontWeight: FontWeight.w600,
                  ),
                ),
            ],
          ),
          const SizedBox(height: 4),
          Text(
            'System events and therapist notes in one stream.',
            style: TextStyle(
              color: Colors.grey.shade700,
              fontSize: 12,
            ),
          ),
          const SizedBox(height: 10),
          if (!hasSessionId)
            _buildStateBanner(
              icon: Icons.sync,
              color: Colors.orange.shade800,
              text:
                  'Timeline waits for session context. Reconnect/attach to start logging notes.',
            )
          else ...[
            Row(
              children: [
                Expanded(
                  child: TextField(
                    controller: _timelineNoteController,
                    enabled: !_timelineNoteInFlight,
                    textInputAction: TextInputAction.done,
                    onSubmitted: (value) {
                      if (_timelineNoteInFlight) {
                        return;
                      }
                      unawaited(
                        _appendTimelineNote(
                          noteText: value,
                          fromQuickTemplate: false,
                        ),
                      );
                    },
                    decoration: const InputDecoration(
                      isDense: true,
                      border: OutlineInputBorder(),
                      labelText: 'Add timeline note',
                      hintText: 'Type note and press Enter',
                    ),
                  ),
                ),
                const SizedBox(width: 8),
                ElevatedButton.icon(
                  onPressed: _timelineNoteInFlight
                      ? null
                      : () => unawaited(
                            _appendTimelineNote(
                              noteText: _timelineNoteController.text,
                              fromQuickTemplate: false,
                            ),
                          ),
                  icon: _timelineNoteInFlight
                      ? const SizedBox(
                          width: 12,
                          height: 12,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Icon(Icons.send, size: 16),
                  label: const Text('Add'),
                ),
              ],
            ),
            const SizedBox(height: 8),
            if (templates.isNotEmpty)
              Wrap(
                spacing: 6,
                runSpacing: 6,
                children: [
                  for (final template in templates)
                    ActionChip(
                      label: Text(template),
                      onPressed: _timelineNoteInFlight
                          ? null
                          : () => unawaited(
                                _appendTimelineNote(
                                  noteText: template,
                                  fromQuickTemplate: true,
                                ),
                              ),
                    ),
                ],
              ),
            const SizedBox(height: 10),
            SizedBox(
              height: 250,
              child: StreamBuilder<List<SessionTimelineEvent>>(
                stream: SessionJournalService.watchSessionTimeline(
                  sessionId: sessionId,
                  limit: 40,
                ),
                builder: (context, snapshot) {
                  if (snapshot.hasError) {
                    return Center(
                      child: Text(
                        'Timeline unavailable: ${snapshot.error}',
                        style: TextStyle(
                          color: Colors.red.shade700,
                          fontSize: 12,
                        ),
                        textAlign: TextAlign.center,
                      ),
                    );
                  }

                  if (!snapshot.hasData) {
                    return const Center(
                      child: CircularProgressIndicator(),
                    );
                  }

                  final events = snapshot.data!;
                  if (events.isEmpty) {
                    return Center(
                      child: Text(
                        'No timeline events yet.',
                        style: TextStyle(
                          color: Colors.grey.shade700,
                          fontSize: 12,
                        ),
                      ),
                    );
                  }

                  return ListView.separated(
                    itemCount: events.length,
                    separatorBuilder: (_, __) => const SizedBox(height: 6),
                    itemBuilder: (context, index) {
                      return _buildTimelineEventTile(events[index]);
                    },
                  );
                },
              ),
            ),
          ],
        ],
      ),
    );
  }

  Widget _buildContentStatusChip(PurchasedContentState state) {
    final label = switch (state.runtimeStatus) {
      ContentRuntimeStatus.notInstalled => 'Available',
      ContentRuntimeStatus.installing => 'Installing',
      ContentRuntimeStatus.ready => 'Installed',
      ContentRuntimeStatus.updateRequired => 'Update required',
      ContentRuntimeStatus.failed => 'Action needed',
    };
    final color = _contentStatusColor(state.runtimeStatus);

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(999),
        border: Border.all(color: color.withValues(alpha: 0.35)),
      ),
      child: Text(
        label,
        style: TextStyle(
          color: color,
          fontSize: 10,
          fontWeight: FontWeight.w700,
        ),
      ),
    );
  }

  Widget _buildVersionChip(PurchasedContentState state) {
    final installedVersion = state.installedVersion ?? '-';
    final targetVersion =
        state.targetVersion.trim().isEmpty ? '-' : state.targetVersion.trim();
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      decoration: BoxDecoration(
        color: Colors.blueGrey.withValues(alpha: 0.08),
        borderRadius: BorderRadius.circular(999),
        border: Border.all(color: Colors.blueGrey.withValues(alpha: 0.2)),
      ),
      child: Text(
        'v$installedVersion -> v$targetVersion',
        style: const TextStyle(
          color: Colors.blueGrey,
          fontSize: 10,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }

  Color _contentStatusColor(ContentRuntimeStatus status) {
    switch (status) {
      case ContentRuntimeStatus.notInstalled:
        return Colors.grey.shade700;
      case ContentRuntimeStatus.installing:
        return Colors.blue.shade700;
      case ContentRuntimeStatus.ready:
        return Colors.green.shade700;
      case ContentRuntimeStatus.updateRequired:
        return Colors.orange.shade700;
      case ContentRuntimeStatus.failed:
        return Colors.red.shade700;
    }
  }

  Widget _buildMoreGamesHint() {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.blueGrey.shade50,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: Colors.blueGrey.shade100),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Icon(Icons.add_circle_outline, size: 18),
          const SizedBox(width: 8),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text(
                  'Need more games?',
                  style: TextStyle(
                    fontWeight: FontWeight.w700,
                    fontSize: 13,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  'Use Installed/Store tabs. Additional titles can be granted or seeded by admin.',
                  style: TextStyle(
                    color: Colors.grey.shade700,
                    fontSize: 12,
                  ),
                ),
              ],
            ),
          ),
          TextButton(
            onPressed: _showMoreGamesInfo,
            child: const Text('How to add'),
          ),
        ],
      ),
    );
  }

  Future<void> _showMoreGamesInfo() async {
    if (!mounted) {
      return;
    }

    await showDialog<void>(
      context: context,
      builder: (context) {
        return AlertDialog(
          title: const Text('Add more games'),
          content: const Text(
            'Store tab shows titles available to add. Installed tab shows games already owned. '
            'If catalog is empty, ask admin to seed game_catalog and grant licenses, then tap Refresh.',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(context).pop(),
              child: const Text('Close'),
            ),
          ],
        );
      },
    );
  }

  Future<void> _endSessionFromGameScreen() async {
    final confirmed = await showDialog<bool>(
          context: context,
          builder: (context) => AlertDialog(
            title: const Text('End session?'),
            content: const Text(
              'This ends the current session and returns to student selection.',
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.of(context).pop(false),
                child: const Text('Cancel'),
              ),
              ElevatedButton(
                onPressed: () => Navigator.of(context).pop(true),
                child: const Text('End Session'),
              ),
            ],
          ),
        ) ??
        false;

    if (!confirmed) {
      return;
    }

    await _runPrimaryAction(() async {
      final ended = await _sendEndSessionWithConfirmation();
      if (!ended) {
        return;
      }

      await _disconnectAndPop(returnToStudentSelection: true);
    });
  }

  Future<void> _terminateActiveSessionFromCatalog() async {
    final confirmed = await showDialog<bool>(
          context: context,
          builder: (context) => AlertDialog(
            title: const Text('Terminate active session?'),
            content: const Text(
              'Use this when runtime keeps stale session ownership/lock. '
              'The app will send END_SESSION and re-attach a new local session context.',
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.of(context).pop(false),
                child: const Text('Cancel'),
              ),
              ElevatedButton(
                onPressed: () => Navigator.of(context).pop(true),
                child: const Text('Terminate'),
              ),
            ],
          ),
        ) ??
        false;

    if (!confirmed) {
      return;
    }

    await _runPrimaryAction(() async {
      final ended = await _sendEndSessionWithConfirmation(
        reasonCode: 'END_SESSION_OVERRIDE',
        extraPayload: const <String, dynamic>{
          'reason': 'OwnershipConflictRecovery',
          'reasonCode': 'END_SESSION_OVERRIDE',
        },
      );
      if (!ended || !mounted) {
        return;
      }

      final newSessionId = _buildLocalSessionId();
      setState(() {
        _activeSessionId = newSessionId;
        _sessionAttachReady = false;
        _requiresSessionDecision = false;
        _remoteSessionIdPendingDecision = null;
      });

      await _ensureSessionAttached(
        reasonCode: 'END_SESSION_OVERRIDE',
        force: true,
        sessionIdOverride: newSessionId,
      );

      if (!mounted) {
        return;
      }

      if (_sessionAttachReady) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text(
              'Session terminated and new context attached.',
            ),
            duration: Duration(seconds: 2),
          ),
        );
      } else {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text(
              'Session terminated. Waiting for attach sync...',
            ),
            backgroundColor: Colors.orange,
            duration: Duration(seconds: 2),
          ),
        );
      }
    });
  }

  String _formatCompactDateTime(DateTime? valueUtc) {
    if (valueUtc == null) {
      return '--';
    }

    final local = valueUtc.toLocal();
    final day = local.day.toString().padLeft(2, '0');
    final month = local.month.toString().padLeft(2, '0');
    final hour = local.hour.toString().padLeft(2, '0');
    final minute = local.minute.toString().padLeft(2, '0');
    return '$day.$month $hour:$minute';
  }

  Widget _buildParentMetricChip({
    required String label,
    required String value,
    required IconData icon,
  }) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 6),
      decoration: BoxDecoration(
        color: Colors.blueGrey.withValues(alpha: 0.08),
        borderRadius: BorderRadius.circular(10),
        border: Border.all(
          color: Colors.blueGrey.withValues(alpha: 0.2),
        ),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(
            icon,
            size: 14,
            color: Colors.blueGrey.shade800,
          ),
          const SizedBox(width: 6),
          Text(
            '$label: $value',
            style: const TextStyle(
              color: Colors.blueGrey,
              fontSize: 11,
              fontWeight: FontWeight.w700,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildParentQuickStartPanel() {
    final canStartNow = _isConnected &&
        _sessionAttachReady &&
        !_isHeadsetPresenceBlocking &&
        !_isPlanBlockingLaunch &&
        _isSelectedGameLaunchable &&
        !_isPrimaryActionInFlight;

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.green.shade50,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: Colors.green.shade200),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(Icons.family_restroom, color: Colors.green.shade800),
              const SizedBox(width: 8),
              Text(
                'Parent guided mode',
                style: TextStyle(
                  color: Colors.green.shade900,
                  fontWeight: FontWeight.w700,
                  fontSize: 13,
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            'Start one guided VR session and monitor live preview on mobile.',
            style: TextStyle(
              color: Colors.green.shade900,
              fontSize: 12,
            ),
          ),
          const SizedBox(height: 8),
          ElevatedButton.icon(
            onPressed: canStartNow
                ? () => unawaited(_startParentGuidedSession())
                : null,
            icon: const Icon(Icons.play_circle_fill),
            label: Text(
              canStartNow
                  ? 'Start guided session now'
                  : 'Waiting for connection/plan gate',
            ),
            style: ElevatedButton.styleFrom(
              backgroundColor: Colors.green.shade700,
              foregroundColor: Colors.white,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildParentProgressPanel() {
    final snapshot = _parentProgressSnapshot;
    final completionPercent = (snapshot.completionRate * 100).toStringAsFixed(
      0,
    );

    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.blueGrey.shade50,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: Colors.blueGrey.shade100),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Row(
            children: [
              Icon(Icons.insights_outlined, size: 18),
              SizedBox(width: 8),
              Text(
                'Parent progress',
                style: TextStyle(
                  fontWeight: FontWeight.w700,
                  fontSize: 13,
                ),
              ),
            ],
          ),
          if (_parentInsightsLoading) ...[
            const SizedBox(height: 8),
            const LinearProgressIndicator(minHeight: 2),
          ],
          const SizedBox(height: 8),
          if (!_isProgressInsightsAllowed)
            Text(
              'Current plan does not include progress insights.',
              style: TextStyle(
                color: Colors.orange.shade800,
                fontSize: 12,
                fontWeight: FontWeight.w600,
              ),
            )
          else if (!snapshot.hasData)
            Text(
              'No completed session history yet for this student.',
              style: TextStyle(
                color: Colors.grey.shade700,
                fontSize: 12,
              ),
            )
          else ...[
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                _buildParentMetricChip(
                  label: 'Sessions',
                  value: snapshot.totalSessions.toString(),
                  icon: Icons.history,
                ),
                _buildParentMetricChip(
                  label: 'Completed',
                  value: snapshot.terminalSessions.toString(),
                  icon: Icons.check_circle_outline,
                ),
                _buildParentMetricChip(
                  label: 'Completion',
                  value: '$completionPercent%',
                  icon: Icons.percent,
                ),
                _buildParentMetricChip(
                  label: 'Pending',
                  value: snapshot.unfinishedSessions.toString(),
                  icon: Icons.pending_actions_outlined,
                ),
              ],
            ),
            const SizedBox(height: 8),
            Text(
              'Last: ${snapshot.lastGameId.isEmpty ? '-' : snapshot.lastGameId} | '
              'state: ${snapshot.lastState.isEmpty ? '-' : snapshot.lastState} | '
              '${_formatCompactDateTime(snapshot.lastUpdatedAtUtc)}',
              style: TextStyle(
                color: Colors.grey.shade700,
                fontSize: 12,
                fontWeight: FontWeight.w600,
              ),
            ),
          ],
          const SizedBox(height: 10),
          Text(
            'Rewards',
            style: TextStyle(
              color: Colors.blueGrey.shade900,
              fontWeight: FontWeight.w700,
              fontSize: 12,
            ),
          ),
          const SizedBox(height: 4),
          if (!_isRewardsUnlocksAllowed)
            Text(
              'Current plan does not include VR reward unlocks.',
              style: TextStyle(
                color: Colors.orange.shade800,
                fontSize: 12,
                fontWeight: FontWeight.w600,
              ),
            )
          else if (_recentRewardUnlocks.isEmpty)
            Text(
              'No unlocked rewards yet.',
              style: TextStyle(
                color: Colors.grey.shade700,
                fontSize: 12,
              ),
            )
          else
            Column(
              children: _recentRewardUnlocks.map((reward) {
                return Padding(
                  padding: const EdgeInsets.only(bottom: 6),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Icon(
                        Icons.emoji_events_outlined,
                        size: 16,
                        color: Colors.amber.shade800,
                      ),
                      const SizedBox(width: 6),
                      Expanded(
                        child: Text(
                          '${reward.rewardTitle} (${reward.gameId}) | '
                          '${_formatCompactDateTime(reward.unlockedAtUtc)}',
                          style: TextStyle(
                            color: Colors.grey.shade800,
                            fontSize: 12,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ),
                    ],
                  ),
                );
              }).toList(growable: false),
            ),
        ],
      ),
    );
  }

  Widget _buildSessionControlPanel({
    required _GameCatalogEntry entry,
    required PurchasedContentState contentState,
  }) {
    final controlsReady =
        _isConnected && _sessionAttachReady && !_isHeadsetPresenceBlocking;
    final planLaunchBlocked = _isPlanBlockingLaunch;
    final canStart = controlsReady &&
        !_isPrimaryActionInFlight &&
        !planLaunchBlocked &&
        entry.runtimeLaunchEnabled &&
        _isLaunchableContentState(contentState) &&
        !_isGameRuntimeActive;
    final canRestart = controlsReady &&
        !_isPrimaryActionInFlight &&
        !planLaunchBlocked &&
        entry.runtimeLaunchEnabled &&
        _isLaunchableContentState(contentState) &&
        _isGameRuntimeActive;
    final canPause = controlsReady &&
        !_isPrimaryActionInFlight &&
        _isGameRuntimeActive &&
        !_isGameRuntimePaused;
    final canResume = controlsReady &&
        !_isPrimaryActionInFlight &&
        _isGameRuntimeActive &&
        _isGameRuntimePaused;
    final canEndGame =
        controlsReady && !_isPrimaryActionInFlight && _isGameRuntimeActive;

    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.grey[100],
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text(
            'Game controls',
            style: TextStyle(
              fontWeight: FontWeight.w700,
              fontSize: 13,
            ),
          ),
          const SizedBox(height: 8),
          Row(
            children: [
              Expanded(
                child: ElevatedButton.icon(
                  onPressed:
                      canStart ? () => unawaited(_startFromSetup()) : null,
                  icon: const Icon(Icons.play_arrow),
                  label: const Text('Start'),
                  style:
                      ElevatedButton.styleFrom(backgroundColor: Colors.green),
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: ElevatedButton.icon(
                  onPressed: canPause || canResume
                      ? () => unawaited(
                            _runPrimaryAction(() async {
                              await _sendCommand(
                                canResume
                                    ? CriticalCommandIds.resumeGame
                                    : CriticalCommandIds.pauseGame,
                              );
                            }),
                          )
                      : null,
                  icon: Icon(canResume ? Icons.play_circle : Icons.pause),
                  label: Text(canResume ? 'Resume' : 'Pause'),
                  style: ElevatedButton.styleFrom(
                    backgroundColor:
                        canResume ? Colors.teal : Colors.orange.shade700,
                  ),
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: ElevatedButton.icon(
                  onPressed:
                      canRestart ? () => unawaited(_restartFromSetup()) : null,
                  icon: const Icon(Icons.restart_alt),
                  label: const Text('Restart'),
                  style:
                      ElevatedButton.styleFrom(backgroundColor: Colors.indigo),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          ElevatedButton.icon(
            onPressed: canEndGame
                ? () => unawaited(
                      _runPrimaryAction(() async {
                        await _sendCommand(CriticalCommandIds.stopGame);
                      }),
                    )
                : null,
            icon: const Icon(Icons.stop_circle_outlined),
            label: const Text('End Game'),
            style: ElevatedButton.styleFrom(
              minimumSize: const Size.fromHeight(44),
              backgroundColor: Colors.red.shade700,
            ),
          ),
          if (_planGateBannerText != null) ...[
            const SizedBox(height: 8),
            Text(
              _planGateBannerText!,
              style: TextStyle(
                color: Colors.orange.shade900,
                fontWeight: FontWeight.w600,
                fontSize: 12,
              ),
            ),
          ],
          if (!entry.runtimeLaunchEnabled) ...[
            const SizedBox(height: 8),
            Text(
              'Runtime launch for this game is disabled in current build.',
              style: TextStyle(
                color: Colors.orange.shade900,
                fontWeight: FontWeight.w600,
                fontSize: 12,
              ),
            ),
          ],
        ],
      ),
    );
  }

  Widget _buildGameCatalogStep() {
    final catalog = _entitledGameCatalog;
    final installedCatalog = <_GameCatalogEntry>[];
    final storeCatalog = <_GameCatalogEntry>[];
    for (final entry in catalog) {
      final state = _contentStateForGame(entry.gameId);
      if (state.owned) {
        installedCatalog.add(entry);
      } else if (entry.availableForPurchase) {
        storeCatalog.add(entry);
      }
    }
    final visibleCatalog = _catalogFilterTab == _CatalogFilterTab.installed
        ? installedCatalog
        : storeCatalog;
    final selectedEntry = _selectedGameEntry;
    final planGateBannerText = _planGateBannerText;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _buildVideoPreviewPanel(
          subtitle: 'Optional live feed from the headset.',
        ),
        const SizedBox(height: 8),
        Row(
          children: [
            Expanded(
              child: Text(
                'Game catalog',
                style: TextStyle(
                  color: Colors.grey[900],
                  fontSize: 15,
                  fontWeight: FontWeight.w700,
                ),
              ),
            ),
            if (_contentDeliveryEnabled)
              TextButton.icon(
                onPressed: _isConnected && !_contentSyncInFlight
                    ? () => unawaited(_syncContentCatalog())
                    : null,
                icon: _contentSyncInFlight
                    ? const SizedBox(
                        width: 14,
                        height: 14,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.sync, size: 16),
                label: Text(_contentSyncInFlight ? 'Syncing...' : 'Refresh'),
              ),
          ],
        ),
        Text(
          _contentDeliveryEnabled
              ? 'Choose a game, ensure it is ready, then open the game session screen.'
              : 'Choose a game and open the game session screen.',
          style: TextStyle(
            color: Colors.grey[700],
            fontSize: 12,
          ),
        ),
        const SizedBox(height: 8),
        Container(
          padding: const EdgeInsets.all(4),
          decoration: BoxDecoration(
            color: Colors.grey.shade200,
            borderRadius: BorderRadius.circular(10),
          ),
          child: Row(
            children: [
              Expanded(
                child: FilledButton.tonal(
                  onPressed: _catalogFilterTab == _CatalogFilterTab.installed
                      ? null
                      : () {
                          setState(() {
                            _catalogFilterTab = _CatalogFilterTab.installed;
                          });
                        },
                  child: Text('Installed (${installedCatalog.length})'),
                ),
              ),
              const SizedBox(width: 6),
              Expanded(
                child: FilledButton.tonal(
                  onPressed: _catalogFilterTab == _CatalogFilterTab.store
                      ? null
                      : () {
                          setState(() {
                            _catalogFilterTab = _CatalogFilterTab.store;
                          });
                        },
                  child: Text('Store (${storeCatalog.length})'),
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 6),
        if (!_isConnected) ...[
          _buildStateBanner(
            icon: Icons.wifi_off,
            color: Colors.red.shade700,
            text: _contentDeliveryEnabled
                ? 'Headset is offline. Install/update actions will stay disabled until reconnect.'
                : 'Headset is offline. Reconnect to continue.',
          ),
          const SizedBox(height: 6),
        ] else if (_headsetPresenceBannerText != null) ...[
          _buildStateBanner(
            icon: Icons.warning_amber_rounded,
            color: Colors.orange.shade800,
            text: _headsetPresenceBannerText!,
          ),
          const SizedBox(height: 6),
        ] else if (!_sessionAttachReady) ...[
          _buildStateBanner(
            icon: Icons.sync,
            color: Colors.orange.shade800,
            text: _sessionAttachInFlight
                ? 'Synchronizing session context with headset...'
                : 'Session context not synced yet. Commands stay blocked until sync completes.',
          ),
          const SizedBox(height: 6),
        ],
        if (_hasDeferredHandoff) ...[
          _buildDeferredHandoffBanner(),
          const SizedBox(height: 6),
        ],
        if (planGateBannerText != null) ...[
          _buildStateBanner(
            icon: Icons.lock_outline,
            color: Colors.orange.shade800,
            text: planGateBannerText,
          ),
          const SizedBox(height: 6),
        ],
        if (_isSelectedGameLaunchable) ...[
          _buildStateBanner(
            icon: Icons.check_circle,
            color: Colors.green.shade700,
            text: 'Selected game `${selectedEntry.title}` is ready to open.',
          ),
          const SizedBox(height: 6),
        ] else if (!selectedEntry.runtimeLaunchEnabled) ...[
          _buildStateBanner(
            icon: Icons.hourglass_bottom,
            color: Colors.orange.shade800,
            text:
                'Selected game `${selectedEntry.title}` is catalog-only for now. Runtime launch is not enabled yet.',
          ),
          const SizedBox(height: 6),
        ] else if (_contentDeliveryEnabled) ...[
          _buildStateBanner(
            icon: Icons.warning_amber_rounded,
            color: Colors.orange.shade800,
            text:
                'Selected game `${selectedEntry.title}` needs install/update before opening.',
          ),
          const SizedBox(height: 6),
        ],
        if (_isParentRole) ...[
          _buildParentQuickStartPanel(),
          const SizedBox(height: 6),
          _buildParentProgressPanel(),
          const SizedBox(height: 6),
        ],
        Expanded(
          child: visibleCatalog.isEmpty
              ? Center(
                  child: Text(
                    _catalogFilterTab == _CatalogFilterTab.installed
                        ? 'No installed games yet for this account.'
                        : 'No store items available right now.',
                    style: TextStyle(color: Colors.grey[600]),
                  ),
                )
              : LayoutBuilder(
                  builder: (context, constraints) {
                    final maxWidth = constraints.maxWidth;
                    final crossAxisCount = maxWidth >= 980
                        ? 3
                        : maxWidth >= 620
                            ? 2
                            : 1;
                    return GridView.builder(
                      itemCount: visibleCatalog.length,
                      gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
                        crossAxisCount: crossAxisCount,
                        crossAxisSpacing: 8,
                        mainAxisSpacing: 8,
                        childAspectRatio: 0.88,
                      ),
                      itemBuilder: (context, index) {
                        final entry = visibleCatalog[index];
                        final selected = entry.gameId == _selectedGameId;
                        final contentState = _contentStateForGame(entry.gameId);
                        final actionInFlight =
                            _contentActionsInFlight.contains(entry.gameId);
                        final shouldInstallOrUpdate = contentState.owned &&
                            (contentState.runtimeStatus ==
                                    ContentRuntimeStatus.notInstalled ||
                                contentState.runtimeStatus ==
                                    ContentRuntimeStatus.updateRequired ||
                                contentState.runtimeStatus ==
                                    ContentRuntimeStatus.failed ||
                                contentState.updateRequired);
                        final primaryStoreActionEnabled =
                            !contentState.owned && entry.availableForPurchase;

                        return Card(
                          elevation: selected ? 2 : 0.5,
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(12),
                            side: BorderSide(
                              color: selected
                                  ? Colors.blue.shade300
                                  : Colors.grey.shade300,
                            ),
                          ),
                          child: InkWell(
                            borderRadius: BorderRadius.circular(12),
                            onTap: () {
                              setState(() {
                                _selectedGameId = entry.gameId;
                              });
                            },
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.stretch,
                              children: [
                                _buildCatalogArtwork(entry, selected: selected),
                                Padding(
                                  padding:
                                      const EdgeInsets.fromLTRB(10, 10, 10, 4),
                                  child: Text(
                                    entry.title,
                                    style: const TextStyle(
                                      fontWeight: FontWeight.w700,
                                    ),
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                  ),
                                ),
                                Padding(
                                  padding: const EdgeInsets.symmetric(
                                    horizontal: 10,
                                  ),
                                  child: Text(
                                    entry.description.isEmpty
                                        ? 'Description placeholder.'
                                        : entry.description,
                                    style: TextStyle(
                                      color: Colors.grey.shade700,
                                      fontSize: 12,
                                    ),
                                    maxLines: 2,
                                    overflow: TextOverflow.ellipsis,
                                  ),
                                ),
                                Padding(
                                  padding: const EdgeInsets.fromLTRB(
                                    10,
                                    6,
                                    10,
                                    0,
                                  ),
                                  child: Wrap(
                                    spacing: 6,
                                    runSpacing: 6,
                                    children: [
                                      _buildContentStatusChip(contentState),
                                      _buildVersionChip(contentState),
                                    ],
                                  ),
                                ),
                                if (entry.previewLines.isNotEmpty)
                                  Padding(
                                    padding: const EdgeInsets.fromLTRB(
                                      10,
                                      6,
                                      10,
                                      0,
                                    ),
                                    child: Text(
                                      entry.previewLines.first,
                                      style: TextStyle(
                                        color: Colors.grey.shade600,
                                        fontSize: 11,
                                      ),
                                      maxLines: 1,
                                      overflow: TextOverflow.ellipsis,
                                    ),
                                  ),
                                if (!entry.runtimeLaunchEnabled)
                                  Padding(
                                    padding: const EdgeInsets.fromLTRB(
                                      10,
                                      6,
                                      10,
                                      0,
                                    ),
                                    child: Text(
                                      'Catalog preview only (runtime launch pending).',
                                      style: TextStyle(
                                        color: Colors.orange.shade800,
                                        fontSize: 11,
                                        fontWeight: FontWeight.w600,
                                      ),
                                    ),
                                  ),
                                if (contentState.lastError != null &&
                                    contentState.lastError!.trim().isNotEmpty)
                                  Padding(
                                    padding: const EdgeInsets.fromLTRB(
                                      10,
                                      6,
                                      10,
                                      0,
                                    ),
                                    child: Text(
                                      'Last issue: ${contentState.lastError}',
                                      style: TextStyle(
                                        color: Colors.red.shade700,
                                        fontSize: 11,
                                      ),
                                      maxLines: 1,
                                      overflow: TextOverflow.ellipsis,
                                    ),
                                  ),
                                const Spacer(),
                                Padding(
                                  padding: const EdgeInsets.fromLTRB(
                                    10,
                                    8,
                                    10,
                                    10,
                                  ),
                                  child: _catalogFilterTab ==
                                          _CatalogFilterTab.store
                                      ? ElevatedButton.icon(
                                          onPressed: primaryStoreActionEnabled
                                              ? () => unawaited(
                                                    _simulateStorePurchase(
                                                      entry,
                                                    ),
                                                  )
                                              : null,
                                          icon: const Icon(
                                            Icons.shopping_cart_checkout,
                                          ),
                                          label: Text(
                                            contentState.owned
                                                ? 'Owned'
                                                : 'Buy (sim)',
                                          ),
                                        )
                                      : (_contentDeliveryEnabled
                                          ? Row(
                                              children: [
                                                Expanded(
                                                  child: ElevatedButton.icon(
                                                    onPressed: !_isConnected ||
                                                            actionInFlight ||
                                                            !shouldInstallOrUpdate
                                                        ? null
                                                        : () => unawaited(
                                                              _requestInstallOrUpdate(
                                                                contentState,
                                                              ),
                                                            ),
                                                    icon: const Icon(
                                                      Icons.download,
                                                    ),
                                                    label: Text(
                                                      contentState.runtimeStatus ==
                                                              ContentRuntimeStatus
                                                                  .updateRequired
                                                          ? 'Update'
                                                          : contentState
                                                                      .runtimeStatus ==
                                                                  ContentRuntimeStatus
                                                                      .failed
                                                              ? 'Retry'
                                                              : 'Install',
                                                    ),
                                                  ),
                                                ),
                                                if (contentState
                                                    .isInstalled) ...[
                                                  const SizedBox(width: 8),
                                                  Expanded(
                                                    child: OutlinedButton.icon(
                                                      onPressed:
                                                          !_isConnected ||
                                                                  actionInFlight
                                                              ? null
                                                              : () => unawaited(
                                                                    _requestUninstall(
                                                                      contentState,
                                                                    ),
                                                                  ),
                                                      icon: const Icon(
                                                        Icons.delete_outline,
                                                      ),
                                                      label:
                                                          const Text('Remove'),
                                                    ),
                                                  ),
                                                ],
                                              ],
                                            )
                                          : const SizedBox.shrink()),
                                ),
                              ],
                            ),
                          ),
                        );
                      },
                    );
                  },
                ),
        ),
        const SizedBox(height: 8),
        _buildMoreGamesHint(),
        const SizedBox(height: 8),
        if (_showCatalogRescueTerminateButton) ...[
          OutlinedButton.icon(
            onPressed: _isConnected && !_isPrimaryActionInFlight
                ? () => unawaited(_terminateActiveSessionFromCatalog())
                : null,
            icon: const Icon(Icons.power_settings_new),
            label: const Text('Terminate active session (rescue)'),
            style: OutlinedButton.styleFrom(
              foregroundColor: Colors.deepOrange.shade700,
              side: BorderSide(color: Colors.deepOrange.shade300),
              padding: const EdgeInsets.symmetric(vertical: 12),
            ),
          ),
          const SizedBox(height: 8),
        ],
        ElevatedButton.icon(
          onPressed: _isConnected &&
                  _sessionAttachReady &&
                  !_isHeadsetPresenceBlocking &&
                  !_isPlanBlockingLaunch &&
                  _isSelectedGameLaunchable
              ? () {
                  setState(() {
                    _workflowStep = _WorkflowStep.gameSetup;
                    _isVideoPreviewExpanded = true;
                  });
                }
              : null,
          icon: const Icon(Icons.videogame_asset),
          label: Text(
            !_sessionAttachReady
                ? 'Wait for session sync first'
                : _isHeadsetPresenceBlocking
                    ? 'Headset not in active VR app yet'
                    : _isPlanBlockingLaunch
                        ? 'Current plan blocks launching this session'
                        : !selectedEntry.runtimeLaunchEnabled
                            ? 'Selected game is catalog-only for now'
                            : _isSelectedGameLaunchable
                                ? 'Open game session'
                                : _contentDeliveryEnabled
                                    ? 'Install or update selected game first'
                                    : 'Select available game first',
          ),
          style: ElevatedButton.styleFrom(
            padding: const EdgeInsets.symmetric(vertical: 12),
          ),
        ),
      ],
    );
  }

  Widget _buildGameSetupStep() {
    final entry = _selectedGameEntry;
    final contentState = _selectedContentState;
    final setupLockedByRuntime = _isSetupLockedByRuntime;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _buildVideoPreviewPanel(
          subtitle: 'Live view while configuring and running this game.',
        ),
        const SizedBox(height: 8),
        Expanded(
          child: ListView(
            children: [
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: Colors.blueGrey[50],
                  borderRadius: BorderRadius.circular(8),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      entry.title,
                      style: const TextStyle(
                        fontSize: 15,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      entry.description,
                      style: TextStyle(color: Colors.grey[800]),
                    ),
                    const SizedBox(height: 6),
                    Wrap(
                      spacing: 6,
                      runSpacing: 6,
                      children: [
                        _buildContentStatusChip(contentState),
                        _buildVersionChip(contentState),
                      ],
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 8),
              if (!_isConnected) ...[
                _buildStateBanner(
                  icon: Icons.wifi_off,
                  color: Colors.red.shade700,
                  text:
                      'Headset is offline. Controls are disabled until reconnect.',
                ),
                const SizedBox(height: 8),
              ] else if (_headsetPresenceBannerText != null) ...[
                _buildStateBanner(
                  icon: Icons.warning_amber_rounded,
                  color: Colors.orange.shade800,
                  text: _headsetPresenceBannerText!,
                ),
                const SizedBox(height: 8),
              ] else if (!_sessionAttachReady) ...[
                _buildStateBanner(
                  icon: Icons.sync,
                  color: Colors.orange.shade800,
                  text: _sessionAttachInFlight
                      ? 'Synchronizing session context...'
                      : 'Session context is not synced yet. Controls remain locked.',
                ),
                const SizedBox(height: 8),
              ],
              if (_hasDeferredHandoff) ...[
                _buildDeferredHandoffBanner(),
                const SizedBox(height: 8),
              ],
              if (!_isLaunchableContentState(contentState)) ...[
                _buildStateBanner(
                  icon: Icons.warning_amber_rounded,
                  color: Colors.orange.shade800,
                  text:
                      'This game is not launch-ready. Return to catalog and run install/update.',
                ),
                const SizedBox(height: 8),
              ],
              if (setupLockedByRuntime) ...[
                _buildStateBanner(
                  icon: Icons.lock,
                  color: Colors.orange.shade800,
                  text:
                      'Settings are locked while a game is active. Use controls below.',
                ),
                const SizedBox(height: 8),
              ],
              if (entry.mobileControlSchema == null &&
                  entry.mobileControlSchemaReasonCode.trim().isNotEmpty) ...[
                _buildStateBanner(
                  icon: Icons.rule_folder_outlined,
                  color: Colors.orange.shade800,
                  text:
                      'Invalid mobile schema (${entry.mobileControlSchemaReasonCode}). Falling back to static setup.',
                ),
                const SizedBox(height: 8),
              ],
              if (entry.mobileControlSchema != null)
                _buildSchemaDrivenSettings(
                  entry.mobileControlSchema!,
                  lockedByRuntime: setupLockedByRuntime,
                )
              else ...[
                if (_isDemoCubeGameSelected)
                  _buildDemoCubeSettings(lockedByRuntime: setupLockedByRuntime),
                if (_isPulseTargetGameSelected)
                  _buildPulseTargetsSettings(
                      lockedByRuntime: setupLockedByRuntime),
                if (!_isDemoCubeGameSelected && !_isPulseTargetGameSelected)
                  _buildGenericGameSettings(
                    entry,
                    lockedByRuntime: setupLockedByRuntime,
                  ),
              ],
              const SizedBox(height: 8),
              _buildSessionControlPanel(
                entry: entry,
                contentState: contentState,
              ),
              const SizedBox(height: 8),
              _isParentRole
                  ? _buildParentProgressPanel()
                  : _buildTherapistTimelinePanel(),
            ],
          ),
        ),
        const SizedBox(height: 8),
        ElevatedButton.icon(
          onPressed: _isPrimaryActionInFlight || !_isConnected
              ? null
              : () => unawaited(_endSessionFromGameScreen()),
          icon: const Icon(Icons.flag),
          label: const Text('End Session'),
          style: ElevatedButton.styleFrom(
            padding: const EdgeInsets.symmetric(vertical: 12),
            backgroundColor: Colors.deepOrange.shade700,
            foregroundColor: Colors.white,
          ),
        ),
      ],
    );
  }

  Future<void> _simulateStorePurchase(_GameCatalogEntry entry) async {
    if (entry.gameId.trim().isEmpty) {
      return;
    }

    final nowUtc = DateTime.now().toUtc();
    final currentState = _contentStateForGame(entry.gameId);
    setState(() {
      _simulatedOwnedGameIds.add(entry.gameId);
      _catalogFilterTab = _CatalogFilterTab.installed;
      _selectedGameId = entry.gameId;
      _contentStatesByGameId[entry.gameId] = currentState.copyWith(
        owned: true,
        targetVersion: entry.targetContentVersion,
        installedVersion:
            entry.requiresExplicitLicense ? null : entry.targetContentVersion,
        runtimeStatus: entry.requiresExplicitLicense
            ? ContentRuntimeStatus.notInstalled
            : ContentRuntimeStatus.ready,
        updateRequired: false,
        lastError: null,
        updatedAtUtc: nowUtc,
      );
    });

    if (!mounted) {
      return;
    }

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text('Simulated purchase completed for ${entry.title}.'),
        duration: const Duration(seconds: 2),
      ),
    );
  }

  Widget _buildCatalogArtwork(
    _GameCatalogEntry entry, {
    required bool selected,
  }) {
    final borderColor = selected ? Colors.blue.shade300 : Colors.grey.shade300;
    final imageUrl = entry.thumbnailUrl.trim();

    return Container(
      decoration: BoxDecoration(
        borderRadius: const BorderRadius.vertical(top: Radius.circular(12)),
        border: Border(bottom: BorderSide(color: borderColor)),
      ),
      child: AspectRatio(
        aspectRatio: 16 / 9,
        child: ClipRRect(
          borderRadius: const BorderRadius.vertical(top: Radius.circular(12)),
          child: imageUrl.isEmpty
              ? Container(
                  decoration: BoxDecoration(
                    gradient: LinearGradient(
                      begin: Alignment.topLeft,
                      end: Alignment.bottomRight,
                      colors: <Color>[
                        Colors.blueGrey.shade200,
                        Colors.blueGrey.shade100,
                      ],
                    ),
                  ),
                  child: Center(
                    child: Icon(
                      Icons.videogame_asset,
                      size: 36,
                      color: Colors.blueGrey.shade700,
                    ),
                  ),
                )
              : Image.network(
                  imageUrl,
                  fit: BoxFit.cover,
                  errorBuilder: (_, __, ___) => Container(
                    color: Colors.blueGrey.shade100,
                    child: Center(
                      child: Icon(
                        Icons.broken_image_outlined,
                        size: 28,
                        color: Colors.blueGrey.shade700,
                      ),
                    ),
                  ),
                ),
        ),
      ),
    );
  }

  Widget _buildSchemaDrivenSettings(
    MobileControlSchema schema, {
    required bool lockedByRuntime,
  }) {
    _ensureDynamicControlValuesForSelectedSchema();

    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.grey[100],
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (lockedByRuntime)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: Text(
                'Runtime active: value controls are locked. Schema buttons remain available.',
                style: TextStyle(
                  color: Colors.orange[800],
                  fontSize: 12,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
          MobileControlRenderer(
            schema: schema,
            valuesByControlId: _dynamicControlValuesByControlId,
            locked: lockedByRuntime,
            onValueChanged: (change) {
              setState(() {
                _dynamicControlValuesByControlId[change.control.controlId] =
                    change.value;
              });
            },
            onButtonPressed: (control) {
              unawaited(_handleSchemaButtonControl(control));
            },
          ),
        ],
      ),
    );
  }

  Widget _buildDemoCubeSettings({required bool lockedByRuntime}) {
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.grey[100],
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Ustawienia gry',
            style: TextStyle(
              color: Colors.grey[800],
              fontSize: 12,
              fontWeight: FontWeight.w700,
            ),
          ),
          if (lockedByRuntime)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: Text(
                'Ustawienia zablokowane podczas aktywnej gry. Dostepne akcje: Pause, Stop, Restart, End Session.',
                style: TextStyle(
                  color: Colors.orange[800],
                  fontSize: 12,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
          const SizedBox(height: 8),
          Text(
            'Ilosc cubey: $_demoCubeCount',
            style: TextStyle(color: Colors.grey[700], fontSize: 12),
          ),
          Slider(
            value: _demoCubeCount.toDouble(),
            min: 4,
            max: 40,
            divisions: 36,
            label: _demoCubeCount.toString(),
            onChanged: lockedByRuntime
                ? null
                : (value) {
                    setState(() {
                      _demoCubeCount = value.round();
                    });
                  },
          ),
          Text(
            'Predkosc: ${_demoCubeSpeed.toStringAsFixed(2)}',
            style: TextStyle(color: Colors.grey[700], fontSize: 12),
          ),
          Slider(
            value: _demoCubeSpeed,
            min: 0.2,
            max: 2.2,
            divisions: 20,
            label: _demoCubeSpeed.toStringAsFixed(2),
            onChanged: lockedByRuntime
                ? null
                : (value) {
                    setState(() {
                      _demoCubeSpeed = value;
                    });
                  },
          ),
          DropdownButtonFormField<String>(
            initialValue: _demoLevelMode,
            decoration: InputDecoration(
              labelText: 'Poziom',
              border: OutlineInputBorder(
                borderRadius: BorderRadius.circular(6),
              ),
              contentPadding: const EdgeInsets.symmetric(
                horizontal: 8,
                vertical: 8,
              ),
            ),
            items: _demoLevelModes
                .map(
                  (mode) => DropdownMenuItem<String>(
                    value: mode,
                    child: Text(mode),
                  ),
                )
                .toList(),
            onChanged: lockedByRuntime
                ? null
                : (value) {
                    if (value == null) {
                      return;
                    }

                    setState(() {
                      _demoLevelMode = value;
                    });
                  },
          ),
        ],
      ),
    );
  }

  Widget _buildGenericGameSettings(
    _GameCatalogEntry entry, {
    required bool lockedByRuntime,
  }) {
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.grey[100],
        borderRadius: BorderRadius.circular(8),
      ),
      child: Text(
        lockedByRuntime
            ? 'Gra aktywna: zmiana konfiguracji jest chwilowo zablokowana. Zatrzymaj lub zakoncz sesje, aby edytowac ustawienia.'
            : 'Gra `${entry.title}` nie ma dedykowanego panelu ustawien po stronie mobilki. '
                'Uzywamy domyslnej konfiguracji runtime.',
        style: TextStyle(color: Colors.grey[700], fontSize: 12),
      ),
    );
  }

  Widget _buildPulseTargetsSettings({required bool lockedByRuntime}) {
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.grey[100],
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Ustawienia gry',
            style: TextStyle(
              color: Colors.grey[800],
              fontSize: 12,
              fontWeight: FontWeight.w700,
            ),
          ),
          if (lockedByRuntime)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Text(
                'Ustawienia zablokowane podczas aktywnej gry. Dostepne akcje: Pause, Stop, Restart, End Session.',
                style: TextStyle(
                  color: Colors.orange[800],
                  fontSize: 12,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
          const SizedBox(height: 8),
          Text(
            'Ilosc celow: $_pulseTargetCount',
            style: TextStyle(color: Colors.grey[700], fontSize: 12),
          ),
          Slider(
            value: _pulseTargetCount.toDouble(),
            min: 3,
            max: 32,
            divisions: 29,
            label: _pulseTargetCount.toString(),
            onChanged: lockedByRuntime
                ? null
                : (value) {
                    setState(() {
                      _pulseTargetCount = value.round();
                    });
                  },
          ),
          Text(
            'Predkosc celu: ${_pulseTargetSpeed.toStringAsFixed(2)}',
            style: TextStyle(color: Colors.grey[700], fontSize: 12),
          ),
          Slider(
            value: _pulseTargetSpeed,
            min: 0.2,
            max: 2.2,
            divisions: 20,
            label: _pulseTargetSpeed.toStringAsFixed(2),
            onChanged: lockedByRuntime
                ? null
                : (value) {
                    setState(() {
                      _pulseTargetSpeed = value;
                    });
                  },
          ),
          Text(
            'Skala celu: ${_pulseTargetScale.toStringAsFixed(2)}',
            style: TextStyle(color: Colors.grey[700], fontSize: 12),
          ),
          Slider(
            value: _pulseTargetScale,
            min: 0.12,
            max: 1.0,
            divisions: 22,
            label: _pulseTargetScale.toStringAsFixed(2),
            onChanged: lockedByRuntime
                ? null
                : (value) {
                    setState(() {
                      _pulseTargetScale = value;
                    });
                  },
          ),
        ],
      ),
    );
  }
}

class _GameCatalogEntry {
  final String gameId;
  final String title;
  final String description;
  final String targetContentVersion;
  final String packageUri;
  final String thumbnailUrl;
  final bool supportsSaveResume;
  final bool availableForPurchase;
  final bool requiresExplicitLicense;
  final bool runtimeLaunchEnabled;
  final int sortOrder;
  final List<String> previewLines;
  final MobileControlSchema? mobileControlSchema;
  final String mobileControlSchemaReasonCode;

  const _GameCatalogEntry({
    required this.gameId,
    required this.title,
    required this.description,
    required this.targetContentVersion,
    required this.packageUri,
    required this.thumbnailUrl,
    required this.supportsSaveResume,
    required this.availableForPurchase,
    required this.requiresExplicitLicense,
    required this.runtimeLaunchEnabled,
    required this.sortOrder,
    required this.previewLines,
    this.mobileControlSchema,
    this.mobileControlSchemaReasonCode = '',
  });

  factory _GameCatalogEntry.fromRemoteEntry(GameCatalogEntry entry) {
    return _GameCatalogEntry(
      gameId: entry.gameId,
      title: entry.title,
      description: entry.description,
      targetContentVersion: entry.targetContentVersion,
      packageUri: entry.packageUri,
      thumbnailUrl: entry.thumbnailUrl,
      supportsSaveResume: entry.supportsSaveResume,
      availableForPurchase: entry.availableForPurchase,
      requiresExplicitLicense: entry.requiresExplicitLicense,
      runtimeLaunchEnabled: entry.runtimeLaunchEnabled,
      sortOrder: entry.sortOrder,
      previewLines: List<String>.from(entry.previewLines),
      mobileControlSchema: entry.mobileControlSchema,
      mobileControlSchemaReasonCode: entry.mobileControlSchemaReasonCode,
    );
  }
}
