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

  test('deleteUserEntitlement removes document and writes audit', () async {
    const userId = 'delete-me-user';
    await firestore.collection('user_entitlements').doc(userId).set(
      <String, dynamic>{
        'role': 'THERAPIST',
        'appLicense': <String, dynamic>{'status': 'ACTIVE', 'perpetual': true},
      },
    );

    await EntitlementAdminService.deleteUserEntitlement(
      userId: userId,
      reason: 'manual-delete',
      correlationId: 'corr-delete-entitlement-1',
    );

    final entitlement =
        await firestore.collection('user_entitlements').doc(userId).get();
    expect(entitlement.exists, isFalse);

    final auditSnapshot = await firestore
        .collection('admin_audit_trail')
        .where('targetCollection', isEqualTo: 'user_entitlements')
        .where('targetDocumentId', isEqualTo: userId)
        .get();
    expect(auditSnapshot.docs, hasLength(1));
    expect(
        auditSnapshot.docs.single.data()['action'], 'DELETE_USER_ENTITLEMENT');
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
    expect(payload['contentVersion'], '1.2.0');
    expect(payload['sceneKey'], 'demo_cube_clicker');
    expect(payload['entitlementKey'], 'game:demo_cube_clicker');
    expect(payload['deliveryMode'], 'bundled');
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

  test('AdminGameCatalogSeedEntry.fromSeedMap parses optional mobile schema',
      () {
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
    expect(entry.contentVersion, '1.0.0');
    expect(entry.sceneKey, 'pulse_target_tap');
    expect(entry.entitlementKey, 'game:pulse_target_tap');
    expect(entry.deliveryMode, 'bundled');
    expect(entry.parameterSchema, isNull);
    expect(entry.mobileControlSchema, isNotNull);
    expect(
        entry.mobileControlSchema?['schema'], 'THERAPLY_MOBILE_CONTROL_SCHEMA');
  });

  test('AdminGameCatalogSeedEntry.fromSeedMap parses shared contract fields',
      () {
    final entry = AdminGameCatalogSeedEntry.fromSeedMap(
      <String, dynamic>{
        'gameId': 'demo_cube_clicker',
        'title': 'Demo',
        'description': 'Demo',
        'targetContentVersion': '1.2.0',
        'contentVersion': '1.3.0',
        'sceneKey': 'DemoCubeScene',
        'entitlementKey': 'game:demo_cube_clicker',
        'deliveryMode': 'on_demand',
        'parameterSchema': <String, dynamic>{
          'schema': 'demo_parameter_schema',
          'schemaVersion': '2026-02-25',
        },
        'packageUri': '',
        'thumbnailUrl': '',
        'supportsSaveResume': true,
        'availableForPurchase': false,
        'requiresExplicitLicense': false,
        'runtimeLaunchEnabled': true,
        'sortOrder': 10,
        'active': true,
        'previewLines': <String>['Preview'],
      },
    );

    expect(entry.contentVersion, '1.3.0');
    expect(entry.sceneKey, 'DemoCubeScene');
    expect(entry.entitlementKey, 'game:demo_cube_clicker');
    expect(entry.deliveryMode, 'on_demand');
    expect(entry.parameterSchema?['schema'], 'demo_parameter_schema');
  });

  test('upsertGameCatalogEntry writes row and audit event', () async {
    const correlationId = 'corr-catalog-upsert-1';
    const reason = 'catalog-manual-edit';
    const gameId = 'board_probe_game';

    await EntitlementAdminService.upsertGameCatalogEntry(
      entry: const AdminGameCatalogSeedEntry(
        gameId: gameId,
        title: 'Board Probe',
        description: 'Board entry',
        targetContentVersion: '1.0.0',
        packageUri: 'https://example.com/probe.pkg.json',
        thumbnailUrl: 'https://example.com/thumb.png',
        supportsSaveResume: false,
        availableForPurchase: false,
        requiresExplicitLicense: false,
        runtimeLaunchEnabled: true,
        sortOrder: 77,
        active: true,
        previewLines: <String>['line1'],
      ),
      reason: reason,
      correlationId: correlationId,
      action: 'CREATE_GAME_CATALOG_ENTRY',
    );

    final doc = await firestore.collection('game_catalog').doc(gameId).get();
    final data = doc.data()!;
    expect(data['title'], 'Board Probe');
    expect(data['sortOrder'], 77);
    expect(data['updatedBy'], 'admin-user-1');
    expect(data['correlationId'], correlationId);

    final auditSnapshot = await firestore
        .collection('admin_audit_trail')
        .where('correlationId', isEqualTo: correlationId)
        .get();
    expect(auditSnapshot.docs, hasLength(1));
    expect(auditSnapshot.docs.single.data()['action'],
        'CREATE_GAME_CATALOG_ENTRY');
  });

  test('watchGameCatalog returns rows sorted by sortOrder then gameId',
      () async {
    await firestore.collection('game_catalog').doc('game-z').set(
      <String, dynamic>{
        'gameId': 'game-z',
        'title': 'Game Z',
        'targetContentVersion': '1.0.0',
        'sortOrder': 20,
      },
    );
    await firestore.collection('game_catalog').doc('game-a').set(
      <String, dynamic>{
        'gameId': 'game-a',
        'title': 'Game A',
        'targetContentVersion': '1.0.0',
        'sortOrder': 10,
      },
    );

    final rows = await EntitlementAdminService.watchGameCatalog().first;
    expect(rows, hasLength(2));
    expect(rows.first.gameId, 'game-a');
    expect(rows.last.gameId, 'game-z');
  });

  test('upsert and delete student record write students collection and audit',
      () async {
    final createdStudentId = await EntitlementAdminService.upsertStudentRecord(
      studentId: '',
      therapistId: 'therapist-1',
      firstName: 'Jan',
      lastName: 'Kowalski',
      reason: 'student-create',
      correlationId: 'corr-student-create-1',
      action: 'CREATE_STUDENT_DIRECTORY_ENTRY',
    );

    final created =
        await firestore.collection('students').doc(createdStudentId).get();
    expect(created.exists, isTrue);
    final createdData = created.data()!;
    expect(createdData['therapistId'], 'therapist-1');
    expect(createdData['firstName'], 'Jan');

    await EntitlementAdminService.upsertStudentRecord(
      studentId: createdStudentId,
      therapistId: 'therapist-2',
      firstName: 'Janina',
      lastName: 'Kowalska',
      reason: 'student-update',
      correlationId: 'corr-student-update-1',
      action: 'UPDATE_STUDENT_DIRECTORY_ENTRY',
    );

    final updated =
        await firestore.collection('students').doc(createdStudentId).get();
    final updatedData = updated.data()!;
    expect(updatedData['therapistId'], 'therapist-2');
    expect(updatedData['firstName'], 'Janina');

    await EntitlementAdminService.deleteStudentRecord(
      studentId: createdStudentId,
      reason: 'student-delete',
      correlationId: 'corr-student-delete-1',
    );

    final deleted =
        await firestore.collection('students').doc(createdStudentId).get();
    expect(deleted.exists, isFalse);

    final auditSnapshot = await firestore
        .collection('admin_audit_trail')
        .where('targetCollection', isEqualTo: 'students')
        .where('targetDocumentId', isEqualTo: createdStudentId)
        .get();
    final actions = auditSnapshot.docs
        .map((doc) => doc.data()['action'] as String? ?? '')
        .toSet();
    expect(actions.contains('CREATE_STUDENT_DIRECTORY_ENTRY'), isTrue);
    expect(actions.contains('UPDATE_STUDENT_DIRECTORY_ENTRY'), isTrue);
    expect(actions.contains('DELETE_STUDENT_DIRECTORY_ENTRY'), isTrue);
  });

  test('deactivate and delete game catalog entry update data and audit',
      () async {
    const gameId = 'game-to-disable';
    await firestore.collection('game_catalog').doc(gameId).set(
      <String, dynamic>{
        'gameId': gameId,
        'title': 'Disable Me',
        'targetContentVersion': '1.0.0',
        'sortOrder': 5,
        'active': true,
        'runtimeLaunchEnabled': true,
      },
    );

    await EntitlementAdminService.deactivateGameCatalogEntry(
      gameId: gameId,
      reason: 'ops-deactivate',
      correlationId: 'corr-catalog-deactivate-1',
    );

    final deactivated =
        await firestore.collection('game_catalog').doc(gameId).get();
    final deactivatedData = deactivated.data()!;
    expect(deactivatedData['active'], isFalse);
    expect(deactivatedData['runtimeLaunchEnabled'], isFalse);

    await EntitlementAdminService.deleteGameCatalogEntry(
      gameId: gameId,
      reason: 'ops-delete',
      correlationId: 'corr-catalog-delete-1',
    );

    final deleted =
        await firestore.collection('game_catalog').doc(gameId).get();
    expect(deleted.exists, isFalse);

    final auditSnapshot = await firestore
        .collection('admin_audit_trail')
        .where('targetDocumentId', isEqualTo: gameId)
        .get();
    final actions = auditSnapshot.docs
        .map((doc) => doc.data()['action'] as String? ?? '')
        .toSet();
    expect(actions.contains('DEACTIVATE_GAME_CATALOG_ENTRY'), isTrue);
    expect(actions.contains('DELETE_GAME_CATALOG_ENTRY'), isTrue);
  });

  test('watchRecentTherapySessions returns sorted session rows', () async {
    await firestore.collection('therapy_sessions').doc('session-a').set(
      <String, dynamic>{
        'sessionId': 'session-a',
        'studentId': 'student-1',
        'therapistId': 'therapist-1',
        'state': 'IN_PROGRESS',
        'updatedAtUnixMs': 1000,
        'updatedAtUtc': '2026-03-02T12:00:00Z',
      },
    );
    await firestore.collection('therapy_sessions').doc('session-b').set(
      <String, dynamic>{
        'sessionId': 'session-b',
        'studentId': 'student-2',
        'therapistId': 'therapist-2',
        'state': 'COMPLETED',
        'isTerminal': true,
        'reasonCode': 'THERAPIST_CONFIRMED_END',
        'updatedAtUnixMs': 3000,
        'updatedAtUtc': '2026-03-02T12:05:00Z',
      },
    );

    final sessions =
        await EntitlementAdminService.watchRecentTherapySessions().first;
    expect(sessions, hasLength(2));
    expect(sessions.first.sessionId, 'session-b');
    expect(sessions.first.isTerminal, isTrue);
    expect(sessions.first.reasonCode, 'THERAPIST_CONFIRMED_END');
    expect(sessions.last.sessionId, 'session-a');
  });

  test('watchSessionEvents streams ordered events for selected session',
      () async {
    await firestore.collection('therapy_sessions').doc('session-x').set(
      <String, dynamic>{
        'sessionId': 'session-x',
        'studentId': 'student-x',
        'therapistId': 'therapist-x',
        'state': 'IN_PROGRESS',
        'updatedAtUnixMs': 5000,
      },
    );

    final events = firestore
        .collection('therapy_sessions')
        .doc('session-x')
        .collection('events');

    await events.doc('event-old').set(
      <String, dynamic>{
        'sessionId': 'session-x',
        'studentId': 'student-x',
        'therapistId': 'therapist-x',
        'eventType': 'GAME_START',
        'source': 'mobile_controller',
        'eventAtUnixMs': 1000,
        'eventAtUtc': '2026-03-02T11:00:00Z',
        'details': <String, dynamic>{'reasonCode': 'MANUAL_START'},
      },
    );
    await events.doc('event-new').set(
      <String, dynamic>{
        'sessionId': 'session-x',
        'studentId': 'student-x',
        'therapistId': 'therapist-x',
        'eventType': 'GAME_STOP',
        'source': 'mobile_controller',
        'eventAtUnixMs': 2000,
        'eventAtUtc': '2026-03-02T11:01:00Z',
        'details': <String, dynamic>{'reasonCode': 'THERAPIST_STOP'},
      },
    );

    final streamed = await EntitlementAdminService.watchSessionEvents(
      sessionId: 'session-x',
      limit: 20,
    ).first;

    expect(streamed, hasLength(2));
    expect(streamed.first.eventId, 'event-new');
    expect(streamed.first.eventType, 'GAME_STOP');
    expect(streamed.first.details['reasonCode'], 'THERAPIST_STOP');
    expect(streamed.last.eventId, 'event-old');
  });
}
