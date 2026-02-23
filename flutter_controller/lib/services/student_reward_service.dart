import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_controller/models/student_reward_unlock.dart';
import 'package:flutter_controller/services/firebase_service.dart';

class StudentRewardService {
  static const String _source = 'mobile_controller';
  static FirebaseFirestore? _firestoreOverride;
  static FirebaseFirestore get _firestore =>
      _firestoreOverride ?? FirebaseService.firestore;
  static CollectionReference<Map<String, dynamic>> get _rewardsCollection =>
      _firestore.collection('student_rewards');

  @visibleForTesting
  static void setFirestoreInstanceForTesting(FirebaseFirestore firestore) {
    _firestoreOverride = firestore;
  }

  @visibleForTesting
  static void clearTestingOverrides() {
    _firestoreOverride = null;
  }

  static Future<StudentRewardUnlockResult?> unlockForCompletedSession({
    required String studentId,
    required String therapistId,
    required String sessionId,
    required String gameId,
    String reasonCode = 'THERAPIST_CONFIRMED_END',
  }) async {
    final normalizedStudentId = studentId.trim();
    final normalizedTherapistId = therapistId.trim();
    final normalizedSessionId = sessionId.trim();
    final normalizedGameId = gameId.trim();
    final normalizedReason = reasonCode.trim().isEmpty
        ? 'THERAPIST_CONFIRMED_END'
        : reasonCode.trim();
    if (normalizedStudentId.isEmpty ||
        normalizedTherapistId.isEmpty ||
        normalizedSessionId.isEmpty ||
        normalizedGameId.isEmpty) {
      return null;
    }

    final rewardCode = _rewardCodeForGame(normalizedGameId);
    final rewardTitle = _rewardTitleForGame(normalizedGameId);
    final unlockId = _buildUnlockId(
      studentId: normalizedStudentId,
      sessionId: normalizedSessionId,
      rewardCode: rewardCode,
    );

    final docRef = _rewardsCollection.doc(unlockId);
    final existingSnapshot = await docRef.get();
    if (existingSnapshot.exists) {
      final existingData = existingSnapshot.data() ?? <String, dynamic>{};
      return StudentRewardUnlockResult(
        reward: StudentRewardUnlock.fromFirestore(unlockId, existingData),
        unlockedNow: false,
      );
    }

    final nowUtc = DateTime.now().toUtc();
    final reward = StudentRewardUnlock(
      unlockId: unlockId,
      rewardCode: rewardCode,
      rewardTitle: rewardTitle,
      studentId: normalizedStudentId,
      therapistId: normalizedTherapistId,
      sessionId: normalizedSessionId,
      gameId: normalizedGameId,
      reasonCode: normalizedReason,
      source: _source,
      unlockedAtUtc: nowUtc,
      unlockedAtUnixMs: nowUtc.millisecondsSinceEpoch,
    );
    await docRef.set(reward.toFirestore());

    return StudentRewardUnlockResult(
      reward: reward,
      unlockedNow: true,
    );
  }

  static Future<List<StudentRewardUnlock>> fetchRecentRewardsForStudent({
    required String studentId,
    int limit = 8,
  }) async {
    final normalizedStudentId = studentId.trim();
    if (normalizedStudentId.isEmpty) {
      return const <StudentRewardUnlock>[];
    }

    final resolvedLimit = limit <= 0 ? 1 : limit;
    QuerySnapshot<Map<String, dynamic>> snapshot;
    try {
      snapshot = await _rewardsCollection
          .where('studentId', isEqualTo: normalizedStudentId)
          .orderBy('unlockedAtUnixMs', descending: true)
          .limit(resolvedLimit)
          .get();
    } catch (_) {
      snapshot = await _rewardsCollection
          .where('studentId', isEqualTo: normalizedStudentId)
          .limit(resolvedLimit * 4)
          .get();
    }

    final rewards = snapshot.docs
        .map((doc) => StudentRewardUnlock.fromFirestore(doc.id, doc.data()))
        .toList();
    rewards.sort(
      (a, b) => b.unlockedAtUnixMs.compareTo(a.unlockedAtUnixMs),
    );

    if (rewards.length > resolvedLimit) {
      return rewards.sublist(0, resolvedLimit);
    }
    return rewards;
  }

  static String _rewardCodeForGame(String gameId) {
    switch (gameId) {
      case 'demo_cube_clicker':
        return 'VR_REWARD_CUBE_FINISHER';
      case 'pulse_target_tap':
        return 'VR_REWARD_PULSE_FINISHER';
      default:
        return 'VR_REWARD_SESSION_FINISHER';
    }
  }

  static String _rewardTitleForGame(String gameId) {
    switch (gameId) {
      case 'demo_cube_clicker':
        return 'Cube Finisher';
      case 'pulse_target_tap':
        return 'Pulse Finisher';
      default:
        return 'VR Session Finisher';
    }
  }

  static String _buildUnlockId({
    required String studentId,
    required String sessionId,
    required String rewardCode,
  }) {
    final raw = '${studentId}_${sessionId}_$rewardCode';
    return raw.replaceAll(RegExp(r'[^A-Za-z0-9_-]'), '_');
  }
}
