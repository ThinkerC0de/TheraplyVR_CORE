import 'package:fake_cloud_firestore/fake_cloud_firestore.dart';
import 'package:flutter_controller/services/entitlement_service.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  late FakeFirebaseFirestore firestore;
  const userId = 'matrix-user-1';
  final nowUtc = DateTime.utc(2026, 2, 18, 12, 0, 0);

  setUp(() {
    firestore = FakeFirebaseFirestore();
    EntitlementService.setFirestoreInstanceForTesting(firestore);
    EntitlementService.setGateFlagsForTesting(
      strictEntitlementGate: false,
      enableDevEntitlementBootstrap: false,
    );
    EntitlementService.clearSessionAccess();
  });

  tearDown(() {
    EntitlementService.clearFirestoreInstanceForTesting();
  });

  test('active entitlement allows login', () async {
    await firestore.collection('user_entitlements').doc(userId).set(
          _entitlementDoc(
            status: 'ACTIVE',
            updatedAtUtc: nowUtc,
          ),
        );

    final decision =
        await EntitlementService.evaluateLoginGateForUserId(userId);
    expect(decision.isAllowed, isTrue);
    expect(decision.reasonCode, 'APP_LICENSE_ACTIVE');
  });

  test('revoked entitlement denies login', () async {
    await firestore.collection('user_entitlements').doc(userId).set(
          _entitlementDoc(
            status: 'REVOKED',
            updatedAtUtc: nowUtc,
          ),
        );

    final decision =
        await EntitlementService.evaluateLoginGateForUserId(userId);
    expect(decision.isAllowed, isFalse);
    expect(decision.reasonCode, 'APP_LICENSE_INACTIVE');
  });

  test('active APP grant override allows revoked base entitlement', () async {
    await firestore.collection('user_entitlements').doc(userId).set(
          _entitlementDoc(
            status: 'REVOKED',
            updatedAtUtc: nowUtc,
          ),
        );

    await firestore.collection('entitlement_grants').doc('grant-1').set(
      <String, dynamic>{
        'granteeUserId': userId,
        'scope': 'APP',
        'gameId': null,
        'licenseGrant': <String, dynamic>{
          'status': 'ACTIVE',
          'fromUtc':
              nowUtc.subtract(const Duration(minutes: 1)).toIso8601String(),
          'toUtc': null,
          'perpetual': true,
        },
        'source': 'ADMIN',
        'assignedBy': 'admin-user',
        'assignedAtUtc': nowUtc.toIso8601String(),
        'revoked': false,
        'revokedAtUtc': null,
        'role': null,
        'note': 'temporary restore',
        'reason': 'matrix-smoke-override',
        'correlationId': 'corr-matrix-override',
      },
    );

    final decision =
        await EntitlementService.evaluateLoginGateForUserId(userId);
    expect(decision.isAllowed, isTrue);
    expect(decision.reasonCode, 'APP_LICENSE_ACTIVE');
  });

  test('missing entitlement denies in strict mode', () async {
    EntitlementService.setGateFlagsForTesting(
      strictEntitlementGate: true,
      enableDevEntitlementBootstrap: false,
    );

    final decision =
        await EntitlementService.evaluateLoginGateForUserId(userId);
    expect(decision.isAllowed, isFalse);
    expect(decision.reasonCode, 'ENTITLEMENT_RECORD_REQUIRED');
  });
}

Map<String, dynamic> _entitlementDoc({
  required String status,
  required DateTime updatedAtUtc,
}) {
  return <String, dynamic>{
    'role': 'THERAPIST',
    'appLicense': <String, dynamic>{
      'status': status,
      'fromUtc': updatedAtUtc.toIso8601String(),
      'toUtc': null,
      'perpetual': true,
    },
    'gameLicenses': <String, dynamic>{},
    'policyVersion': 'matrix-test-v1',
    'updatedAtUtc': updatedAtUtc.toIso8601String(),
    'updatedBy': 'matrix-test',
    'updateReason': 'matrix-test',
    'correlationId': 'corr-matrix',
  };
}
