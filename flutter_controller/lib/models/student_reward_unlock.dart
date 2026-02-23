class StudentRewardUnlock {
  final String unlockId;
  final String rewardCode;
  final String rewardTitle;
  final String studentId;
  final String therapistId;
  final String sessionId;
  final String gameId;
  final String reasonCode;
  final String source;
  final DateTime unlockedAtUtc;
  final int unlockedAtUnixMs;

  const StudentRewardUnlock({
    required this.unlockId,
    required this.rewardCode,
    required this.rewardTitle,
    required this.studentId,
    required this.therapistId,
    required this.sessionId,
    required this.gameId,
    required this.reasonCode,
    required this.source,
    required this.unlockedAtUtc,
    required this.unlockedAtUnixMs,
  });

  factory StudentRewardUnlock.fromFirestore(
    String unlockId,
    Map<String, dynamic> json,
  ) {
    final unlockedAtRaw = (json['unlockedAtUtc'] as String? ?? '').trim();
    final unlockedAtUtc = DateTime.tryParse(unlockedAtRaw)?.toUtc() ??
        DateTime.fromMillisecondsSinceEpoch(
          _asInt(json['unlockedAtUnixMs']),
          isUtc: true,
        );

    return StudentRewardUnlock(
      unlockId: unlockId.trim(),
      rewardCode: (json['rewardCode'] as String? ?? '').trim(),
      rewardTitle: (json['rewardTitle'] as String? ?? '').trim(),
      studentId: (json['studentId'] as String? ?? '').trim(),
      therapistId: (json['therapistId'] as String? ?? '').trim(),
      sessionId: (json['sessionId'] as String? ?? '').trim(),
      gameId: (json['gameId'] as String? ?? '').trim(),
      reasonCode: (json['reasonCode'] as String? ?? '').trim(),
      source: (json['source'] as String? ?? '').trim(),
      unlockedAtUtc: unlockedAtUtc,
      unlockedAtUnixMs: _asInt(json['unlockedAtUnixMs']),
    );
  }

  Map<String, dynamic> toFirestore() {
    return <String, dynamic>{
      'unlockId': unlockId,
      'rewardCode': rewardCode,
      'rewardTitle': rewardTitle,
      'studentId': studentId,
      'therapistId': therapistId,
      'sessionId': sessionId,
      'gameId': gameId,
      'reasonCode': reasonCode,
      'source': source,
      'unlockedAtUtc': unlockedAtUtc.toIso8601String(),
      'unlockedAtUnixMs': unlockedAtUnixMs,
    };
  }

  static int _asInt(dynamic value) {
    if (value is int) {
      return value;
    }
    if (value is num) {
      return value.toInt();
    }
    if (value is String) {
      return int.tryParse(value.trim()) ?? 0;
    }
    return 0;
  }
}

class StudentRewardUnlockResult {
  final StudentRewardUnlock reward;
  final bool unlockedNow;

  const StudentRewardUnlockResult({
    required this.reward,
    required this.unlockedNow,
  });
}
