import 'package:flutter/material.dart';
import 'package:flutter_controller/services/discovery_service.dart';
import 'package:flutter_controller/services/firebase_service.dart';
import 'package:flutter_controller/models/device_info.dart';
import 'package:flutter_controller/screens/control_screen.dart';
import 'dart:async';

class ScannerScreen extends StatefulWidget {
  const ScannerScreen({super.key});

  @override
  State<ScannerScreen> createState() => _ScannerScreenState();
}

class _ScannerScreenState extends State<ScannerScreen> {
  final DiscoveryService _discovery = DiscoveryService();
  final List<DeviceInfo> _devices = [];
  bool _isScanning = false;
  StreamSubscription<DeviceInfo>? _deviceSubscription;
  
  @override
  void initState() {
    super.initState();
    _startScanning();
  }
  
  void _startScanning() {
    setState(() {
      _isScanning = true;
      _devices.clear();
    });
    
    _deviceSubscription = _discovery.devices.listen((device) {
      setState(() {
        // Replace if same device (by IP)
        _devices.removeWhere((d) => d.ip == device.ip);
        _devices.add(device);
      });
    });
    
    _discovery.startScanning();
  }
  
  void _stopScanning() {
    _discovery.stopScanning();
    _deviceSubscription?.cancel();
    setState(() {
      _isScanning = false;
    });
  }
  
  void _connectToDevice(DeviceInfo device) {
    _stopScanning();
    
    Navigator.push(
      context,
      MaterialPageRoute(
        builder: (context) => ControlScreen(device: device),
      ),
    ).then((_) {
      // Restart scanning when returning
      _startScanning();
    });
  }
  
  Future<void> _handleLogout() async {
    await FirebaseService.signOut();
    if (mounted) {
      Navigator.pop(context);
    }
  }
  
  @override
  void dispose() {
    _discovery.dispose();
    _deviceSubscription?.cancel();
    super.dispose();
  }
  
  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Scan for Quest Devices'),
        actions: [
          IconButton(
            icon: const Icon(Icons.logout),
            onPressed: _handleLogout,
            tooltip: 'Logout',
          ),
        ],
      ),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.all(16),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    _isScanning
                        ? 'Scanning for devices...'
                        : '${_devices.length} device(s) found',
                    style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w500),
                  ),
                ),
                if (_isScanning)
                  const SizedBox(
                    width: 20,
                    height: 20,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                else
                  ElevatedButton.icon(
                    onPressed: _startScanning,
                    icon: const Icon(Icons.refresh, size: 18),
                    label: const Text('Scan'),
                  ),
              ],
            ),
          ),
          
          const Divider(height: 1),
          
          Expanded(
            child: _devices.isEmpty
                ? Center(
                    child: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        Icon(Icons.devices, size: 64, color: Colors.grey[400]),
                        const SizedBox(height: 16),
                        const Text(
                          'No devices found',
                          style: TextStyle(fontSize: 18, fontWeight: FontWeight.w500),
                        ),
                        const SizedBox(height: 8),
                        Text(
                          'Make sure Quest is on the same network',
                          style: TextStyle(color: Colors.grey[600]),
                        ),
                        const SizedBox(height: 24),
                        if (!_isScanning)
                          ElevatedButton.icon(
                            onPressed: _startScanning,
                            icon: const Icon(Icons.refresh),
                            label: const Text('Start Scanning'),
                          ),
                      ],
                    ),
                  )
                : ListView.builder(
                    itemCount: _devices.length,
                    itemBuilder: (context, index) {
                      final device = _devices[index];
                      return Card(
                        margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                        child: ListTile(
                          leading: const Icon(Icons.headset_mic, size: 40, color: Colors.blue),
                          title: Text(
                            device.deviceName,
                            style: const TextStyle(fontWeight: FontWeight.bold),
                          ),
                          subtitle: Text('${device.ip}:${device.controlPort}'),
                          trailing: ElevatedButton(
                            onPressed: () => _connectToDevice(device),
                            child: const Text('Connect'),
                          ),
                        ),
                      );
                    },
                  ),
          ),
        ],
      ),
    );
  }
}
