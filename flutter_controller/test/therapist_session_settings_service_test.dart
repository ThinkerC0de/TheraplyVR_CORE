import 'dart:async';

import 'package:fake_cloud_firestore/fake_cloud_firestore.dart';
import 'package:flutter_controller/models/guided_session_plan.dart';
import 'package:flutter_controller/models/therapist_session_settings.dart';
import 'package:flutter_controller/services/therapist_session_settings_service.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('TherapistSessionSettingsService', () {
    late FakeFirebaseFirestore firestore;

    setUp(() {
      firestore = FakeFirebaseFirestore();
      TherapistSessionSettingsService.setFirestoreInstanceForTesting(firestore);
      TherapistSessionSettingsService.setTherapistIdForTesting('therapist-1');
    });

    tearDown(() {
      TherapistSessionSettingsService.clearTestingOverrides();
    });

    test('returns defaults when entitlement document is missing', () async {
      final settings =
          await TherapistSessionSettingsService.fetchCurrentTherapistSettings();

      expect(
        settings.sessionRecoveryWindowMinutes,
        TherapistSessionSettings.defaultSessionRecoveryWindowMinutes,
      );
      expect(
        settings.interruptedSessionAutoCloseHours,
        TherapistSessionSettings.defaultInterruptedSessionAutoCloseHours,
      );
      expect(
        settings.timelineQuickNoteTemplates,
        TherapistSessionSettings.defaults().timelineQuickNoteTemplates,
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
    });

    test('saves settings with merge and fetch returns persisted values',
        () async {
      await firestore.collection('user_entitlements').doc('therapist-1').set(
        <String, dynamic>{
          'role': 'therapist',
          'policyVersion': 'preexisting',
        },
      );

      final targetSettings = TherapistSessionSettings.fromMap(
        <String, dynamic>{
          'sessionRecoveryWindowMinutes': 90,
          'interruptedSessionAutoCloseHours': 24,
          'autoCloseInterruptedSessionsEnabled': false,
          'requireResumeConfirmationAfterRecoveryWindow': false,
          'criticalCommandMaxRetries': 5,
          'criticalCommandAckTimeoutMs': 1800,
          'adaptiveDifficultyEnabled': false,
          'adaptiveDifficultySensitivity': 0.2,
          'labelPipelineEnabled': false,
          'keepScreenAwakeWhenForeground': true,
          'previewStreamBitrateKbps': 850,
          'mobileDisconnectBehavior': 'continue',
          'operatorUiLanguage': 'pl',
          'timelineQuickNoteTemplates': <String>[
            'Need pause',
            'Refocus prompt',
          ],
        },
      );

      final saved =
          await TherapistSessionSettingsService.saveCurrentTherapistSettings(
              targetSettings);
      expect(saved, isTrue);

      final snapshot = await firestore
          .collection('therapist_session_settings')
          .doc('therapist-1')
          .get();
      final payload = snapshot.data()!;
      expect(payload['therapistId'], 'therapist-1');
      expect(payload['sessionRecoveryWindowMinutes'], 90);
      expect(payload['interruptedSessionAutoCloseHours'], 24);
      expect(payload['autoCloseInterruptedSessionsEnabled'], isFalse);
      expect(
        payload['requireResumeConfirmationAfterRecoveryWindow'],
        isFalse,
      );
      expect(payload['criticalCommandMaxRetries'], 5);
      expect(payload['criticalCommandAckTimeoutMs'], 1800);
      expect(payload['adaptiveDifficultyEnabled'], isFalse);
      expect(payload['adaptiveDifficultySensitivity'], 0.2);
      expect(payload['labelPipelineEnabled'], isFalse);
      expect(payload['keepScreenAwakeWhenForeground'], isTrue);
      expect(payload['previewStreamBitrateKbps'], 850);
      expect(payload['mobileDisconnectBehavior'], 'continue');
      expect(payload['operatorUiLanguage'], 'pl');
      expect(
        payload['timelineQuickNoteTemplates'],
        <String>['Need pause', 'Refocus prompt'],
      );
      expect(payload['updatedAtUtc'], isA<String>());
      expect(payload['updatedBy'], 'therapist-1');
      final legacySnapshot = await firestore
          .collection('user_entitlements')
          .doc('therapist-1')
          .get();
      final legacyPayload = legacySnapshot.data()!;
      expect(legacyPayload['role'], 'therapist');
      expect(legacyPayload['policyVersion'], 'preexisting');
      expect(
          legacyPayload.containsKey('sessionRecoveryWindowMinutes'), isFalse);

      final fetched =
          await TherapistSessionSettingsService.fetchCurrentTherapistSettings();
      expect(fetched.sessionRecoveryWindowMinutes, 90);
      expect(fetched.interruptedSessionAutoCloseHours, 24);
      expect(fetched.autoCloseInterruptedSessionsEnabled, isFalse);
      expect(
        fetched.requireResumeConfirmationAfterRecoveryWindow,
        isFalse,
      );
      expect(fetched.criticalCommandMaxRetries, 5);
      expect(fetched.criticalCommandAckTimeoutMs, 1800);
      expect(fetched.adaptiveDifficultyEnabled, isFalse);
      expect(fetched.adaptiveDifficultySensitivity, 0.2);
      expect(fetched.labelPipelineEnabled, isFalse);
      expect(fetched.keepScreenAwakeWhenForeground, isTrue);
      expect(fetched.previewStreamBitrateKbps, 850);
      expect(
        fetched.mobileDisconnectBehavior,
        MobileDisconnectBehavior.continueGameplay,
      );
      expect(fetched.operatorUiLanguage, TherapistUiLanguage.polish);
      expect(
        fetched.timelineQuickNoteTemplates,
        <String>['Need pause', 'Refocus prompt'],
      );
    });

    test('save returns false when therapist id is missing', () async {
      TherapistSessionSettingsService.setTherapistIdForTesting('  ');

      final saved =
          await TherapistSessionSettingsService.saveCurrentTherapistSettings(
              TherapistSessionSettings.defaults());

      expect(saved, isFalse);
    });

    test('falls back to legacy entitlement document when settings doc missing',
        () async {
      await firestore
          .collection('user_entitlements')
          .doc('therapist-owner')
          .set(
        <String, dynamic>{
          'sessionRecoveryWindowMinutes': 88,
          'guidedSessionContinuationPolicy': 'resume_always',
          'guidedSessionPlanSteps': <Map<String, dynamic>>[
            <String, dynamic>{
              'stepId': 'step_1_demo',
              'gameId': 'demo_cube_clicker',
              'displayName': 'Demo',
              'configPreset': <String, dynamic>{'cubeCount': 9},
            },
          ],
        },
      );

      final settings =
          await TherapistSessionSettingsService.fetchSettingsForTherapistId(
              'therapist-owner');

      expect(settings.sessionRecoveryWindowMinutes, 88);
      expect(
        settings.guidedSessionContinuationPolicy.wireValue,
        'resume_always',
      );
      expect(settings.guidedSessionPlanSteps, isNotEmpty);
      expect(settings.guidedSessionPlanSteps.first.gameId, 'demo_cube_clicker');
    });

    test('fetches settings for explicit therapist id (parent guided read)',
        () async {
      await firestore
          .collection('therapist_session_settings')
          .doc('therapist-owner')
          .set(
        <String, dynamic>{
          'sessionRecoveryWindowMinutes': 88,
          'guidedSessionContinuationPolicy': 'resume_always',
          'guidedSessionPlanSteps': <Map<String, dynamic>>[
            <String, dynamic>{
              'stepId': 'step_1_demo',
              'gameId': 'demo_cube_clicker',
              'displayName': 'Demo',
              'configPreset': <String, dynamic>{'cubeCount': 9},
            },
          ],
        },
      );

      final settings =
          await TherapistSessionSettingsService.fetchSettingsForTherapistId(
              'therapist-owner');

      expect(settings.sessionRecoveryWindowMinutes, 88);
      expect(
        settings.guidedSessionContinuationPolicy.wireValue,
        'resume_always',
      );
      expect(settings.guidedSessionPlanSteps, isNotEmpty);
      expect(settings.guidedSessionPlanSteps.first.gameId, 'demo_cube_clicker');
    });

    test(
        'legacy entitlement write is denied by rules but settings save succeeds on dedicated path',
        () async {
      final auth = StreamController<Map<String, dynamic>?>.broadcast();
      final securedFirestore = FakeFirebaseFirestore(
        securityRules: _boardDemoSettingsRules,
        authObject: auth.stream,
      );
      TherapistSessionSettingsService.setFirestoreInstanceForTesting(
        securedFirestore,
      );
      TherapistSessionSettingsService.setTherapistIdForTesting('therapist-1');
      auth.add(
        <String, dynamic>{
          'uid': 'therapist-1',
          'token': <String, dynamic>{'role': 'therapist'},
        },
      );
      await Future<void>.delayed(Duration.zero);

      expect(
        () => securedFirestore
            .collection('user_entitlements')
            .doc('therapist-1')
            .set(<String, dynamic>{
          'sessionRecoveryWindowMinutes': 30,
        }),
        throwsException,
      );

      final saved =
          await TherapistSessionSettingsService.saveCurrentTherapistSettings(
        TherapistSessionSettings.defaults(),
      );
      expect(saved, isTrue);

      final settingsSnapshot = await securedFirestore
          .collection('therapist_session_settings')
          .doc('therapist-1')
          .get();
      expect(settingsSnapshot.exists, isTrue);

      await auth.close();
    });
  });
}

const _boardDemoSettingsRules = '''
service cloud.firestore {
  match /databases/{database}/documents {
    match /user_entitlements/{userId} {
      allow read, write: if request.auth.uid == 'admin-operator';
    }

    match /therapist_session_settings/{therapistId} {
      allow read, write: if request.auth.uid == therapistId;
    }
  }
}
''';
