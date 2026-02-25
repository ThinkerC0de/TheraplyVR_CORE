import 'package:fake_cloud_firestore/fake_cloud_firestore.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:admin_console_web/models/entitlement_access.dart';
import 'package:admin_console_web/models/entitlement_grant_contract.dart';
import 'package:admin_console_web/services/entitlement_admin_service.dart';

void main() {
  late FakeFirebaseFirestore firestore;

  setUp(() {
    firestore = FakeFirebaseFirestore();
    EntitlementAdminService.setFirestoreInstanceForTesting(firestore);
    EntitlementAdminService.setActorOverrideForTesting(
      uid: 'admin-user-1',
      email: 'ops@example.com',
    );
  });

  tearDown(() {
    EntitlementAdminService.clearTestOverrides();
  });

  test('upsertUserEntitlement writes payload and audit event', () async {
    const correlationId = 'corr-entitlement-1';
    const userId = 'mobile-user-1';
    const reason = 'license-renewal';

    await EntitlementAdminService.upsertUserEntitlement(
      userId: userId,
      role: EntitlementRole.therapist,
      appLicense: const LicenseGrant(
        status: LicenseStatus.active,
        perpetual: true,
      ),
      reason: reason,
      correlationId: correlationId,
    );

    final entitlement =
        await firestore.collection('user_entitlements').doc(userId).get();
    final entitlementData = entitlement.data()!;
    expect(entitlementData['updatedBy'], 'admin-user-1');
    expect(entitlementData['updateReason'], reason);
    expect(entitlementData['correlationId'], correlationId);
    final planProfile =
        entitlementData['planProfile'] as Map<String, dynamic>? ??
            const <String, dynamic>{};
    expect(planProfile['tier'], 'BASIC');

    final auditSnapshot = await firestore
        .collection('admin_audit_trail')
        .where('correlationId', isEqualTo: correlationId)
        .get();
    expect(auditSnapshot.docs, hasLength(1));
    final auditData = auditSnapshot.docs.single.data();
    expect(auditData['actorUid'], 'admin-user-1');
    expect(auditData['actorRole'], 'admin_operator');
    expect(auditData['action'], 'UPSERT_USER_ENTITLEMENT');
    expect(auditData['targetCollection'], 'user_entitlements');
    expect(auditData['targetDocumentId'], userId);
    expect(auditData['reason'], reason);
    final payloadSummary =
        auditData['payloadSummary'] as Map<String, dynamic>? ??
            const <String, dynamic>{};
    expect(payloadSummary['planTier'], 'BASIC');
  });

  test('upsertUserEntitlement defaults parent role to FREE plan', () async {
    const correlationId = 'corr-entitlement-parent';
    const userId = 'parent-user-1';
    const reason = 'parent-onboarding';

    await EntitlementAdminService.upsertUserEntitlement(
      userId: userId,
      role: EntitlementRole.parent,
      appLicense: const LicenseGrant(
        status: LicenseStatus.active,
        perpetual: true,
      ),
      reason: reason,
      correlationId: correlationId,
    );

    final entitlement =
        await firestore.collection('user_entitlements').doc(userId).get();
    final entitlementData = entitlement.data()!;
    final planProfile =
        entitlementData['planProfile'] as Map<String, dynamic>? ??
            const <String, dynamic>{};
    expect(planProfile['tier'], 'FREE');
  });

  test('upsertGrantAssignment writes grant and audit event', () async {
    const correlationId = 'corr-grant-1';
    const reason = 'temporary-access';

    await EntitlementAdminService.upsertGrantAssignment(
      assignment: EntitlementGrantAssignment(
        grantId: '',
        granteeUserId: 'mobile-user-2',
        scope: EntitlementGrantScope.app,
        gameId: null,
        licenseGrant: const LicenseGrant(
          status: LicenseStatus.active,
          perpetual: true,
        ),
        source: EntitlementGrantSource.admin,
        assignedBy: 'admin-user-1',
        assignedAtUtc: DateTime.utc(2026, 2, 18, 10),
        revoked: false,
        revokedAtUtc: null,
        roleOverride: null,
        note: 'smoke grant',
        reason: reason,
        correlationId: correlationId,
      ),
      reason: reason,
      correlationId: correlationId,
    );

    final grantsSnapshot =
        await firestore.collection('entitlement_grants').get();
    expect(grantsSnapshot.docs, hasLength(1));
    final grantData = grantsSnapshot.docs.single.data();
    expect(grantData['reason'], reason);
    expect(grantData['correlationId'], correlationId);
    expect(grantData['assignedBy'], 'admin-user-1');

    final auditSnapshot = await firestore
        .collection('admin_audit_trail')
        .where('correlationId', isEqualTo: correlationId)
        .get();
    expect(auditSnapshot.docs, hasLength(1));
    final auditData = auditSnapshot.docs.single.data();
    expect(auditData['action'], 'UPSERT_GRANT_ASSIGNMENT');
    expect(auditData['targetCollection'], 'entitlement_grants');
    expect(auditData['targetUserId'], 'mobile-user-2');
  });

  test('seedGameCatalog writes entries and audit event', () async {
    const correlationId = 'corr-seed-catalog-1';
    const reason = 'seed-catalog';

    await EntitlementAdminService.seedGameCatalog(
      entries: const <AdminGameCatalogSeedEntry>[
        AdminGameCatalogSeedEntry(
          gameId: 'demo_cube_clicker',
          title: 'Demo Cube Clicker',
          description: 'Demo',
          targetContentVersion: '1.2.0',
          packageUri: '',
          thumbnailUrl: '',
          supportsSaveResume: true,
          availableForPurchase: false,
          requiresExplicitLicense: false,
          runtimeLaunchEnabled: true,
          sortOrder: 10,
          active: true,
          previewLines: <String>['Preview'],
          mobileControlSchema: <String, dynamic>{
            'schema': 'THERAPLY_MOBILE_CONTROL_SCHEMA',
            'schemaVersion': '2026-02-25',
            'gameId': 'demo_cube_clicker',
            'title': 'Demo Controls',
            'layout': <String, dynamic>{'mode': 'stack', 'columns': 1},
            'payload': <String, dynamic>{
              'target': 'game_config',
              'gameConfigType': 'demo_cube_config_v1',
              'gameConfigVersion': 1,
            },
            'controls': <Map<String, dynamic>>[
              <String, dynamic>{
                'controlId': 'cube_count',
                'type': 'slider',
                'label': 'Cube Count',
                'binding': <String, dynamic>{
                  'target': 'game_config',
                  'path': 'cubeCount',
                  'valueType': 'int',
                },
              },
            ],
          },
        ),
        AdminGameCatalogSeedEntry(
          gameId: 'puzzle_paths',
          title: 'Puzzle Paths',
          description: 'Store placeholder',
          targetContentVersion: '0.9.0',
          packageUri: 'https://cdn.example/puzzle_paths_0_9_0',
          thumbnailUrl: '',
          supportsSaveResume: false,
          availableForPurchase: true,
          requiresExplicitLicense: true,
          runtimeLaunchEnabled: false,
          sortOrder: 30,
          active: true,
          previewLines: <String>['Store'],
        ),
      ],
      reason: reason,
      correlationId: correlationId,
    );

    final gameDoc = await firestore
        .collection('game_catalog')
        .doc('demo_cube_clicker')
        .get();
    expect(gameDoc.exists, isTrue);
    final payload = gameDoc.data()!;
    expect(payload['targetContentVersion'], '1.2.0');
    expect(payload['runtimeLaunchEnabled'], isTrue);
    expect(payload['updatedBy'], 'admin-user-1');
    expect(payload['correlationId'], correlationId);
    final mobileSchema =
        payload['mobileControlSchema'] as Map<String, dynamic>? ??
            const <String, dynamic>{};
    expect(mobileSchema['schema'], 'THERAPLY_MOBILE_CONTROL_SCHEMA');

    final auditSnapshot = await firestore
        .collection('admin_audit_trail')
        .where('correlationId', isEqualTo: correlationId)
        .get();
    expect(auditSnapshot.docs, hasLength(1));
    expect(auditSnapshot.docs.single.data()['action'], 'SEED_GAME_CATALOG');
  });

  test('AdminGameCatalogSeedEntry.fromSeedMap parses optional mobile schema', () {
    final entry = AdminGameCatalogSeedEntry.fromSeedMap(
      <String, dynamic>{
        'gameId': 'pulse_target_tap',
        'title': 'Pulse Targets',
        'description': 'Demo',
        'targetContentVersion': '1.0.0',
        'packageUri': '',
        'thumbnailUrl': '',
        'supportsSaveResume': false,
        'availableForPurchase': false,
        'requiresExplicitLicense': false,
        'runtimeLaunchEnabled': true,
        'sortOrder': 20,
        'active': true,
        'previewLines': <String>['Adaptive'],
        'mobileControlSchema': <String, dynamic>{
          'schema': 'THERAPLY_MOBILE_CONTROL_SCHEMA',
          'schemaVersion': '2026-02-25',
          'gameId': 'pulse_target_tap',
        },
      },
    );

    expect(entry.gameId, 'pulse_target_tap');
    expect(entry.mobileControlSchema, isNotNull);
    expect(entry.mobileControlSchema?['schema'], 'THERAPLY_MOBILE_CONTROL_SCHEMA');
  });
}
