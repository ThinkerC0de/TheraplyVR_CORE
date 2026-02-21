import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_controller/models/content_delivery_contract.dart';
import 'package:flutter_controller/models/critical_command_envelope.dart';
import 'package:flutter_controller/models/device_info.dart';
import 'package:flutter_controller/models/runtime_status_signal.dart';
import 'package:flutter_controller/models/session_fsm_contract.dart';
import 'package:flutter_controller/models/session_recovery_policy.dart';
import 'package:flutter_controller/models/student.dart';
import 'package:flutter_controller/services/connection_service.dart';
import 'package:flutter_controller/services/discovery_service.dart';
import 'package:flutter_controller/services/firebase_service.dart';
import 'package:flutter_controller/services/foreground_service_bridge.dart';
import 'package:flutter_controller/services/session_journal_service.dart';
import 'package:flutter_controller/models/therapy_session_record.dart';
import 'package:flutter_controller/widgets/media_stream_widget.dart';
import 'package:wakelock_plus/wakelock_plus.dart';

enum _SessionGateAction { resume, startNew }

enum _ResumeStrategy { fromSavedState, fromBeginning }

enum _WorkflowStep { gameCatalog, gameSetup }

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
  static final bool _contentDeliveryEnabled = false;
  static final bool _serverAuthoritativeHandoffGate = true;
  static const List<_GameCatalogEntry> _gameCatalog = <_GameCatalogEntry>[
    _GameCatalogEntry(
      gameId: 'demo_cube_clicker',
      title: 'Demo Cube Clicker',
      description:
          'Wersja pogladowa: klikaj poruszajace sie cubey, mierz czas i best score.',
      targetContentVersion: '1.2.0',
      supportsSaveResume: true,
      previewLines: <String>[
        'Poziom basic: dowolny kolor.',
        'Poziom alternation: kolory na zmiane.',
        'Poziom random target: aktywny kolor celu zmienia sie dynamicznie.',
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
  final Set<String> _expandedPreviewGameIds = <String>{};

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

  SessionLifecycleState? _sessionLifecycleState;
  TherapistRuntimeStatus? _runtimeStatus;
  final Map<String, PurchasedContentState> _contentStatesByGameId =
      <String, PurchasedContentState>{};
  final Set<String> _contentActionsInFlight = <String>{};

  StreamSubscription<bool>? _connectionSubscription;
  StreamSubscription<Map<String, dynamic>>? _messageSubscription;
  StreamSubscription<DeviceInfo>? _discoverySubscription;
  Timer? _connectionLivenessTimer;

  late String _activeSessionId;
  late String _selectedGameId;
  String? _remoteSessionIdPendingDecision;
  String? _remoteActiveGameId;
  String? _lastSessionStateUpdateSessionId;
  String? _lastRuntimeStatusSessionId;
  TherapySessionRecord? _latestPersistedSession;
  bool _persistedSessionRefreshInFlight = false;
  DeviceInfo? _latestDiscoveryReconnectCandidate;
  DateTime? _latestDiscoveryReconnectSeenAt;
  DateTime? _connectedAtUtc;
  DateTime? _lastRuntimeSignalAtUtc;
  DevicePresenceUpdateSignal? _lastDevicePresenceSignal;
  int _lastWatchdogStaleAfterMs = 6000;
  bool _livenessRecoveryInFlight = false;
  String? _disconnectReasonOverride;

  _WorkflowStep _workflowStep = _WorkflowStep.gameCatalog;
  bool _resumeFromSavedPreference = false;

  int _demoCubeCount = 12;
  double _demoCubeSpeed = 0.7;
  String _demoLevelMode = _demoLevelModes.first;
  int _pulseTargetCount = 8;
  double _pulseTargetSpeed = 0.7;
  double _pulseTargetScale = 0.3;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);

    _connection.setDiscoveryService(widget.discoveryService);
    _activeSessionId = _buildLocalSessionId();
    _selectedGameId = _gameCatalog.first.gameId;
    _bootstrapLocalContentStates();

    _setupConnectionListeners();
    _setupDiscoveryListener();
    _startConnectionLivenessWatchdog();
    unawaited(ForegroundServiceBridge.start());
    unawaited(WakelockPlus.enable());
    unawaited(_refreshPersistedSessionSnapshot(triggerPrompt: true));
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
    _connectionSubscription?.cancel();
    _messageSubscription?.cancel();
    _discoverySubscription?.cancel();
    _connectionLivenessTimer?.cancel();
    unawaited(ForegroundServiceBridge.stop());
    unawaited(WakelockPlus.disable());
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
      final sessionUpdate = SessionStateUpdateSignal.tryFromNetworkMessage(
        message,
      );
      final runtimeUpdate = RuntimeStatusUpdateSignal.tryFromNetworkMessage(
        message,
      );
      final devicePresenceUpdate =
          DevicePresenceUpdateSignal.tryFromNetworkMessage(message);
      final watchdogHeartbeat =
          SessionWatchdogHeartbeatSignal.tryFromNetworkMessage(message);
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
        _lastRuntimeSignalAtUtc = DateTime.now().toUtc();
        if (watchdogHeartbeat != null && watchdogHeartbeat.staleAfterMs > 0) {
          _lastWatchdogStaleAfterMs = watchdogHeartbeat.staleAfterMs;
        }
        setState(() {
          if (sessionUpdate != null) {
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
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Failed to connect to device'),
          backgroundColor: Colors.red,
        ),
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
  }) async {
    if (!mounted || !_isConnected || _allowSystemPop) {
      return;
    }
    if (_sessionAttachInFlight) {
      return;
    }
    if (_sessionAttachReady && !force) {
      return;
    }

    final targetSessionId = _resolveAttachTargetSessionId().trim();
    if (targetSessionId.isEmpty) {
      return;
    }

    final therapistId = _resolveActorTherapistId();
    _sessionAttachInFlight = true;
    if (mounted) {
      setState(() {});
    }

    try {
      await _connection.sendCriticalCommand(
        commandId: CriticalCommandIds.sessionAttach,
        sessionId: targetSessionId,
        payload: <String, dynamic>{
          'sessionId': targetSessionId,
          'studentId': widget.student.id,
          'patientId': widget.student.id,
          'therapistId': therapistId,
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

      await _recordConnectionLifecycleEvent(
        eventType: 'SESSION_ATTACH_ACK',
        reasonCode: reasonCode,
        sessionIdOverride: targetSessionId,
      );
    } catch (e) {
      if (!mounted) {
        return;
      }

      setState(() {
        _sessionAttachReady = false;
      });
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Session attach failed ($reasonCode): $e'),
          backgroundColor: Colors.orange,
        ),
      );
    } finally {
      _sessionAttachInFlight = false;
      if (mounted) {
        setState(() {});
      }
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

  bool _isKnownGameId(String gameId) {
    final normalizedGameId = gameId.trim();
    if (normalizedGameId.isEmpty) {
      return false;
    }
    for (final entry in _gameCatalog) {
      if (entry.gameId == normalizedGameId) {
        return true;
      }
    }
    return false;
  }

  Map<String, dynamic> _buildSelectedGameConfigSnapshot() {
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
      return <String, dynamic>{
        'gameConfigType': 'pulse_targets_config_v1',
        'gameConfigVersion': 1,
        'targetCount': _pulseTargetCount,
        'targetSpeed': double.parse(_pulseTargetSpeed.toStringAsFixed(2)),
        'targetScale': double.parse(_pulseTargetScale.toStringAsFixed(2)),
      };
    }

    return <String, dynamic>{};
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
      );
      if (!mounted) {
        return;
      }

      setState(() {
        _latestPersistedSession = latest;
      });

      if (triggerPrompt) {
        _applyPersistedSessionDecisionGate();
      }
    } catch (e) {
      debugPrint(
        '[ControlScreen] Persisted session snapshot refresh failed: $e',
      );
    } finally {
      _persistedSessionRefreshInFlight = false;
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

    if (persisted.requiresHandoffDecision &&
        persistedSessionId != _activeSessionId &&
        !_wasSessionRecentlyEnded(persistedSessionId)) {
      if (_isKnownGameId(persisted.latestGameId) &&
          persisted.latestGameId != _selectedGameId) {
        setState(() {
          _selectedGameId = persisted.latestGameId;
        });
      }

      _remoteSessionIdPendingDecision = persistedSessionId;
      _requiresSessionDecision = true;
      if (_sessionAttachReady && mounted) {
        setState(() {
          _sessionAttachReady = false;
        });
      }
      _promptSessionDecisionIfNeeded();
      return true;
    }

    if (!persisted.requiresHandoffDecision &&
        _requiresSessionDecision &&
        _remoteSessionIdPendingDecision == persistedSessionId) {
      setState(() {
        _requiresSessionDecision = false;
        _remoteSessionIdPendingDecision = null;
      });
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

  Future<void> _persistRuntimeSessionState(
    SessionStateUpdateSignal sessionUpdate,
  ) async {
    final sessionId = sessionUpdate.sessionId.trim();
    if (sessionId.isEmpty) {
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
            'resumeFromSaved': _resumeFromSavedPreference,
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
            'resumeFromSaved': _resumeFromSavedPreference,
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

  _GameCatalogEntry get _selectedGameEntry {
    for (final entry in _gameCatalog) {
      if (entry.gameId == _selectedGameId) {
        return entry;
      }
    }

    return _gameCatalog.first;
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
    final normalized = reasonCode.trim();
    if (normalized.isEmpty) {
      return 'unknown reason';
    }

    switch (normalized) {
      case 'APP_PAUSED':
        return 'app paused';
      case 'APP_RESUMED':
        return 'app resumed';
      case 'APP_FOCUS_LOST':
        return 'focus lost';
      case 'APP_FOCUS_GAINED':
        return 'focus regained';
      case 'APP_QUIT':
        return 'app quit';
      case 'TCP_CLIENT_CONNECTED':
        return 'transport connected';
      default:
        return normalized;
    }
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
    return _isLaunchableContentState(_selectedContentState);
  }

  void _bootstrapLocalContentStates() {
    final nowUtc = DateTime.now().toUtc();
    for (final entry in _gameCatalog) {
      _contentStatesByGameId[entry.gameId] = PurchasedContentState(
        gameId: entry.gameId,
        owned: true,
        installedVersion: entry.targetContentVersion,
        targetVersion: entry.targetContentVersion,
        updateRequired: false,
        updateOptional: false,
        runtimeStatus: ContentRuntimeStatus.ready,
        lastError: null,
        updatedAtUtc: nowUtc,
      );
    }
  }

  PurchasedContentState _contentStateForGame(String gameId) {
    final existing = _contentStatesByGameId[gameId];
    if (existing != null) {
      return existing;
    }

    final fallbackEntry = _gameCatalog.where((entry) => entry.gameId == gameId);
    if (fallbackEntry.isNotEmpty) {
      return PurchasedContentState(
        gameId: gameId,
        owned: true,
        installedVersion: fallbackEntry.first.targetContentVersion,
        targetVersion: fallbackEntry.first.targetContentVersion,
        updateRequired: false,
        updateOptional: false,
        runtimeStatus: ContentRuntimeStatus.ready,
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
          actorId: widget.student.therapistId,
          role: 'THERAPIST',
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

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Catalog sync failed: $e'),
          backgroundColor: Colors.red,
        ),
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
          actorId: widget.student.therapistId,
          gameId: state.gameId,
          targetVersion: state.targetVersion,
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

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Install/update failed for ${state.gameId}: $e'),
          backgroundColor: Colors.red,
        ),
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
          actorId: widget.student.therapistId,
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

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Uninstall failed for ${state.gameId}: $e'),
          backgroundColor: Colors.red,
        ),
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
      _remoteSessionIdPendingDecision = remoteSessionId;
      _requiresSessionDecision = true;
      _promptSessionDecisionIfNeeded();
      return;
    }

    final shouldUpdateActiveSession =
        remoteState == SessionLifecycleState.created &&
            remoteSessionId != _activeSessionId;
    final shouldClearDecisionState = _requiresSessionDecision;

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
            'Gra zakonczona. Mozesz zmienic ustawienia i uruchomic nowa runde.',
          ),
          duration: Duration(seconds: 2),
        ),
      );
    } else {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            'Sesja zakonczona (${state.wireValue}${runtime == null ? '' : ', runtime=${runtime.wireValue}'}).',
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

    final action = await showDialog<_SessionGateAction>(
      context: context,
      barrierDismissible: false,
      builder: (context) {
        return AlertDialog(
          title: const Text('Session handoff needed'),
          content: const Text(
            'The headset reports an unfinished session.\n\n'
            'Continue that session or start a new one?',
          ),
          actions: [
            TextButton(
              onPressed: () =>
                  Navigator.of(context).pop(_SessionGateAction.resume),
              child: const Text('Continue'),
            ),
            ElevatedButton(
              onPressed: () =>
                  Navigator.of(context).pop(_SessionGateAction.startNew),
              child: const Text('Start new'),
            ),
          ],
        );
      },
    );

    if (!mounted) {
      _isSessionDecisionDialogOpen = false;
      return;
    }

    switch (action) {
      case _SessionGateAction.resume:
        await _handleResumeDecision(remoteSessionId);
        break;
      case _SessionGateAction.startNew:
        await _handleStartNewDecision(remoteSessionId);
        break;
      case null:
        break;
    }

    _isSessionDecisionDialogOpen = false;
    if (_requiresSessionDecision) {
      _promptSessionDecisionIfNeeded();
    }
  }

  Future<void> _handleResumeDecision(String remoteSessionId) async {
    final resolvedGameId = _resolveRemoteGameIdForResume();

    setState(() {
      _activeSessionId = remoteSessionId;
      _requiresSessionDecision = false;
      _remoteSessionIdPendingDecision = null;
      _sessionAttachReady = false;
      _workflowStep = _WorkflowStep.gameSetup;
      _isVideoPreviewExpanded = true;
      if (resolvedGameId != null) {
        _selectedGameId = resolvedGameId;
      }
    });
    await _ensureSessionAttached(
      reasonCode: 'HANDOFF_RESUME',
      force: true,
    );
    if (!mounted || !_sessionAttachReady) {
      return;
    }

    final strategy = await _showResumeStrategyDialog(_selectedGameEntry);
    if (!mounted || strategy == null) {
      return;
    }

    switch (strategy) {
      case _ResumeStrategy.fromSavedState:
        _resumeFromSavedPreference = true;
        await _sendCommand(
          CriticalCommandIds.resumeGame,
          extraPayload: const <String, dynamic>{
            'resumeFromSaved': true,
          },
        );
        break;
      case _ResumeStrategy.fromBeginning:
        _resumeFromSavedPreference = false;
        await _sendCommand(
          CriticalCommandIds.startGame,
          extraPayload: const <String, dynamic>{
            'resumeFromSaved': false,
          },
        );
        break;
    }
  }

  Future<_ResumeStrategy?> _showResumeStrategyDialog(
    _GameCatalogEntry entry,
  ) async {
    if (!entry.supportsSaveResume) {
      await showDialog<void>(
        context: context,
        builder: (context) {
          return AlertDialog(
            title: const Text('Kontynuacja gry'),
            content: Text(
              'Gra `${entry.title}` nie ma jeszcze pelnego saveGame. '
              'Uruchomimy od poczatku.',
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.of(context).pop(),
                child: const Text('OK'),
              ),
            ],
          );
        },
      );
      return _ResumeStrategy.fromBeginning;
    }

    return showDialog<_ResumeStrategy>(
      context: context,
      barrierDismissible: false,
      builder: (context) {
        return AlertDialog(
          title: const Text('Tryb wznowienia'),
          content: Text(
            'Dla gry `${entry.title}` wybierz sposob kontynuacji:\n\n'
            '1) Start od poczatku\n'
            '2) Wczytaj ostatni zapisany status',
          ),
          actions: [
            TextButton(
              onPressed: () =>
                  Navigator.of(context).pop(_ResumeStrategy.fromBeginning),
              child: const Text('Od poczatku'),
            ),
            ElevatedButton(
              onPressed: () =>
                  Navigator.of(context).pop(_ResumeStrategy.fromSavedState),
              child: const Text('Wznow zapis'),
            ),
          ],
        );
      },
    );
  }

  String? _resolveRemoteGameIdForResume() {
    final remoteGameId = _remoteActiveGameId?.trim() ?? '';
    if (remoteGameId.isEmpty) {
      return null;
    }

    for (final entry in _gameCatalog) {
      if (entry.gameId == remoteGameId) {
        return remoteGameId;
      }
    }

    return null;
  }

  Future<void> _handleStartNewDecision(String remoteSessionId) async {
    final confirmed = await showDialog<bool>(
          context: context,
          builder: (context) {
            return AlertDialog(
              title: const Text('Potwierdz nowa sesje'),
              content: const Text(
                'Wyslemy END_SESSION dla aktywnej sesji i utworzymy nowa. Kontynuowac?',
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.of(context).pop(false),
                  child: const Text('Anuluj'),
                ),
                ElevatedButton(
                  onPressed: () => Navigator.of(context).pop(true),
                  child: const Text('Tak'),
                ),
              ],
            );
          },
        ) ??
        false;

    if (!confirmed || !mounted) {
      return;
    }

    try {
      await _connection.sendCriticalCommand(
        commandId: CriticalCommandIds.endSession,
        sessionId: remoteSessionId,
        payload: _buildCriticalPayload(CriticalCommandIds.endSession),
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
        _sessionAttachReady = false;
        _workflowStep = _WorkflowStep.gameCatalog;
        _isVideoPreviewExpanded = false;
      });
      await _ensureSessionAttached(
        reasonCode: 'HANDOFF_START_NEW',
        force: true,
      );
      if (!mounted || !_sessionAttachReady) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Poprzednia sesja zakonczona. Mozesz uruchomic nowa.'),
        ),
      );
    } catch (e) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Nie udalo sie zakonczyc sesji: $e'),
          backgroundColor: Colors.red,
        ),
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
        !_sessionAttachReady) {
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
    }

    final commandSessionId = CriticalCommandIds.isCritical(command)
        ? _resolveSessionIdForCriticalCommand(command)
        : _activeSessionId;
    if (CriticalCommandIds.isCritical(command) &&
        commandSessionId.trim().isEmpty) {
      if (!mounted) {
        return false;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Cannot resolve active session id for this command.'),
          backgroundColor: Colors.red,
        ),
      );
      return false;
    }

    try {
      if (CriticalCommandIds.isCritical(command)) {
        await _connection.sendCriticalCommand(
          commandId: command,
          sessionId: commandSessionId,
          payload: _buildCriticalPayload(command, extraPayload: extraPayload),
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

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Failed: $command ($e)'),
          backgroundColor: Colors.red,
        ),
      );
      return false;
    }
  }

  Map<String, dynamic> _buildCriticalPayload(
    String command, {
    Map<String, dynamic>? extraPayload,
  }) {
    final payload = <String, dynamic>{
      'studentId': widget.student.id,
      'therapistId': _resolveActorTherapistId(),
    };

    if (_gameScopedCriticalCommands.contains(command)) {
      payload['gameId'] = _selectedGameId;
      payload['resumeFromSaved'] = _resumeFromSavedPreference;
    }

    if (command == CriticalCommandIds.startGame && _isDemoCubeGameSelected) {
      payload['gameConfigType'] = 'demo_cube_config_v1';
      payload['gameConfigVersion'] = 1;
      payload['gameConfigJson'] = jsonEncode(<String, dynamic>{
        'cubeCount': _demoCubeCount,
        'cubeSpeed': double.parse(_demoCubeSpeed.toStringAsFixed(2)),
        'levelMode': _demoLevelMode,
        'version': 1,
      });
    }

    if (command == CriticalCommandIds.startGame && _isPulseTargetGameSelected) {
      payload['gameConfigType'] = 'pulse_targets_config_v1';
      payload['gameConfigVersion'] = 1;
      payload['gameConfigJson'] = jsonEncode(<String, dynamic>{
        'targetCount': _pulseTargetCount,
        'targetSpeed': double.parse(_pulseTargetSpeed.toStringAsFixed(2)),
        'targetScale': double.parse(_pulseTargetScale.toStringAsFixed(2)),
        'version': 1,
      });
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

  Future<void> _startFromSetup({required bool resumeFromSaved}) async {
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
      _resumeFromSavedPreference = resumeFromSaved;
      await _sendCommand(
        resumeFromSaved
            ? CriticalCommandIds.resumeGame
            : CriticalCommandIds.startGame,
        extraPayload: <String, dynamic>{
          'resumeFromSaved': resumeFromSaved,
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
            'Restart jest dostepny tylko podczas aktywnej gry.',
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
      _resumeFromSavedPreference = false;
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
            builder: (context) {
              return AlertDialog(
                title: const Text('Wrocic do wyboru gry?'),
                content: const Text(
                  'To zatrzyma aktualna gre i przeniesie do katalogu mini-gier.',
                ),
                actions: [
                  TextButton(
                    onPressed: () => Navigator.of(context).pop(false),
                    child: const Text('Anuluj'),
                  ),
                  ElevatedButton(
                    onPressed: () => Navigator.of(context).pop(true),
                    child: const Text('Tak, wroc'),
                  ),
                ],
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

  Future<bool> _sendEndSessionWithConfirmation() async {
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
    final ended = await _sendCommand(
      CriticalCommandIds.endSession,
      showSuccessSnack: false,
    );
    if (!ended) {
      return false;
    }

    _markSessionAsRecentlyEnded(sessionIdToEnd);
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
      builder: (context) {
        return AlertDialog(
          title: const Text('Zamknac sterowanie?'),
          content: const Text(
            'Czy zakonczyc sesje teraz, czy zostawic ja jako niedokonczona?',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(context).pop(_ExitChoice.cancel),
              child: const Text('Anuluj'),
            ),
            TextButton(
              onPressed: () =>
                  Navigator.of(context).pop(_ExitChoice.keepUnfinished),
              child: const Text('Zostaw niedokonczona'),
            ),
            ElevatedButton(
              onPressed: () =>
                  Navigator.of(context).pop(_ExitChoice.endSession),
              child: const Text('Zakoncz sesje'),
            ),
          ],
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
                  'Only licensed games are shown here. Ask an admin to grant additional titles.',
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
            'This catalog only shows games available under the current license. '
            'Ask an admin operator to grant additional game access, then sync the catalog.',
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

  Widget _buildSessionControlPanel({
    required _GameCatalogEntry entry,
    required PurchasedContentState contentState,
  }) {
    final controlsReady =
        _isConnected && _sessionAttachReady && !_isHeadsetPresenceBlocking;
    final canStart = controlsReady &&
        !_isPrimaryActionInFlight &&
        _isLaunchableContentState(contentState) &&
        !_isGameRuntimeActive;
    final canRestart = controlsReady &&
        !_isPrimaryActionInFlight &&
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
    final canResumeFromSaved = controlsReady &&
        !_isPrimaryActionInFlight &&
        _isLaunchableContentState(contentState) &&
        !_isGameRuntimeActive &&
        entry.supportsSaveResume;

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
                  onPressed: canStart
                      ? () => unawaited(_startFromSetup(resumeFromSaved: false))
                      : null,
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
          if (entry.supportsSaveResume) ...[
            const SizedBox(height: 8),
            OutlinedButton.icon(
              onPressed: canResumeFromSaved
                  ? () => unawaited(_startFromSetup(resumeFromSaved: true))
                  : null,
              icon: const Icon(Icons.restore),
              label: const Text('Start from saved state'),
            ),
          ],
        ],
      ),
    );
  }

  Widget _buildGameCatalogStep() {
    final selectedEntry = _selectedGameEntry;

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
        if (_isSelectedGameLaunchable) ...[
          _buildStateBanner(
            icon: Icons.check_circle,
            color: Colors.green.shade700,
            text: 'Selected game `${selectedEntry.title}` is ready to open.',
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
        Expanded(
          child: _gameCatalog.isEmpty
              ? Center(
                  child: Text(
                    'No games available in catalog yet.',
                    style: TextStyle(color: Colors.grey[600]),
                  ),
                )
              : ListView.separated(
                  itemCount: _gameCatalog.length,
                  separatorBuilder: (_, __) => const SizedBox(height: 8),
                  itemBuilder: (context, index) {
                    final entry = _gameCatalog[index];
                    final selected = entry.gameId == _selectedGameId;
                    final expanded =
                        _expandedPreviewGameIds.contains(entry.gameId);
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

                    return Card(
                      elevation: selected ? 1.5 : 0,
                      shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(10),
                        side: BorderSide(
                          color: selected
                              ? Colors.blue.shade300
                              : Colors.grey.shade300,
                        ),
                      ),
                      child: Column(
                        children: [
                          ListTile(
                            onTap: () {
                              setState(() {
                                _selectedGameId = entry.gameId;
                              });
                            },
                            leading: Icon(
                              selected
                                  ? Icons.check_circle
                                  : Icons.radio_button_unchecked,
                              color: selected ? Colors.blue : Colors.grey,
                            ),
                            title: Text(
                              entry.title,
                              style:
                                  const TextStyle(fontWeight: FontWeight.w700),
                            ),
                            subtitle: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(entry.description),
                                const SizedBox(height: 4),
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
                            trailing: IconButton(
                              icon: Icon(
                                expanded
                                    ? Icons.expand_less
                                    : Icons.expand_more,
                              ),
                              onPressed: () {
                                setState(() {
                                  if (expanded) {
                                    _expandedPreviewGameIds
                                        .remove(entry.gameId);
                                  } else {
                                    _expandedPreviewGameIds.add(entry.gameId);
                                  }
                                });
                              },
                            ),
                          ),
                          if (expanded)
                            Container(
                              width: double.infinity,
                              padding: const EdgeInsets.fromLTRB(16, 0, 16, 12),
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(
                                    'Preview',
                                    style: TextStyle(
                                      color: Colors.grey[800],
                                      fontSize: 12,
                                      fontWeight: FontWeight.w700,
                                    ),
                                  ),
                                  const SizedBox(height: 4),
                                  for (final line in entry.previewLines)
                                    Padding(
                                      padding: const EdgeInsets.only(bottom: 2),
                                      child: Text(
                                        '- $line',
                                        style: TextStyle(
                                          color: Colors.grey[700],
                                          fontSize: 12,
                                        ),
                                      ),
                                    ),
                                  if (contentState.lastError != null &&
                                      contentState.lastError!.trim().isNotEmpty)
                                    Padding(
                                      padding: const EdgeInsets.only(top: 6),
                                      child: Text(
                                        'Last issue: ${contentState.lastError}',
                                        style: TextStyle(
                                          color: Colors.red[700],
                                          fontSize: 12,
                                        ),
                                      ),
                                    ),
                                ],
                              ),
                            ),
                          Padding(
                            padding: const EdgeInsets.fromLTRB(12, 0, 12, 12),
                            child: _contentDeliveryEnabled
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
                                                        contentState),
                                                  ),
                                          icon: const Icon(Icons.download),
                                          label: Text(
                                            contentState.runtimeStatus ==
                                                    ContentRuntimeStatus
                                                        .updateRequired
                                                ? 'Update'
                                                : contentState.runtimeStatus ==
                                                        ContentRuntimeStatus
                                                            .failed
                                                    ? 'Retry install'
                                                    : 'Install',
                                          ),
                                        ),
                                      ),
                                      if (contentState.isInstalled) ...[
                                        const SizedBox(width: 8),
                                        Expanded(
                                          child: OutlinedButton.icon(
                                            onPressed:
                                                !_isConnected || actionInFlight
                                                    ? null
                                                    : () => unawaited(
                                                          _requestUninstall(
                                                              contentState),
                                                        ),
                                            icon: const Icon(
                                                Icons.delete_outline),
                                            label: const Text('Uninstall'),
                                          ),
                                        ),
                                      ],
                                    ],
                                  )
                                : const SizedBox.shrink(),
                          ),
                        ],
                      ),
                    );
                  },
                ),
        ),
        const SizedBox(height: 8),
        _buildMoreGamesHint(),
        const SizedBox(height: 8),
        ElevatedButton.icon(
          onPressed: _isConnected &&
                  _sessionAttachReady &&
                  !_isHeadsetPresenceBlocking &&
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
              const SizedBox(height: 8),
              _buildSessionControlPanel(
                entry: entry,
                contentState: contentState,
              ),
            ],
          ),
        ),
        const SizedBox(height: 8),
        ElevatedButton.icon(
          onPressed:
              _isPrimaryActionInFlight || !_isConnected || !_sessionAttachReady
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
  final bool supportsSaveResume;
  final List<String> previewLines;

  const _GameCatalogEntry({
    required this.gameId,
    required this.title,
    required this.description,
    required this.targetContentVersion,
    required this.supportsSaveResume,
    required this.previewLines,
  });
}
