import 'package:flutter_controller/models/guided_session_plan.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('GuidedSessionPlanEditorCodec', () {
    test('parses step lines with optional preset json', () {
      final steps = GuidedSessionPlanEditorCodec.parseStepLines(
        'demo_cube_clicker|{"cubeCount":10}\n'
        'pulse_target_tap',
      );

      expect(steps.length, 2);
      expect(steps.first.gameId, 'demo_cube_clicker');
      expect(steps.first.configPreset['cubeCount'], 10);
      expect(steps.last.gameId, 'pulse_target_tap');
      expect(steps.last.configPreset, isEmpty);
    });

    test('throws format exception for invalid preset object', () {
      expect(
        () => GuidedSessionPlanEditorCodec.parseStepLines(
          'demo_cube_clicker|[1,2,3]',
        ),
        throwsA(isA<FormatException>()),
      );
    });

    test('serializes parsed lines back to stable text', () {
      final input = 'demo_cube_clicker|{"cubeSpeed":0.7}';
      final steps = GuidedSessionPlanEditorCodec.parseStepLines(input);
      final output = GuidedSessionPlanEditorCodec.serializeStepLines(steps);

      expect(output, contains('demo_cube_clicker|'));
      expect(output, contains('"cubeSpeed":0.7'));
    });
  });

  group('GuidedSessionPlan', () {
    test('maps from map with defaults', () {
      final plan = GuidedSessionPlan.fromMap(<String, dynamic>{
        'planId': 'parent-default',
        'displayName': 'Parent default',
        'enabled': true,
        'continuationPolicy': 'resume_under_recovery_window',
        'steps': <Map<String, dynamic>>[
          <String, dynamic>{
            'stepId': 'step-1',
            'gameId': 'demo_cube_clicker',
            'displayName': 'Demo',
            'configPreset': <String, dynamic>{'cubeCount': 12},
          },
        ],
      });

      expect(plan.planId, 'parent-default');
      expect(plan.displayName, 'Parent default');
      expect(plan.hasSteps, isTrue);
      expect(
        plan.continuationPolicy,
        GuidedSessionContinuationPolicy.resumeUnderRecoveryWindow,
      );
      expect(plan.steps.first.configPreset['cubeCount'], 12);
    });
  });
}
