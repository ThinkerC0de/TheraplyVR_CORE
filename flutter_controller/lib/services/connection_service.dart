import 'dart:io';
import 'dart:convert';
import 'dart:async';
import 'dart:typed_data';
import 'package:flutter/foundation.dart';
import 'discovery_service.dart';

class ConnectionService {
  Socket? _socket;
  bool _isConnected = false;
  
  final StreamController<Map<String, dynamic>> _messageController = StreamController<Map<String, dynamic>>.broadcast();
  final StreamController<bool> _connectionController = StreamController<bool>.broadcast();
  
  // Discovery service reference (for automatic pause/resume)
  DiscoveryService? _discoveryService;
  
  // Buffer for incomplete messages
  final List<int> _receiveBuffer = [];
  
  Stream<Map<String, dynamic>> get messages => _messageController.stream;
  Stream<bool> get connectionStatus => _connectionController.stream;
  bool get isConnected => _isConnected;
  
  /// Set discovery service for automatic pause/resume
  void setDiscoveryService(DiscoveryService discoveryService) {
    _discoveryService = discoveryService;
  }
  
  Future<bool> connect(String ip, int port) async {
    try {
      print('[Connection] 🔌 Connecting to $ip:$port');
      
      _socket = await Socket.connect(
        ip, 
        port, 
        timeout: const Duration(seconds: 5),
      );
      
      // TCP no delay for low latency
      _socket!.setOption(SocketOption.tcpNoDelay, true);
      
      _isConnected = true;
      _connectionController.add(true);
      
      // Pause UDP discovery when TCP connected
      _discoveryService?.pauseScanning();
      
      print('[Connection] ✅ Connected successfully');
      
      // Listen for incoming messages
      _socket!.listen(
        _handleData,
        onError: (error) {
          print('[Connection] ❌ Error: $error');
          disconnect();
        },
        onDone: () {
          print('[Connection] 🔌 Disconnected');
          disconnect();
        },
        cancelOnError: false,
      );
      
      return true;
    } catch (e) {
      print('[Connection] ❌ Failed to connect: $e');
      _isConnected = false;
      _connectionController.add(false);
      return false;
    }
  }
  
  void _handleData(Uint8List data) {
    // Add received data to buffer
    _receiveBuffer.addAll(data);
    
    // Process all complete messages in buffer
    while (_receiveBuffer.length >= 4) {
      try {
        // Read length prefix (4 bytes, big-endian)
        final lengthBytes = _receiveBuffer.sublist(0, 4);
        final messageLength = ByteData.sublistView(Uint8List.fromList(lengthBytes)).getInt32(0, Endian.big);
        
        // Validate length
        if (messageLength <= 0 || messageLength > 10 * 1024 * 1024) {
          print('[Connection] ❌ Invalid message length: $messageLength');
          _receiveBuffer.clear();
          disconnect();
          return;
        }
        
        // Check if we have the complete message
        if (_receiveBuffer.length < 4 + messageLength) {
          // Wait for more data
          break;
        }
        
        // Extract message
        final messageBytes = _receiveBuffer.sublist(4, 4 + messageLength);
        final messageJson = utf8.decode(messageBytes);
        
        // Remove processed bytes from buffer
        _receiveBuffer.removeRange(0, 4 + messageLength);
        
        // Parse and emit message
        try {
          final message = jsonDecode(messageJson) as Map<String, dynamic>;
          print('[Connection] 📥 Received: ${message['commandId']}');
          _messageController.add(message);
        } catch (e) {
          print('[Connection] ⚠️ Failed to parse JSON: $e');
        }
      } catch (e) {
        print('[Connection] ⚠️ Error processing message: $e');
        _receiveBuffer.clear();
        break;
      }
    }
  }
  
  Future<void> sendCommand(String commandId, Map<String, dynamic>? payload) async {
    if (!_isConnected || _socket == null) {
      print('[Connection] ⚠️ Cannot send - not connected');
      return;
    }
    
    try {
      // Create message matching Unity's NetworkMessage structure
      final message = {
        'messageId': DateTime.now().millisecondsSinceEpoch.toString(),
        'timestamp': (DateTime.now().millisecondsSinceEpoch / 1000).floor(),
        'commandId': commandId,
        'payload': payload != null ? utf8.encode(jsonEncode(payload)) : null,
      };
      
      final messageJson = jsonEncode(message);
      final messageBytes = utf8.encode(messageJson);
      
      // Send length prefix (4 bytes, big-endian)
      final lengthBytes = ByteData(4)
        ..setInt32(0, messageBytes.length, Endian.big);
      
      _socket!.add(lengthBytes.buffer.asUint8List());
      _socket!.add(messageBytes);
      await _socket!.flush();
      
      print('[Connection] 📤 Sent: $commandId (${messageBytes.length} bytes)');
    } catch (e) {
      print('[Connection] ❌ Send failed: $e');
      disconnect();
    }
  }
  
  void disconnect() async {
    if (_socket == null) return;
    
    try {
      // Flush any pending data before closing
      await _socket!.flush();
      // Give a moment for flush to complete
      await Future.delayed(const Duration(milliseconds: 50));
      // Gracefully close the socket
      await _socket!.close();
    } catch (e) {
      // Ignore errors during disconnect
      print('[Connection] ⚠️ Error during disconnect: $e');
    }
    
    _socket = null;
    _isConnected = false;
    _receiveBuffer.clear();
    _connectionController.add(false);
    
    // Resume UDP discovery when TCP disconnected
    _discoveryService?.resumeScanning();
    
    print('[Connection] 🔌 Disconnected');
  }
  
  void dispose() {
    disconnect();
    _messageController.close();
    _connectionController.close();
  }
}
