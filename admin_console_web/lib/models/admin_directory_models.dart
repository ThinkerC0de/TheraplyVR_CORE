import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:admin_console_web/models/entitlement_access.dart';

class AdminDirectoryUserRow {
  final String userId;
  final EntitlementRole role;
  final LicenseStatus appLicenseStatus;
  final DateTime? updatedAtUtc;
  final String? updatedBy;

  const AdminDirectoryUserRow({
    required this.userId,
    required this.role,
    required this.appLicenseStatus,
    required this.updatedAtUtc,
    required this.updatedBy,
  });

  factory AdminDirectoryUserRow.fromEntitlementDocument(
    DocumentSnapshot<Map<String, dynamic>> doc,
  ) {
    final data = doc.data() ?? const <String, dynamic>{};
    final appLicense = LicenseGrant.fromMap(data['appLicense'] as Map<String, dynamic>?);

    return AdminDirectoryUserRow(
      userId: doc.id,
      role: EntitlementRoleCodec.fromWire(data['role'] as String?),
      appLicenseStatus: appLicense.status,
      updatedAtUtc: _toUtcDateTime(data['updatedAtUtc']),
      updatedBy: data['updatedBy'] as String?,
    );
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

class AdminStudentDirectoryRow {
  final String studentId;
  final String firstName;
  final String lastName;
  final String therapistId;
  final DateTime? updatedAtUtc;

  const AdminStudentDirectoryRow({
    required this.studentId,
    required this.firstName,
    required this.lastName,
    required this.therapistId,
    required this.updatedAtUtc,
  });

  String get fullName {
    final normalized = '$firstName $lastName'.trim();
    return normalized.isEmpty ? '(Unnamed student)' : normalized;
  }

  factory AdminStudentDirectoryRow.fromStudentDocument(
    DocumentSnapshot<Map<String, dynamic>> doc,
  ) {
    final data = doc.data() ?? const <String, dynamic>{};
    return AdminStudentDirectoryRow(
      studentId: doc.id,
      firstName: data['firstName'] as String? ?? '',
      lastName: data['lastName'] as String? ?? '',
      therapistId: data['therapistId'] as String? ?? '',
      updatedAtUtc: _toUtcDateTime(data['updatedAt']),
    );
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

class AdminGameGrantStats {
  final int activeAssignments;
  final int revokedAssignments;

  const AdminGameGrantStats({
    required this.activeAssignments,
    required this.revokedAssignments,
  });
}
