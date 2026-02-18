import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:admin_console_web/models/entitlement_access.dart';

enum EntitlementGrantScope {
  app,
  game,
}

extension EntitlementGrantScopeCodec on EntitlementGrantScope {
  static EntitlementGrantScope fromWire(String? wireValue) {
    switch (wireValue?.trim().toUpperCase()) {
      case 'GAME':
        return EntitlementGrantScope.game;
      case 'APP':
      default:
        return EntitlementGrantScope.app;
    }
  }

  String get wireValue {
    switch (this) {
      case EntitlementGrantScope.app:
        return 'APP';
      case EntitlementGrantScope.game:
        return 'GAME';
    }
  }
}

enum EntitlementGrantSource {
  admin,
  system,
}

extension EntitlementGrantSourceCodec on EntitlementGrantSource {
  static EntitlementGrantSource fromWire(String? wireValue) {
    switch (wireValue?.trim().toUpperCase()) {
      case 'SYSTEM':
        return EntitlementGrantSource.system;
      case 'ADMIN':
      default:
        return EntitlementGrantSource.admin;
    }
  }

  String get wireValue {
    switch (this) {
      case EntitlementGrantSource.admin:
        return 'ADMIN';
      case EntitlementGrantSource.system:
        return 'SYSTEM';
    }
  }
}

class EntitlementGrantAssignment {
  final String grantId;
  final String granteeUserId;
  final EntitlementGrantScope scope;
  final String? gameId;
  final LicenseGrant licenseGrant;
  final EntitlementGrantSource source;
  final String assignedBy;
  final DateTime assignedAtUtc;
  final bool revoked;
  final DateTime? revokedAtUtc;
  final EntitlementRole? roleOverride;
  final String? note;

  const EntitlementGrantAssignment({
    required this.grantId,
    required this.granteeUserId,
    required this.scope,
    required this.gameId,
    required this.licenseGrant,
    required this.source,
    required this.assignedBy,
    required this.assignedAtUtc,
    required this.revoked,
    required this.revokedAtUtc,
    required this.roleOverride,
    required this.note,
  });

  factory EntitlementGrantAssignment.fromFirestore(
    DocumentSnapshot<Map<String, dynamic>> doc,
  ) {
    final data = doc.data() ?? <String, dynamic>{};
    return EntitlementGrantAssignment(
      grantId: doc.id,
      granteeUserId: data['granteeUserId'] as String? ?? '',
      scope: EntitlementGrantScopeCodec.fromWire(data['scope'] as String?),
      gameId: data['gameId'] as String?,
      licenseGrant:
          LicenseGrant.fromMap(data['licenseGrant'] as Map<String, dynamic>?),
      source:
          EntitlementGrantSourceCodec.fromWire(data['source'] as String?),
      assignedBy: data['assignedBy'] as String? ?? '',
      assignedAtUtc: _toUtcDateTime(data['assignedAtUtc']) ??
          DateTime.fromMillisecondsSinceEpoch(0, isUtc: true),
      revoked: data['revoked'] as bool? ?? false,
      revokedAtUtc: _toUtcDateTime(data['revokedAtUtc']),
      roleOverride: _parseRoleOrNull(data['role'] as String?),
      note: data['note'] as String?,
    );
  }

  Map<String, dynamic> toFirestore() {
    return <String, dynamic>{
      'granteeUserId': granteeUserId,
      'scope': scope.wireValue,
      'gameId': gameId,
      'licenseGrant': licenseGrant.toMap(),
      'source': source.wireValue,
      'assignedBy': assignedBy,
      'assignedAtUtc': assignedAtUtc.toUtc().toIso8601String(),
      'revoked': revoked,
      'revokedAtUtc': revokedAtUtc?.toUtc().toIso8601String(),
      'role': roleOverride?.wireValue,
      'note': note,
    };
  }

  static DateTime? _toUtcDateTime(dynamic value) {
    if (value is Timestamp) {
      return value.toDate().toUtc();
    }
    if (value is DateTime) {
      return value.toUtc();
    }
    if (value is String) {
      return DateTime.tryParse(value)?.toUtc();
    }
    return null;
  }

  static EntitlementRole? _parseRoleOrNull(String? wireValue) {
    if (wireValue == null || wireValue.trim().isEmpty) {
      return null;
    }

    final parsed = EntitlementRoleCodec.fromWire(wireValue);
    return parsed == EntitlementRole.unknown ? null : parsed;
  }
}
