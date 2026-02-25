import 'dart:convert';

enum GuidedSessionContinuationPolicy {
  manual,
  resumeUnderRecoveryWindow,
  resumeAlways,
}

extension GuidedSessionContinuationPolicyCodec on GuidedSessionContinuationPolicy {
  static GuidedSessionContinuationPolicy fromWire(String? wireValue) {
    switch (wireValue?.trim().toLowerCase()) {
      case 'resume_under_recovery_window':
        return GuidedSessionContinuationPolicy.resumeUnderRecoveryWindow;
      case 'resume_always':
        return GuidedSessionContinuationPolicy.resumeAlways;
      case 'manual':
      default:
        return GuidedSessionContinuationPolicy.manual;
    }
  }

  String get wireValue {
    switch (this) {
      case GuidedSessionContinuationPolicy.manual:
        return 'manual';
      case GuidedSessionContinuationPolicy.resumeUnderRecoveryWindow:
        return 'resume_under_recovery_window';
      case GuidedSessionContinuationPolicy.resumeAlways:
        return 'resume_always';
    }
  }
}

class GuidedSessionPlanStep {
  final String stepId;
  final String gameId;
  final String displayName;
  final Map<String, dynamic> configPreset;

  const GuidedSessionPlanStep({
    required this.stepId,
    required this.gameId,
    required this.displayName,
    required this.configPreset,
  });

  factory GuidedSessionPlanStep.fromMap(Map<String, dynamic>? data) {
    final source = data ?? const <String, dynamic>{};
    final gameId = (source['gameId'] as String? ?? '').trim();
    final stepIdRaw = (source['stepId'] as String? ?? '').trim();
    final displayNameRaw = (source['displayName'] as String? ?? '').trim();
    final configPreset = source['configPreset'] is Map<String, dynamic>
        ? Map<String, dynamic>.from(
            source['configPreset'] as Map<String, dynamic>,
          )
        : <String, dynamic>{};

    return GuidedSessionPlanStep(
      stepId: stepIdRaw.isNotEmpty ? stepIdRaw : gameId,
      gameId: gameId,
      displayName: displayNameRaw.isNotEmpty ? displayNameRaw : gameId,
      configPreset: configPreset,
    );
  }

  Map<String, dynamic> toMap() {
    return <String, dynamic>{
      'stepId': stepId,
      'gameId': gameId,
      'displayName': displayName,
      'configPreset': configPreset,
    };
  }
}

class GuidedSessionPlan {
  final String planId;
  final String displayName;
  final bool enabled;
  final GuidedSessionContinuationPolicy continuationPolicy;
  final List<GuidedSessionPlanStep> steps;

  const GuidedSessionPlan({
    required this.planId,
    required this.displayName,
    required this.enabled,
    required this.continuationPolicy,
    required this.steps,
  });

  bool get hasSteps => steps.isNotEmpty;

  factory GuidedSessionPlan.fromMap(Map<String, dynamic>? data) {
    final source = data ?? const <String, dynamic>{};
    final planIdRaw = (source['planId'] as String? ?? '').trim();
    final displayNameRaw = (source['displayName'] as String? ?? '').trim();
    final rawSteps = source['steps'];
    final steps = <GuidedSessionPlanStep>[];
    if (rawSteps is List) {
      for (var i = 0; i < rawSteps.length; i++) {
        final raw = rawSteps[i];
        if (raw is Map<String, dynamic>) {
          final step = GuidedSessionPlanStep.fromMap(raw);
          if (step.gameId.trim().isNotEmpty) {
            steps.add(step);
          }
          continue;
        }
        if (raw is Map) {
          final step = GuidedSessionPlanStep.fromMap(
            Map<String, dynamic>.from(raw),
          );
          if (step.gameId.trim().isNotEmpty) {
            steps.add(step);
          }
        }
      }
    }

    final resolvedPlanId = planIdRaw.isNotEmpty ? planIdRaw : 'default_plan';
    return GuidedSessionPlan(
      planId: resolvedPlanId,
      displayName: displayNameRaw.isNotEmpty ? displayNameRaw : resolvedPlanId,
      enabled: _asBool(source['enabled'], fallback: true),
      continuationPolicy: GuidedSessionContinuationPolicyCodec.fromWire(
        source['continuationPolicy'] as String?,
      ),
      steps: List<GuidedSessionPlanStep>.unmodifiable(steps),
    );
  }

  Map<String, dynamic> toMap() {
    return <String, dynamic>{
      'planId': planId,
      'displayName': displayName,
      'enabled': enabled,
      'continuationPolicy': continuationPolicy.wireValue,
      'steps': steps.map((step) => step.toMap()).toList(growable: false),
    };
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
}

class GuidedSessionPlanEditorCodec {
  static List<GuidedSessionPlanStep> parseStepLines(String source) {
    final lines = const LineSplitter().convert(source);
    final steps = <GuidedSessionPlanStep>[];
    for (var i = 0; i < lines.length; i++) {
      final line = lines[i].trim();
      if (line.isEmpty) {
        continue;
      }

      final separatorIndex = line.indexOf('|');
      final gameId = separatorIndex >= 0
          ? line.substring(0, separatorIndex).trim()
          : line.trim();
      if (gameId.isEmpty) {
        throw FormatException('Line ${i + 1}: gameId is required.');
      }

      var configPreset = <String, dynamic>{};
      if (separatorIndex >= 0) {
        final presetRaw = line.substring(separatorIndex + 1).trim();
        if (presetRaw.isNotEmpty) {
          final decoded = jsonDecode(presetRaw);
          if (decoded is! Map<String, dynamic>) {
            throw FormatException(
              'Line ${i + 1}: preset must be a JSON object.',
            );
          }
          configPreset = decoded;
        }
      }

      steps.add(
        GuidedSessionPlanStep(
          stepId: 'step_${i + 1}_$gameId',
          gameId: gameId,
          displayName: gameId,
          configPreset: configPreset,
        ),
      );
    }

    return List<GuidedSessionPlanStep>.unmodifiable(steps);
  }

  static String serializeStepLines(List<GuidedSessionPlanStep> steps) {
    if (steps.isEmpty) {
      return '';
    }

    final lines = <String>[];
    for (final step in steps) {
      final gameId = step.gameId.trim();
      if (gameId.isEmpty) {
        continue;
      }
      if (step.configPreset.isEmpty) {
        lines.add(gameId);
      } else {
        lines.add('$gameId|${jsonEncode(step.configPreset)}');
      }
    }

    return lines.join('\n');
  }
}
