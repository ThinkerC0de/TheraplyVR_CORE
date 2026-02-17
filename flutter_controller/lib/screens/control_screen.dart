import 'package:flutter/material.dart';
import 'package:flutter_controller/services/connection_service.dart';
import 'package:flutter_controller/services/discovery_service.dart';
import 'package:flutter_controller/services/foreground_service_bridge.dart';
import 'package:flutter_controller/models/critical_command_envelope.dart';
import 'package:flutter_controller/models/runtime_status_signal.dart';
import 'package:flutter_controller/models/manual_resync_report_signal.dart';
import 'package:flutter_controller/models/device_info.dart';
import 'package:flutter_controller/models/session_recovery_policy.dart';
import 'package:flutter_controller/models/session_fsm_contract.dart';
import 'package:flutter_controller/models/student.dart';
import 'package:flutter_controller/widgets/media_stream_widget.dart';
import 'package:wakelock_plus/wakelock_plus.dart';
import 'dart:async';

enum _SessionGateAction { resume, startNew }

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
  final ConnectionService _connection = ConnectionService();
  bool _isConnected = false;
  String _statusMessage = 'Connecting...';
  StreamSubscription<bool>? _connectionSubscription;
  StreamSubscription<Map<String, dynamic>>? _messageSubscription;
  Timer? _heartbeatUiTimer;
  bool _isReconnecting = false;
  SessionLifecycleState? _sessionLifecycleState;
  TherapistRuntimeStatus? _runtimeStatus;
  SessionWatchdogHeartbeatSignal? _lastWatchdogHeartbeat;
  late String _activeSessionId;
  bool _requiresSessionDecision = false;
  bool _isSessionDecisionDialogOpen = false;
  String? _remoteSessionIdPendingDecision;
  bool _manualResyncInFlight = false;
  ManualResyncReportSignal? _lastManualResyncReport;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);

    // Link discovery service for automatic pause/resume
    _connection.setDiscoveryService(widget.discoveryService);
    _activeSessionId = _buildLocalSessionId();

    _setupConnectionListeners();
    _startHeartbeatUiTicker();
    unawaited(ForegroundServiceBridge.start());
    unawaited(WakelockPlus.enable());
    _connect();
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

  void _setupConnectionListeners() {
    _connectionSubscription = _connection.connectionStatus.listen((connected) {
      if (mounted) {
        setState(() {
          _isConnected = connected;
          _statusMessage = connected ? 'Connected' : 'Disconnected';
        });
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

      print('[Control] Message: ${message['commandId']}');
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
      Navigator.pop(context);
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      unawaited(_recoverConnectionAfterResume());
    }
  }

  Future<void> _recoverConnectionAfterResume() async {
    if (!mounted || _isReconnecting) return;
    if (_connection.isConnected) return;

    _isReconnecting = true;
    if (mounted) {
      setState(() => _statusMessage = 'Reconnecting...');
    }

    final ok = await _connection.reconnect();
    if (!mounted) return;

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

  Future<void> _sendCommand(String command) async {
    if (_requiresSessionDecision && command != CriticalCommandIds.endSession) {
      if (!mounted) return;
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
          payload: _buildCriticalPayload(command),
          expiresAtUtc: DateTime.now().toUtc().add(const Duration(seconds: 30)),
        );
      } else {
        await _connection.sendCommand(command, null);
      }

      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('Sent: $command'),
            duration: const Duration(seconds: 1),
          ),
        );
      }
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Failed: $command ($e)'),
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

      if (!mounted) return;

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content:
              Text('Manual re-sync requested. Waiting for Quest report...'),
          duration: Duration(seconds: 2),
        ),
      );
    } catch (e) {
      if (!mounted) return;
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

  void _handleDisconnect() {
    unawaited(ForegroundServiceBridge.stop());
    _connection.disconnect();
    Navigator.pop(context);
  }

  String _buildLocalSessionId() {
    return 'mobile-${widget.student.id}-${DateTime.now().toUtc().millisecondsSinceEpoch}';
  }

  Map<String, dynamic> _buildCriticalPayload(String command) {
    final payload = <String, dynamic>{
      'studentId': widget.student.id,
      'therapistId': widget.student.therapistId,
    };

    if (command == CriticalCommandIds.stopGame) {
      payload['reason'] = 'TherapistStop';
    } else if (command == CriticalCommandIds.endSession) {
      payload['reason'] = 'TherapistEndedSession';
    }

    return payload;
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
    final needsDecision = _shouldRequireSessionDecision(
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

  bool _shouldRequireSessionDecision({
    required String remoteSessionId,
    required SessionLifecycleState? remoteState,
    required TherapistRuntimeStatus? remoteRuntimeStatus,
  }) {
    return SessionRecoveryPolicy.shouldRequireDecision(
      localSessionId: _activeSessionId,
      remoteSessionId: remoteSessionId,
      remoteState: remoteState,
      remoteRuntimeStatus: remoteRuntimeStatus,
    );
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
          title: const Text('Active Session Detected'),
          content: Text(
            'Quest reports active session `$remoteSessionId`.\n\n'
            'Choose Resume to attach, or Start New to explicitly end current session first.',
          ),
          actions: [
            TextButton(
              onPressed: () =>
                  Navigator.of(context).pop(_SessionGateAction.resume),
              child: const Text('Resume'),
            ),
            ElevatedButton(
              onPressed: () =>
                  Navigator.of(context).pop(_SessionGateAction.startNew),
              child: const Text('Start New'),
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
        setState(() {
          _activeSessionId = remoteSessionId;
          _requiresSessionDecision = false;
        });
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

  Future<void> _handleStartNewDecision(String remoteSessionId) async {
    final confirmed = await showDialog<bool>(
          context: context,
          builder: (context) {
            return AlertDialog(
              title: const Text('Confirm Start New'),
              content: const Text(
                'This will send END_SESSION for current Quest session first. Continue?',
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.of(context).pop(false),
                  child: const Text('Cancel'),
                ),
                ElevatedButton(
                  onPressed: () => Navigator.of(context).pop(true),
                  child: const Text('Confirm'),
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

      if (!mounted) return;

      setState(() {
        _activeSessionId = _buildLocalSessionId();
        _requiresSessionDecision = false;
      });

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Previous session ended. You can start a new one now.'),
        ),
      );
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Failed to end previous session: $e'),
          backgroundColor: Colors.red,
        ),
      );
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
    return Scaffold(
      appBar: AppBar(
        title:
            Text('${widget.student.firstName} — ${widget.device.deviceName}'),
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
          // Media Stream (top half)
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

          // Controls (bottom half)
          Expanded(
            flex: 4,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(12, 4, 12, 8),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  // Device info row
                  Container(
                    padding:
                        const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                    decoration: BoxDecoration(
                      color: Colors.grey[100],
                      borderRadius: BorderRadius.circular(8),
                    ),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            const Icon(Icons.headset_mic,
                                size: 20, color: Colors.blue),
                            const SizedBox(width: 8),
                            Text(
                              '${widget.device.ip}:${widget.device.controlPort}',
                              style: TextStyle(
                                  color: Colors.grey[600], fontSize: 13),
                            ),
                            const Spacer(),
                            Text(
                              'Video: ${widget.device.videoPort}',
                              style: TextStyle(
                                  color: Colors.grey[500], fontSize: 12),
                            ),
                          ],
                        ),
                        const SizedBox(height: 4),
                        Text(
                          'Session: ${_sessionLifecycleState?.wireValue ?? 'UNKNOWN'}',
                          style:
                              TextStyle(color: Colors.grey[700], fontSize: 12),
                        ),
                        const SizedBox(height: 2),
                        Text(
                          'Runtime: ${_runtimeStatus?.wireValue ?? 'unknown'}',
                          style:
                              TextStyle(color: Colors.grey[700], fontSize: 12),
                        ),
                        if (_lastWatchdogHeartbeat != null) ...[
                          const SizedBox(height: 2),
                          Builder(
                            builder: (context) {
                              final heartbeat = _lastWatchdogHeartbeat!;
                              final stale = _isWatchdogHeartbeatStale(
                                heartbeat,
                              );
                              final healthy = heartbeat.healthy && !stale;
                              final color =
                                  healthy ? Colors.green[700] : Colors.red[700];
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
                        ],
                        if (_requiresSessionDecision) ...[
                          const SizedBox(height: 2),
                          const Text(
                            'Action required: Resume or Start New',
                            style: TextStyle(
                              color: Colors.deepOrange,
                              fontSize: 12,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                        ],
                        if (_manualResyncInFlight) ...[
                          const SizedBox(height: 2),
                          const Text(
                            'Support re-sync in progress...',
                            style: TextStyle(
                              color: Colors.teal,
                              fontSize: 12,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                        ],
                        if (_lastManualResyncReport != null) ...[
                          const SizedBox(height: 2),
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
                    ),
                  ),

                  const SizedBox(height: 12),

                  // Game control buttons
                  Row(
                    children: [
                      Expanded(
                        child: _buildCommandButton(
                          icon: Icons.play_arrow,
                          label: 'START',
                          color: Colors.green,
                          command: CriticalCommandIds.startGame,
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
                      const SizedBox(width: 8),
                      Expanded(
                        child: _buildCommandButton(
                          icon: Icons.refresh,
                          label: 'RESET',
                          color: Colors.blue,
                          command: 'RESET_GAME',
                        ),
                      ),
                    ],
                  ),

                  const SizedBox(height: 8),

                  // Pause/Resume row
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
                              valueColor:
                                  AlwaysStoppedAnimation<Color>(Colors.white),
                            ),
                          )
                        : const Icon(Icons.sync),
                    label: Text(
                      _manualResyncInFlight
                          ? 'SYNCING...'
                          : 'MANUAL RE-SYNC (SUPPORT)',
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

                  const Spacer(),

                  // Disconnect button
                  OutlinedButton.icon(
                    onPressed: _handleDisconnect,
                    icon: const Icon(Icons.logout, size: 18),
                    label: const Text('Disconnect'),
                    style: OutlinedButton.styleFrom(
                      padding: const EdgeInsets.symmetric(vertical: 12),
                      foregroundColor: Colors.red[400],
                      side: BorderSide(color: Colors.red[300]!),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
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
          ? () => _sendCommand(command)
          : null,
      style: ElevatedButton.styleFrom(
        backgroundColor: color,
        foregroundColor: Colors.white,
        disabledBackgroundColor: Colors.grey[300],
        padding: const EdgeInsets.symmetric(vertical: 14),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 22),
          const SizedBox(height: 2),
          Text(label,
              style:
                  const TextStyle(fontSize: 11, fontWeight: FontWeight.bold)),
        ],
      ),
    );
  }
}
