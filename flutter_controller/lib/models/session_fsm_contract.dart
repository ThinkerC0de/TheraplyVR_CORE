import 'dart:convert';

enum SessionLifecycleState {
  created('CREATED'),
  inProgress('IN_PROGRESS'),
  paused('PAUSED'),
  interrupted('INTERRUPTED'),
  completed('COMPLETED'),
  abortedByTherapist('ABORTED_BY_THERAPIST'),
  failedTechnical('FAILED_TECHNICAL');

  const SessionLifecycleState(this.wireValue);

  final String wireValue;

  static SessionLifecycleState? tryParse(String? wireValue) {
    if (wireValue == null || wireValue.isEmpty) {
      return null;
    }

    for (final state in values) {
      if (state.wireValue == wireValue) {
        return state;
      }
    }

    return null;
  }
}

class SessionFsmContract {
  static final Map<SessionLifecycleState, Set<SessionLifecycleState>>
      _allowedTransitions = <SessionLifecycleState, Set<SessionLifecycleState>>{
    SessionLifecycleState.created: <SessionLifecycleState>{
      SessionLifecycleState.inProgress,
      SessionLifecycleState.abortedByTherapist,
      SessionLifecycleState.failedTechnical,
    },
    SessionLifecycleState.inProgress: <SessionLifecycleState>{
      SessionLifecycleState.paused,
      SessionLifecycleState.interrupted,
      SessionLifecycleState.completed,
      SessionLifecycleState.abortedByTherapist,
      SessionLifecycleState.failedTechnical,
    },
    SessionLifecycleState.paused: <SessionLifecycleState>{
      SessionLifecycleState.inProgress,
      SessionLifecycleState.interrupted,
      SessionLifecycleState.abortedByTherapist,
      SessionLifecycleState.failedTechnical,
    },
    SessionLifecycleState.interrupted: <SessionLifecycleState>{
      SessionLifecycleState.inProgress,
      SessionLifecycleState.abortedByTherapist,
      SessionLifecycleState.failedTechnical,
    },
    SessionLifecycleState.completed: <SessionLifecycleState>{},
    SessionLifecycleState.abortedByTherapist: <SessionLifecycleState>{},
    SessionLifecycleState.failedTechnical: <SessionLifecycleState>{},
  };

  static bool canTransition(
    SessionLifecycleState fromState,
    SessionLifecycleState toState,
  ) {
    if (fromState == toState) {
      return true;
    }

    final allowedNextStates = _allowedTransitions[fromState];
    return allowedNextStates?.contains(toState) ?? false;
  }
}

class SessionSignalCommandIds {
  static const String sessionStateUpdate = 'SESSION_STATE_UPDATE';
}

class SessionStateUpdateSignal {
  final String sessionId;
  final String studentId;
  final String patientId;
  final String therapistId;
  final String ownerKey;
  final String sessionKey;
  final SessionLifecycleState state;
  final SessionLifecycleState? previousState;
  final String reasonCode;
  final DateTime changedAtUtc;

  const SessionStateUpdateSignal({
    required this.sessionId,
    required this.studentId,
    required this.patientId,
    required this.therapistId,
    required this.ownerKey,
    required this.sessionKey,
    required this.state,
    required this.previousState,
    required this.reasonCode,
    required this.changedAtUtc,
  });

  static SessionStateUpdateSignal? tryFromNetworkMessage(
    Map<String, dynamic> message,
  ) {
    final commandId = message['commandId'] as String?;
    if (commandId != SessionSignalCommandIds.sessionStateUpdate) {
      return null;
    }

    final payload = _decodePayloadMap(message['payload']);
    if (payload == null) {
      return null;
    }

    final state =
        SessionLifecycleState.tryParse(payload['state'] as String? ?? '');
    if (state == null) {
      return null;
    }

    final previousState = SessionLifecycleState.tryParse(
      payload['previousState'] as String?,
    );

    final changedAtUnixMs = (payload['changedAtUnixMs'] as num?)?.toInt() ?? 0;
    final changedAtUtc = changedAtUnixMs > 0
        ? DateTime.fromMillisecondsSinceEpoch(changedAtUnixMs, isUtc: true)
        : DateTime.now().toUtc();
    final patientId = payload['patientId'] as String? ?? '';
    final studentId = payload['studentId'] as String? ?? patientId;

    return SessionStateUpdateSignal(
      sessionId: payload['sessionId'] as String? ?? '',
      studentId: studentId,
      patientId: patientId,
      therapistId: payload['therapistId'] as String? ?? '',
      ownerKey: payload['ownerKey'] as String? ?? '',
      sessionKey: payload['sessionKey'] as String? ?? '',
      state: state,
      previousState: previousState,
      reasonCode: payload['reasonCode'] as String? ?? '',
      changedAtUtc: changedAtUtc,
    );
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
