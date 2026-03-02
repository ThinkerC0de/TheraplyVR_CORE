import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:admin_console_web/models/entitlement_access.dart';

class AdminDirectoryUserRow {
  final String userId;
  final EntitlementRole role;
  final SubscriptionPlanTier planTier;
  final LicenseStatus appLicenseStatus;
  final String? firstName;
  final String? lastName;
  final String? preferredDisplayName;
  final DateTime? updatedAtUtc;
  final String? updatedBy;

  const AdminDirectoryUserRow({
    required this.userId,
    required this.role,
    required this.planTier,
    required this.appLicenseStatus,
    this.firstName,
    this.lastName,
    this.preferredDisplayName,
    required this.updatedAtUtc,
    required this.updatedBy,
  });

  String get displayName {
    if (preferredDisplayName != null &&
        preferredDisplayName!.trim().isNotEmpty) {
      return preferredDisplayName!.trim();
    }
    final fullName = '${firstName ?? ''} ${lastName ?? ''}'.trim();
    if (fullName.isNotEmpty) {
      return fullName;
    }
    return userId;
  }

  factory AdminDirectoryUserRow.fromEntitlementDocument(
    DocumentSnapshot<Map<String, dynamic>> doc,
  ) {
    final data = doc.data() ?? const <String, dynamic>{};
    final appLicense =
        LicenseGrant.fromMap(data['appLicense'] as Map<String, dynamic>?);
    final planProfile = EntitlementPlanProfile.fromMap(
        data['planProfile'] as Map<String, dynamic>?);

    return AdminDirectoryUserRow(
      userId: doc.id,
      role: EntitlementRoleCodec.fromWire(data['role'] as String?),
      planTier: planProfile.tier,
      appLicenseStatus: appLicense.status,
      firstName: _readPreferredString(
        data,
        const <String>['firstName', 'profileFirstName', 'givenName'],
      ),
      lastName: _readPreferredString(
        data,
        const <String>['lastName', 'profileLastName', 'familyName'],
      ),
      preferredDisplayName: _readPreferredString(
        data,
        const <String>['displayName', 'fullName', 'name'],
      ),
      updatedAtUtc: _toUtcDateTime(data['updatedAtUtc']),
      updatedBy: data['updatedBy'] as String?,
    );
  }

  static String? _readPreferredString(
    Map<String, dynamic> data,
    List<String> keys,
  ) {
    for (final key in keys) {
      final value = data[key];
      if (value is String && value.trim().isNotEmpty) {
        return value.trim();
      }
    }
    return null;
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

class AdminTherapySessionRow {
  final String documentId;
  final String sessionId;
  final String studentId;
  final String therapistId;
  final String ownerKey;
  final String sessionKey;
  final String stateWire;
  final bool unfinished;
  final bool isTerminal;
  final String latestGameId;
  final String reasonCode;
  final DateTime? startedAtUtc;
  final DateTime? interruptedAtUtc;
  final DateTime? endedAtUtc;
  final DateTime? updatedAtUtc;
  final int updatedAtUnixMs;
  final Map<String, dynamic> metadata;

  const AdminTherapySessionRow({
    required this.documentId,
    required this.sessionId,
    required this.studentId,
    required this.therapistId,
    required this.ownerKey,
    required this.sessionKey,
    required this.stateWire,
    required this.unfinished,
    required this.isTerminal,
    required this.latestGameId,
    required this.reasonCode,
    required this.startedAtUtc,
    required this.interruptedAtUtc,
    required this.endedAtUtc,
    required this.updatedAtUtc,
    required this.updatedAtUnixMs,
    required this.metadata,
  });

  String get stateLabel {
    final normalized = stateWire.trim();
    return normalized.isEmpty ? 'UNKNOWN' : normalized;
  }

  factory AdminTherapySessionRow.fromSessionDocument(
    DocumentSnapshot<Map<String, dynamic>> doc,
  ) {
    final data = doc.data() ?? const <String, dynamic>{};
    final metadata = data['metadata'] is Map<String, dynamic>
        ? Map<String, dynamic>.from(data['metadata'] as Map<String, dynamic>)
        : <String, dynamic>{};
    final sessionIdRaw = (data['sessionId'] as String? ?? '').trim();
    final sessionId = sessionIdRaw.isNotEmpty ? sessionIdRaw : doc.id.trim();
    final updatedAtUnixMs = _asInt(data['updatedAtUnixMs']);
    final startedAtUnixMs = _asInt(data['startedAtUnixMs']);
    final interruptedAtUnixMs = _asInt(data['interruptedAtUnixMs']);
    final endedAtUnixMs = _asInt(data['endedAtUnixMs']);

    return AdminTherapySessionRow(
      documentId: doc.id.trim(),
      sessionId: sessionId,
      studentId: (data['studentId'] as String? ?? '').trim(),
      therapistId: (data['therapistId'] as String? ?? '').trim(),
      ownerKey: (data['ownerKey'] as String? ?? '').trim(),
      sessionKey: (data['sessionKey'] as String? ?? '').trim(),
      stateWire: (data['state'] as String? ?? '').trim(),
      unfinished: _asBool(data['unfinished']),
      isTerminal: _asBool(data['isTerminal']),
      latestGameId: (data['latestGameId'] as String? ?? '').trim(),
      reasonCode: (data['reasonCode'] as String? ?? '').trim(),
      startedAtUtc: _toUtcDateTime(data['startedAtUtc']) ??
          _toUtcDateTimeFromUnixMs(startedAtUnixMs),
      interruptedAtUtc: _toUtcDateTime(data['interruptedAtUtc']) ??
          _toUtcDateTimeFromUnixMs(interruptedAtUnixMs),
      endedAtUtc: _toUtcDateTime(data['endedAtUtc']) ??
          _toUtcDateTimeFromUnixMs(endedAtUnixMs),
      updatedAtUtc: _toUtcDateTime(data['updatedAtUtc']) ??
          _toUtcDateTimeFromUnixMs(updatedAtUnixMs),
      updatedAtUnixMs: updatedAtUnixMs,
      metadata: metadata,
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

  static DateTime? _toUtcDateTimeFromUnixMs(int unixMs) {
    if (unixMs <= 0) {
      return null;
    }
    return DateTime.fromMillisecondsSinceEpoch(unixMs, isUtc: true);
  }

  static bool _asBool(dynamic value) {
    if (value is bool) {
      return value;
    }
    if (value is num) {
      return value != 0;
    }
    if (value is String) {
      final normalized = value.trim().toLowerCase();
      return normalized == 'true' || normalized == '1' || normalized == 'yes';
    }
    return false;
  }

  static int _asInt(dynamic value) {
    if (value is int) {
      return value;
    }
    if (value is num) {
      return value.toInt();
    }
    if (value is String) {
      return int.tryParse(value) ?? 0;
    }
    return 0;
  }
}

class AdminSessionEventRow {
  final String eventId;
  final String timelineEventId;
  final String sessionId;
  final String studentId;
  final String therapistId;
  final String eventType;
  final String gameId;
  final String source;
  final DateTime? eventAtUtc;
  final int eventAtUnixMs;
  final Map<String, dynamic> details;

  const AdminSessionEventRow({
    required this.eventId,
    required this.timelineEventId,
    required this.sessionId,
    required this.studentId,
    required this.therapistId,
    required this.eventType,
    required this.gameId,
    required this.source,
    required this.eventAtUtc,
    required this.eventAtUnixMs,
    required this.details,
  });

  String detailsPreview({int maxEntries = 3}) {
    if (details.isEmpty) {
      return '-';
    }

    final keys = details.keys.toList(growable: false)..sort();
    final parts = <String>[];
    for (var i = 0; i < keys.length; i += 1) {
      if (parts.length >= maxEntries) {
        break;
      }
      final key = keys[i];
      parts.add('$key=${_valuePreview(details[key])}');
    }

    if (keys.length > maxEntries) {
      parts.add('...');
    }
    return parts.join(', ');
  }

  factory AdminSessionEventRow.fromSessionEventDocument(
    QueryDocumentSnapshot<Map<String, dynamic>> doc,
  ) {
    final data = doc.data();
    final details = data['details'] is Map<String, dynamic>
        ? Map<String, dynamic>.from(data['details'] as Map<String, dynamic>)
        : <String, dynamic>{};
    final eventAtUnixMs = _asInt(data['eventAtUnixMs']);

    return AdminSessionEventRow(
      eventId: doc.id.trim(),
      timelineEventId: (data['timelineEventId'] as String? ?? '').trim(),
      sessionId: (data['sessionId'] as String? ?? '').trim(),
      studentId: (data['studentId'] as String? ?? '').trim(),
      therapistId: (data['therapistId'] as String? ?? '').trim(),
      eventType: (data['eventType'] as String? ?? '').trim(),
      gameId: (data['gameId'] as String? ?? '').trim(),
      source: (data['source'] as String? ?? '').trim(),
      eventAtUtc: _toUtcDateTime(data['eventAtUtc']) ??
          _toUtcDateTimeFromUnixMs(eventAtUnixMs),
      eventAtUnixMs: eventAtUnixMs,
      details: details,
    );
  }

  static String _valuePreview(dynamic value) {
    if (value == null) {
      return 'null';
    }
    if (value is Map) {
      return '{${value.length}}';
    }
    if (value is List) {
      return '[${value.length}]';
    }
    final raw = value.toString().trim();
    if (raw.length > 48) {
      return '${raw.substring(0, 45)}...';
    }
    return raw;
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

  static DateTime? _toUtcDateTimeFromUnixMs(int unixMs) {
    if (unixMs <= 0) {
      return null;
    }
    return DateTime.fromMillisecondsSinceEpoch(unixMs, isUtc: true);
  }

  static int _asInt(dynamic value) {
    if (value is int) {
      return value;
    }
    if (value is num) {
      return value.toInt();
    }
    if (value is String) {
      return int.tryParse(value) ?? 0;
    }
    return 0;
  }
}
