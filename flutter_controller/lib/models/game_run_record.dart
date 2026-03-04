class GameRunRecord {
  final String gameRunId;
  final String gameId;
  final DateTime? startedAtUtc;
  final DateTime? endedAtUtc;
  final String? finalState; // "completed" | "failed" | null
  final int? durationSec;
  final int interactionCount;
  final int hitCount;
  final int missCount;

  const GameRunRecord({
    required this.gameRunId,
    required this.gameId,
    this.startedAtUtc,
    this.endedAtUtc,
    this.finalState,
    this.durationSec,
    required this.interactionCount,
    required this.hitCount,
    required this.missCount,
  });

  factory GameRunRecord.fromFirestore(
    String id,
    Map<String, dynamic> json,
  ) {
    final summary = json['summary'] is Map<String, dynamic>
        ? json['summary'] as Map<String, dynamic>
        : <String, dynamic>{};

    return GameRunRecord(
      gameRunId: id,
      gameId: (json['gameId'] as String? ?? '').trim(),
      startedAtUtc: _parseDateTime(json['startedAtUtc']),
      endedAtUtc: _parseDateTime(json['endedAtUtc']),
      finalState: json['finalState'] as String?,
      durationSec: _asInt(json['durationSec']),
      interactionCount: _asInt(summary['interactionCount']),
      hitCount: _asInt(summary['hitCount']),
      missCount: _asInt(summary['missCount']),
    );
  }

  bool get isCompleted => finalState == 'completed';
  bool get isFailed => finalState == 'failed';
  bool get isInProgress => finalState == null || finalState!.isEmpty;

  static DateTime? _parseDateTime(dynamic value) {
    if (value is String && value.isNotEmpty) {
      return DateTime.tryParse(value)?.toUtc();
    }
    return null;
  }

  static int _asInt(dynamic value) {
    if (value is int) return value;
    if (value is num) return value.toInt();
    if (value is String) return int.tryParse(value) ?? 0;
    return 0;
  }
}
