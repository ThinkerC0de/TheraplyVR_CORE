import 'dart:convert';

class ManualResyncSignalCommandIds {
  static const String manualResync = 'MANUAL_RESYNC';
  static const String manualResyncReport = 'MANUAL_RESYNC_REPORT';
}

class ManualResyncReportSignal {
  final String correlationId;
  final bool success;
  final String reasonCode;
  final String sessionId;
  final String requestedBy;
  final String requestedReasonCode;
  final String requestedAtUtc;
  final bool includeSyncedEvents;
  final int targetedEvents;
  final int outboxRowsUpdated;
  final bool uploadCycleTriggered;
  final int localEvents;
  final String beforeReasonCode;
  final int beforeMissingOnServerCount;
  final int beforeMissingOnDeviceCount;
  final int beforeOutboxPending;
  final String afterReasonCode;
  final int afterMissingOnServerCount;
  final int afterMissingOnDeviceCount;
  final int afterOutboxPending;
  final String targetedSequencePreview;
  final String details;

  const ManualResyncReportSignal({
    required this.correlationId,
    required this.success,
    required this.reasonCode,
    required this.sessionId,
    required this.requestedBy,
    required this.requestedReasonCode,
    required this.requestedAtUtc,
    required this.includeSyncedEvents,
    required this.targetedEvents,
    required this.outboxRowsUpdated,
    required this.uploadCycleTriggered,
    required this.localEvents,
    required this.beforeReasonCode,
    required this.beforeMissingOnServerCount,
    required this.beforeMissingOnDeviceCount,
    required this.beforeOutboxPending,
    required this.afterReasonCode,
    required this.afterMissingOnServerCount,
    required this.afterMissingOnDeviceCount,
    required this.afterOutboxPending,
    required this.targetedSequencePreview,
    required this.details,
  });

  static ManualResyncReportSignal? tryFromNetworkMessage(
    Map<String, dynamic> message,
  ) {
    final commandId = message['commandId'] as String?;
    if (commandId != ManualResyncSignalCommandIds.manualResyncReport) {
      return null;
    }

    final payload = _decodePayloadMap(message['payload']);
    if (payload == null) {
      return null;
    }

    return ManualResyncReportSignal(
      correlationId: payload['correlationId'] as String? ?? '',
      success: _asBool(payload['success']),
      reasonCode: payload['reasonCode'] as String? ?? '',
      sessionId: payload['sessionId'] as String? ?? '',
      requestedBy: payload['requestedBy'] as String? ?? '',
      requestedReasonCode: payload['requestedReasonCode'] as String? ?? '',
      requestedAtUtc: payload['requestedAtUtc'] as String? ?? '',
      includeSyncedEvents: _asBool(payload['includeSyncedEvents']),
      targetedEvents: _asInt(payload['targetedEvents']),
      outboxRowsUpdated: _asInt(payload['outboxRowsUpdated']),
      uploadCycleTriggered: _asBool(payload['uploadCycleTriggered']),
      localEvents: _asInt(payload['localEvents']),
      beforeReasonCode: payload['beforeReasonCode'] as String? ?? '',
      beforeMissingOnServerCount: _asInt(payload['beforeMissingOnServerCount']),
      beforeMissingOnDeviceCount: _asInt(payload['beforeMissingOnDeviceCount']),
      beforeOutboxPending: _asInt(payload['beforeOutboxPending']),
      afterReasonCode: payload['afterReasonCode'] as String? ?? '',
      afterMissingOnServerCount: _asInt(payload['afterMissingOnServerCount']),
      afterMissingOnDeviceCount: _asInt(payload['afterMissingOnDeviceCount']),
      afterOutboxPending: _asInt(payload['afterOutboxPending']),
      targetedSequencePreview:
          payload['targetedSequencePreview'] as String? ?? '[]',
      details: payload['details'] as String? ?? '',
    );
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

  static Map<String, dynamic>? _decodePayloadMap(dynamic payload) {
    if (payload is! String || payload.isEmpty) {
      return null;
    }

    try {
      final payloadString = utf8.decode(base64Decode(payload));
      final decoded = jsonDecode(payloadString);
      if (decoded is Map<String, dynamic>) {
        return decoded;
      }
    } catch (_) {
      return null;
    }

    return null;
  }
}
