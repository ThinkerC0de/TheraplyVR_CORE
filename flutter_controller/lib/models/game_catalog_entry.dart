class GameCatalogEntry {
  final String gameId;
  final String title;
  final String description;
  final String targetContentVersion;
  final String packageUri;
  final String thumbnailUrl;
  final bool supportsSaveResume;
  final bool availableForPurchase;
  final bool requiresExplicitLicense;
  final bool runtimeLaunchEnabled;
  final int sortOrder;
  final bool active;
  final List<String> previewLines;

  const GameCatalogEntry({
    required this.gameId,
    required this.title,
    required this.description,
    required this.targetContentVersion,
    required this.packageUri,
    required this.thumbnailUrl,
    required this.supportsSaveResume,
    required this.availableForPurchase,
    required this.requiresExplicitLicense,
    required this.runtimeLaunchEnabled,
    required this.sortOrder,
    required this.active,
    required this.previewLines,
  });

  factory GameCatalogEntry.fromMap(Map<String, dynamic> data) {
    final rawGameId = (data['gameId'] as String? ?? '').trim();
    return GameCatalogEntry(
      gameId: rawGameId,
      title: (data['title'] as String? ?? rawGameId).trim(),
      description: (data['description'] as String? ?? '').trim(),
      targetContentVersion:
          (data['targetContentVersion'] as String? ?? '1.0.0').trim(),
      packageUri: (data['packageUri'] as String? ?? '').trim(),
      thumbnailUrl: (data['thumbnailUrl'] as String? ?? '').trim(),
      supportsSaveResume: data['supportsSaveResume'] as bool? ?? false,
      availableForPurchase: data['availableForPurchase'] as bool? ?? true,
      requiresExplicitLicense:
          data['requiresExplicitLicense'] as bool? ?? false,
      runtimeLaunchEnabled: data['runtimeLaunchEnabled'] as bool? ?? false,
      sortOrder: _readInt(data['sortOrder'], 0),
      active: data['active'] as bool? ?? true,
      previewLines: _parsePreviewLines(data['previewLines']),
    );
  }

  Map<String, dynamic> toMap() {
    return <String, dynamic>{
      'gameId': gameId,
      'title': title,
      'description': description,
      'targetContentVersion': targetContentVersion,
      'packageUri': packageUri,
      'thumbnailUrl': thumbnailUrl,
      'supportsSaveResume': supportsSaveResume,
      'availableForPurchase': availableForPurchase,
      'requiresExplicitLicense': requiresExplicitLicense,
      'runtimeLaunchEnabled': runtimeLaunchEnabled,
      'sortOrder': sortOrder,
      'active': active,
      'previewLines': List<String>.from(previewLines),
    };
  }

  static List<String> _parsePreviewLines(dynamic value) {
    if (value is! List<dynamic>) {
      return const <String>[];
    }

    final parsed = <String>[];
    for (final item in value) {
      if (item is String && item.trim().isNotEmpty) {
        parsed.add(item.trim());
      }
    }
    return parsed;
  }

  static int _readInt(dynamic value, int fallback) {
    if (value is int) {
      return value;
    }
    if (value is num) {
      return value.toInt();
    }
    if (value is String) {
      final parsed = int.tryParse(value.trim());
      if (parsed != null) {
        return parsed;
      }
    }
    return fallback;
  }
}
