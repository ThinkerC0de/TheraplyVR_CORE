import 'package:flutter_controller/models/session_fsm_contract.dart';

class TherapySessionRecord {
  final String sessionId;
  final String studentId;
  final String therapistId;
  final String stateWire;
  final SessionLifecycleState? state;
  final bool unfinishedFlag;
  final bool terminalFlag;
  final String latestGameId;
  final String reasonCode;
  final DateTime? startedAtUtc;
  final DateTime? endedAtUtc;
  final DateTime? updatedAtUtc;
  final int updatedAtUnixMs;
  final Map<String, dynamic> metadata;

  const TherapySessionRecord({
    required this.sessionId,
    required this.studentId,
    required this.therapistId,
    required this.stateWire,
    required this.state,
    required this.unfinishedFlag,
    required this.terminalFlag,
    required this.latestGameId,
    required this.reasonCode,
    required this.startedAtUtc,
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

    return TherapySessionRecord(
      sessionId: (json['sessionId'] as String? ?? '').trim(),
      studentId: (json['studentId'] as String? ?? '').trim(),
      therapistId: (json['therapistId'] as String? ?? '').trim(),
      stateWire: stateWire,
      state: state,
      unfinishedFlag: _asBool(json['unfinished']),
      terminalFlag: _asBool(json['isTerminal']),
      latestGameId: (json['latestGameId'] as String? ?? '').trim(),
      reasonCode: (json['reasonCode'] as String? ?? '').trim(),
      startedAtUtc: _parseDateTime(json['startedAtUtc']),
      endedAtUtc: _parseDateTime(json['endedAtUtc']),
      updatedAtUtc: _parseDateTime(json['updatedAtUtc']),
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
