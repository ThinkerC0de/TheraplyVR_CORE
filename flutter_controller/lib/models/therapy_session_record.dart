import 'package:flutter_controller/models/session_fsm_contract.dart';
import 'package:flutter_controller/models/session_ownership.dart';

class TherapySessionRecord {
  final String sessionId;
  final String studentId;
  final String therapistId;
  final String ownerKey;
  final String sessionKey;
  final String stateWire;
  final SessionLifecycleState? state;
  final bool unfinishedFlag;
  final bool terminalFlag;
  final String latestGameId;
  final String reasonCode;
  final DateTime? startedAtUtc;
  final DateTime? interruptedAtUtc;
  final DateTime? endedAtUtc;
  final DateTime? updatedAtUtc;
  final int updatedAtUnixMs;
  final Map<String, dynamic> metadata;

  const TherapySessionRecord({
    required this.sessionId,
    required this.studentId,
    required this.therapistId,
    required this.ownerKey,
    required this.sessionKey,
    required this.stateWire,
    required this.state,
    required this.unfinishedFlag,
    required this.terminalFlag,
    required this.latestGameId,
    required this.reasonCode,
    required this.startedAtUtc,
    required this.interruptedAtUtc,
    required this.endedAtUtc,
    required this.updatedAtUtc,
    required this.updatedAtUnixMs,
    required this.metadata,
  });

  factory TherapySessionRecord.fromFirestore(Map<String, dynamic> json) {
    final stateWire = (json['state'] as String? ?? '').trim();
    final state = SessionLifecycleState.tryParse(stateWire);
    final metadata = json['metadata'] is Map<String, dynamic>
        ? Map<String, dynamic>.from(json['metadata'] as Map<String, dynamic>)
        : <String, dynamic>{};
    final sessionId = (json['sessionId'] as String? ?? '').trim();
    final studentId = (json['studentId'] as String? ?? '').trim();
    final therapistId = (json['therapistId'] as String? ?? '').trim();
    final ownerKeyRaw = (json['ownerKey'] as String? ?? '').trim();
    final sessionKeyRaw = (json['sessionKey'] as String? ?? '').trim();
    final ownerKey = ownerKeyRaw.isNotEmpty
        ? ownerKeyRaw
        : SessionOwnership.ownerKey(
            therapistId: therapistId,
            studentId: studentId,
          );
    final sessionKey = sessionKeyRaw.isNotEmpty
        ? sessionKeyRaw
        : SessionOwnership.sessionKey(
            therapistId: therapistId,
            studentId: studentId,
            sessionId: sessionId,
          );
    final startedAtUtc = _parseDateTime(json['startedAtUtc']);
    final endedAtUtc = _parseDateTime(json['endedAtUtc']);
    final updatedAtUtc = _parseDateTime(json['updatedAtUtc']);
    final interruptedAtUtc = _resolveInterruptedAt(
      json: json,
      state: state,
      updatedAtUtc: updatedAtUtc,
    );

    return TherapySessionRecord(
      sessionId: sessionId,
      studentId: studentId,
      therapistId: therapistId,
      ownerKey: ownerKey,
      sessionKey: sessionKey,
      stateWire: stateWire,
      state: state,
      unfinishedFlag: _asBool(json['unfinished']),
      terminalFlag: _asBool(json['isTerminal']),
      latestGameId: (json['latestGameId'] as String? ?? '').trim(),
      reasonCode: (json['reasonCode'] as String? ?? '').trim(),
      startedAtUtc: startedAtUtc,
      interruptedAtUtc: interruptedAtUtc,
      endedAtUtc: endedAtUtc,
      updatedAtUtc: updatedAtUtc,
      updatedAtUnixMs: _asInt(json['updatedAtUnixMs']),
      metadata: metadata,
    );
  }

  bool get isTerminal {
    if (state != null) {
      switch (state!) {
        case SessionLifecycleState.completed:
        case SessionLifecycleState.abortedByTherapist:
        case SessionLifecycleState.failedTechnical:
          return true;
        case SessionLifecycleState.created:
        case SessionLifecycleState.inProgress:
        case SessionLifecycleState.paused:
        case SessionLifecycleState.interrupted:
          return false;
      }
    }

    if (terminalFlag) {
      return true;
    }

    final normalized = stateWire.toUpperCase();
    return normalized == SessionLifecycleState.completed.wireValue ||
        normalized == SessionLifecycleState.abortedByTherapist.wireValue ||
        normalized == SessionLifecycleState.failedTechnical.wireValue;
  }

  bool get requiresHandoffDecision {
    if (sessionId.isEmpty) {
      return false;
    }
    if (isTerminal) {
      return false;
    }

    if (state != null) {
      switch (state!) {
        case SessionLifecycleState.inProgress:
        case SessionLifecycleState.paused:
        case SessionLifecycleState.interrupted:
          return true;
        case SessionLifecycleState.created:
        case SessionLifecycleState.completed:
        case SessionLifecycleState.abortedByTherapist:
        case SessionLifecycleState.failedTechnical:
          return false;
      }
    }

    return unfinishedFlag;
  }

  static DateTime? _parseDateTime(dynamic value) {
    if (value is String && value.isNotEmpty) {
      return DateTime.tryParse(value)?.toUtc();
    }
    return null;
  }

  static DateTime? _resolveInterruptedAt({
    required Map<String, dynamic> json,
    required SessionLifecycleState? state,
    required DateTime? updatedAtUtc,
  }) {
    final interruptedAtUtc = _parseDateTime(json['interruptedAtUtc']) ??
        _parseDateTimeFromUnixMs(json['interruptedAtUnixMs']);
    if (interruptedAtUtc != null) {
      return interruptedAtUtc;
    }

    if (state != SessionLifecycleState.interrupted) {
      return null;
    }

    final metadata = json['metadata'];
    if (metadata is Map<String, dynamic>) {
      final metadataInterruptedAt =
          _parseDateTime(metadata['interruptedAtUtc']) ??
              _parseDateTimeFromUnixMs(metadata['interruptedAtUnixMs']);
      if (metadataInterruptedAt != null) {
        return metadataInterruptedAt;
      }
    }

    return updatedAtUtc;
  }

  static DateTime? _parseDateTimeFromUnixMs(dynamic value) {
    final unixMs = _asInt(value);
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
