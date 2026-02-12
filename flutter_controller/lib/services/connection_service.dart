import 'dart:io';
import 'dart:convert';
import 'dart:async';
import 'dart:typed_data';

class ConnectionService {
  Socket? _socket;
  bool _isConnected = false;
  
  final StreamController<String> _messageController = StreamController<String>.broadcast();
  final StreamController<bool> _connectionController = StreamController<bool>.broadcast();
  
  Stream<String> get messages => _messageController.stream;
  Stream<bool> get connectionStatus => _connectionController.stream;
  bool get isConnected => _isConnected;
  
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
      
      print('[Connection] ✅ Connected successfully');
      
      // Listen for incoming messages
      _socket!.listen(
        _handleData,
        onError: (error) {
          print('[Connection] ❌ Error: $error');
          disconnect();
        },
        onDone: () {
          print('[Connection] 🔌 Connection closed');
          disconnect();
        },
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
    try {
      final message = utf8.decode(data);
      print('[Connection] 📥 Received: ${message.length > 100 ? message.substring(0, 100) + "..." : message}');
      _messageController.add(message);
    } catch (e) {
      print('[Connection] ⚠️ Failed to decode message: $e');
    }
  }
  
  Future<void> sendCommand(String commandId, Map<String, dynamic>? payload) async {
    if (!_isConnected || _socket == null) {
      print('[Connection] ⚠️ Cannot send - not connected');
      return;
    }
    
    try {
      final message = jsonEncode({
        'commandId': commandId,
        'timestamp': DateTime.now().millisecondsSinceEpoch,
        'payload': payload,
      });
      
      _socket!.write(message);
      await _socket!.flush();
      
      print('[Connection] 📤 Sent: $commandId');
    } catch (e) {
      print('[Connection] ❌ Send failed: $e');
    }
  }
  
  void disconnect() {
    _socket?.close();
    _socket = null;
    _isConnected = false;
    _connectionController.add(false);
    print('[Connection] 🔌 Disconnected');
  }
  
  void dispose() {
    disconnect();
    _messageController.close();
    _connectionController.close();
  }
}
