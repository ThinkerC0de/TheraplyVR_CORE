import 'package:flutter_controller/models/guided_session_plan.dart';
import 'package:flutter_controller/models/therapist_session_settings.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('TherapistSessionSettings', () {
    test('returns defaults when source map is null', () {
      final settings = TherapistSessionSettings.fromMap(null);

      expect(
        settings.sessionRecoveryWindowMinutes,
        TherapistSessionSettings.defaultSessionRecoveryWindowMinutes,
      );
      expect(
        settings.interruptedSessionAutoCloseHours,
        TherapistSessionSettings.defaultInterruptedSessionAutoCloseHours,
      );
      expect(
        settings.autoCloseInterruptedSessionsEnabled,
        TherapistSessionSettings.defaultAutoCloseInterruptedSessionsEnabled,
      );
      expect(
        settings.adaptiveDifficultyEnabled,
        TherapistSessionSettings.defaultAdaptiveDifficultyEnabled,
      );
      expect(
        settings.adaptiveDifficultySensitivity,
        TherapistSessionSettings.defaultAdaptiveDifficultySensitivity,
      );
      expect(
        settings.labelPipelineEnabled,
        TherapistSessionSettings.defaultLabelPipelineEnabled,
      );
      expect(
        settings.keepScreenAwakeWhenForeground,
        TherapistSessionSettings.defaultKeepScreenAwakeWhenForeground,
      );
      expect(
        settings.previewStreamBitrateKbps,
        TherapistSessionSettings.defaultPreviewStreamBitrateKbps,
      );
      expect(
        settings.mobileDisconnectBehavior,
        TherapistSessionSettings.defaultMobileDisconnectBehavior,
      );
      expect(
        settings.operatorUiLanguage,
        TherapistSessionSettings.defaultOperatorUiLanguage,
      );
      expect(settings.guidedSessionPlanSteps, isNotEmpty);
      expect(
        settings.guidedSessionContinuationPolicy.wireValue,
        'resume_under_recovery_window',
      );
    });

    test('clamps integer settings to documented bounds', () {
      final settings = TherapistSessionSettings.fromMap(<String, dynamic>{
        'sessionRecoveryWindowMinutes': 1000,
        'interruptedSessionAutoCloseHours': 0,
        'criticalCommandMaxRetries': -1,
        'criticalCommandAckTimeoutMs': 999999,
        'adaptiveDifficultySensitivity': 99.0,
        'previewStreamBitrateKbps': 999999,
      });

      expect(
        settings.sessionRecoveryWindowMinutes,
        TherapistSessionSettings.maxSessionRecoveryWindowMinutes,
      );
      expect(
        settings.interruptedSessionAutoCloseHours,
        TherapistSessionSettings.minInterruptedSessionAutoCloseHours,
      );
      expect(
        settings.criticalCommandMaxRetries,
        TherapistSessionSettings.minCriticalCommandMaxRetries,
      );
      expect(
        settings.criticalCommandAckTimeoutMs,
        TherapistSessionSettings.maxCriticalCommandAckTimeoutMs,
      );
      expect(
        settings.adaptiveDifficultySensitivity,
        TherapistSessionSettings.maxAdaptiveDifficultySensitivity,
      );
      expect(
        settings.previewStreamBitrateKbps,
        TherapistSessionSettings.maxPreviewStreamBitrateKbps,
      );
    });

    test('parses booleans and note templates from source map', () {
      final settings = TherapistSessionSettings.fromMap(<String, dynamic>{
        'autoCloseInterruptedSessionsEnabled': false,
        'requireResumeConfirmationAfterRecoveryWindow': false,
        'keepScreenAwakeWhenForeground': true,
        'previewStreamBitrateKbps': 640,
        'mobileDisconnectBehavior': 'continue',
        'operatorUiLanguage': 'pl',
        'timelineQuickNoteTemplates': <dynamic>['A', ' ', 'B'],
        'guidedSessionContinuationPolicy': 'resume_always',
        'guidedSessionPlanSteps': <Map<String, dynamic>>[
          <String, dynamic>{
            'stepId': 'step-1',
            'gameId': 'demo_cube_clicker',
            'displayName': 'Demo',
            'configPreset': <String, dynamic>{'cubeCount': 9},
          },
        ],
      });

      expect(settings.autoCloseInterruptedSessionsEnabled, isFalse);
      expect(settings.requireResumeConfirmationAfterRecoveryWindow, isFalse);
      expect(settings.keepScreenAwakeWhenForeground, isTrue);
      expect(settings.previewStreamBitrateKbps, 640);
      expect(
        settings.mobileDisconnectBehavior,
        MobileDisconnectBehavior.continueGameplay,
      );
      expect(settings.operatorUiLanguage, TherapistUiLanguage.polish);
      expect(settings.timelineQuickNoteTemplates, <String>['A', 'B']);
      expect(
        settings.guidedSessionContinuationPolicy.wireValue,
        'resume_always',
      );
      expect(settings.guidedSessionPlanSteps.length, 1);
      expect(settings.guidedSessionPlanSteps.first.gameId, 'demo_cube_clicker');
    });

    test('serializes and deserializes stable settings map', () {
      final source = TherapistSessionSettings.fromMap(<String, dynamic>{
        'sessionRecoveryWindowMinutes': 120,
        'interruptedSessionAutoCloseHours': 24,
        'autoCloseInterruptedSessionsEnabled': false,
        'requireResumeConfirmationAfterRecoveryWindow': true,
        'criticalCommandMaxRetries': 6,
        'criticalCommandAckTimeoutMs': 1200,
        'adaptiveDifficultyEnabled': true,
        'adaptiveDifficultySensitivity': 0.74,
        'labelPipelineEnabled': false,
        'keepScreenAwakeWhenForeground': true,
        'previewStreamBitrateKbps': 900,
        'mobileDisconnectBehavior': 'continue',
        'operatorUiLanguage': 'pl',
        'timelineQuickNoteTemplates': <String>['Template A', 'Template B'],
        'guidedSessionContinuationPolicy': 'manual',
        'guidedSessionPlanSteps': <Map<String, dynamic>>[
          <String, dynamic>{
            'stepId': 'step-a',
            'gameId': 'demo_cube_clicker',
            'displayName': 'Demo',
            'configPreset': <String, dynamic>{'cubeCount': 10},
          },
          <String, dynamic>{
            'stepId': 'step-b',
            'gameId': 'pulse_target_tap',
            'displayName': 'Pulse',
            'configPreset': <String, dynamic>{'targetCount': 7},
          },
        ],
      });

      final roundtrip = TherapistSessionSettings.fromMap(source.toMap());

      expect(roundtrip.sessionRecoveryWindowMinutes, 120);
      expect(roundtrip.interruptedSessionAutoCloseHours, 24);
      expect(roundtrip.autoCloseInterruptedSessionsEnabled, isFalse);
      expect(roundtrip.requireResumeConfirmationAfterRecoveryWindow, isTrue);
      expect(roundtrip.criticalCommandMaxRetries, 6);
      expect(roundtrip.criticalCommandAckTimeoutMs, 1200);
      expect(roundtrip.adaptiveDifficultyEnabled, isTrue);
      expect(roundtrip.adaptiveDifficultySensitivity, 0.74);
      expect(roundtrip.labelPipelineEnabled, isFalse);
      expect(roundtrip.keepScreenAwakeWhenForeground, isTrue);
      expect(roundtrip.previewStreamBitrateKbps, 900);
      expect(
        roundtrip.mobileDisconnectBehavior,
        MobileDisconnectBehavior.continueGameplay,
      );
      expect(roundtrip.operatorUiLanguage, TherapistUiLanguage.polish);
      expect(
        roundtrip.timelineQuickNoteTemplates,
        <String>['Template A', 'Template B'],
      );
      expect(roundtrip.guidedSessionContinuationPolicy.wireValue, 'manual');
      expect(roundtrip.guidedSessionPlanSteps.length, 2);
      expect(
          roundtrip.guidedSessionPlanSteps.first.gameId, 'demo_cube_clicker');
    });

    test('copyWith overrides selected fields only', () {
      final source = TherapistSessionSettings.defaults();

      final updated = source.copyWith(
        sessionRecoveryWindowMinutes: 90,
        criticalCommandMaxRetries: 5,
        adaptiveDifficultyEnabled: false,
        adaptiveDifficultySensitivity: 0.25,
        labelPipelineEnabled: false,
        keepScreenAwakeWhenForeground: true,
        previewStreamBitrateKbps: 700,
        mobileDisconnectBehavior: MobileDisconnectBehavior.continueGameplay,
        operatorUiLanguage: TherapistUiLanguage.polish,
        timelineQuickNoteTemplates: <String>['Custom quick note'],
        guidedSessionContinuationPolicy:
            GuidedSessionContinuationPolicy.resumeAlways,
      );

      expect(updated.sessionRecoveryWindowMinutes, 90);
      expect(
        updated.interruptedSessionAutoCloseHours,
        source.interruptedSessionAutoCloseHours,
      );
      expect(updated.criticalCommandMaxRetries, 5);
      expect(updated.adaptiveDifficultyEnabled, isFalse);
      expect(updated.adaptiveDifficultySensitivity, 0.25);
      expect(updated.labelPipelineEnabled, isFalse);
      expect(updated.keepScreenAwakeWhenForeground, isTrue);
      expect(updated.previewStreamBitrateKbps, 700);
      expect(
        updated.mobileDisconnectBehavior,
        MobileDisconnectBehavior.continueGameplay,
      );
      expect(updated.operatorUiLanguage, TherapistUiLanguage.polish);
      expect(
        updated.timelineQuickNoteTemplates,
        <String>['Custom quick note'],
      );
      expect(
        updated.guidedSessionContinuationPolicy,
        GuidedSessionContinuationPolicy.resumeAlways,
      );
    });
  });
}
