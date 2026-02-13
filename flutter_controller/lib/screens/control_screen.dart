import 'package:flutter/material.dart';
import 'package:flutter_controller/services/connection_service.dart';
import 'package:flutter_controller/models/device_info.dart';
import 'dart:async';

class ControlScreen extends StatefulWidget {
  final DeviceInfo device;
  
  const ControlScreen({super.key, required this.device});
  
  @override
  State<ControlScreen> createState() => _ControlScreenState();
}

class _ControlScreenState extends State<ControlScreen> {
  final ConnectionService _connection = ConnectionService();
  bool _isConnected = false;
  String _statusMessage = 'Connecting...';
  StreamSubscription<bool>? _connectionSubscription;
  StreamSubscription<Map<String, dynamic>>? _messageSubscription;
  
  @override
  void initState() {
    super.initState();
    _connect();
  }
  
  Future<void> _connect() async {
    // Listen to connection status
    _connectionSubscription = _connection.connectionStatus.listen((connected) {
      if (mounted) {
        setState(() {
          _isConnected = connected;
          _statusMessage = connected ? 'Connected' : 'Disconnected';
        });
      }
    });
    
    // Listen to messages
    _messageSubscription = _connection.messages.listen((message) {
      print('[Control] Received command: ${message['commandId']}');
      // TODO: Handle game status updates based on commandId
      // Example: if (message['commandId'] == 'GAME_STATUS') { ... }
    });
    
    // Attempt connection
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
    _connection.disconnect();
    Navigator.pop(context);
  }
  
  @override
  void dispose() {
    _connectionSubscription?.cancel();
    _messageSubscription?.cancel();
    _connection.dispose();
    super.dispose();
  }
  
  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Control Panel'),
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
      body: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            // Device info card
            Card(
              elevation: 2,
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        const Icon(Icons.headset_mic, size: 32, color: Colors.blue),
                        const SizedBox(width: 12),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                widget.device.deviceName,
                                style: const TextStyle(
                                  fontSize: 20,
                                  fontWeight: FontWeight.bold,
                                ),
                              ),
                              const SizedBox(height: 4),
                              Text(
                                '${widget.device.ip}:${widget.device.controlPort}',
                                style: TextStyle(color: Colors.grey[600]),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            ),
            
            const SizedBox(height: 24),
            
            // Game controls section
            const Text(
              'Game Controls',
              style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
            ),
            const SizedBox(height: 16),
            
            // START button
            ElevatedButton.icon(
              onPressed: _isConnected ? () => _sendCommand('START_GAME') : null,
              icon: const Icon(Icons.play_arrow),
              label: const Text('START GAME'),
              style: ElevatedButton.styleFrom(
                padding: const EdgeInsets.symmetric(vertical: 16),
                backgroundColor: Colors.green,
                foregroundColor: Colors.white,
                disabledBackgroundColor: Colors.grey[300],
              ),
            ),
            const SizedBox(height: 12),
            
            // STOP button
            ElevatedButton.icon(
              onPressed: _isConnected ? () => _sendCommand('STOP_GAME') : null,
              icon: const Icon(Icons.stop),
              label: const Text('STOP GAME'),
              style: ElevatedButton.styleFrom(
                padding: const EdgeInsets.symmetric(vertical: 16),
                backgroundColor: Colors.red,
                foregroundColor: Colors.white,
                disabledBackgroundColor: Colors.grey[300],
              ),
            ),
            const SizedBox(height: 12),
            
            // RESET button
            ElevatedButton.icon(
              onPressed: _isConnected ? () => _sendCommand('RESET_GAME') : null,
              icon: const Icon(Icons.refresh),
              label: const Text('RESET GAME'),
              style: ElevatedButton.styleFrom(
                padding: const EdgeInsets.symmetric(vertical: 16),
                disabledBackgroundColor: Colors.grey[300],
              ),
            ),
            
            const Spacer(),
            
            // Disconnect button
            OutlinedButton.icon(
              onPressed: _handleDisconnect,
              icon: const Icon(Icons.logout),
              label: const Text('Disconnect'),
              style: OutlinedButton.styleFrom(
                padding: const EdgeInsets.symmetric(vertical: 16),
              ),
            ),
            
            const SizedBox(height: 8),
            Text(
              'Commands sent via TCP',
              style: TextStyle(fontSize: 12, color: Colors.grey[600]),
              textAlign: TextAlign.center,
            ),
          ],
        ),
      ),
    );
  }
}
