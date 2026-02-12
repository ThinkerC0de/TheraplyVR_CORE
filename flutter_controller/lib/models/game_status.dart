class GameStatus {
  final String gameId;
  final String state;
  final int score;
  final double timeElapsed;
  
  GameStatus({
    required this.gameId,
    required this.state,
    required this.score,
    required this.timeElapsed,
  });
  
  factory GameStatus.fromJson(Map<String, dynamic> json) {
    return GameStatus(
      gameId: json['gameId'] as String? ?? '',
      state: json['state'] as String? ?? 'unknown',
      score: json['score'] as int? ?? 0,
      timeElapsed: (json['timeElapsed'] as num?)?.toDouble() ?? 0.0,
    );
  }
  
  Map<String, dynamic> toJson() {
    return {
      'gameId': gameId,
      'state': state,
      'score': score,
      'timeElapsed': timeElapsed,
    };
  }
}
