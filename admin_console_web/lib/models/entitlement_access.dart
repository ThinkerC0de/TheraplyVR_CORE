import 'package:cloud_firestore/cloud_firestore.dart';

enum EntitlementRole {
  therapist,
  parent,
  unknown,
}

extension EntitlementRoleCodec on EntitlementRole {
  static EntitlementRole fromWire(String? wireValue) {
    switch (wireValue?.trim().toUpperCase()) {
      case 'THERAPIST':
        return EntitlementRole.therapist;
      case 'PARENT':
        return EntitlementRole.parent;
      default:
        return EntitlementRole.unknown;
    }
  }

  String get wireValue {
    switch (this) {
      case EntitlementRole.therapist:
        return 'THERAPIST';
      case EntitlementRole.parent:
        return 'PARENT';
      case EntitlementRole.unknown:
        return 'UNKNOWN';
    }
  }
}

enum LicenseStatus {
  active,
  expired,
  revoked,
  none,
}

extension LicenseStatusCodec on LicenseStatus {
  static LicenseStatus fromWire(String? wireValue) {
    switch (wireValue?.trim().toUpperCase()) {
      case 'ACTIVE':
        return LicenseStatus.active;
      case 'EXPIRED':
        return LicenseStatus.expired;
      case 'REVOKED':
        return LicenseStatus.revoked;
      default:
        return LicenseStatus.none;
    }
  }

  String get wireValue {
    switch (this) {
      case LicenseStatus.active:
        return 'ACTIVE';
      case LicenseStatus.expired:
        return 'EXPIRED';
      case LicenseStatus.revoked:
        return 'REVOKED';
      case LicenseStatus.none:
        return 'NONE';
    }
  }
}

class LicenseGrant {
  final LicenseStatus status;
  final DateTime? fromUtc;
  final DateTime? toUtc;
  final bool perpetual;

  const LicenseGrant({
    required this.status,
    this.fromUtc,
    this.toUtc,
    this.perpetual = false,
  });

  factory LicenseGrant.fromMap(Map<String, dynamic>? data) {
    if (data == null) {
      return const LicenseGrant(status: LicenseStatus.none);
    }

    return LicenseGrant(
      status: LicenseStatusCodec.fromWire(data['status'] as String?),
      fromUtc: _toUtcDateTime(data['fromUtc']),
      toUtc: _toUtcDateTime(data['toUtc']),
      perpetual: data['perpetual'] as bool? ?? false,
    );
  }

  Map<String, dynamic> toMap() {
    return <String, dynamic>{
      'status': status.wireValue,
      'fromUtc': fromUtc?.toIso8601String(),
      'toUtc': toUtc?.toIso8601String(),
      'perpetual': perpetual,
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
}

class EntitlementAccess {
  final EntitlementRole role;
  final LicenseGrant appLicense;
  final Map<String, LicenseGrant> gameLicenses;
  final String? policyVersion;

  const EntitlementAccess({
    required this.role,
    required this.appLicense,
    required this.gameLicenses,
    this.policyVersion,
  });

  factory EntitlementAccess.fromBackend(Map<String, dynamic> data) {
    final parsedGameLicenses = <String, LicenseGrant>{};
    final rawGameLicenses = data['gameLicenses'];
    if (rawGameLicenses is Map<String, dynamic>) {
      for (final entry in rawGameLicenses.entries) {
        if (entry.value is Map<String, dynamic>) {
          parsedGameLicenses[entry.key] =
              LicenseGrant.fromMap(entry.value as Map<String, dynamic>);
        }
      }
    }

    return EntitlementAccess(
      role: EntitlementRoleCodec.fromWire(data['role'] as String?),
      appLicense:
          LicenseGrant.fromMap(data['appLicense'] as Map<String, dynamic>?),
      gameLicenses: parsedGameLicenses,
      policyVersion: data['policyVersion'] as String?,
    );
  }
}
