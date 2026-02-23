import 'package:flutter_controller/models/session_ownership.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('SessionOwnership', () {
    test('builds owner and session keys from therapist, student and session',
        () {
      final ownerKey = SessionOwnership.ownerKey(
        therapistId: 'therapist-1',
        studentId: 'student-1',
      );
      final sessionKey = SessionOwnership.sessionKey(
        therapistId: 'therapist-1',
        studentId: 'student-1',
        sessionId: 'session-1',
      );

      expect(ownerKey, 'therapist-1|student-1');
      expect(sessionKey, 'therapist-1|student-1|session-1');
    });

    test('matchesOwner validates equivalent ownership tuples', () {
      final accepted = SessionOwnership.matchesOwner(
        expectedTherapistId: 'therapist-1',
        expectedStudentId: 'student-1',
        incomingTherapistId: 'therapist-1',
        incomingStudentId: 'student-1',
      );
      final rejected = SessionOwnership.matchesOwner(
        expectedTherapistId: 'therapist-1',
        expectedStudentId: 'student-1',
        incomingTherapistId: 'therapist-2',
        incomingStudentId: 'student-1',
      );

      expect(accepted, isTrue);
      expect(rejected, isFalse);
    });

    test(
        'resolveStudentId prefers explicit studentId and falls back to patientId',
        () {
      final fromStudent = SessionOwnership.resolveStudentId(
        studentId: 'student-7',
        patientId: 'patient-legacy',
      );
      final fromPatient = SessionOwnership.resolveStudentId(
        studentId: '',
        patientId: 'patient-legacy',
      );

      expect(fromStudent, 'student-7');
      expect(fromPatient, 'patient-legacy');
    });
  });
}
