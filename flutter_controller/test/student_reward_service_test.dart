import 'package:fake_cloud_firestore/fake_cloud_firestore.dart';
import 'package:flutter_controller/services/student_reward_service.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('StudentRewardService', () {
    late FakeFirebaseFirestore firestore;

    setUp(() {
      firestore = FakeFirebaseFirestore();
      StudentRewardService.setFirestoreInstanceForTesting(firestore);
    });

    tearDown(() {
      StudentRewardService.clearTestingOverrides();
    });

    test('creates idempotent reward unlock per session', () async {
      final first = await StudentRewardService.unlockForCompletedSession(
        studentId: 'student-a',
        therapistId: 'therapist-a',
        sessionId: 'session-a',
        gameId: 'demo_cube_clicker',
      );
      final second = await StudentRewardService.unlockForCompletedSession(
        studentId: 'student-a',
        therapistId: 'therapist-a',
        sessionId: 'session-a',
        gameId: 'demo_cube_clicker',
      );

      expect(first, isNotNull);
      expect(first!.unlockedNow, isTrue);
      expect(second, isNotNull);
      expect(second!.unlockedNow, isFalse);
      expect(second.reward.unlockId, first.reward.unlockId);

      final snapshot = await firestore.collection('student_rewards').get();
      expect(snapshot.docs, hasLength(1));
    });

    test('fetchRecentRewardsForStudent returns latest rewards first', () async {
      await StudentRewardService.unlockForCompletedSession(
        studentId: 'student-b',
        therapistId: 'therapist-b',
        sessionId: 'session-1',
        gameId: 'demo_cube_clicker',
      );
      await Future<void>.delayed(const Duration(milliseconds: 2));
      await StudentRewardService.unlockForCompletedSession(
        studentId: 'student-b',
        therapistId: 'therapist-b',
        sessionId: 'session-2',
        gameId: 'pulse_target_tap',
      );

      final rewards = await StudentRewardService.fetchRecentRewardsForStudent(
        studentId: 'student-b',
      );

      expect(rewards, hasLength(2));
      expect(rewards.first.sessionId, 'session-2');
      expect(rewards.last.sessionId, 'session-1');
    });

    test('returns null for invalid unlock request', () async {
      final result = await StudentRewardService.unlockForCompletedSession(
        studentId: '',
        therapistId: 'therapist-a',
        sessionId: '',
        gameId: '',
      );

      expect(result, isNull);
    });
  });
}
