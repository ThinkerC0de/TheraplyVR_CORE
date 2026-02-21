import 'dart:convert';

import 'package:flutter_controller/models/runtime_status_signal.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('parses SESSION_WATCHDOG_HEARTBEAT payload', () {
    final nowUtc = DateTime.now().toUtc();
    final message = <String, dynamic>{
      'commandId': RuntimeStatusSignalCommandIds.sessionWatchdogHeartbeat,
      'payload': base64Encode(
        utf8.encode(
          jsonEncode(<String, dynamic>{
            'sessionId': 'session-1',
            'patientId': 'patient-1',
            'therapistId': 'therapist-1',
            'sessionState': 'IN_PROGRESS',
            'runtimeStatus': TherapistRuntimeStatus.playing.wireValue,
            'healthCode': 'OK',
            'healthy': true,
            'activeGameId': 'focus_ball',
            'activeGameState': 'Playing',
            'heartbeatUnixMs': nowUtc.millisecondsSinceEpoch,
            'lastHealthyUnixMs': nowUtc.millisecondsSinceEpoch,
            'pendingQueueSize': 2,
            'expectedIntervalMs': 2000,
            'staleAfterMs': 6000,
          }),
        ),
      ),
    };

    final signal =
        SessionWatchdogHeartbeatSignal.tryFromNetworkMessage(message);

    expect(signal, isNotNull);
    expect(signal!.sessionId, 'session-1');
    expect(signal.runtimeStatus, TherapistRuntimeStatus.playing);
    expect(signal.healthy, isTrue);
    expect(signal.healthCode, 'OK');
    expect(signal.pendingQueueSize, 2);
    expect(signal.expectedIntervalMs, 2000);
    expect(signal.staleAfterMs, 6000);
  });

  test('heartbeat stale detection returns true after staleAfterMs', () {
    final oldUtc = DateTime.now().toUtc().subtract(const Duration(seconds: 10));
    final signal = SessionWatchdogHeartbeatSignal(
      sessionId: 'session-1',
      patientId: 'patient-1',
      therapistId: 'therapist-1',
      sessionState: 'IN_PROGRESS',
      runtimeStatus: TherapistRuntimeStatus.playing,
      healthCode: 'OK',
      healthy: true,
      activeGameId: 'focus_ball',
      activeGameState: 'Playing',
      heartbeatAtUtc: DateTime.utc(2026, 1, 1),
      lastHealthyAtUtc: DateTime.utc(2026, 1, 1),
      pendingQueueSize: 0,
      expectedIntervalMs: 2000,
      staleAfterMs: 3000,
    );

    final customSignal = SessionWatchdogHeartbeatSignal(
      sessionId: signal.sessionId,
      patientId: signal.patientId,
      therapistId: signal.therapistId,
      sessionState: signal.sessionState,
      runtimeStatus: signal.runtimeStatus,
      healthCode: signal.healthCode,
      healthy: signal.healthy,
      activeGameId: signal.activeGameId,
      activeGameState: signal.activeGameState,
      heartbeatAtUtc: oldUtc,
      lastHealthyAtUtc: oldUtc,
      pendingQueueSize: signal.pendingQueueSize,
      expectedIntervalMs: signal.expectedIntervalMs,
      staleAfterMs: signal.staleAfterMs,
    );

    expect(customSignal.isStale(DateTime.now().toUtc()), isTrue);
  });

  test('parses DEVICE_PRESENCE_UPDATE payload', () {
    final nowUtc = DateTime.now().toUtc();
    final message = <String, dynamic>{
      'commandId': RuntimeStatusSignalCommandIds.devicePresenceUpdate,
      'payload': base64Encode(
        utf8.encode(
          jsonEncode(<String, dynamic>{
            'sessionId': 'session-2',
            'patientId': 'patient-2',
            'therapistId': 'therapist-2',
            'presenceState': DevicePresenceState.focusLost.wireValue,
            'reasonCode': 'APP_FOCUS_LOST',
            'changedAtUnixMs': nowUtc.millisecondsSinceEpoch,
            'appPaused': false,
            'appFocused': false,
            'hasTcpClient': true,
            'activeGameId': 'demo_cube_clicker',
            'activeGameState': 'Playing',
          }),
        ),
      ),
    };

    final signal = DevicePresenceUpdateSignal.tryFromNetworkMessage(message);

    expect(signal, isNotNull);
    expect(signal!.sessionId, 'session-2');
    expect(signal.presenceState, DevicePresenceState.focusLost);
    expect(signal.reasonCode, 'APP_FOCUS_LOST');
    expect(signal.appFocused, isFalse);
    expect(signal.hasTcpClient, isTrue);
  });
}
