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

    test('derives owner and session keys when record does not store them', () {
      final record = TherapySessionRecord.fromFirestore(<String, dynamic>{
        'sessionId': 'session-5',
        'studentId': 'student-9',
        'therapistId': 'therapist-4',
        'state': 'CREATED',
      });

      expect(record.ownerKey, 'therapist-4|student-9');
      expect(record.sessionKey, 'therapist-4|student-9|session-5');
    });

    test('reads interruptedAtUtc when explicit field is present', () {
      final interruptedAtUtc = DateTime.utc(2026, 2, 22, 1, 2, 3);
      final record = TherapySessionRecord.fromFirestore(<String, dynamic>{
        'sessionId': 'session-6',
        'studentId': 'student-1',
        'therapistId': 'therapist-1',
        'state': 'INTERRUPTED',
        'interruptedAtUtc': interruptedAtUtc.toIso8601String(),
      });

      expect(record.interruptedAtUtc, interruptedAtUtc);
    });

    test('falls back to updatedAtUtc for interrupted legacy record', () {
      final updatedAtUtc = DateTime.utc(2026, 2, 22, 5, 0, 0);
      final record = TherapySessionRecord.fromFirestore(<String, dynamic>{
        'sessionId': 'session-7',
        'studentId': 'student-1',
        'therapistId': 'therapist-1',
        'state': 'INTERRUPTED',
        'updatedAtUtc': updatedAtUtc.toIso8601String(),
      });

      expect(record.interruptedAtUtc, updatedAtUtc);
    });
  });
}
