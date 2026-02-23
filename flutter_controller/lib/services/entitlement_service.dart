import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:firebase_auth/firebase_auth.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_controller/models/entitlement_access.dart';
import 'package:flutter_controller/models/entitlement_grant_contract.dart';
import 'package:flutter_controller/services/firebase_service.dart';

class EntitlementService {
  static FirebaseFirestore? _firestoreOverride;
  static FirebaseFirestore get _firestore =>
      _firestoreOverride ?? FirebaseService.firestore;
  static CollectionReference<Map<String, dynamic>>
      get _entitlementsCollection => _firestore.collection('user_entitlements');
  static CollectionReference<Map<String, dynamic>> get _grantsCollection =>
      _firestore.collection('entitlement_grants');
  static const bool _strictEntitlementGateFlag = bool.fromEnvironment(
    'STRICT_ENTITLEMENT_GATE',
    defaultValue: false,
  );
  static const bool _enableDevEntitlementBootstrapFlag = bool.fromEnvironment(
    'ENABLE_DEV_ENTITLEMENT_BOOTSTRAP',
    defaultValue: false,
  );
  static bool? _strictEntitlementGateOverrideForTesting;
  static bool? _enableDevEntitlementBootstrapOverrideForTesting;

  static EntitlementAccess? _activeAccess;

  static EntitlementAccess? get activeAccess => _activeAccess;
  static bool get isStrictEntitlementGateEnabled =>
      _strictEntitlementGateEnabled;
  static bool get isDevEntitlementBootstrapEnabled =>
      _devEntitlementBootstrapEnabled;

  static bool get _strictEntitlementGateEnabled {
    final override = _strictEntitlementGateOverrideForTesting;
    if (override != null) {
      return override;
    }

    // Release builds must never silently fallback to legacy entitlement mode.
    if (kReleaseMode) {
      return true;
    }

    return _strictEntitlementGateFlag;
  }

  static bool get _devEntitlementBootstrapEnabled {
    final override = _enableDevEntitlementBootstrapOverrideForTesting;
    if (override != null) {
      return override;
    }

    // Development bootstrap is never allowed in release profile.
    if (kReleaseMode) {
      return false;
    }

    return _enableDevEntitlementBootstrapFlag;
  }

  @visibleForTesting
  static void setFirestoreInstanceForTesting(FirebaseFirestore firestore) {
    _firestoreOverride = firestore;
  }

  @visibleForTesting
  static void clearFirestoreInstanceForTesting() {
    _firestoreOverride = null;
    _activeAccess = null;
    _strictEntitlementGateOverrideForTesting = null;
    _enableDevEntitlementBootstrapOverrideForTesting = null;
  }

  @visibleForTesting
  static void setGateFlagsForTesting({
    bool? strictEntitlementGate,
    bool? enableDevEntitlementBootstrap,
  }) {
    _strictEntitlementGateOverrideForTesting = strictEntitlementGate;
    _enableDevEntitlementBootstrapOverrideForTesting =
        enableDevEntitlementBootstrap;
  }

  static Future<bool> tryBootstrapDevelopmentEntitlement(User user) async {
    if (!_devEntitlementBootstrapEnabled) {
      return false;
    }

    final tokenResult = await user.getIdTokenResult();
    final claims = tokenResult.claims ?? const <String, dynamic>{};
    final role = (claims['role'] as String?)?.trim().toLowerCase();
    final isAdminOperator =
        claims['admin_operator'] == true || role == 'admin_operator';
    if (!isAdminOperator) {
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
          'planProfile': EntitlementPlanProfile.fromMap(
            const <String, dynamic>{'tier': 'BASIC'},
          ).toMap(),
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
    return evaluateLoginGateForUserId(user.uid);
  }

  @visibleForTesting
  static Future<EntitlementGateDecision> evaluateLoginGateForUserId(
    String userId,
  ) async {
    final normalizedUserId = userId.trim();
    if (normalizedUserId.isEmpty) {
      if (_strictEntitlementGateEnabled) {
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

    final nowUtc = DateTime.now().toUtc();

    try {
      final docSnapshot =
          await _entitlementsCollection.doc(normalizedUserId).get();
      if (!docSnapshot.exists) {
        if (_strictEntitlementGateEnabled) {
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
        sourceTag: 'firestore:user_entitlements/$normalizedUserId',
      );
      final grants = await _fetchEffectiveGrantsForUser(
        userId: normalizedUserId,
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
      if (_strictEntitlementGateEnabled) {
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

  static bool isFeatureEnabled(String featureKey) {
    return _activeAccess?.isFeatureEnabled(featureKey) ?? false;
  }

  static bool hasRemainingDemoSessions() {
    final access = _activeAccess;
    if (access == null) {
      return false;
    }
    return access.planProfile.hasDemoSessionsRemaining;
  }

  static bool canLaunchGame(String gameId, {DateTime? atUtc}) {
    final access = _activeAccess;
    if (access == null) {
      return false;
    }

    final nowUtc = atUtc ?? DateTime.now().toUtc();
    return access.hasGameAccess(gameId: gameId, atUtc: nowUtc) &&
        access.isGameAllowedByPlan(gameId);
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
    final gameLicenses =
        Map<String, LicenseGrant>.from(baseAccess.gameLicenses);

    for (final grant in grants) {
      if (grant.roleOverride != null) {
        role = grant.roleOverride!;
      }

      switch (grant.scope) {
        case EntitlementGrantScope.app:
          if (!appLicense.isActiveAt(nowUtc) ||
              grant.licenseGrant.isActiveAt(nowUtc)) {
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
      planProfile: baseAccess.planProfile,
      sourceTag: '${baseAccess.sourceTag}+grants',
      policyVersion: baseAccess.policyVersion,
    );
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
      planProfile: EntitlementPlanProfile(
        tier: SubscriptionPlanTier.basic,
        featureFlags: <String, bool>{
          EntitlementFeatureKeys.vrSessionControl: true,
          EntitlementFeatureKeys.parentGuidedStart: true,
          EntitlementFeatureKeys.progressInsights: true,
          EntitlementFeatureKeys.rewardsUnlocks: true,
        },
        allowedGameIds: <String>{},
        demoSessionLimit: 0,
        demoSessionsUsed: 0,
      ),
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
      planProfile: EntitlementPlanProfile(
        tier: SubscriptionPlanTier.unknown,
        featureFlags: <String, bool>{},
        allowedGameIds: <String>{},
        demoSessionLimit: 0,
        demoSessionsUsed: 0,
      ),
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
