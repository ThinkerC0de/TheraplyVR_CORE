import 'package:fake_cloud_firestore/fake_cloud_firestore.dart';
import 'package:flutter_controller/models/content_delivery_contract.dart';
import 'package:flutter_controller/models/entitlement_access.dart';
import 'package:flutter_controller/services/entitlement_service.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  late FakeFirebaseFirestore firestore;
  const userId = 'role-matrix-user';

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

  test(
      'therapist profile keeps local launch gate usable from cached access (offline path)',
      () async {
    final nowUtc = DateTime.now().toUtc();
    await _setEntitlementDocument(
      firestore: firestore,
      userId: userId,
      role: 'THERAPIST',
      appLicense: _activeLicense(
        fromUtc: nowUtc.subtract(const Duration(days: 1)),
        toUtc: null,
      ),
      gameLicenses: const <String, dynamic>{},
      planProfile: const <String, dynamic>{
        'tier': 'BASIC',
      },
    );

    final decision =
        await EntitlementService.evaluateLoginGateForUserId(userId);
    expect(decision.isAllowed, isTrue);
    expect(
      EntitlementService.resolveRuntimeProfileId(),
      RuntimeEntitlementProfileIds.therapistFull,
    );

    // This uses active access only, without another backend read.
    final entitledGameIds = EntitlementService.resolveRuntimeEntitledGameIds(
      const <String>[
        'demo_cube_clicker',
        'pulse_target_tap',
      ],
      atUtc: nowUtc,
    );

    expect(
      entitledGameIds,
      containsAll(<String>['demo_cube_clicker', 'pulse_target_tap']),
    );
    expect(
      EntitlementService.canLaunchGame('demo_cube_clicker', atUtc: nowUtc),
      isTrue,
    );
  });

  test('parent purchased packs profile limits entitled game set', () async {
    final nowUtc = DateTime.now().toUtc();
    await _setEntitlementDocument(
      firestore: firestore,
      userId: userId,
      role: 'PARENT',
      appLicense: _activeLicense(
        fromUtc: nowUtc.subtract(const Duration(days: 1)),
        toUtc: null,
      ),
      gameLicenses: <String, dynamic>{
        'demo_cube_clicker': _activeLicense(
          fromUtc: nowUtc.subtract(const Duration(days: 1)),
          toUtc: null,
        ),
      },
      planProfile: const <String, dynamic>{
        'tier': 'FREE',
        'allowedGameIds': <String>['demo_cube_clicker'],
      },
    );

    final decision =
        await EntitlementService.evaluateLoginGateForUserId(userId);
    expect(decision.isAllowed, isTrue);
    expect(
      EntitlementService.resolveRuntimeProfileId(),
      RuntimeEntitlementProfileIds.parentPurchasedPacks,
    );

    final entitledGameIds = EntitlementService.resolveRuntimeEntitledGameIds(
      const <String>[
        'demo_cube_clicker',
        'pulse_target_tap',
      ],
      atUtc: nowUtc,
    );
    expect(entitledGameIds, <String>{'demo_cube_clicker'});
    expect(
      EntitlementService.canLaunchGame('demo_cube_clicker', atUtc: nowUtc),
      isTrue,
    );
    expect(
      EntitlementService.canLaunchGame('pulse_target_tap', atUtc: nowUtc),
      isFalse,
    );
  });

  test('role switch updates runtime profile and entitled launch set', () async {
    final nowUtc = DateTime.now().toUtc();
    await _setEntitlementDocument(
      firestore: firestore,
      userId: userId,
      role: 'THERAPIST',
      appLicense: _activeLicense(
        fromUtc: nowUtc.subtract(const Duration(days: 1)),
        toUtc: null,
      ),
      gameLicenses: const <String, dynamic>{},
      planProfile: const <String, dynamic>{
        'tier': 'BASIC',
      },
    );

    final therapistDecision =
        await EntitlementService.evaluateLoginGateForUserId(userId);
    expect(therapistDecision.isAllowed, isTrue);
    expect(
      EntitlementService.resolveRuntimeProfileId(),
      RuntimeEntitlementProfileIds.therapistFull,
    );

    await _setEntitlementDocument(
      firestore: firestore,
      userId: userId,
      role: 'PARENT',
      appLicense: _activeLicense(
        fromUtc: nowUtc.subtract(const Duration(days: 1)),
        toUtc: null,
      ),
      gameLicenses: <String, dynamic>{
        'demo_cube_clicker': _activeLicense(
          fromUtc: nowUtc.subtract(const Duration(days: 1)),
          toUtc: null,
        ),
      },
      planProfile: const <String, dynamic>{
        'tier': 'FREE',
        'allowedGameIds': <String>['demo_cube_clicker'],
      },
    );

    final parentDecision =
        await EntitlementService.evaluateLoginGateForUserId(userId);
    expect(parentDecision.isAllowed, isTrue);
    expect(
      EntitlementService.resolveRuntimeProfileId(),
      RuntimeEntitlementProfileIds.parentPurchasedPacks,
    );
    expect(
      EntitlementService.resolveRuntimeEntitledGameIds(
        const <String>['demo_cube_clicker', 'pulse_target_tap'],
        atUtc: nowUtc,
      ),
      <String>{'demo_cube_clicker'},
    );
  });

  test('stale entitlement cache denies launch once app license expires',
      () async {
    final nowUtc = DateTime.now().toUtc();
    final expiresAtUtc = nowUtc.add(const Duration(minutes: 15));
    await _setEntitlementDocument(
      firestore: firestore,
      userId: userId,
      role: 'THERAPIST',
      appLicense: _activeLicense(
        fromUtc: nowUtc.subtract(const Duration(hours: 1)),
        toUtc: expiresAtUtc,
      ),
      gameLicenses: const <String, dynamic>{},
      planProfile: const <String, dynamic>{
        'tier': 'BASIC',
      },
    );

    final decision =
        await EntitlementService.evaluateLoginGateForUserId(userId);
    expect(decision.isAllowed, isTrue);
    expect(
      EntitlementService.canLaunchGame('demo_cube_clicker', atUtc: nowUtc),
      isTrue,
    );
    expect(
      EntitlementService.canLaunchGame(
        'demo_cube_clicker',
        atUtc: expiresAtUtc.add(const Duration(minutes: 1)),
      ),
      isFalse,
    );
  });

  test(
      'partial download and uninstall states block launch even when entitlement allows',
      () async {
    final nowUtc = DateTime.now().toUtc();
    await _setEntitlementDocument(
      firestore: firestore,
      userId: userId,
      role: 'THERAPIST',
      appLicense: _activeLicense(
        fromUtc: nowUtc.subtract(const Duration(days: 1)),
        toUtc: null,
      ),
      gameLicenses: const <String, dynamic>{},
      planProfile: const <String, dynamic>{
        'tier': 'BASIC',
      },
    );

    final decision =
        await EntitlementService.evaluateLoginGateForUserId(userId);
    expect(decision.isAllowed, isTrue);
    expect(
      EntitlementService.canLaunchGame('demo_cube_clicker', atUtc: nowUtc),
      isTrue,
    );

    final partialDownloadState = PurchasedContentState(
      gameId: 'demo_cube_clicker',
      owned: true,
      installedVersion: null,
      targetVersion: '1.2.0',
      updateRequired: false,
      updateOptional: false,
      runtimeStatus: ContentRuntimeStatus.installing,
      lastError: null,
      updatedAtUtc: nowUtc,
    );
    final uninstalledState = partialDownloadState.copyWith(
      runtimeStatus: ContentRuntimeStatus.notInstalled,
      installedVersion: null,
    );
    final updateRequiredState = partialDownloadState.copyWith(
      runtimeStatus: ContentRuntimeStatus.ready,
      installedVersion: '1.0.0',
      updateRequired: true,
    );

    expect(
      ContentLaunchGate.isLaunchable(
        state: partialDownloadState,
        contentDeliveryEnabled: true,
        hasQuestStatusSignal: true,
      ),
      isFalse,
    );
    expect(
      ContentLaunchGate.isLaunchable(
        state: uninstalledState,
        contentDeliveryEnabled: true,
        hasQuestStatusSignal: true,
      ),
      isFalse,
    );
    expect(
      ContentLaunchGate.isLaunchable(
        state: updateRequiredState,
        contentDeliveryEnabled: true,
        hasQuestStatusSignal: true,
      ),
      isFalse,
    );
  });
}

Future<void> _setEntitlementDocument({
  required FakeFirebaseFirestore firestore,
  required String userId,
  required String role,
  required Map<String, dynamic> appLicense,
  required Map<String, dynamic> gameLicenses,
  required Map<String, dynamic> planProfile,
}) async {
  final nowUtc = DateTime.now().toUtc();
  await firestore.collection('user_entitlements').doc(userId).set(
    <String, dynamic>{
      'role': role,
      'appLicense': appLicense,
      'gameLicenses': gameLicenses,
      'planProfile': planProfile,
      'policyVersion': 'role-matrix-v1',
      'updatedAtUtc': nowUtc.toIso8601String(),
      'updatedBy': 'role-matrix-test',
      'updateReason': 'role-matrix-test',
      'correlationId': 'corr-role-matrix',
    },
  );
}

Map<String, dynamic> _activeLicense({
  required DateTime fromUtc,
  required DateTime? toUtc,
}) {
  return <String, dynamic>{
    'status': 'ACTIVE',
    'fromUtc': fromUtc.toIso8601String(),
    'toUtc': toUtc?.toIso8601String(),
    'perpetual': toUtc == null,
  };
}
