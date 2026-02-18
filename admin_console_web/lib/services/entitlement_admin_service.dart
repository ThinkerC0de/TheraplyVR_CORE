import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:firebase_auth/firebase_auth.dart';
import 'package:admin_console_web/models/entitlement_access.dart';
import 'package:admin_console_web/models/entitlement_grant_contract.dart';
import 'package:admin_console_web/services/firebase_service.dart';

class EntitlementAdminService {
  static final CollectionReference<Map<String, dynamic>>
      _entitlementsCollection =
      FirebaseService.firestore.collection('user_entitlements');
  static final CollectionReference<Map<String, dynamic>> _grantsCollection =
      FirebaseService.firestore.collection('entitlement_grants');

  static Future<void> upsertUserEntitlement({
    required String userId,
    required EntitlementRole role,
    required LicenseGrant appLicense,
    Map<String, LicenseGrant> gameLicenses = const <String, LicenseGrant>{},
    String? policyVersion,
  }) async {
    final normalizedUserId = userId.trim();
    if (normalizedUserId.isEmpty) {
      throw ArgumentError('userId is required');
    }

    final nowUtc = DateTime.now().toUtc();
    final actor = FirebaseAuth.instance.currentUser?.uid ?? 'admin-console-web';
    final gameLicensesWire = gameLicenses.map(
      (key, value) => MapEntry(key, value.toMap()),
    );

    await _entitlementsCollection.doc(normalizedUserId).set(
      <String, dynamic>{
        'role': role.wireValue,
        'appLicense': appLicense.toMap(),
        'gameLicenses': gameLicensesWire,
        'policyVersion': policyVersion ?? 'admin-console-web-v1',
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
      final list = snapshot.docs
          .map(EntitlementGrantAssignment.fromFirestore)
          .toList();
      list.sort((a, b) => b.assignedAtUtc.compareTo(a.assignedAtUtc));
      return list;
    });
  }

  static Future<String> upsertGrantAssignment({
    required EntitlementGrantAssignment assignment,
  }) async {
    final grantId = assignment.grantId.isNotEmpty
        ? assignment.grantId
        : _grantsCollection.doc().id;

    await _grantsCollection.doc(grantId).set(assignment.toFirestore());
    return grantId;
  }
}
