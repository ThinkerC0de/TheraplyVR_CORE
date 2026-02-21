import 'dart:convert';

enum TherapistRuntimeStatus {
  connected('connected'),
  playing('playing'),
  paused('paused'),
  interrupted('interrupted'),
  syncPending('sync_pending');

  const TherapistRuntimeStatus(this.wireValue);

  final String wireValue;

  static TherapistRuntimeStatus? tryParse(String? wireValue) {
    if (wireValue == null || wireValue.isEmpty) {
      return null;
    }

    for (final status in values) {
      if (status.wireValue == wireValue) {
        return status;
      }
    }

    return null;
  }
}

enum DevicePresenceState {
  connected('connected'),
  foreground('foreground'),
  background('background'),
  focusLost('focus_lost'),
  quitting('quitting');

  const DevicePresenceState(this.wireValue);

  final String wireValue;

  static DevicePresenceState? tryParse(String? wireValue) {
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

class RuntimeStatusSignalCommandIds {
  static const String runtimeStatusUpdate = 'RUNTIME_STATUS_UPDATE';
  static const String sessionWatchdogHeartbeat = 'SESSION_WATCHDOG_HEARTBEAT';
  static const String devicePresenceUpdate = 'DEVICE_PRESENCE_UPDATE';
}

class RuntimeStatusUpdateSignal {
  final String sessionId;
  final String patientId;
  final String therapistId;
  final TherapistRuntimeStatus status;
  final TherapistRuntimeStatus? previousStatus;
  final String reasonCode;
  final DateTime changedAtUtc;
  final int pendingQueueSize;

  const RuntimeStatusUpdateSignal({
    required this.sessionId,
    required this.patientId,
    required this.therapistId,
    required this.status,
    required this.previousStatus,
    required this.reasonCode,
    required this.changedAtUtc,
    required this.pendingQueueSize,
  });

  static RuntimeStatusUpdateSignal? tryFromNetworkMessage(
    Map<String, dynamic> message,
  ) {
    final commandId = message['commandId'] as String?;
    if (commandId != RuntimeStatusSignalCommandIds.runtimeStatusUpdate) {
      return null;
    }

    final payload = _decodePayloadMap(message['payload']);
    if (payload == null) {
      return null;
    }

    final status =
        TherapistRuntimeStatus.tryParse(payload['status'] as String? ?? '');
    if (status == null) {
      return null;
    }

    final previousStatus = TherapistRuntimeStatus.tryParse(
      payload['previousStatus'] as String?,
    );

    final changedAtUnixMs = (payload['changedAtUnixMs'] as num?)?.toInt() ?? 0;
    final changedAtUtc = changedAtUnixMs > 0
        ? DateTime.fromMillisecondsSinceEpoch(changedAtUnixMs, isUtc: true)
        : DateTime.now().toUtc();

    return RuntimeStatusUpdateSignal(
      sessionId: payload['sessionId'] as String? ?? '',
      patientId: payload['patientId'] as String? ?? '',
      therapistId: payload['therapistId'] as String? ?? '',
      status: status,
      previousStatus: previousStatus,
      reasonCode: payload['reasonCode'] as String? ?? '',
      changedAtUtc: changedAtUtc,
      pendingQueueSize: (payload['pendingQueueSize'] as num?)?.toInt() ?? 0,
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

class DevicePresenceUpdateSignal {
  final String sessionId;
  final String patientId;
  final String therapistId;
  final DevicePresenceState presenceState;
  final String reasonCode;
  final DateTime changedAtUtc;
  final bool appPaused;
  final bool appFocused;
  final bool hasTcpClient;
  final String activeGameId;
  final String activeGameState;

  const DevicePresenceUpdateSignal({
    required this.sessionId,
    required this.patientId,
    required this.therapistId,
    required this.presenceState,
    required this.reasonCode,
    required this.changedAtUtc,
    required this.appPaused,
    required this.appFocused,
    required this.hasTcpClient,
    required this.activeGameId,
    required this.activeGameState,
  });

  static DevicePresenceUpdateSignal? tryFromNetworkMessage(
    Map<String, dynamic> message,
  ) {
    final commandId = message['commandId'] as String?;
    if (commandId != RuntimeStatusSignalCommandIds.devicePresenceUpdate) {
      return null;
    }

    final payload = _decodePayloadMap(message['payload']);
    if (payload == null) {
      return null;
    }

    final presenceState = DevicePresenceState.tryParse(
      payload['presenceState'] as String? ?? '',
    );
    if (presenceState == null) {
      return null;
    }

    final changedAtUnixMs = (payload['changedAtUnixMs'] as num?)?.toInt() ?? 0;
    final changedAtUtc = changedAtUnixMs > 0
        ? DateTime.fromMillisecondsSinceEpoch(changedAtUnixMs, isUtc: true)
        : DateTime.now().toUtc();

    return DevicePresenceUpdateSignal(
      sessionId: payload['sessionId'] as String? ?? '',
      patientId: payload['patientId'] as String? ?? '',
      therapistId: payload['therapistId'] as String? ?? '',
      presenceState: presenceState,
      reasonCode: payload['reasonCode'] as String? ?? '',
      changedAtUtc: changedAtUtc,
      appPaused: payload['appPaused'] as bool? ?? false,
      appFocused: payload['appFocused'] as bool? ?? true,
      hasTcpClient: payload['hasTcpClient'] as bool? ?? false,
      activeGameId: payload['activeGameId'] as String? ?? '',
      activeGameState: payload['activeGameState'] as String? ?? '',
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

class SessionWatchdogHeartbeatSignal {
  final String sessionId;
  final String patientId;
  final String therapistId;
  final String sessionState;
  final TherapistRuntimeStatus? runtimeStatus;
  final String healthCode;
  final bool healthy;
  final String activeGameId;
  final String activeGameState;
  final DateTime heartbeatAtUtc;
  final DateTime lastHealthyAtUtc;
  final int pendingQueueSize;
  final int expectedIntervalMs;
  final int staleAfterMs;

  const SessionWatchdogHeartbeatSignal({
    required this.sessionId,
    required this.patientId,
    required this.therapistId,
    required this.sessionState,
    required this.runtimeStatus,
    required this.healthCode,
    required this.healthy,
    required this.activeGameId,
    required this.activeGameState,
    required this.heartbeatAtUtc,
    required this.lastHealthyAtUtc,
    required this.pendingQueueSize,
    required this.expectedIntervalMs,
    required this.staleAfterMs,
  });

  bool isStale(DateTime nowUtc) {
    return nowUtc.toUtc().difference(heartbeatAtUtc).inMilliseconds >
        staleAfterMs;
  }

  static SessionWatchdogHeartbeatSignal? tryFromNetworkMessage(
    Map<String, dynamic> message,
  ) {
    final commandId = message['commandId'] as String?;
    if (commandId != RuntimeStatusSignalCommandIds.sessionWatchdogHeartbeat) {
      return null;
    }

    final payload = _decodePayloadMap(message['payload']);
    if (payload == null) {
      return null;
    }

    final heartbeatUnixMs = (payload['heartbeatUnixMs'] as num?)?.toInt() ?? 0;
    final heartbeatAtUtc = heartbeatUnixMs > 0
        ? DateTime.fromMillisecondsSinceEpoch(heartbeatUnixMs, isUtc: true)
        : DateTime.now().toUtc();

    final lastHealthyUnixMs =
        (payload['lastHealthyUnixMs'] as num?)?.toInt() ?? heartbeatUnixMs;
    final lastHealthyAtUtc = lastHealthyUnixMs > 0
        ? DateTime.fromMillisecondsSinceEpoch(lastHealthyUnixMs, isUtc: true)
        : heartbeatAtUtc;

    final expectedIntervalMs =
        (payload['expectedIntervalMs'] as num?)?.toInt() ?? 2000;
    final staleAfterMs = (payload['staleAfterMs'] as num?)?.toInt() ??
        (expectedIntervalMs > 0 ? expectedIntervalMs * 3 : 6000);

    return SessionWatchdogHeartbeatSignal(
      sessionId: payload['sessionId'] as String? ?? '',
      patientId: payload['patientId'] as String? ?? '',
      therapistId: payload['therapistId'] as String? ?? '',
      sessionState: payload['sessionState'] as String? ?? '',
      runtimeStatus:
          TherapistRuntimeStatus.tryParse(payload['runtimeStatus'] as String?),
      healthCode: payload['healthCode'] as String? ?? '',
      healthy: payload['healthy'] as bool? ?? false,
      activeGameId: payload['activeGameId'] as String? ?? '',
      activeGameState: payload['activeGameState'] as String? ?? '',
      heartbeatAtUtc: heartbeatAtUtc,
      lastHealthyAtUtc: lastHealthyAtUtc,
      pendingQueueSize: (payload['pendingQueueSize'] as num?)?.toInt() ?? 0,
      expectedIntervalMs: expectedIntervalMs,
      staleAfterMs: staleAfterMs,
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
