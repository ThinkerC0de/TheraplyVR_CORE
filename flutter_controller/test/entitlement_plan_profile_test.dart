import 'package:fake_cloud_firestore/fake_cloud_firestore.dart';
import 'package:flutter_controller/models/entitlement_access.dart';
import 'package:flutter_controller/services/entitlement_service.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  late FakeFirebaseFirestore firestore;
  const userId = 'entitlement-plan-user';
  final activeFromUtc = DateTime.utc(2020, 1, 1, 0, 0, 0);
  final nowUtc = DateTime.utc(2026, 2, 23, 12, 0, 0);

  setUp(() {
    firestore = FakeFirebaseFirestore();
    EntitlementService.setFirestoreInstanceForTesting(firestore);
    EntitlementService.setGateFlagsForTesting(
      strictEntitlementGate: true,
      enableDevEntitlementBootstrap: false,
    );
    EntitlementService.clearSessionAccess();
  });

  tearDown(() {
    EntitlementService.clearFirestoreInstanceForTesting();
  });

  test('legacy therapist profile defaults to BASIC plan tier', () async {
    await firestore.collection('user_entitlements').doc(userId).set(
      <String, dynamic>{
        'role': 'THERAPIST',
        'appLicense': <String, dynamic>{
          'status': 'ACTIVE',
          'fromUtc': activeFromUtc.toIso8601String(),
          'toUtc': null,
          'perpetual': true,
        },
        'gameLicenses': <String, dynamic>{},
        'policyVersion': 'legacy-entitlement-v1',
      },
    );

    final decision =
        await EntitlementService.evaluateLoginGateForUserId(userId);
    expect(decision.isAllowed, isTrue);
    expect(decision.access.planProfile.tier, SubscriptionPlanTier.basic);
    expect(
      decision.access.isFeatureEnabled(EntitlementFeatureKeys.vrSessionControl),
      isTrue,
    );
  });

  test('plan profile enforces allow-list and demo quota visibility', () async {
    await firestore.collection('user_entitlements').doc(userId).set(
      <String, dynamic>{
        'role': 'PARENT',
        'appLicense': <String, dynamic>{
          'status': 'ACTIVE',
          'fromUtc': activeFromUtc.toIso8601String(),
          'toUtc': null,
          'perpetual': true,
        },
        'gameLicenses': <String, dynamic>{},
        'planProfile': <String, dynamic>{
          'tier': 'FREE',
          'allowedGameIds': <String>['demo_cube_clicker'],
          'demoSessionLimit': 2,
          'demoSessionsUsed': 2,
        },
      },
    );

    final decision =
        await EntitlementService.evaluateLoginGateForUserId(userId);
    expect(decision.isAllowed, isTrue);
    expect(decision.access.planProfile.tier, SubscriptionPlanTier.free);
    expect(
      EntitlementService.isFeatureEnabled(
        EntitlementFeatureKeys.parentGuidedStart,
      ),
      isTrue,
    );

    expect(
      EntitlementService.canLaunchGame('demo_cube_clicker', atUtc: nowUtc),
      isTrue,
    );
    expect(
      EntitlementService.canLaunchGame('pulse_target_tap', atUtc: nowUtc),
      isFalse,
    );
    expect(EntitlementService.hasRemainingDemoSessions(), isFalse);
  });
}
