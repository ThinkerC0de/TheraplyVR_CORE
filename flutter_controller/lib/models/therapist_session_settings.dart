enum TherapistUiLanguage {
  english('en'),
  polish('pl');

  const TherapistUiLanguage(this.wireValue);

  final String wireValue;

  static TherapistUiLanguage fromWireValue(String? value) {
    final normalized = value?.trim().toLowerCase() ?? '';
    switch (normalized) {
      case 'pl':
        return TherapistUiLanguage.polish;
      case 'en':
        return TherapistUiLanguage.english;
      default:
        return TherapistUiLanguage.english;
    }
  }
}

class TherapistSessionSettings {
  static const int defaultSessionRecoveryWindowMinutes = 60;
  static const int minSessionRecoveryWindowMinutes = 5;
  static const int maxSessionRecoveryWindowMinutes = 240;

  static const int defaultInterruptedSessionAutoCloseHours = 48;
  static const int minInterruptedSessionAutoCloseHours = 1;
  static const int maxInterruptedSessionAutoCloseHours = 168;

  static const bool defaultAutoCloseInterruptedSessionsEnabled = true;
  static const bool defaultRequireResumeConfirmationAfterRecoveryWindow = true;

  static const int defaultCriticalCommandMaxRetries = 3;
  static const int minCriticalCommandMaxRetries = 1;
  static const int maxCriticalCommandMaxRetries = 8;

  static const int defaultCriticalCommandAckTimeoutMs = 3000;
  static const int minCriticalCommandAckTimeoutMs = 500;
  static const int maxCriticalCommandAckTimeoutMs = 15000;

  static const bool defaultAdaptiveDifficultyEnabled = true;
  static const double defaultAdaptiveDifficultySensitivity = 0.55;
  static const double minAdaptiveDifficultySensitivity = 0.0;
  static const double maxAdaptiveDifficultySensitivity = 1.0;

  static const bool defaultLabelPipelineEnabled = true;
  static const bool defaultKeepScreenAwakeWhenForeground = false;
  static const TherapistUiLanguage defaultOperatorUiLanguage =
      TherapistUiLanguage.polish;

  final int sessionRecoveryWindowMinutes;
  final int interruptedSessionAutoCloseHours;
  final bool autoCloseInterruptedSessionsEnabled;
  final bool requireResumeConfirmationAfterRecoveryWindow;
  final int criticalCommandMaxRetries;
  final int criticalCommandAckTimeoutMs;
  final bool adaptiveDifficultyEnabled;
  final double adaptiveDifficultySensitivity;
  final bool labelPipelineEnabled;
  final bool keepScreenAwakeWhenForeground;
  final TherapistUiLanguage operatorUiLanguage;
  final List<String> timelineQuickNoteTemplates;

  const TherapistSessionSettings({
    required this.sessionRecoveryWindowMinutes,
    required this.interruptedSessionAutoCloseHours,
    required this.autoCloseInterruptedSessionsEnabled,
    required this.requireResumeConfirmationAfterRecoveryWindow,
    required this.criticalCommandMaxRetries,
    required this.criticalCommandAckTimeoutMs,
    required this.adaptiveDifficultyEnabled,
    required this.adaptiveDifficultySensitivity,
    required this.labelPipelineEnabled,
    required this.keepScreenAwakeWhenForeground,
    required this.operatorUiLanguage,
    required this.timelineQuickNoteTemplates,
  });

  factory TherapistSessionSettings.defaults() {
    return const TherapistSessionSettings(
      sessionRecoveryWindowMinutes: defaultSessionRecoveryWindowMinutes,
      interruptedSessionAutoCloseHours: defaultInterruptedSessionAutoCloseHours,
      autoCloseInterruptedSessionsEnabled:
          defaultAutoCloseInterruptedSessionsEnabled,
      requireResumeConfirmationAfterRecoveryWindow:
          defaultRequireResumeConfirmationAfterRecoveryWindow,
      criticalCommandMaxRetries: defaultCriticalCommandMaxRetries,
      criticalCommandAckTimeoutMs: defaultCriticalCommandAckTimeoutMs,
      adaptiveDifficultyEnabled: defaultAdaptiveDifficultyEnabled,
      adaptiveDifficultySensitivity: defaultAdaptiveDifficultySensitivity,
      labelPipelineEnabled: defaultLabelPipelineEnabled,
      keepScreenAwakeWhenForeground: defaultKeepScreenAwakeWhenForeground,
      operatorUiLanguage: defaultOperatorUiLanguage,
      timelineQuickNoteTemplates: <String>[
        'Need short break',
        'Reduced focus',
        'Helped with instruction',
      ],
    );
  }

  factory TherapistSessionSettings.fromMap(Map<String, dynamic>? data) {
    final source = data ?? const <String, dynamic>{};
    final defaults = TherapistSessionSettings.defaults();

    return TherapistSessionSettings(
      sessionRecoveryWindowMinutes: _clampInt(
        source['sessionRecoveryWindowMinutes'],
        min: minSessionRecoveryWindowMinutes,
        max: maxSessionRecoveryWindowMinutes,
        fallback: defaults.sessionRecoveryWindowMinutes,
      ),
      interruptedSessionAutoCloseHours: _clampInt(
        source['interruptedSessionAutoCloseHours'],
        min: minInterruptedSessionAutoCloseHours,
        max: maxInterruptedSessionAutoCloseHours,
        fallback: defaults.interruptedSessionAutoCloseHours,
      ),
      autoCloseInterruptedSessionsEnabled: _asBool(
        source['autoCloseInterruptedSessionsEnabled'],
        fallback: defaults.autoCloseInterruptedSessionsEnabled,
      ),
      requireResumeConfirmationAfterRecoveryWindow: _asBool(
        source['requireResumeConfirmationAfterRecoveryWindow'],
        fallback: defaults.requireResumeConfirmationAfterRecoveryWindow,
      ),
      criticalCommandMaxRetries: _clampInt(
        source['criticalCommandMaxRetries'],
        min: minCriticalCommandMaxRetries,
        max: maxCriticalCommandMaxRetries,
        fallback: defaults.criticalCommandMaxRetries,
      ),
      criticalCommandAckTimeoutMs: _clampInt(
        source['criticalCommandAckTimeoutMs'],
        min: minCriticalCommandAckTimeoutMs,
        max: maxCriticalCommandAckTimeoutMs,
        fallback: defaults.criticalCommandAckTimeoutMs,
      ),
      adaptiveDifficultyEnabled: _asBool(
        source['adaptiveDifficultyEnabled'],
        fallback: defaults.adaptiveDifficultyEnabled,
      ),
      adaptiveDifficultySensitivity: _clampDouble(
        source['adaptiveDifficultySensitivity'],
        min: minAdaptiveDifficultySensitivity,
        max: maxAdaptiveDifficultySensitivity,
        fallback: defaults.adaptiveDifficultySensitivity,
      ),
      labelPipelineEnabled: _asBool(
        source['labelPipelineEnabled'],
        fallback: defaults.labelPipelineEnabled,
      ),
      keepScreenAwakeWhenForeground: _asBool(
        source['keepScreenAwakeWhenForeground'],
        fallback: defaults.keepScreenAwakeWhenForeground,
      ),
      operatorUiLanguage: _asOperatorUiLanguage(
        source['operatorUiLanguage'],
        fallback: defaults.operatorUiLanguage,
      ),
      timelineQuickNoteTemplates: _asStringList(
        source['timelineQuickNoteTemplates'],
        fallback: defaults.timelineQuickNoteTemplates,
      ),
    );
  }

  TherapistSessionSettings copyWith({
    int? sessionRecoveryWindowMinutes,
    int? interruptedSessionAutoCloseHours,
    bool? autoCloseInterruptedSessionsEnabled,
    bool? requireResumeConfirmationAfterRecoveryWindow,
    int? criticalCommandMaxRetries,
    int? criticalCommandAckTimeoutMs,
    bool? adaptiveDifficultyEnabled,
    double? adaptiveDifficultySensitivity,
    bool? labelPipelineEnabled,
    bool? keepScreenAwakeWhenForeground,
    TherapistUiLanguage? operatorUiLanguage,
    List<String>? timelineQuickNoteTemplates,
  }) {
    return TherapistSessionSettings(
      sessionRecoveryWindowMinutes:
          sessionRecoveryWindowMinutes ?? this.sessionRecoveryWindowMinutes,
      interruptedSessionAutoCloseHours: interruptedSessionAutoCloseHours ??
          this.interruptedSessionAutoCloseHours,
      autoCloseInterruptedSessionsEnabled:
          autoCloseInterruptedSessionsEnabled ??
              this.autoCloseInterruptedSessionsEnabled,
      requireResumeConfirmationAfterRecoveryWindow:
          requireResumeConfirmationAfterRecoveryWindow ??
              this.requireResumeConfirmationAfterRecoveryWindow,
      criticalCommandMaxRetries:
          criticalCommandMaxRetries ?? this.criticalCommandMaxRetries,
      criticalCommandAckTimeoutMs:
          criticalCommandAckTimeoutMs ?? this.criticalCommandAckTimeoutMs,
      adaptiveDifficultyEnabled:
          adaptiveDifficultyEnabled ?? this.adaptiveDifficultyEnabled,
      adaptiveDifficultySensitivity:
          adaptiveDifficultySensitivity ?? this.adaptiveDifficultySensitivity,
      labelPipelineEnabled: labelPipelineEnabled ?? this.labelPipelineEnabled,
      keepScreenAwakeWhenForeground:
          keepScreenAwakeWhenForeground ?? this.keepScreenAwakeWhenForeground,
      operatorUiLanguage: operatorUiLanguage ?? this.operatorUiLanguage,
      timelineQuickNoteTemplates: List<String>.from(
        timelineQuickNoteTemplates ?? this.timelineQuickNoteTemplates,
      ),
    );
  }

  Map<String, dynamic> toMap() {
    return <String, dynamic>{
      'sessionRecoveryWindowMinutes': sessionRecoveryWindowMinutes,
      'interruptedSessionAutoCloseHours': interruptedSessionAutoCloseHours,
      'autoCloseInterruptedSessionsEnabled':
          autoCloseInterruptedSessionsEnabled,
      'requireResumeConfirmationAfterRecoveryWindow':
          requireResumeConfirmationAfterRecoveryWindow,
      'criticalCommandMaxRetries': criticalCommandMaxRetries,
      'criticalCommandAckTimeoutMs': criticalCommandAckTimeoutMs,
      'adaptiveDifficultyEnabled': adaptiveDifficultyEnabled,
      'adaptiveDifficultySensitivity': adaptiveDifficultySensitivity,
      'labelPipelineEnabled': labelPipelineEnabled,
      'keepScreenAwakeWhenForeground': keepScreenAwakeWhenForeground,
      'operatorUiLanguage': operatorUiLanguage.wireValue,
      'timelineQuickNoteTemplates':
          List<String>.from(timelineQuickNoteTemplates),
    };
  }

  static int _clampInt(
    dynamic value, {
    required int min,
    required int max,
    required int fallback,
  }) {
    var resolved = fallback;
    if (value is int) {
      resolved = value;
    } else if (value is num) {
      resolved = value.toInt();
    } else if (value is String) {
      resolved = int.tryParse(value) ?? fallback;
    }

    if (resolved < min) {
      return min;
    }
    if (resolved > max) {
      return max;
    }
    return resolved;
  }

  static bool _asBool(dynamic value, {required bool fallback}) {
    if (value is bool) {
      return value;
    }
    if (value is num) {
      return value != 0;
    }
    if (value is String) {
      final normalized = value.trim().toLowerCase();
      if (normalized == 'true' || normalized == '1' || normalized == 'yes') {
        return true;
      }
      if (normalized == 'false' || normalized == '0' || normalized == 'no') {
        return false;
      }
    }
    return fallback;
  }

  static double _clampDouble(
    dynamic value, {
    required double min,
    required double max,
    required double fallback,
  }) {
    var resolved = fallback;
    if (value is double) {
      resolved = value;
    } else if (value is num) {
      resolved = value.toDouble();
    } else if (value is String) {
      resolved = double.tryParse(value) ?? fallback;
    }

    if (resolved < min) {
      return min;
    }
    if (resolved > max) {
      return max;
    }
    return resolved;
  }

  static List<String> _asStringList(
    dynamic value, {
    required List<String> fallback,
  }) {
    if (value is List) {
      final list = value
          .map((entry) => entry == null ? '' : entry.toString().trim())
          .where((entry) => entry.isNotEmpty)
          .toList(growable: false);
      if (list.isNotEmpty) {
        return list;
      }
    }

    return List<String>.from(fallback);
  }

  static TherapistUiLanguage _asOperatorUiLanguage(
    dynamic value, {
    required TherapistUiLanguage fallback,
  }) {
    if (value is TherapistUiLanguage) {
      return value;
    }
    if (value is String) {
      final normalized = value.trim().toLowerCase();
      if (normalized == 'pl') {
        return TherapistUiLanguage.polish;
      }
      if (normalized == 'en') {
        return TherapistUiLanguage.english;
      }
    }
    return fallback;
  }
}
