import 'package:flutter/material.dart';
import 'package:flutter_controller/services/connection_service.dart';
import 'package:flutter_controller/services/discovery_service.dart';
import 'package:flutter_controller/services/foreground_service_bridge.dart';
import 'package:flutter_controller/models/device_info.dart';
import 'package:flutter_controller/models/student.dart';
import 'package:flutter_controller/widgets/media_stream_widget.dart';
import 'package:wakelock_plus/wakelock_plus.dart';
import 'dart:async';

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

class _ControlScreenState extends State<ControlScreen> with WidgetsBindingObserver {
  final ConnectionService _connection = ConnectionService();
  bool _isConnected = false;
  String _statusMessage = 'Connecting...';
  StreamSubscription<bool>? _connectionSubscription;
  StreamSubscription<Map<String, dynamic>>? _messageSubscription;
  bool _isReconnecting = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);

    // Link discovery service for automatic pause/resume
    _connection.setDiscoveryService(widget.discoveryService);

    _setupConnectionListeners();
    unawaited(ForegroundServiceBridge.start());
    unawaited(WakelockPlus.enable());
    _connect();
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
    await _connection.sendCommand(command, null);

    if (mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Sent: $command'),
          duration: const Duration(seconds: 1),
        ),
      );
    }
  }

  void _handleDisconnect() {
    unawaited(ForegroundServiceBridge.stop());
    _connection.disconnect();
    Navigator.pop(context);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _connectionSubscription?.cancel();
    _messageSubscription?.cancel();
    unawaited(ForegroundServiceBridge.stop());
    unawaited(WakelockPlus.disable());
    _connection.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text('${widget.student.firstName} — ${widget.device.deviceName}'),
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
                    padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                    decoration: BoxDecoration(
                      color: Colors.grey[100],
                      borderRadius: BorderRadius.circular(8),
                    ),
                    child: Row(
                      children: [
                        const Icon(Icons.headset_mic, size: 20, color: Colors.blue),
                        const SizedBox(width: 8),
                        Text(
                          '${widget.device.ip}:${widget.device.controlPort}',
                          style: TextStyle(color: Colors.grey[600], fontSize: 13),
                        ),
                        const Spacer(),
                        Text(
                          'Video: ${widget.device.videoPort}',
                          style: TextStyle(color: Colors.grey[500], fontSize: 12),
                        ),
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
                          command: 'START_GAME',
                        ),
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: _buildCommandButton(
                          icon: Icons.stop,
                          label: 'STOP',
                          color: Colors.red,
                          command: 'STOP_GAME',
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
                          command: 'PAUSE_GAME',
                        ),
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: _buildCommandButton(
                          icon: Icons.play_circle_outline,
                          label: 'RESUME',
                          color: Colors.teal,
                          command: 'RESUME_GAME',
                        ),
                      ),
                    ],
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
      onPressed: _isConnected ? () => _sendCommand(command) : null,
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
          Text(label, style: const TextStyle(fontSize: 11, fontWeight: FontWeight.bold)),
        ],
      ),
    );
  }
}
