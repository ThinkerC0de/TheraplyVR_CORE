import 'package:flutter_controller/models/therapy_session_record.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('TherapySessionRecord', () {
    test('requires handoff for non-terminal in-progress state', () {
      final record = TherapySessionRecord.fromFirestore(<String, dynamic>{
        'sessionId': 'session-1',
        'studentId': 'student-1',
        'therapistId': 'therapist-1',
        'state': 'IN_PROGRESS',
        'unfinished': true,
      });

      expect(record.isTerminal, isFalse);
      expect(record.requiresHandoffDecision, isTrue);
    });

    test('does not require handoff for created state', () {
      final record = TherapySessionRecord.fromFirestore(<String, dynamic>{
        'sessionId': 'session-2',
        'studentId': 'student-1',
        'therapistId': 'therapist-1',
        'state': 'CREATED',
        'unfinished': false,
      });

      expect(record.isTerminal, isFalse);
      expect(record.requiresHandoffDecision, isFalse);
    });

    test('does not require handoff for terminal state', () {
      final record = TherapySessionRecord.fromFirestore(<String, dynamic>{
        'sessionId': 'session-3',
        'studentId': 'student-1',
        'therapistId': 'therapist-1',
        'state': 'COMPLETED',
      });

      expect(record.isTerminal, isTrue);
      expect(record.requiresHandoffDecision, isFalse);
    });

    test('falls back to unfinished flag when state is unknown', () {
      final record = TherapySessionRecord.fromFirestore(<String, dynamic>{
        'sessionId': 'session-4',
        'studentId': 'student-1',
        'therapistId': 'therapist-1',
        'state': 'UNKNOWN_STATE',
        'unfinished': true,
      });

      expect(record.isTerminal, isFalse);
      expect(record.requiresHandoffDecision, isTrue);
    });
  });
}
