import 'package:flutter/material.dart';
import 'package:flutter_controller/services/discovery_service.dart';
import 'package:flutter_controller/services/firebase_service.dart';
import 'package:flutter_controller/models/device_info.dart';
import 'package:flutter_controller/models/student.dart';
import 'package:flutter_controller/screens/control_screen.dart';
import 'dart:async';

class ScannerScreen extends StatefulWidget {
  final Student student;
  
  const ScannerScreen({super.key, required this.student});

  @override
  State<ScannerScreen> createState() => _ScannerScreenState();
}

class _ScannerScreenState extends State<ScannerScreen> {
  final DiscoveryService _discovery = DiscoveryService();
  final Map<String, DeviceInfo> _devices = {}; // Changed to Map for easier updates
  final Map<String, DateTime> _lastSeen = {}; // Track when device was last seen
  bool _isScanning = false;
  StreamSubscription<DeviceInfo>? _deviceSubscription;
  Timer? _cleanupTimer;
  
  @override
  void initState() {
    super.initState();
    _startScanning();
  }
  
  void _startScanning() {
    setState(() {
      _isScanning = true;
      // DON'T clear devices - keep them in list!
    });
    
    // Listen for device updates
    _deviceSubscription = _discovery.devices.listen((device) {
      setState(() {
        // Update or add device (keyed by IP)
        _devices[device.ip] = device;
        _lastSeen[device.ip] = DateTime.now();
      });
    });
    
    // Cleanup old devices every 5 seconds
    _cleanupTimer?.cancel();
    _cleanupTimer = Timer.periodic(const Duration(seconds: 5), (timer) {
      final now = DateTime.now();
      final toRemove = <String>[];
      
      _lastSeen.forEach((ip, lastSeenTime) {
        // Remove if not seen in last 15 seconds
        if (now.difference(lastSeenTime).inSeconds > 15) {
          toRemove.add(ip);
        }
      });
      
      if (toRemove.isNotEmpty) {
        setState(() {
          for (var ip in toRemove) {
            _devices.remove(ip);
            _lastSeen.remove(ip);
          }
        });
        print('[Scanner] Removed ${toRemove.length} stale device(s)');
      }
    });
    
    _discovery.startScanning();
  }
  
  void _stopScanning() {
    _discovery.stopScanning();
    _deviceSubscription?.cancel();
    _cleanupTimer?.cancel();
    setState(() {
      _isScanning = false;
    });
  }
  
  void _connectToDevice(DeviceInfo device) {
    // Don't stop scanning - let ControlScreen pause it automatically
    
    Navigator.push(
      context,
      MaterialPageRoute(
        builder: (context) => ControlScreen(
          device: device,
          student: widget.student,
          discoveryService: _discovery,
        ),
      ),
    );
    // Discovery will auto-resume when connection is lost
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
    _cleanupTimer?.cancel();
    super.dispose();
  }
  
  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text('Scan for ${widget.student.firstName}\'s Device'),
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
                        ? 'Scanning for devices... (${_devices.length} found)'
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
                      final device = _devices.values.toList()[index];
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
