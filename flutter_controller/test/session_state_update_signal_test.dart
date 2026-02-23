import 'dart:convert';

import 'package:flutter_controller/models/session_fsm_contract.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('parses SESSION_STATE_UPDATE with ownership keys', () {
    final nowUtc = DateTime.now().toUtc();
    final message = <String, dynamic>{
      'commandId': SessionSignalCommandIds.sessionStateUpdate,
      'payload': base64Encode(
        utf8.encode(
          jsonEncode(<String, dynamic>{
            'sessionId': 'session-1',
            'studentId': 'student-1',
            'patientId': 'patient-legacy',
            'therapistId': 'therapist-1',
            'ownerKey': 'therapist-1|student-1',
            'sessionKey': 'therapist-1|student-1|session-1',
            'state': 'IN_PROGRESS',
            'previousState': 'CREATED',
            'reasonCode': 'START_GAME',
            'changedAtUnixMs': nowUtc.millisecondsSinceEpoch,
          }),
        ),
      ),
    };

    final signal = SessionStateUpdateSignal.tryFromNetworkMessage(message);

    expect(signal, isNotNull);
    expect(signal!.sessionId, 'session-1');
    expect(signal.studentId, 'student-1');
    expect(signal.patientId, 'patient-legacy');
    expect(signal.therapistId, 'therapist-1');
    expect(signal.ownerKey, 'therapist-1|student-1');
    expect(signal.sessionKey, 'therapist-1|student-1|session-1');
    expect(signal.state, SessionLifecycleState.inProgress);
    expect(signal.previousState, SessionLifecycleState.created);
  });

  test('falls back studentId to patientId when studentId is missing', () {
    final message = <String, dynamic>{
      'commandId': SessionSignalCommandIds.sessionStateUpdate,
      'payload': base64Encode(
        utf8.encode(
          jsonEncode(<String, dynamic>{
            'sessionId': 'session-2',
            'patientId': 'patient-2',
            'therapistId': 'therapist-2',
            'state': 'PAUSED',
            'changedAtUnixMs': DateTime.now().toUtc().millisecondsSinceEpoch,
          }),
        ),
      ),
    };

    final signal = SessionStateUpdateSignal.tryFromNetworkMessage(message);

    expect(signal, isNotNull);
    expect(signal!.studentId, 'patient-2');
    expect(signal.patientId, 'patient-2');
  });
}
