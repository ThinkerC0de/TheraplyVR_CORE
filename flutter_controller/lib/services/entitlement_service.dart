import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:firebase_auth/firebase_auth.dart';
import 'package:flutter_controller/models/entitlement_access.dart';
import 'package:flutter_controller/models/entitlement_grant_contract.dart';
import 'package:flutter_controller/services/firebase_service.dart';

class EntitlementService {
  static final CollectionReference<Map<String, dynamic>>
      _entitlementsCollection =
      FirebaseService.firestore.collection('user_entitlements');
  static final CollectionReference<Map<String, dynamic>> _grantsCollection =
      FirebaseService.firestore.collection('entitlement_grants');
  static final CollectionReference<Map<String, dynamic>>
      _grantRequestsCollection =
      FirebaseService.firestore.collection('entitlement_grant_requests');
  static const bool _strictEntitlementGate = bool.fromEnvironment(
    'STRICT_ENTITLEMENT_GATE',
    defaultValue: false,
  );
  static const bool _enableDevEntitlementBootstrap = bool.fromEnvironment(
    'ENABLE_DEV_ENTITLEMENT_BOOTSTRAP',
    defaultValue: false,
  );

  static EntitlementAccess? _activeAccess;

  static EntitlementAccess? get activeAccess => _activeAccess;
  static bool get isStrictEntitlementGateEnabled => _strictEntitlementGate;
  static bool get isDevEntitlementBootstrapEnabled =>
      _enableDevEntitlementBootstrap;

  static Future<void> upsertUserEntitlement({
    required String userId,
    required EntitlementRole role,
    required LicenseGrant appLicense,
    Map<String, LicenseGrant> gameLicenses = const <String, LicenseGrant>{},
    String? policyVersion,
    String? updatedBy,
  }) async {
    final normalizedUserId = userId.trim();
    if (normalizedUserId.isEmpty) {
      throw ArgumentError('userId is required');
    }

    final nowUtc = DateTime.now().toUtc();
    final gameLicensesWire = gameLicenses.map(
      (key, value) => MapEntry(key, value.toMap()),
    );
    final actor = updatedBy ?? FirebaseAuth.instance.currentUser?.uid ?? 'ops-panel';

    await _entitlementsCollection.doc(normalizedUserId).set(
      <String, dynamic>{
        'role': role.wireValue,
        'appLicense': appLicense.toMap(),
        'gameLicenses': gameLicensesWire,
        'policyVersion': policyVersion ?? 'ops-panel-v1',
        'updatedAtUtc': nowUtc.toIso8601String(),
        'updatedBy': actor,
      },
      SetOptions(merge: true),
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
        return EntitlementAccess.fromBackend(
          data: payload,
          sourceTag: 'firestore:user_entitlements/$normalizedUserId',
        );
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
        .map((querySnapshot) {
      final grants = querySnapshot.docs
          .map(EntitlementGrantAssignment.fromFirestore)
          .toList();
      grants.sort((a, b) => b.assignedAtUtc.compareTo(a.assignedAtUtc));
      return grants;
    });
  }

  static Future<bool> tryBootstrapDevelopmentEntitlement(User user) async {
    if (!_enableDevEntitlementBootstrap) {
      return false;
    }

    try {
      final docRef = _entitlementsCollection.doc(user.uid);
      final snapshot = await docRef.get();
      if (snapshot.exists) {
        return false;
      }

      final nowUtc = DateTime.now().toUtc();
      await docRef.set(
        <String, dynamic>{
          'role': EntitlementRole.therapist.wireValue,
          'appLicense': <String, dynamic>{
            'status': LicenseStatus.active.wireValue,
            'fromUtc': nowUtc.toIso8601String(),
            'toUtc': null,
            'perpetual': true,
          },
          'gameLicenses': <String, dynamic>{},
          'policyVersion': 'dev-bootstrap-v1',
          'updatedAtUtc': nowUtc.toIso8601String(),
          'updatedBy': 'dev-bootstrap:${user.uid}',
        },
      );

      return true;
    } catch (_) {
      return false;
    }
  }

  static Future<EntitlementGateDecision> evaluateLoginGate(User user) async {
    final nowUtc = DateTime.now().toUtc();

    try {
      final docSnapshot = await _entitlementsCollection.doc(user.uid).get();
      if (!docSnapshot.exists) {
        if (_strictEntitlementGate) {
          return _buildStrictDeniedDecision(
            reasonCode: 'ENTITLEMENT_RECORD_REQUIRED',
            message:
                'Entitlement profile is required and missing. Contact your administrator.',
          );
        }

        final fallbackDecision = _buildLegacyFallbackDecision(
          reasonCode: 'LEGACY_FALLBACK_NO_RECORD',
          message:
              'No entitlement profile found. Legacy therapist access applied.',
        );
        _activeAccess = fallbackDecision.access;
        return fallbackDecision;
      }

      final payload = docSnapshot.data() ?? <String, dynamic>{};
      final access = EntitlementAccess.fromBackend(
        data: payload,
        sourceTag: 'firestore:user_entitlements/${user.uid}',
      );
      final grants = await _fetchEffectiveGrantsForUser(
        userId: user.uid,
        atUtc: nowUtc,
      );
      final effectiveAccess = _applyGrantsToAccess(
        baseAccess: access,
        grants: grants,
        nowUtc: nowUtc,
      );

      final decision = _evaluateAccess(effectiveAccess, nowUtc);
      _activeAccess = decision.isAllowed ? effectiveAccess : null;
      return decision;
    } catch (e) {
      if (_strictEntitlementGate) {
        return _buildStrictDeniedDecision(
          reasonCode: 'ENTITLEMENT_BACKEND_UNAVAILABLE',
          message:
              'Entitlement backend is unavailable and strict gate is enabled.',
        );
      }

      final fallbackDecision = _buildLegacyFallbackDecision(
        reasonCode: 'LEGACY_FALLBACK_FIRESTORE_ERROR',
        message:
            'Entitlement backend unavailable. Legacy therapist access applied.',
      );
      _activeAccess = fallbackDecision.access;
      return fallbackDecision;
    }
  }

  static void clearSessionAccess() {
    _activeAccess = null;
  }

  static EntitlementGateDecision _evaluateAccess(
    EntitlementAccess access,
    DateTime nowUtc,
  ) {
    if (access.role == EntitlementRole.unknown) {
      return EntitlementGateDecision(
        isAllowed: false,
        reasonCode: 'ROLE_UNDEFINED',
        message: 'Your role is not allowed to access this application.',
        access: access,
      );
    }

    if (!access.hasAppAccess(nowUtc)) {
      return EntitlementGateDecision(
        isAllowed: false,
        reasonCode: 'APP_LICENSE_INACTIVE',
        message:
            'Your app license is inactive or expired. Contact your administrator.',
        access: access,
      );
    }

    return EntitlementGateDecision(
      isAllowed: true,
      reasonCode: 'APP_LICENSE_ACTIVE',
      message: 'Access granted.',
      access: access,
    );
  }

  static Future<List<EntitlementGrantAssignment>> _fetchEffectiveGrantsForUser({
    required String userId,
    required DateTime atUtc,
  }) async {
    final querySnapshot = await _grantsCollection
        .where('granteeUserId', isEqualTo: userId)
        .where('revoked', isEqualTo: false)
        .get();

    final grants = querySnapshot.docs
        .map(EntitlementGrantAssignment.fromFirestore)
        .where((grant) => grant.isEffectiveAt(atUtc))
        .toList();

    return grants;
  }

  static EntitlementAccess _applyGrantsToAccess({
    required EntitlementAccess baseAccess,
    required List<EntitlementGrantAssignment> grants,
    required DateTime nowUtc,
  }) {
    if (grants.isEmpty) {
      return baseAccess;
    }

    var role = baseAccess.role;
    var appLicense = baseAccess.appLicense;
    final gameLicenses = Map<String, LicenseGrant>.from(baseAccess.gameLicenses);

    for (final grant in grants) {
      if (grant.roleOverride != null) {
        role = grant.roleOverride!;
      }

      switch (grant.scope) {
        case EntitlementGrantScope.app:
          if (!appLicense.isActiveAt(nowUtc) || grant.licenseGrant.isActiveAt(nowUtc)) {
            appLicense = grant.licenseGrant;
          }
          break;
        case EntitlementGrantScope.game:
          final gameId = grant.gameId;
          if (gameId != null && gameId.trim().isNotEmpty) {
            gameLicenses[gameId] = grant.licenseGrant;
          }
          break;
      }
    }

    return EntitlementAccess(
      role: role,
      appLicense: appLicense,
      gameLicenses: gameLicenses,
      sourceTag: '${baseAccess.sourceTag}+grants',
      policyVersion: baseAccess.policyVersion,
    );
  }

  static Future<String> submitGrantRequest({
    required EntitlementGrantRequest request,
  }) async {
    final requestId =
        request.requestId.isNotEmpty ? request.requestId : _grantRequestsCollection.doc().id;
    await _grantRequestsCollection.doc(requestId).set(request.toFirestore());
    return requestId;
  }

  static Future<String> upsertGrantAssignment({
    required EntitlementGrantAssignment assignment,
  }) async {
    final grantId =
        assignment.grantId.isNotEmpty ? assignment.grantId : _grantsCollection.doc().id;
    await _grantsCollection.doc(grantId).set(assignment.toFirestore());
    return grantId;
  }

  static EntitlementGateDecision _buildLegacyFallbackDecision({
    required String reasonCode,
    required String message,
  }) {
    final fallbackAccess = const EntitlementAccess(
      role: EntitlementRole.therapist,
      appLicense: LicenseGrant(
        status: LicenseStatus.active,
        perpetual: true,
      ),
      gameLicenses: <String, LicenseGrant>{},
      sourceTag: 'legacy-default',
      policyVersion: 'legacy-v1',
    );

    return EntitlementGateDecision(
      isAllowed: true,
      reasonCode: reasonCode,
      message: message,
      access: fallbackAccess,
      usedLegacyFallback: true,
    );
  }

  static EntitlementGateDecision _buildStrictDeniedDecision({
    required String reasonCode,
    required String message,
  }) {
    const deniedAccess = EntitlementAccess(
      role: EntitlementRole.unknown,
      appLicense: LicenseGrant(status: LicenseStatus.none),
      gameLicenses: <String, LicenseGrant>{},
      sourceTag: 'strict-denied',
      policyVersion: 'strict-v1',
    );

    _activeAccess = null;
    return EntitlementGateDecision(
      isAllowed: false,
      reasonCode: reasonCode,
      message: message,
      access: deniedAccess,
      usedLegacyFallback: false,
    );
  }
}
