import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_controller/models/content_delivery_contract.dart';
import 'package:flutter_controller/models/critical_command_envelope.dart';
import 'package:flutter_controller/models/device_info.dart';
import 'package:flutter_controller/models/manual_resync_report_signal.dart';
import 'package:flutter_controller/models/runtime_status_signal.dart';
import 'package:flutter_controller/models/session_fsm_contract.dart';
import 'package:flutter_controller/models/session_recovery_policy.dart';
import 'package:flutter_controller/models/student.dart';
import 'package:flutter_controller/services/connection_service.dart';
import 'package:flutter_controller/services/discovery_service.dart';
import 'package:flutter_controller/services/foreground_service_bridge.dart';
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
  static const List<_GameCatalogEntry> _gameCatalog = <_GameCatalogEntry>[
    _GameCatalogEntry(
      gameId: 'smoke_test_game',
      title: 'Smoke Test Game',
      description:
          'Szybki test komend START/PAUSE/RESUME/STOP i stabilnosci polaczenia.',
      targetContentVersion: '1.0.0',
      supportsSaveResume: false,
      previewLines: <String>[
        'Minimalna gra diagnostyczna.',
        'Dobra do sprawdzenia mobile -> Unity Editor.',
        'Wynik konczy sie po STOP lub auto-complete.',
      ],
    ),
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
    _GameCatalogEntry(
      gameId: 'pulse_target_tap',
      title: 'Pulse Target Tap',
      description: 'Prosta gra pokazowa: klikaj kolejne poruszajace sie cele.',
      targetContentVersion: '1.0.0',
      supportsSaveResume: false,
      previewLines: <String>[
        'Kolejne cele pojawiaja sie pojedynczo.',
        'Kazdy cel porusza sie po ekranie.',
        'Dobra do pokazu flow i telemetry per hit.',
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
  static const List<String> _demoLevelModes = <String>[
    'basic',
    'alternate_colors',
    'random_target_color',
  ];

  final ConnectionService _connection = ConnectionService();
  final Set<String> _expandedPreviewGameIds = <String>{};

  bool _isConnected = false;
  String _statusMessage = 'Connecting...';
  bool _isReconnecting = false;
  bool _requiresSessionDecision = false;
  bool _isSessionDecisionDialogOpen = false;
  bool _manualResyncInFlight = false;
  bool _isPrimaryActionInFlight = false;
  bool _allowSystemPop = false;
  bool _contentSyncInFlight = false;

  SessionLifecycleState? _sessionLifecycleState;
  TherapistRuntimeStatus? _runtimeStatus;
  SessionWatchdogHeartbeatSignal? _lastWatchdogHeartbeat;
  ManualResyncReportSignal? _lastManualResyncReport;
  final Map<String, PurchasedContentState> _contentStatesByGameId =
      <String, PurchasedContentState>{};
  final Set<String> _contentActionsInFlight = <String>{};

  StreamSubscription<bool>? _connectionSubscription;
  StreamSubscription<Map<String, dynamic>>? _messageSubscription;
  Timer? _heartbeatUiTimer;

  late String _activeSessionId;
  late String _selectedGameId;
  String? _remoteSessionIdPendingDecision;
  String? _remoteActiveGameId;

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
    _startHeartbeatUiTicker();
    unawaited(ForegroundServiceBridge.start());
    unawaited(WakelockPlus.enable());
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
    _connectionSubscription?.cancel();
    _messageSubscription?.cancel();
    _heartbeatUiTimer?.cancel();
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

      setState(() {
        _isConnected = connected;
        _statusMessage = connected ? 'Connected' : 'Disconnected';
      });

      if (connected) {
        unawaited(_syncContentCatalog(silent: true));
      }
    });

    _messageSubscription = _connection.messages.listen((message) {
      final sessionUpdate = SessionStateUpdateSignal.tryFromNetworkMessage(
        message,
      );
      final runtimeUpdate = RuntimeStatusUpdateSignal.tryFromNetworkMessage(
        message,
      );
      final watchdogHeartbeat =
          SessionWatchdogHeartbeatSignal.tryFromNetworkMessage(message);
      final manualResyncReport = ManualResyncReportSignal.tryFromNetworkMessage(
        message,
      );
      final contentStatusSignal =
          ContentInstallStatusSignal.tryFromNetworkMessage(message);

      if (mounted &&
          (sessionUpdate != null ||
              runtimeUpdate != null ||
              watchdogHeartbeat != null)) {
        setState(() {
          if (sessionUpdate != null) {
            _sessionLifecycleState = sessionUpdate.state;
          }

          if (runtimeUpdate != null) {
            _runtimeStatus = runtimeUpdate.status;
          }

          if (watchdogHeartbeat != null) {
            _lastWatchdogHeartbeat = watchdogHeartbeat;
            final activeGameId = watchdogHeartbeat.activeGameId.trim();
            if (activeGameId.isNotEmpty) {
              _remoteActiveGameId = activeGameId;
            }
          }
        });

        _handlePotentialSessionDecisionGate(sessionUpdate, runtimeUpdate);
      }

      if (mounted && manualResyncReport != null) {
        setState(() {
          _manualResyncInFlight = false;
          _lastManualResyncReport = manualResyncReport;
        });
        unawaited(_showManualResyncReportDialog(manualResyncReport));
      }

      if (mounted && contentStatusSignal != null) {
        _applyContentInstallStatusSignal(contentStatusSignal);
      }
    });
  }

  void _startHeartbeatUiTicker() {
    _heartbeatUiTimer?.cancel();
    _heartbeatUiTimer = Timer.periodic(const Duration(seconds: 1), (_) {
      if (!mounted || _lastWatchdogHeartbeat == null) {
        return;
      }

      setState(() {});
    });
  }

  Future<void> _connect() async {
    final success = await _connection.connect(
      widget.device.ip,
      widget.device.controlPort,
    );

    if (!success && mounted) {
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
    if (!mounted || _isReconnecting || _connection.isConnected) {
      return;
    }

    _isReconnecting = true;
    setState(() {
      _statusMessage = 'Reconnecting...';
    });

    final ok = await _connection.reconnect();
    if (!mounted) {
      return;
    }

    setState(() {
      _isConnected = ok;
      _statusMessage = ok ? 'Connected' : 'Disconnected';
    });

    if (!ok) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Connection lost. Reconnect failed.'),
          backgroundColor: Colors.red,
        ),
      );
    }

    _isReconnecting = false;
  }

  String _buildLocalSessionId() {
    return 'mobile-${widget.student.id}-${DateTime.now().toUtc().millisecondsSinceEpoch}';
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
    return _runtimeStatus == TherapistRuntimeStatus.playing ||
        _runtimeStatus == TherapistRuntimeStatus.paused ||
        _sessionLifecycleState == SessionLifecycleState.inProgress ||
        _sessionLifecycleState == SessionLifecycleState.paused;
  }

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
  ) {
    final remoteSessionId =
        _resolveRemoteSessionId(sessionUpdate, runtimeUpdate);
    if (remoteSessionId == null || remoteSessionId.isEmpty) {
      return;
    }

    final remoteState = sessionUpdate?.state ?? _sessionLifecycleState;
    final remoteRuntime = runtimeUpdate?.status ?? _runtimeStatus;
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

    if (!_requiresSessionDecision && remoteSessionId != _activeSessionId) {
      setState(() {
        _activeSessionId = remoteSessionId;
      });
    }
  }

  String? _resolveRemoteSessionId(
    SessionStateUpdateSignal? sessionUpdate,
    RuntimeStatusUpdateSignal? runtimeUpdate,
  ) {
    final fromSessionState = sessionUpdate?.sessionId ?? '';
    if (fromSessionState.isNotEmpty) {
      return fromSessionState;
    }

    final fromRuntimeStatus = runtimeUpdate?.sessionId ?? '';
    if (fromRuntimeStatus.isNotEmpty) {
      return fromRuntimeStatus;
    }

    return null;
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
    if (remoteSessionId == null || remoteSessionId.isEmpty) {
      _isSessionDecisionDialogOpen = false;
      return;
    }

    final action = await showDialog<_SessionGateAction>(
      context: context,
      barrierDismissible: false,
      builder: (context) {
        return AlertDialog(
          title: const Text('Aktywna niedokonczona sesja'),
          content: Text(
            'Urzadzenie raportuje aktywna sesje `$remoteSessionId`.\n\n'
            'Kontynuowac obecna sesje czy zakonczyc i rozpoczac nowa?',
          ),
          actions: [
            TextButton(
              onPressed: () =>
                  Navigator.of(context).pop(_SessionGateAction.resume),
              child: const Text('Kontynuuj'),
            ),
            ElevatedButton(
              onPressed: () =>
                  Navigator.of(context).pop(_SessionGateAction.startNew),
              child: const Text('Nowa sesja'),
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
      _workflowStep = _WorkflowStep.gameSetup;
      if (resolvedGameId != null) {
        _selectedGameId = resolvedGameId;
      }
    });

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

      if (!mounted) {
        return;
      }

      setState(() {
        _activeSessionId = _buildLocalSessionId();
        _requiresSessionDecision = false;
        _workflowStep = _WorkflowStep.gameCatalog;
      });

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

  Future<void> _triggerManualResync({bool includeSyncedEvents = false}) async {
    if (!_isConnected || _manualResyncInFlight) {
      return;
    }

    final correlationId = CriticalCommandEnvelope.newMessageId();
    setState(() {
      _manualResyncInFlight = true;
    });

    try {
      await _connection.sendCommand(
        ManualResyncSignalCommandIds.manualResync,
        <String, dynamic>{
          'correlationId': correlationId,
          'sessionId': _activeSessionId,
          'includeSyncedEvents': includeSyncedEvents,
          'requestedBy': widget.student.therapistId,
          'reasonCode': 'SUPPORT_MANUAL_RESYNC',
        },
        messageId: correlationId,
      );

      if (!mounted) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content:
              Text('Manual re-sync requested. Waiting for Quest report...'),
          duration: Duration(seconds: 2),
        ),
      );
    } catch (e) {
      if (!mounted) {
        return;
      }

      setState(() {
        _manualResyncInFlight = false;
      });
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Manual re-sync failed to send: $e'),
          backgroundColor: Colors.red,
        ),
      );
    }
  }

  Future<void> _showManualResyncReportDialog(
    ManualResyncReportSignal report,
  ) async {
    if (!mounted) {
      return;
    }

    await showDialog<void>(
      context: context,
      builder: (context) {
        return AlertDialog(
          title: const Text('Manual Re-sync Report'),
          content: SingleChildScrollView(
            child: Text(
              'Session: ${report.sessionId}\n'
              'Success: ${report.success}\n'
              'Reason: ${report.reasonCode}\n'
              'Targeted events: ${report.targetedEvents}\n'
              'Outbox rows updated: ${report.outboxRowsUpdated}\n'
              'Upload cycle triggered: ${report.uploadCycleTriggered}\n'
              'Missing on server (before/after): '
              '${report.beforeMissingOnServerCount}/${report.afterMissingOnServerCount}\n'
              'Missing on device (before/after): '
              '${report.beforeMissingOnDeviceCount}/${report.afterMissingOnDeviceCount}\n'
              'Outbox pending (before/after): '
              '${report.beforeOutboxPending}/${report.afterOutboxPending}\n'
              'Sequence preview: ${report.targetedSequencePreview}\n'
              'Details: ${report.details}',
            ),
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

  Future<void> _sendCommand(
    String command, {
    Map<String, dynamic>? extraPayload,
    bool showSuccessSnack = true,
  }) async {
    if (_gameScopedCriticalCommands.contains(command) &&
        _selectedGameId.trim().isEmpty) {
      if (!mounted) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Select a game before sending this command.'),
          backgroundColor: Colors.orange,
        ),
      );
      return;
    }

    if (_requiresSessionDecision && command != CriticalCommandIds.endSession) {
      if (!mounted) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Choose Resume or Start New before sending commands.'),
          backgroundColor: Colors.orange,
        ),
      );
      _promptSessionDecisionIfNeeded();
      return;
    }

    try {
      if (CriticalCommandIds.isCritical(command)) {
        await _connection.sendCriticalCommand(
          commandId: command,
          sessionId: _activeSessionId,
          payload: _buildCriticalPayload(command, extraPayload: extraPayload),
          expiresAtUtc: DateTime.now().toUtc().add(const Duration(seconds: 30)),
        );
      } else {
        await _connection.sendCommand(command, null);
      }

      if (!mounted || !showSuccessSnack) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Sent: $command'),
          duration: const Duration(seconds: 1),
        ),
      );
    } catch (e) {
      if (!mounted) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Failed: $command ($e)'),
          backgroundColor: Colors.red,
        ),
      );
    }
  }

  Map<String, dynamic> _buildCriticalPayload(
    String command, {
    Map<String, dynamic>? extraPayload,
  }) {
    final payload = <String, dynamic>{
      'studentId': widget.student.id,
      'therapistId': widget.student.therapistId,
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
      payload['reason'] = 'TherapistStop';
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
    await _runPrimaryAction(() async {
      _resumeFromSavedPreference = false;
      await _sendCommand(
        CriticalCommandIds.stopGame,
        showSuccessSnack: false,
      );
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
      });
    });
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
      await _sendCommand(
        CriticalCommandIds.endSession,
        showSuccessSnack: false,
      );
    }

    await _disconnectAndPop();
    return false;
  }

  Future<void> _disconnectAndPop() async {
    unawaited(ForegroundServiceBridge.stop());
    await _connection.disconnect();

    if (!mounted) {
      return;
    }

    _allowSystemPop = true;
    Navigator.pop(context);
  }

  bool _isWatchdogHeartbeatStale(SessionWatchdogHeartbeatSignal heartbeat) {
    return heartbeat.isStale(DateTime.now().toUtc());
  }

  String _formatHeartbeatAge(DateTime heartbeatAtUtc) {
    final age = DateTime.now().toUtc().difference(heartbeatAtUtc.toUtc());
    final seconds = age.inSeconds;
    if (seconds < 0) {
      return '0s';
    }

    if (seconds < 60) {
      return '${seconds}s';
    }

    final minutes = seconds ~/ 60;
    final remSec = seconds % 60;
    return '${minutes}m ${remSec}s';
  }

  @override
  Widget build(BuildContext context) {
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
          title:
              Text('${widget.student.firstName} - ${widget.device.deviceName}'),
          actions: [
            Padding(
              padding: const EdgeInsets.only(right: 16),
              child: Center(
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(
                      _isConnected ? Icons.wifi : Icons.wifi_off,
                      color: _isConnected ? Colors.green : Colors.red,
                      size: 20,
                    ),
                    const SizedBox(width: 8),
                    Text(
                      _statusMessage,
                      style: TextStyle(
                        color: _isConnected ? Colors.green : Colors.red,
                        fontSize: 14,
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
        body: Column(
          children: [
            Expanded(
              flex: 5,
              child: Padding(
                padding: const EdgeInsets.fromLTRB(12, 8, 12, 4),
                child: Align(
                  alignment: Alignment.topCenter,
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
            ),
            Expanded(
              flex: 6,
              child: Padding(
                padding: const EdgeInsets.fromLTRB(12, 4, 12, 8),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    _buildSessionHeaderCard(),
                    const SizedBox(height: 8),
                    _buildFlowStepIndicator(),
                    const SizedBox(height: 8),
                    Expanded(
                      child: _workflowStep == _WorkflowStep.gameCatalog
                          ? _buildGameCatalogStep()
                          : _buildGameSetupStep(),
                    ),
                    const SizedBox(height: 8),
                    OutlinedButton.icon(
                      onPressed: _isPrimaryActionInFlight
                          ? null
                          : () => unawaited(_disconnectAndPop()),
                      icon: const Icon(Icons.arrow_back, size: 18),
                      label: const Text('Wroc do wyboru ucznia'),
                      style: OutlinedButton.styleFrom(
                        padding: const EdgeInsets.symmetric(vertical: 12),
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildSessionHeaderCard() {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
      decoration: BoxDecoration(
        color: Colors.grey[100],
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(Icons.headset_mic, size: 20, color: Colors.blue),
              const SizedBox(width: 8),
              Text(
                '${widget.device.ip}:${widget.device.controlPort}',
                style: TextStyle(color: Colors.grey[700], fontSize: 12),
              ),
              const Spacer(),
              Text(
                'Video: ${widget.device.videoPort}',
                style: TextStyle(color: Colors.grey[600], fontSize: 12),
              ),
            ],
          ),
          const SizedBox(height: 4),
          Text(
            'Session: ${_sessionLifecycleState?.wireValue ?? 'UNKNOWN'}',
            style: TextStyle(color: Colors.grey[700], fontSize: 12),
          ),
          Text(
            'Runtime: ${_runtimeStatus?.wireValue ?? 'unknown'}',
            style: TextStyle(color: Colors.grey[700], fontSize: 12),
          ),
          Text(
            'Active Session ID: $_activeSessionId',
            style: TextStyle(color: Colors.grey[700], fontSize: 12),
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
          ),
          if (_remoteActiveGameId != null && _remoteActiveGameId!.isNotEmpty)
            Text(
              'Remote active game: $_remoteActiveGameId',
              style: TextStyle(color: Colors.grey[700], fontSize: 12),
            ),
          if (_lastWatchdogHeartbeat != null)
            Builder(
              builder: (context) {
                final heartbeat = _lastWatchdogHeartbeat!;
                final stale = _isWatchdogHeartbeatStale(heartbeat);
                final healthy = heartbeat.healthy && !stale;
                final color = healthy ? Colors.green[700] : Colors.red[700];
                final label = healthy
                    ? 'healthy'
                    : stale
                        ? 'stale'
                        : 'alert';
                return Text(
                  'Watchdog: $label (${_formatHeartbeatAge(heartbeat.heartbeatAtUtc)} ago, ${heartbeat.healthCode})',
                  style: TextStyle(color: color, fontSize: 12),
                );
              },
            ),
          if (_requiresSessionDecision)
            const Text(
              'Action required: Continue or start a new session first.',
              style: TextStyle(
                color: Colors.deepOrange,
                fontSize: 12,
                fontWeight: FontWeight.w600,
              ),
            ),
          if (_manualResyncInFlight)
            const Text(
              'Support re-sync in progress...',
              style: TextStyle(
                color: Colors.teal,
                fontSize: 12,
                fontWeight: FontWeight.w600,
              ),
            ),
        ],
      ),
    );
  }

  Widget _buildFlowStepIndicator() {
    return Row(
      children: [
        Expanded(
          child: _buildStepChip(
            title: '4) Wybor mini-gry',
            active: _workflowStep == _WorkflowStep.gameCatalog,
          ),
        ),
        const SizedBox(width: 8),
        Expanded(
          child: _buildStepChip(
            title: '5) Ustawienia i start',
            active: _workflowStep == _WorkflowStep.gameSetup,
          ),
        ),
      ],
    );
  }

  Widget _buildStepChip({required String title, required bool active}) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
      decoration: BoxDecoration(
        color: active ? Colors.blue[50] : Colors.grey[100],
        borderRadius: BorderRadius.circular(8),
        border: Border.all(
          color: active ? Colors.blue.shade300 : Colors.grey.shade300,
        ),
      ),
      child: Text(
        title,
        style: TextStyle(
          fontSize: 12,
          color: active ? Colors.blue[800] : Colors.grey[700],
          fontWeight: active ? FontWeight.w700 : FontWeight.w500,
        ),
        textAlign: TextAlign.center,
      ),
    );
  }

  Widget _buildContentStatusChip(PurchasedContentState state) {
    final label = state.runtimeStatus.wireValue;
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

  Widget _buildOwnershipChip(PurchasedContentState state) {
    final color = state.owned ? Colors.green : Colors.red;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(999),
        border: Border.all(color: color.withValues(alpha: 0.35)),
      ),
      child: Text(
        state.owned ? 'OWNED' : 'NOT_OWNED',
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
    final targetVersion = state.targetVersion.trim().isEmpty
        ? '-'
        : state.targetVersion.trim();
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

  Widget _buildGameCatalogStep() {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Expanded(
              child: Text(
                'Katalog mini-gier',
                style: TextStyle(
                  color: Colors.grey[900],
                  fontSize: 15,
                  fontWeight: FontWeight.w700,
                ),
              ),
            ),
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
              label: Text(_contentSyncInFlight ? 'Syncing...' : 'Sync'),
            ),
          ],
        ),
        const SizedBox(height: 6),
        Expanded(
          child: ListView.separated(
            itemCount: _gameCatalog.length,
            separatorBuilder: (_, __) => const SizedBox(height: 8),
            itemBuilder: (context, index) {
              final entry = _gameCatalog[index];
              final selected = entry.gameId == _selectedGameId;
              final expanded = _expandedPreviewGameIds.contains(entry.gameId);
              final contentState = _contentStateForGame(entry.gameId);
              final actionInFlight =
                  _contentActionsInFlight.contains(entry.gameId);
              final shouldInstallOrUpdate = contentState.owned &&
                  (contentState.runtimeStatus == ContentRuntimeStatus.notInstalled ||
                      contentState.runtimeStatus ==
                          ContentRuntimeStatus.updateRequired ||
                      contentState.runtimeStatus == ContentRuntimeStatus.failed ||
                      contentState.updateRequired);

              return Card(
                elevation: selected ? 1.5 : 0,
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(10),
                  side: BorderSide(
                    color:
                        selected ? Colors.blue.shade300 : Colors.grey.shade300,
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
                        style: const TextStyle(fontWeight: FontWeight.w700),
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
                              _buildOwnershipChip(contentState),
                              _buildVersionChip(contentState),
                            ],
                          ),
                        ],
                      ),
                      trailing: IconButton(
                        icon: Icon(
                          expanded ? Icons.expand_less : Icons.expand_more,
                        ),
                        onPressed: () {
                          setState(() {
                            if (expanded) {
                              _expandedPreviewGameIds.remove(entry.gameId);
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
                              'Podglad',
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
                                  'Error: ${contentState.lastError}',
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
                      child: Row(
                        children: [
                          Expanded(
                            child: ElevatedButton.icon(
                              onPressed:
                                  !_isConnected || actionInFlight || !shouldInstallOrUpdate
                                      ? null
                                      : () => unawaited(
                                            _requestInstallOrUpdate(contentState),
                                          ),
                              icon: const Icon(Icons.download),
                              label: Text(
                                contentState.runtimeStatus ==
                                        ContentRuntimeStatus.updateRequired
                                    ? 'Update'
                                    : contentState.runtimeStatus ==
                                            ContentRuntimeStatus.failed
                                        ? 'Retry install'
                                        : 'Install',
                              ),
                            ),
                          ),
                          if (contentState.isInstalled) ...[
                            const SizedBox(width: 8),
                            Expanded(
                              child: OutlinedButton.icon(
                                onPressed: !_isConnected || actionInFlight
                                    ? null
                                    : () =>
                                        unawaited(_requestUninstall(contentState)),
                                icon: const Icon(Icons.delete_outline),
                                label: const Text('Uninstall'),
                              ),
                            ),
                          ],
                        ],
                      ),
                    ),
                  ],
                ),
              );
            },
          ),
        ),
        const SizedBox(height: 8),
        ElevatedButton.icon(
          onPressed: _isConnected && _isSelectedGameLaunchable
              ? () {
                  setState(() {
                    _workflowStep = _WorkflowStep.gameSetup;
                  });
                }
              : null,
          icon: const Icon(Icons.settings),
          label: Text(
            _isSelectedGameLaunchable
                ? 'Konfiguruj wybrana gre'
                : 'Gra niegotowa (instalacja/aktualizacja wymagana)',
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

    return ListView(
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
                style:
                    const TextStyle(fontSize: 15, fontWeight: FontWeight.w700),
              ),
              const SizedBox(height: 2),
              Text(entry.description,
                  style: TextStyle(color: Colors.grey[800])),
            ],
          ),
        ),
        const SizedBox(height: 8),
        Container(
          padding: const EdgeInsets.all(12),
          decoration: BoxDecoration(
            color: Colors.grey[100],
            borderRadius: BorderRadius.circular(8),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Wrap(
                spacing: 6,
                runSpacing: 6,
                children: [
                  _buildContentStatusChip(contentState),
                  _buildOwnershipChip(contentState),
                  _buildVersionChip(contentState),
                ],
              ),
              if (!_isLaunchableContentState(contentState))
                Padding(
                  padding: const EdgeInsets.only(top: 6),
                  child: Text(
                    'Ta gra nie jest gotowa do uruchomienia. Wroc do katalogu i wykonaj instalacje/aktualizacje.',
                    style: TextStyle(
                      color: Colors.orange[800],
                      fontSize: 12,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ),
            ],
          ),
        ),
        const SizedBox(height: 8),
        if (_isDemoCubeGameSelected) _buildDemoCubeSettings(),
        if (_isPulseTargetGameSelected) _buildPulseTargetsSettings(),
        if (!_isDemoCubeGameSelected && !_isPulseTargetGameSelected)
          _buildGenericGameSettings(entry),
        const SizedBox(height: 8),
        _buildSetupPreviewCard(entry),
        const SizedBox(height: 8),
        _buildSetupActionBar(entry),
        const SizedBox(height: 8),
        _buildRuntimeActionRows(),
      ],
    );
  }

  Widget _buildDemoCubeSettings() {
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
            onChanged: (value) {
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
            onChanged: (value) {
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
            onChanged: (value) {
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

  Widget _buildGenericGameSettings(_GameCatalogEntry entry) {
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.grey[100],
        borderRadius: BorderRadius.circular(8),
      ),
      child: Text(
        'Gra `${entry.title}` nie ma dedykowanego panelu ustawien po stronie mobilki. '
        'Uzywamy domyslnej konfiguracji runtime.',
        style: TextStyle(color: Colors.grey[700], fontSize: 12),
      ),
    );
  }

  Widget _buildPulseTargetsSettings() {
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
            onChanged: (value) {
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
            onChanged: (value) {
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
            onChanged: (value) {
              setState(() {
                _pulseTargetScale = value;
              });
            },
          ),
        ],
      ),
    );
  }

  Widget _buildSetupPreviewCard(_GameCatalogEntry entry) {
    final expanded = _expandedPreviewGameIds.contains('setup:${entry.gameId}');

    return Card(
      child: Column(
        children: [
          ListTile(
            title: const Text('Podglad i wskazowki'),
            subtitle: Text(
              'Tryb saveGame: ${entry.supportsSaveResume ? 'wspierany' : 'brak wsparcia'}',
            ),
            trailing: IconButton(
              icon: Icon(expanded ? Icons.expand_less : Icons.expand_more),
              onPressed: () {
                setState(() {
                  if (expanded) {
                    _expandedPreviewGameIds.remove('setup:${entry.gameId}');
                  } else {
                    _expandedPreviewGameIds.add('setup:${entry.gameId}');
                  }
                });
              },
            ),
          ),
          if (expanded)
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 0, 16, 12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  for (final line in entry.previewLines)
                    Padding(
                      padding: const EdgeInsets.only(bottom: 2),
                      child: Text(
                        '- $line',
                        style: TextStyle(color: Colors.grey[700], fontSize: 12),
                      ),
                    ),
                ],
              ),
            ),
        ],
      ),
    );
  }

  Widget _buildSetupActionBar(_GameCatalogEntry entry) {
    return Row(
      children: [
        Expanded(
          child: ElevatedButton.icon(
            onPressed:
                _isConnected && !_isPrimaryActionInFlight && _isSelectedGameLaunchable
                ? () => unawaited(_startFromSetup(resumeFromSaved: false))
                : null,
            icon: const Icon(Icons.play_arrow),
            label: const Text('Start'),
            style: ElevatedButton.styleFrom(backgroundColor: Colors.green),
          ),
        ),
        const SizedBox(width: 8),
        Expanded(
          child: ElevatedButton.icon(
            onPressed:
                _isConnected && !_isPrimaryActionInFlight && _isSelectedGameLaunchable
                ? () => unawaited(_restartFromSetup())
                : null,
            icon: const Icon(Icons.restart_alt),
            label: const Text('Restart'),
            style: ElevatedButton.styleFrom(backgroundColor: Colors.orange),
          ),
        ),
        const SizedBox(width: 8),
        Expanded(
          child: OutlinedButton.icon(
            onPressed: _isPrimaryActionInFlight
                ? null
                : () => unawaited(_returnToGameCatalog()),
            icon: const Icon(Icons.arrow_back),
            label: const Text('Wroc'),
          ),
        ),
      ],
    );
  }

  Widget _buildRuntimeActionRows() {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Expanded(
              child: _buildCommandButton(
                icon: Icons.pause,
                label: 'PAUSE',
                color: Colors.orange,
                command: CriticalCommandIds.pauseGame,
              ),
            ),
            const SizedBox(width: 8),
            Expanded(
              child: _buildCommandButton(
                icon: Icons.play_circle_outline,
                label: 'RESUME',
                color: Colors.teal,
                command: CriticalCommandIds.resumeGame,
              ),
            ),
            const SizedBox(width: 8),
            Expanded(
              child: _buildCommandButton(
                icon: Icons.stop,
                label: 'STOP',
                color: Colors.red,
                command: CriticalCommandIds.stopGame,
              ),
            ),
          ],
        ),
        const SizedBox(height: 8),
        _buildCommandButton(
          icon: Icons.flag,
          label: 'END SESSION',
          color: Colors.deepOrange,
          command: CriticalCommandIds.endSession,
        ),
        const SizedBox(height: 8),
        ElevatedButton.icon(
          onPressed: _isConnected && !_manualResyncInFlight
              ? () => _triggerManualResync(includeSyncedEvents: false)
              : null,
          icon: _manualResyncInFlight
              ? const SizedBox(
                  width: 18,
                  height: 18,
                  child: CircularProgressIndicator(
                    strokeWidth: 2,
                    valueColor: AlwaysStoppedAnimation<Color>(Colors.white),
                  ),
                )
              : const Icon(Icons.sync),
          label: Text(
            _manualResyncInFlight ? 'SYNCING...' : 'MANUAL RE-SYNC (SUPPORT)',
          ),
          style: ElevatedButton.styleFrom(
            backgroundColor: Colors.indigo,
            foregroundColor: Colors.white,
            disabledBackgroundColor: Colors.grey[300],
            padding: const EdgeInsets.symmetric(vertical: 12),
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(8),
            ),
          ),
        ),
        if (_lastManualResyncReport != null) ...[
          const SizedBox(height: 8),
          Text(
            'Last re-sync: ${_lastManualResyncReport!.reasonCode} '
            '(missing server ${_lastManualResyncReport!.beforeMissingOnServerCount}'
            '->${_lastManualResyncReport!.afterMissingOnServerCount})',
            style: TextStyle(
              color: _lastManualResyncReport!.success
                  ? Colors.green[700]
                  : Colors.red[700],
              fontSize: 12,
            ),
          ),
        ],
      ],
    );
  }

  Widget _buildCommandButton({
    required IconData icon,
    required String label,
    required Color color,
    required String command,
  }) {
    return ElevatedButton(
      onPressed: _isConnected &&
              (!_requiresSessionDecision ||
                  command == CriticalCommandIds.endSession)
          ? () => unawaited(_sendCommand(command))
          : null,
      style: ElevatedButton.styleFrom(
        backgroundColor: color,
        foregroundColor: Colors.white,
        disabledBackgroundColor: Colors.grey[300],
        padding: const EdgeInsets.symmetric(vertical: 12),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 20),
          const SizedBox(height: 2),
          Text(
            label,
            style: const TextStyle(fontSize: 10, fontWeight: FontWeight.bold),
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
