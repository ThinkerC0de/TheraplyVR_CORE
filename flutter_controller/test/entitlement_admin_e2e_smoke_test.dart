import 'package:fake_cloud_firestore/fake_cloud_firestore.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_controller/services/entitlement_service.dart';

void main() {
  late FakeFirebaseFirestore firestore;

  setUp(() {
    firestore = FakeFirebaseFirestore();
    EntitlementService.setFirestoreInstanceForTesting(firestore);
    EntitlementService.clearSessionAccess();
  });

  tearDown(() {
    EntitlementService.clearFirestoreInstanceForTesting();
  });

  test('web entitlement change is applied on next mobile login gate check', () async {
    const userId = 'mobile-user-1';
    final nowUtc = DateTime.utc(2026, 2, 18, 10);

    await firestore.collection('user_entitlements').doc(userId).set(
      <String, dynamic>{
        'role': 'THERAPIST',
        'appLicense': <String, dynamic>{
          'status': 'ACTIVE',
          'fromUtc': nowUtc.toIso8601String(),
          'toUtc': null,
          'perpetual': true,
        },
        'gameLicenses': <String, dynamic>{},
        'policyVersion': 'admin-console-web-v1',
        'updatedAtUtc': nowUtc.toIso8601String(),
        'updatedBy': 'admin-user-1',
        'updateReason': 'initial-smoke-setup',
        'correlationId': 'corr-initial',
      },
    );

    final firstLogin = await EntitlementService.evaluateLoginGateForUserId(userId);
    expect(firstLogin.isAllowed, isTrue);
    expect(firstLogin.reasonCode, 'APP_LICENSE_ACTIVE');
    expect(firstLogin.usedLegacyFallback, isFalse);

    await firestore.collection('user_entitlements').doc(userId).set(
      <String, dynamic>{
        'role': 'THERAPIST',
        'appLicense': <String, dynamic>{
          'status': 'REVOKED',
          'fromUtc': nowUtc.toIso8601String(),
          'toUtc': null,
          'perpetual': true,
        },
        'gameLicenses': <String, dynamic>{},
        'policyVersion': 'admin-console-web-v1',
        'updatedAtUtc': nowUtc.add(const Duration(minutes: 5)).toIso8601String(),
        'updatedBy': 'admin-user-1',
        'updateReason': 'manual-revoke',
        'correlationId': 'corr-revoke',
      },
    );

    final secondLogin = await EntitlementService.evaluateLoginGateForUserId(userId);
    expect(secondLogin.isAllowed, isFalse);
    expect(secondLogin.reasonCode, 'APP_LICENSE_INACTIVE');

    await firestore.collection('entitlement_grants').doc('grant-restore').set(
      <String, dynamic>{
        'granteeUserId': userId,
        'scope': 'APP',
        'gameId': null,
        'licenseGrant': <String, dynamic>{
          'status': 'ACTIVE',
          'fromUtc': nowUtc.add(const Duration(minutes: 10)).toIso8601String(),
          'toUtc': null,
          'perpetual': true,
        },
        'source': 'ADMIN',
        'assignedBy': 'admin-user-1',
        'assignedAtUtc': nowUtc.add(const Duration(minutes: 10)).toIso8601String(),
        'revoked': false,
        'revokedAtUtc': null,
        'role': null,
        'note': 'temporary restore',
        'reason': 'temporary-restore',
        'correlationId': 'corr-grant-restore',
      },
    );

    final thirdLogin = await EntitlementService.evaluateLoginGateForUserId(userId);
    expect(thirdLogin.isAllowed, isTrue);
    expect(thirdLogin.reasonCode, 'APP_LICENSE_ACTIVE');
    expect(thirdLogin.usedLegacyFallback, isFalse);
  });
}
