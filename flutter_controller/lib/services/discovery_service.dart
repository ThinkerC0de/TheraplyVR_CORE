import 'dart:io';
import 'dart:convert';
import 'dart:async';
import 'package:flutter_controller/models/device_info.dart';

class DiscoveryService {
  RawDatagramSocket? _socket;
  final StreamController<DeviceInfo> _deviceController = StreamController<DeviceInfo>.broadcast();
  
  Stream<DeviceInfo> get devices => _deviceController.stream;
  bool _isScanning = false;
  
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
              
              print('[Discovery] ✅ Found: ${device.deviceName} @ ${device.ip}');
              _deviceController.add(device);
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
    
    print('[Discovery] ⏹️ Scanning stopped');
  }
  
  void dispose() {
    stopScanning();
    _deviceController.close();
  }
}
