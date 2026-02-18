import 'package:cloud_firestore/cloud_firestore.dart';

enum StudentRelationRole {
  therapist,
  parent,
  guardian,
  observer,
}

extension StudentRelationRoleCodec on StudentRelationRole {
  static StudentRelationRole fromWire(String? wireValue) {
    switch (wireValue?.trim().toUpperCase()) {
      case 'PARENT':
        return StudentRelationRole.parent;
      case 'GUARDIAN':
        return StudentRelationRole.guardian;
      case 'OBSERVER':
        return StudentRelationRole.observer;
      case 'THERAPIST':
      default:
        return StudentRelationRole.therapist;
    }
  }

  String get wireValue {
    switch (this) {
      case StudentRelationRole.therapist:
        return 'THERAPIST';
      case StudentRelationRole.parent:
        return 'PARENT';
      case StudentRelationRole.guardian:
        return 'GUARDIAN';
      case StudentRelationRole.observer:
        return 'OBSERVER';
    }
  }
}

enum StudentAccessBindingStatus {
  active,
  revoked,
}

extension StudentAccessBindingStatusCodec on StudentAccessBindingStatus {
  static StudentAccessBindingStatus fromWire(String? wireValue) {
    switch (wireValue?.trim().toUpperCase()) {
      case 'REVOKED':
        return StudentAccessBindingStatus.revoked;
      case 'ACTIVE':
      default:
        return StudentAccessBindingStatus.active;
    }
  }

  String get wireValue {
    switch (this) {
      case StudentAccessBindingStatus.active:
        return 'ACTIVE';
      case StudentAccessBindingStatus.revoked:
        return 'REVOKED';
    }
  }
}

class StudentAccessBinding {
  final String bindingId;
  final String studentId;
  final String accountId;
  final StudentRelationRole relationRole;
  final StudentAccessBindingStatus status;
  final String grantedBy;
  final DateTime grantedAtUtc;
  final DateTime? expiresAtUtc;
  final String? note;

  const StudentAccessBinding({
    required this.bindingId,
    required this.studentId,
    required this.accountId,
    required this.relationRole,
    required this.status,
    required this.grantedBy,
    required this.grantedAtUtc,
    required this.expiresAtUtc,
    required this.note,
  });

  factory StudentAccessBinding.fromFirestore(
    DocumentSnapshot<Map<String, dynamic>> doc,
  ) {
    final data = doc.data() ?? <String, dynamic>{};
    return StudentAccessBinding(
      bindingId: doc.id,
      studentId: data['studentId'] as String? ?? '',
      accountId: data['accountId'] as String? ?? '',
      relationRole: StudentRelationRoleCodec.fromWire(
        data['relationRole'] as String?,
      ),
      status: StudentAccessBindingStatusCodec.fromWire(
        data['status'] as String?,
      ),
      grantedBy: data['grantedBy'] as String? ?? '',
      grantedAtUtc: _toUtcDateTime(data['grantedAtUtc']) ??
          DateTime.fromMillisecondsSinceEpoch(0, isUtc: true),
      expiresAtUtc: _toUtcDateTime(data['expiresAtUtc']),
      note: data['note'] as String?,
    );
  }

  bool isActiveAt(DateTime atUtc) {
    if (status != StudentAccessBindingStatus.active) {
      return false;
    }

    if (expiresAtUtc != null && atUtc.isAfter(expiresAtUtc!)) {
      return false;
    }

    return true;
  }

  Map<String, dynamic> toFirestore() {
    return <String, dynamic>{
      'studentId': studentId,
      'accountId': accountId,
      'relationRole': relationRole.wireValue,
      'status': status.wireValue,
      'grantedBy': grantedBy,
      'grantedAtUtc': grantedAtUtc.toUtc().toIso8601String(),
      'expiresAtUtc': expiresAtUtc?.toUtc().toIso8601String(),
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
}
