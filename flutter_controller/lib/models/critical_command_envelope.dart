import 'dart:convert';
import 'dart:math';

class CriticalCommandIds {
  static const String sessionAttach = 'SESSION_ATTACH';
  static const String startGame = 'START_GAME';
  static const String pauseGame = 'PAUSE_GAME';
  static const String resumeGame = 'RESUME_GAME';
  static const String stopGame = 'STOP_GAME';
  static const String endSession = 'END_SESSION';
  static const String syncCatalog = 'SYNC_CATALOG';
  static const String installGame = 'INSTALL_GAME';
  static const String uninstallGame = 'UNINSTALL_GAME';

  static const Set<String> values = <String>{
    sessionAttach,
    startGame,
    pauseGame,
    resumeGame,
    stopGame,
    endSession,
    syncCatalog,
    installGame,
    uninstallGame,
  };

  static bool isCritical(String commandId) => values.contains(commandId);
}

class CommandAckIds {
  static const String commandAck = 'COMMAND_ACK';
}

class CommandAckStatus {
  static const String ack = 'ACK';
  static const String nack = 'NACK';
}

class CriticalCommandEnvelope {
  final String messageId;
  final String sessionId;
  final String commandId;
  final String issuedAtUtc;
  final String? expiresAtUtc;
  final String payloadJson;

  const CriticalCommandEnvelope({
    required this.messageId,
    required this.sessionId,
    required this.commandId,
    required this.issuedAtUtc,
    required this.expiresAtUtc,
    required this.payloadJson,
  });

  Map<String, dynamic> toJson() {
    return <String, dynamic>{
      'messageId': messageId,
      'sessionId': sessionId,
      'commandId': commandId,
      'issuedAtUtc': issuedAtUtc,
      'expiresAtUtc': expiresAtUtc,
      'payloadJson': payloadJson,
    };
  }

  static String newMessageId() {
    final now = DateTime.now().toUtc().microsecondsSinceEpoch;
    final randomPart = Random().nextInt(1 << 32).toRadixString(16);
    return '$now-$randomPart';
  }

  static CriticalCommandEnvelope create({
    required String sessionId,
    required String commandId,
    Map<String, dynamic>? payload,
    DateTime? issuedAtUtc,
    DateTime? expiresAtUtc,
    String? messageId,
  }) {
    final issuedAt = (issuedAtUtc ?? DateTime.now().toUtc()).toUtc();
    return CriticalCommandEnvelope(
      messageId: messageId ?? newMessageId(),
      sessionId: sessionId,
      commandId: commandId,
      issuedAtUtc: issuedAt.toIso8601String(),
      expiresAtUtc: expiresAtUtc?.toUtc().toIso8601String(),
      payloadJson: jsonEncode(payload ?? <String, dynamic>{}),
    );
  }
}

class CriticalCommandAck {
  final String messageId;
  final String commandId;
  final String sessionId;
  final String status;
  final String reasonCode;
  final String processedAtUtc;

  const CriticalCommandAck({
    required this.messageId,
    required this.commandId,
    required this.sessionId,
    required this.status,
    required this.reasonCode,
    required this.processedAtUtc,
  });

  bool get isAck => status == CommandAckStatus.ack;
  bool get isNack => status == CommandAckStatus.nack;

  static CriticalCommandAck? tryFromNetworkMessage(
      Map<String, dynamic> message) {
    final commandId = message['commandId'] as String?;
    if (commandId != CommandAckIds.commandAck) {
      return null;
    }

    final payloadMap = _decodePayloadMap(message['payload']);
    if (payloadMap == null) {
      return null;
    }

    final status = payloadMap['status'] as String? ?? '';
    if (status != CommandAckStatus.ack && status != CommandAckStatus.nack) {
      return null;
    }

    return CriticalCommandAck(
      messageId: payloadMap['messageId'] as String? ?? '',
      commandId: payloadMap['commandId'] as String? ?? '',
      sessionId: payloadMap['sessionId'] as String? ?? '',
      status: status,
      reasonCode: payloadMap['reasonCode'] as String? ?? '',
      processedAtUtc: payloadMap['processedAtUtc'] as String? ?? '',
    );
  }

  static Map<String, dynamic>? _decodePayloadMap(dynamic payload) {
    if (payload is! String || payload.isEmpty) {
      return null;
    }

    try {
      final payloadJson = utf8.decode(base64Decode(payload));
      final decoded = jsonDecode(payloadJson);
      if (decoded is Map<String, dynamic>) {
        return decoded;
      }
    } catch (_) {
      return null;
    }

    return null;
  }
}
