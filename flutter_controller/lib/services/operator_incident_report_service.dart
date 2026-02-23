import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_controller/services/firebase_service.dart';

class OperatorIncidentReportService {
  static const String _source = 'mobile_controller';
  static const String _collectionPath = 'operator_incident_reports';

  static FirebaseFirestore? _firestoreOverride;
  static String? _actorUidOverride;
  static String? _actorEmailOverride;

  static FirebaseFirestore get _firestore =>
      _firestoreOverride ?? FirebaseService.firestore;
  static CollectionReference<Map<String, dynamic>> get _reportsCollection =>
      _firestore.collection(_collectionPath);

  @visibleForTesting
  static void setFirestoreInstanceForTesting(FirebaseFirestore firestore) {
    _firestoreOverride = firestore;
  }

  @visibleForTesting
  static void setActorForTesting({
    required String actorUid,
    String actorEmail = '',
  }) {
    _actorUidOverride = actorUid.trim();
    _actorEmailOverride = actorEmail.trim();
  }

  @visibleForTesting
  static void clearTestingOverrides() {
    _firestoreOverride = null;
    _actorUidOverride = null;
    _actorEmailOverride = null;
  }

  static Future<String> submitIncidentReport({
    required String queueName,
    required DateTime generatedAtUtc,
    required String historyReport,
    required List<Map<String, dynamic>> incidents,
    Map<String, dynamic>? context,
  }) async {
    final normalizedQueueName = queueName.trim();
    if (normalizedQueueName.isEmpty) {
      throw ArgumentError.value(
          queueName, 'queueName', 'Queue name is required');
    }

    final actorUid = _resolveActorUid();
    if (actorUid.isEmpty) {
      throw StateError(
        'Cannot submit incident report without a signed-in therapist account.',
      );
    }

    final actorEmail = _resolveActorEmail();
    final normalizedGeneratedAtUtc = generatedAtUtc.toUtc();
    final normalizedIncidents = incidents
        .map((incident) => _sanitizeMap(incident))
        .toList(growable: false);
    final normalizedContext = _sanitizeMap(
      context ?? const <String, dynamic>{},
    );
    final reasonCodes = _collectReasonCodes(normalizedIncidents);
    final severityBreakdown = _buildSeverityBreakdown(normalizedIncidents);
    final trimmedHistory = historyReport.trim();

    final submittedAtUtc = DateTime.now().toUtc();
    final reportRef = _reportsCollection.doc();

    await reportRef.set(<String, dynamic>{
      'reportId': reportRef.id,
      'source': _source,
      'queueName': normalizedQueueName,
      'actorUid': actorUid,
      'actorEmail': actorEmail,
      'generatedAtUtc': normalizedGeneratedAtUtc.toIso8601String(),
      'generatedAtUnixMs': normalizedGeneratedAtUtc.millisecondsSinceEpoch,
      'submittedAtUtc': submittedAtUtc.toIso8601String(),
      'submittedAtUnixMs': submittedAtUtc.millisecondsSinceEpoch,
      'incidentCount': normalizedIncidents.length,
      'incidentReasonCodes': reasonCodes,
      'severityBreakdown': severityBreakdown,
      'context': normalizedContext,
      'historyReport': trimmedHistory,
      'incidents': normalizedIncidents,
      'schemaVersion': 1,
    });

    return reportRef.id;
  }

  static String _resolveActorUid() {
    final override = _actorUidOverride;
    if (override != null) {
      return override.trim();
    }

    return FirebaseService.currentUser?.uid.trim() ?? '';
  }

  static String _resolveActorEmail() {
    final override = _actorEmailOverride;
    if (override != null) {
      return override.trim();
    }

    return FirebaseService.currentUser?.email?.trim() ?? '';
  }

  static Map<String, dynamic> _sanitizeMap(Map<String, dynamic> input) {
    final result = <String, dynamic>{};
    input.forEach((key, value) {
      result[key] = _sanitizeValue(value);
    });
    return result;
  }

  static dynamic _sanitizeValue(dynamic value) {
    if (value == null || value is num || value is bool || value is String) {
      return value;
    }

    if (value is DateTime) {
      return value.toUtc().toIso8601String();
    }

    if (value is Enum) {
      return value.name;
    }

    if (value is Map) {
      final mapResult = <String, dynamic>{};
      value.forEach((key, nestedValue) {
        mapResult[key.toString()] = _sanitizeValue(nestedValue);
      });
      return mapResult;
    }

    if (value is Iterable) {
      return value.map(_sanitizeValue).toList(growable: false);
    }

    return value.toString();
  }

  static List<String> _collectReasonCodes(
    List<Map<String, dynamic>> incidents,
  ) {
    final reasonCodes = <String>{};
    for (final incident in incidents) {
      final rawReasonCode = incident['reasonCode']?.toString().trim() ?? '';
      if (rawReasonCode.isEmpty) {
        continue;
      }
      reasonCodes.add(rawReasonCode.toUpperCase());
    }

    final sortedReasonCodes = reasonCodes.toList(growable: false);
    sortedReasonCodes.sort();
    return sortedReasonCodes;
  }

  static Map<String, int> _buildSeverityBreakdown(
    List<Map<String, dynamic>> incidents,
  ) {
    final counts = <String, int>{
      'error': 0,
      'warning': 0,
      'info': 0,
      'unknown': 0,
    };

    for (final incident in incidents) {
      final normalizedSeverity =
          incident['severity']?.toString().trim().toLowerCase() ?? '';
      if (counts.containsKey(normalizedSeverity)) {
        counts[normalizedSeverity] = (counts[normalizedSeverity] ?? 0) + 1;
      } else {
        counts['unknown'] = (counts['unknown'] ?? 0) + 1;
      }
    }

    return counts;
  }
}
