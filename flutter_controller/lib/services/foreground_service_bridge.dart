import 'package:flutter/services.dart';

class ForegroundServiceBridge {
  static const MethodChannel _channel = MethodChannel('theraply/foreground_service');

  static Future<void> start() async {
    try {
      await _channel.invokeMethod('start');
    } catch (_) {
      // Keep app logic resilient even if platform call fails.
    }
  }

  static Future<void> stop() async {
    try {
      await _channel.invokeMethod('stop');
    } catch (_) {
      // Keep app logic resilient even if platform call fails.
    }
  }
}

