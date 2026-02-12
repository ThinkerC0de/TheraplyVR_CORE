class DeviceInfo {
  final String deviceId;
  final String deviceName;
  final String ip;
  final int controlPort;
  final int videoPort;
  final int audioPort;
  final String? studentId;
  final DateTime timestamp;
  
  DeviceInfo({
    required this.deviceId,
    required this.deviceName,
    required this.ip,
    required this.controlPort,
    required this.videoPort,
    required this.audioPort,
    this.studentId,
    required this.timestamp,
  });
  
  factory DeviceInfo.fromJson(Map<String, dynamic> json) {
    return DeviceInfo(
      deviceId: json['deviceId'] as String,
      deviceName: json['deviceName'] as String,
      ip: json['ip'] as String,
      controlPort: json['controlPort'] as int,
      videoPort: json['videoPort'] as int,
      audioPort: json['audioPort'] as int,
      studentId: json['studentId'] as String?,
      timestamp: DateTime.fromMillisecondsSinceEpoch(json['timestamp'] as int),
    );
  }
  
  Map<String, dynamic> toJson() {
    return {
      'deviceId': deviceId,
      'deviceName': deviceName,
      'ip': ip,
      'controlPort': controlPort,
      'videoPort': videoPort,
      'audioPort': audioPort,
      'studentId': studentId,
      'timestamp': timestamp.millisecondsSinceEpoch,
    };
  }
}
