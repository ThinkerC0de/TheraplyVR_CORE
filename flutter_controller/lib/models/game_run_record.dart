class GameRunRecord {
  final String gameRunId;
  final String gameId;
  final DateTime? startedAtUtc;
  final int startedAtUnixMs;
  final DateTime? endedAtUtc;
  final int endedAtUnixMs;
  final String? finalState; // "completed" | "interrupted" | "failed" | null
  final int? durationSec;
  final int interactionCount;
  final int hitCount;
  final int missCount;
  final String? motionTraceUrl; // gs:// link — populated in future iteration

  const GameRunRecord({
    required this.gameRunId,
    required this.gameId,
    this.startedAtUtc,
    required this.startedAtUnixMs,
    this.endedAtUtc,
    required this.endedAtUnixMs,
    this.finalState,
    this.durationSec,
    required this.interactionCount,
    required this.hitCount,
    required this.missCount,
    this.motionTraceUrl,
  });

  factory GameRunRecord.fromFirestore(
    String id,
    Map<String, dynamic> json,
  ) {
    final summary = json['summary'] is Map
        ? Map<String, dynamic>.from(json['summary'] as Map)
        : <String, dynamic>{};

    return GameRunRecord(
      gameRunId: id,
      gameId: (json['gameId'] as String? ?? '').trim(),
      startedAtUtc: _parseDateTime(json['startedAtUtc']) ??
          _parseDateTimeFromUnixMs(json['startedAtUnixMs']),
      startedAtUnixMs: _asInt(json['startedAtUnixMs']),
      endedAtUtc:
          _parseDateTime(json['endedAtUtc']) ?? _parseDateTimeFromUnixMs(json['endedAtUnixMs']),
      endedAtUnixMs: _asInt(json['endedAtUnixMs']),
      finalState: json['finalState'] as String?,
      durationSec: _asInt(json['durationSec']),
      interactionCount: _asInt(summary['interactionCount']),
      hitCount: _asInt(summary['hitCount']),
      missCount: _asInt(summary['missCount']),
      motionTraceUrl: json['motionTraceUrl'] as String?,
    );
  }

  bool get isCompleted => finalState == 'completed';
  bool get isFailed => finalState == 'failed';
  bool get isInProgress => finalState == null || finalState!.isEmpty;
  bool get hasEnded => endedAtUnixMs > 0 || endedAtUtc != null;
  bool get hasAnySummary =>
      interactionCount > 0 || hitCount > 0 || missCount > 0;
  bool get isSummaryPlaceholder =>
      !hasEnded &&
      !hasAnySummary &&
      (finalState == null || finalState!.trim().isEmpty);

  DateTime? get sortAnchorUtc => startedAtUtc ?? endedAtUtc;

  int get sortAnchorUnixMs {
    if (startedAtUnixMs > 0) {
      return startedAtUnixMs;
    }
    if (endedAtUnixMs > 0) {
      return endedAtUnixMs;
    }
    final anchor = sortAnchorUtc;
    return anchor?.millisecondsSinceEpoch ?? 0;
  }

  static DateTime? _parseDateTime(dynamic value) {
    if (value is String && value.isNotEmpty) {
      return DateTime.tryParse(value)?.toUtc();
    }
    return null;
  }

  static DateTime? _parseDateTimeFromUnixMs(dynamic value) {
    final unixMs = _asInt(value);
    if (unixMs <= 0) {
      return null;
    }
    return DateTime.fromMillisecondsSinceEpoch(unixMs, isUtc: true);
  }

  static int _asInt(dynamic value) {
    if (value is int) return value;
    if (value is num) return value.toInt();
    if (value is String) return int.tryParse(value) ?? 0;
    return 0;
  }
}
