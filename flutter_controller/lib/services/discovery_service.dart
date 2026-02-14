import 'dart:io';
import 'dart:convert';
import 'dart:async';
import 'package:flutter_controller/models/device_info.dart';

class DiscoveryService {
  RawDatagramSocket? _socket;
  final StreamController<DeviceInfo> _deviceController = StreamController<DeviceInfo>.broadcast();
  
  Stream<DeviceInfo> get devices => _deviceController.stream;
  bool _isScanning = false;
  bool _isPaused = false;
  
  Future<void> startScanning() async {
    if (_isScanning) {
      print('[Discovery] Already scanning');
      return;
    }
    
    try {
      print('[Discovery] 🔍 Starting UDP scan on port 8767');
      
      _socket = await RawDatagramSocket.bind(InternetAddress.anyIPv4, 8767);
      _isScanning = true;
      
      _socket!.listen((RawSocketEvent event) {
        if (event == RawSocketEvent.read) {
          final packet = _socket!.receive();
          if (packet != null) {
            try {
              final message = utf8.decode(packet.data);
              final json = jsonDecode(message) as Map<String, dynamic>;
              final device = DeviceInfo.fromJson(json);
              
              // Only emit device if not paused
              if (!_isPaused) {
                print('[Discovery] ✅ Found: ${device.deviceName} @ ${device.ip}');
                _deviceController.add(device);
              }
            } catch (e) {
              print('[Discovery] ⚠️ Parse error: $e');
            }
          }
        }
      });
      
      print('[Discovery] ✅ Scanning started');
    } catch (e) {
      print('[Discovery] ❌ Failed to start: $e');
      _isScanning = false;
      rethrow;
    }
  }
  
  void stopScanning() {
    if (!_isScanning) return;
    
    _socket?.close();
    _socket = null;
    _isScanning = false;
    _isPaused = false;
    
    print('[Discovery] ⏹️ Scanning stopped');
  }
  
  /// Pause UDP scanning (e.g., when TCP connection established)
  /// Socket remains open but stops emitting discovered devices
  void pauseScanning() {
    if (!_isScanning || _isPaused) return;
    
    _isPaused = true;
    print('[Discovery] ⏸️ Scanning paused - TCP connection established');
  }
  
  /// Resume UDP scanning (e.g., when TCP connection lost)
  void resumeScanning() {
    if (!_isScanning || !_isPaused) return;
    
    _isPaused = false;
    print('[Discovery] ▶️ Scanning resumed - ready to discover devices');
  }
  
  void dispose() {
    stopScanning();
    _deviceController.close();
  }
}
