class ParentProgressSnapshot {
  final int totalSessions;
  final int terminalSessions;
  final int unfinishedSessions;
  final String lastSessionId;
  final String lastGameId;
  final String lastState;
  final DateTime? lastUpdatedAtUtc;

  const ParentProgressSnapshot({
    required this.totalSessions,
    required this.terminalSessions,
    required this.unfinishedSessions,
    required this.lastSessionId,
    required this.lastGameId,
    required this.lastState,
    required this.lastUpdatedAtUtc,
  });

  static const ParentProgressSnapshot empty = ParentProgressSnapshot(
    totalSessions: 0,
    terminalSessions: 0,
    unfinishedSessions: 0,
    lastSessionId: '',
    lastGameId: '',
    lastState: '',
    lastUpdatedAtUtc: null,
  );

  double get completionRate {
    if (totalSessions <= 0) {
      return 0;
    }
    return terminalSessions / totalSessions;
  }

  bool get hasData => totalSessions > 0;
}
