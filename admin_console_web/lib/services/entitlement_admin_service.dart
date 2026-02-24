import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:firebase_auth/firebase_auth.dart';
import 'package:flutter/foundation.dart';
import 'package:admin_console_web/models/admin_directory_models.dart';
import 'package:admin_console_web/models/entitlement_access.dart';
import 'package:admin_console_web/models/admin_audit_event.dart';
import 'package:admin_console_web/models/entitlement_grant_contract.dart';
import 'package:admin_console_web/services/firebase_service.dart';

class EntitlementAdminService {
  static FirebaseFirestore? _firestoreOverride;
  static _AdminActor? _actorOverride;

  static FirebaseFirestore get _firestore =>
      _firestoreOverride ?? FirebaseService.firestore;
  static CollectionReference<Map<String, dynamic>>
      get _entitlementsCollection => _firestore.collection('user_entitlements');
  static CollectionReference<Map<String, dynamic>> get _grantsCollection =>
      _firestore.collection('entitlement_grants');
  static CollectionReference<Map<String, dynamic>> get _auditCollection =>
      _firestore.collection('admin_audit_trail');
  static CollectionReference<Map<String, dynamic>> get _studentsCollection =>
      _firestore.collection('students');
  static CollectionReference<Map<String, dynamic>> get _gameCatalogCollection =>
      _firestore.collection('game_catalog');

  @visibleForTesting
  static void setFirestoreInstanceForTesting(FirebaseFirestore firestore) {
    _firestoreOverride = firestore;
  }

  @visibleForTesting
  static void setActorOverrideForTesting({
    required String uid,
    String? email,
    String role = 'admin_operator',
  }) {
    _actorOverride = _AdminActor(uid: uid, email: email, role: role);
  }

  @visibleForTesting
  static void clearTestOverrides() {
    _firestoreOverride = null;
    _actorOverride = null;
  }

  static String newCorrelationId() {
    final now = DateTime.now().toUtc().microsecondsSinceEpoch;
    final nonce = (now % 100000).toRadixString(36).padLeft(4, '0');
    return 'adm-$now-$nonce';
  }

  static Future<void> upsertUserEntitlement({
    required String userId,
    required EntitlementRole role,
    required LicenseGrant appLicense,
    required String reason,
    required String correlationId,
    Map<String, LicenseGrant> gameLicenses = const <String, LicenseGrant>{},
    EntitlementPlanProfile? planProfile,
    String? policyVersion,
  }) async {
    final normalizedUserId = _requireTrimmed(userId, 'userId');
    final normalizedReason = _requireTrimmed(reason, 'reason');
    final normalizedCorrelationId =
        _requireTrimmed(correlationId, 'correlationId');

    final nowUtc = DateTime.now().toUtc();
    final actor = await _resolveActor();
    final effectivePlanProfile =
        planProfile ?? _defaultPlanProfileForRole(role);
    final gameLicensesWire = gameLicenses.map(
      (key, value) => MapEntry(key, value.toMap()),
    );
    final entitlementPayload = <String, dynamic>{
      'role': role.wireValue,
      'appLicense': appLicense.toMap(),
      'gameLicenses': gameLicensesWire,
      'planProfile': effectivePlanProfile.toMap(),
      'policyVersion': policyVersion ?? 'admin-console-web-v1',
      'updatedAtUtc': nowUtc.toIso8601String(),
      'updatedBy': actor.uid,
      'updateReason': normalizedReason,
      'correlationId': normalizedCorrelationId,
    };

    final auditEvent = AdminAuditEvent(
      actorUid: actor.uid,
      actorEmail: actor.email,
      actorRole: actor.role,
      action: 'UPSERT_USER_ENTITLEMENT',
      targetCollection: 'user_entitlements',
      targetDocumentId: normalizedUserId,
      targetUserId: normalizedUserId,
      occurredAtUtc: nowUtc,
      reason: normalizedReason,
      correlationId: normalizedCorrelationId,
      payloadSummary: <String, dynamic>{
        'role': role.wireValue,
        'planTier': effectivePlanProfile.tier.wireValue,
        'appLicenseStatus': appLicense.status.wireValue,
        'policyVersion': policyVersion ?? 'admin-console-web-v1',
      },
    );

    final batch = _firestore.batch();
    batch.set(
      _entitlementsCollection.doc(normalizedUserId),
      entitlementPayload,
      SetOptions(merge: true),
    );
    batch.set(_auditCollection.doc(), auditEvent.toFirestore());
    await batch.commit();
  }

  static Future<_AdminActor> _resolveActor() async {
    if (_actorOverride != null) {
      return _actorOverride!;
    }

    final currentUser = FirebaseAuth.instance.currentUser;
    final tokenResult = await currentUser?.getIdTokenResult();
    final claims = tokenResult?.claims ?? const <String, dynamic>{};
    final role = (claims['role'] as String?)?.trim().toLowerCase();
    final isAdminOperator =
        claims['admin_operator'] == true || role == 'admin_operator';

    return _AdminActor(
      uid: currentUser?.uid ?? 'admin-console-web',
      email: currentUser?.email,
      role: isAdminOperator ? 'admin_operator' : 'unknown',
    );
  }

  static Stream<EntitlementAccess?> watchEntitlementProfile(String userId) {
    final normalizedUserId = userId.trim();
    if (normalizedUserId.isEmpty) {
      return Stream<EntitlementAccess?>.value(null);
    }

    return _entitlementsCollection.doc(normalizedUserId).snapshots().map(
      (docSnapshot) {
        if (!docSnapshot.exists) {
          return null;
        }

        final payload = docSnapshot.data() ?? <String, dynamic>{};
        return EntitlementAccess.fromBackend(payload);
      },
    );
  }

  static Stream<List<EntitlementGrantAssignment>> watchGrantAssignmentsForUser(
    String userId,
  ) {
    final normalizedUserId = userId.trim();
    if (normalizedUserId.isEmpty) {
      return Stream<List<EntitlementGrantAssignment>>.value(
        <EntitlementGrantAssignment>[],
      );
    }

    return _grantsCollection
        .where('granteeUserId', isEqualTo: normalizedUserId)
        .snapshots()
        .map((snapshot) {
      final list =
          snapshot.docs.map(EntitlementGrantAssignment.fromFirestore).toList();
      list.sort((a, b) => b.assignedAtUtc.compareTo(a.assignedAtUtc));
      return list;
    });
  }

  static Future<String> upsertGrantAssignment({
    required EntitlementGrantAssignment assignment,
    required String reason,
    required String correlationId,
    String action = 'UPSERT_GRANT_ASSIGNMENT',
  }) async {
    final normalizedReason = _requireTrimmed(reason, 'reason');
    final normalizedCorrelationId =
        _requireTrimmed(correlationId, 'correlationId');

    final grantId = assignment.grantId.isNotEmpty
        ? assignment.grantId
        : _grantsCollection.doc().id;
    final actor = await _resolveActor();
    final nowUtc = DateTime.now().toUtc();
    final payload = EntitlementGrantAssignment(
      grantId: grantId,
      granteeUserId: assignment.granteeUserId,
      scope: assignment.scope,
      gameId: assignment.gameId,
      licenseGrant: assignment.licenseGrant,
      source: assignment.source,
      assignedBy: assignment.assignedBy,
      assignedAtUtc: assignment.assignedAtUtc,
      revoked: assignment.revoked,
      revokedAtUtc: assignment.revokedAtUtc,
      roleOverride: assignment.roleOverride,
      note: assignment.note,
      reason: normalizedReason,
      correlationId: normalizedCorrelationId,
    );

    final auditEvent = AdminAuditEvent(
      actorUid: actor.uid,
      actorEmail: actor.email,
      actorRole: actor.role,
      action: action,
      targetCollection: 'entitlement_grants',
      targetDocumentId: grantId,
      targetUserId: assignment.granteeUserId,
      occurredAtUtc: nowUtc,
      reason: normalizedReason,
      correlationId: normalizedCorrelationId,
      payloadSummary: <String, dynamic>{
        'scope': assignment.scope.wireValue,
        'licenseStatus': assignment.licenseGrant.status.wireValue,
        'revoked': assignment.revoked,
        'source': assignment.source.wireValue,
      },
    );

    final batch = _firestore.batch();
    batch.set(_grantsCollection.doc(grantId), payload.toFirestore());
    batch.set(_auditCollection.doc(), auditEvent.toFirestore());
    await batch.commit();
    return grantId;
  }

  static Stream<List<AdminDirectoryUserRow>> watchUsersByRole(
    EntitlementRole role,
  ) {
    return _entitlementsCollection
        .where('role', isEqualTo: role.wireValue)
        .snapshots()
        .map((snapshot) {
      final list = snapshot.docs
          .map(AdminDirectoryUserRow.fromEntitlementDocument)
          .toList();
      list.sort((a, b) => a.userId.compareTo(b.userId));
      return list;
    });
  }

  static Stream<List<AdminStudentDirectoryRow>> watchStudents({
    int limit = 300,
  }) {
    return _studentsCollection.limit(limit).snapshots().map((snapshot) {
      final list = snapshot.docs
          .map(AdminStudentDirectoryRow.fromStudentDocument)
          .toList();
      list.sort((a, b) =>
          a.fullName.toLowerCase().compareTo(b.fullName.toLowerCase()));
      return list;
    });
  }

  static Stream<Map<String, AdminGameGrantStats>> watchGameGrantStats() {
    return _grantsCollection
        .where('scope', isEqualTo: EntitlementGrantScope.game.wireValue)
        .snapshots()
        .map((snapshot) {
      final statsByGameId = <String, AdminGameGrantStats>{};
      for (final doc in snapshot.docs) {
        final assignment = EntitlementGrantAssignment.fromFirestore(doc);
        final gameId = assignment.gameId?.trim();
        if (gameId == null || gameId.isEmpty) {
          continue;
        }

        final current = statsByGameId[gameId] ??
            const AdminGameGrantStats(
              activeAssignments: 0,
              revokedAssignments: 0,
            );
        final next = assignment.revoked
            ? AdminGameGrantStats(
                activeAssignments: current.activeAssignments,
                revokedAssignments: current.revokedAssignments + 1,
              )
            : AdminGameGrantStats(
                activeAssignments: current.activeAssignments + 1,
                revokedAssignments: current.revokedAssignments,
              );
        statsByGameId[gameId] = next;
      }
      return statsByGameId;
    });
  }

  static Future<void> seedGameCatalog({
    required List<AdminGameCatalogSeedEntry> entries,
    required String reason,
    required String correlationId,
  }) async {
    final normalizedReason = _requireTrimmed(reason, 'reason');
    final normalizedCorrelationId =
        _requireTrimmed(correlationId, 'correlationId');
    final actor = await _resolveActor();
    final nowUtc = DateTime.now().toUtc();

    final batch = _firestore.batch();
    final seededGameIds = <String>[];
    for (final entry in entries) {
      final gameId = entry.gameId.trim();
      if (gameId.isEmpty) {
        continue;
      }

      seededGameIds.add(gameId);
      batch.set(
        _gameCatalogCollection.doc(gameId),
        <String, dynamic>{
          ...entry.toFirestore(),
          'gameId': gameId,
          'updatedAtUtc': nowUtc.toIso8601String(),
          'updatedBy': actor.uid,
          'updateReason': normalizedReason,
          'correlationId': normalizedCorrelationId,
        },
        SetOptions(merge: true),
      );
    }

    final auditEvent = AdminAuditEvent(
      actorUid: actor.uid,
      actorEmail: actor.email,
      actorRole: actor.role,
      action: 'SEED_GAME_CATALOG',
      targetCollection: 'game_catalog',
      targetDocumentId: 'bulk',
      targetUserId: null,
      occurredAtUtc: nowUtc,
      reason: normalizedReason,
      correlationId: normalizedCorrelationId,
      payloadSummary: <String, dynamic>{
        'entries': seededGameIds.length,
        'gameIds': seededGameIds..sort(),
      },
    );
    batch.set(_auditCollection.doc(), auditEvent.toFirestore());
    await batch.commit();
  }

  static String _requireTrimmed(String value, String fieldName) {
    final trimmed = value.trim();
    if (trimmed.isEmpty) {
      throw ArgumentError('$fieldName is required');
    }
    return trimmed;
  }

  static EntitlementPlanProfile _defaultPlanProfileForRole(
    EntitlementRole role,
  ) {
    switch (role) {
      case EntitlementRole.parent:
        return EntitlementPlanProfile.fromMap(
          const <String, dynamic>{'tier': 'FREE'},
        );
      case EntitlementRole.therapist:
        return EntitlementPlanProfile.fromMap(
          const <String, dynamic>{'tier': 'BASIC'},
        );
      case EntitlementRole.unknown:
        return EntitlementPlanProfile.fromMap(
          const <String, dynamic>{'tier': 'UNKNOWN'},
        );
    }
  }
}

class _AdminActor {
  final String uid;
  final String? email;
  final String role;

  const _AdminActor({
    required this.uid,
    required this.email,
    required this.role,
  });
}

class AdminGameCatalogSeedEntry {
  final String gameId;
  final String title;
  final String description;
  final String targetContentVersion;
  final String packageUri;
  final String thumbnailUrl;
  final bool supportsSaveResume;
  final bool availableForPurchase;
  final bool requiresExplicitLicense;
  final bool runtimeLaunchEnabled;
  final int sortOrder;
  final bool active;
  final List<String> previewLines;

  const AdminGameCatalogSeedEntry({
    required this.gameId,
    required this.title,
    required this.description,
    required this.targetContentVersion,
    required this.packageUri,
    required this.thumbnailUrl,
    required this.supportsSaveResume,
    required this.availableForPurchase,
    required this.requiresExplicitLicense,
    required this.runtimeLaunchEnabled,
    required this.sortOrder,
    required this.active,
    required this.previewLines,
  });

  Map<String, dynamic> toFirestore() {
    return <String, dynamic>{
      'gameId': gameId.trim(),
      'title': title.trim(),
      'description': description.trim(),
      'targetContentVersion': targetContentVersion.trim(),
      'packageUri': packageUri.trim(),
      'thumbnailUrl': thumbnailUrl.trim(),
      'supportsSaveResume': supportsSaveResume,
      'availableForPurchase': availableForPurchase,
      'requiresExplicitLicense': requiresExplicitLicense,
      'runtimeLaunchEnabled': runtimeLaunchEnabled,
      'sortOrder': sortOrder,
      'active': active,
      'previewLines': List<String>.from(previewLines),
    };
  }
}
