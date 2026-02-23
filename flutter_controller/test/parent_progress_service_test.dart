import 'package:fake_cloud_firestore/fake_cloud_firestore.dart';
import 'package:flutter_controller/models/session_ownership.dart';
import 'package:flutter_controller/services/parent_progress_service.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('ParentProgressService', () {
    late FakeFirebaseFirestore firestore;

    setUp(() {
      firestore = FakeFirebaseFirestore();
      ParentProgressService.setFirestoreInstanceForTesting(firestore);
    });

    tearDown(() {
      ParentProgressService.clearTestingOverrides();
    });

    test('aggregates progress snapshot for owner context', () async {
      final ownerKey = SessionOwnership.ownerKey(
        therapistId: 'therapist-a',
        studentId: 'student-a',
      );

      await firestore.collection('therapy_sessions').doc('session-1').set(
            _sessionDoc(
              sessionId: 'session-1',
              studentId: 'student-a',
              therapistId: 'therapist-a',
              ownerKey: ownerKey,
              state: 'COMPLETED',
              latestGameId: 'demo_cube_clicker',
              updatedAtUnixMs: 3000,
            ),
          );
      await firestore.collection('therapy_sessions').doc('session-2').set(
            _sessionDoc(
              sessionId: 'session-2',
              studentId: 'student-a',
              therapistId: 'therapist-a',
              ownerKey: ownerKey,
              state: 'INTERRUPTED',
              latestGameId: 'pulse_target_tap',
              updatedAtUnixMs: 2000,
            ),
          );
      await firestore.collection('therapy_sessions').doc('session-3').set(
            _sessionDoc(
              sessionId: 'session-3',
              studentId: 'student-a',
              therapistId: 'therapist-other',
              ownerKey: SessionOwnership.ownerKey(
                therapistId: 'therapist-other',
                studentId: 'student-a',
              ),
              state: 'COMPLETED',
              latestGameId: 'demo_cube_clicker',
              updatedAtUnixMs: 4000,
            ),
          );

      final snapshot = await ParentProgressService.fetchSnapshot(
        studentId: 'student-a',
        therapistId: 'therapist-a',
      );

      expect(snapshot.totalSessions, 2);
      expect(snapshot.terminalSessions, 1);
      expect(snapshot.unfinishedSessions, 1);
      expect(snapshot.lastSessionId, 'session-1');
      expect(snapshot.lastGameId, 'demo_cube_clicker');
      expect(snapshot.lastState, 'COMPLETED');
      expect(snapshot.completionRate, 0.5);
    });

    test('returns empty snapshot for invalid actor ids', () async {
      final snapshot = await ParentProgressService.fetchSnapshot(
        studentId: '',
        therapistId: '  ',
      );

      expect(snapshot.totalSessions, 0);
      expect(snapshot.hasData, isFalse);
    });
  });
}

Map<String, dynamic> _sessionDoc({
  required String sessionId,
  required String studentId,
  required String therapistId,
  required String ownerKey,
  required String state,
  required String latestGameId,
  required int updatedAtUnixMs,
}) {
  return <String, dynamic>{
    'sessionId': sessionId,
    'studentId': studentId,
    'therapistId': therapistId,
    'ownerKey': ownerKey,
    'sessionKey': SessionOwnership.sessionKey(
      therapistId: therapistId,
      studentId: studentId,
      sessionId: sessionId,
    ),
    'state': state,
    'unfinished': state == 'INTERRUPTED',
    'isTerminal': state == 'COMPLETED',
    'latestGameId': latestGameId,
    'reasonCode': '',
    'updatedAtUtc': DateTime.fromMillisecondsSinceEpoch(
      updatedAtUnixMs,
      isUtc: true,
    ).toIso8601String(),
    'updatedAtUnixMs': updatedAtUnixMs,
    'metadata': <String, dynamic>{},
  };
}
