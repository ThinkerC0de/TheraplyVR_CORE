import 'dart:async';
import 'dart:collection';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter_controller/models/critical_command_envelope.dart';
import 'package:flutter_controller/models/runtime_status_signal.dart';
import 'package:flutter_controller/services/connection_service.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test(
    'mobile reconnect reuses active Quest session id for critical command',
    () async {
      final server = await _QuestMockServer.start();
      addTearDown(server.dispose);

      final connection = ConnectionService();
      addTearDown(connection.dispose);

      const questSessionId = 'quest-session-007';
      var activeSessionId = 'mobile-local-session';

      final messageSubscription = connection.messages.listen((message) {
        final runtimeUpdate = RuntimeStatusUpdateSignal.tryFromNetworkMessage(
          message,
        );
        if (runtimeUpdate != null && runtimeUpdate.sessionId.isNotEmpty) {
          activeSessionId = runtimeUpdate.sessionId;
        }
      });
      addTearDown(messageSubscription.cancel);

      final connected = await connection.connect(
        InternetAddress.loopbackIPv4.address,
        server.port,
      );
      expect(connected, isTrue);

      final firstClient = await server.nextClient();
      await server.sendRuntimeStatusUpdate(
        firstClient,
        sessionId: questSessionId,
        status: TherapistRuntimeStatus.playing.wireValue,
        reasonCode: 'INITIAL_ATTACH',
      );

      await _waitForCondition(() => activeSessionId == questSessionId);

      await firstClient.close();
      await _waitForCondition(() => !connection.isConnected);

      final reconnectFuture = connection.reconnect(
        maxAttempts: 6,
        baseDelay: const Duration(milliseconds: 80),
      );
      final secondClient = await server.nextClient();
      expect(await reconnectFuture, isTrue);

      final inboundCommandFuture = _readNextNetworkMessage(secondClient);

      final sendFuture = connection.sendCriticalCommand(
        commandId: CriticalCommandIds.pauseGame,
        sessionId: activeSessionId,
        payload: <String, dynamic>{'reason': 'TherapistPause'},
        ackTimeout: const Duration(seconds: 2),
        maxRetries: 2,
      );

      final inboundCommand = await inboundCommandFuture;
      expect(inboundCommand.commandId, CriticalCommandIds.pauseGame);
      expect(inboundCommand.payloadMap, isNotNull);
      expect(inboundCommand.payloadMap!['sessionId'], questSessionId);

      await server.sendCommandAck(
        secondClient,
        requestMessageId: inboundCommand.messageId,
        requestCommandId: inboundCommand.commandId,
        sessionId: questSessionId,
        status: CommandAckStatus.ack,
        reasonCode: 'OK',
      );

      await sendFuture;
    },
  );
}

Future<void> _waitForCondition(
  bool Function() condition, {
  Duration timeout = const Duration(seconds: 5),
}) async {
  final deadline = DateTime.now().add(timeout);
  while (!condition()) {
    if (DateTime.now().isAfter(deadline)) {
      throw TimeoutException('Condition was not met within $timeout');
    }
    await Future<void>.delayed(const Duration(milliseconds: 25));
  }
}

Future<_WireNetworkMessage> _readNextNetworkMessage(
  Socket socket, {
  Duration timeout = const Duration(seconds: 5),
}) {
  final completer = Completer<_WireNetworkMessage>();
  final buffer = <int>[];
  late StreamSubscription<List<int>> subscription;

  _WireNetworkMessage? tryParseFrame() {
    if (buffer.length < 4) {
      return null;
    }

    final messageLength = ByteData.sublistView(
      Uint8List.fromList(buffer.sublist(0, 4)),
    ).getInt32(0, Endian.big);

    if (messageLength <= 0 || messageLength > 10 * 1024 * 1024) {
      throw FormatException('Invalid framed message length: $messageLength');
    }

    if (buffer.length < 4 + messageLength) {
      return null;
    }

    final messageBytes = buffer.sublist(4, 4 + messageLength);
    buffer.removeRange(0, 4 + messageLength);

    final messageJson = utf8.decode(messageBytes);
    final decoded = jsonDecode(messageJson);
    if (decoded is! Map<String, dynamic>) {
      throw const FormatException('Invalid network message payload');
    }

    return _WireNetworkMessage(
      messageId: decoded['messageId'] as String? ?? '',
      commandId: decoded['commandId'] as String? ?? '',
      payloadMap: _decodePayloadMap(decoded['payload']),
    );
  }

  subscription = socket.listen(
    (chunk) {
      if (completer.isCompleted) {
        return;
      }

      buffer.addAll(chunk);
      try {
        final parsed = tryParseFrame();
        if (parsed == null) {
          return;
        }

        completer.complete(parsed);
        unawaited(subscription.cancel());
      } catch (e, st) {
        completer.completeError(e, st);
        unawaited(subscription.cancel());
      }
    },
    onError: (Object error, StackTrace stackTrace) {
      if (!completer.isCompleted) {
        completer.completeError(error, stackTrace);
      }
    },
    onDone: () {
      if (!completer.isCompleted) {
        completer.completeError(
          const SocketException('Socket closed before receiving message'),
        );
      }
    },
    cancelOnError: true,
  );

  return completer.future.timeout(timeout);
}

Map<String, dynamic>? _decodePayloadMap(dynamic payload) {
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

class _QuestMockServer {
  _QuestMockServer._(this._serverSocket);

  final ServerSocket _serverSocket;
  final Queue<Socket> _pendingConnections = Queue<Socket>();
  final Queue<Completer<Socket>> _connectionWaiters =
      Queue<Completer<Socket>>();
  final List<Socket> _allClients = <Socket>[];
  StreamSubscription<Socket>? _acceptSubscription;

  int get port => _serverSocket.port;

  static Future<_QuestMockServer> start() async {
    final serverSocket =
        await ServerSocket.bind(InternetAddress.loopbackIPv4, 0);
    final instance = _QuestMockServer._(serverSocket);
    instance._acceptSubscription = serverSocket.listen(instance._handleClient);
    return instance;
  }

  Future<Socket> nextClient({
    Duration timeout = const Duration(seconds: 5),
  }) {
    if (_pendingConnections.isNotEmpty) {
      return Future<Socket>.value(_pendingConnections.removeFirst());
    }

    final completer = Completer<Socket>();
    _connectionWaiters.add(completer);

    return completer.future.timeout(
      timeout,
      onTimeout: () {
        _connectionWaiters.remove(completer);
        throw TimeoutException('Timed out waiting for incoming client');
      },
    );
  }

  Future<void> sendRuntimeStatusUpdate(
    Socket socket, {
    required String sessionId,
    required String status,
    required String reasonCode,
    int pendingQueueSize = 0,
  }) {
    return _sendNetworkMessage(
      socket,
      commandId: RuntimeStatusSignalCommandIds.runtimeStatusUpdate,
      payload: <String, dynamic>{
        'sessionId': sessionId,
        'patientId': 'patient-1',
        'therapistId': 'therapist-1',
        'status': status,
        'previousStatus': '',
        'reasonCode': reasonCode,
        'changedAtUnixMs': DateTime.now().toUtc().millisecondsSinceEpoch,
        'pendingQueueSize': pendingQueueSize,
      },
    );
  }

  Future<void> sendCommandAck(
    Socket socket, {
    required String requestMessageId,
    required String requestCommandId,
    required String sessionId,
    required String status,
    required String reasonCode,
  }) {
    return _sendNetworkMessage(
      socket,
      commandId: CommandAckIds.commandAck,
      payload: <String, dynamic>{
        'messageId': requestMessageId,
        'commandId': requestCommandId,
        'sessionId': sessionId,
        'status': status,
        'reasonCode': reasonCode,
        'processedAtUtc': DateTime.now().toUtc().toIso8601String(),
      },
    );
  }

  Future<void> dispose() async {
    while (_connectionWaiters.isNotEmpty) {
      final waiter = _connectionWaiters.removeFirst();
      if (!waiter.isCompleted) {
        waiter.completeError(StateError('Mock server disposed'));
      }
    }

    await _acceptSubscription?.cancel();

    for (final client in _allClients) {
      await client.close();
      client.destroy();
    }
    _allClients.clear();

    await _serverSocket.close();
  }

  void _handleClient(Socket socket) {
    _allClients.add(socket);
    if (_connectionWaiters.isNotEmpty) {
      final waiter = _connectionWaiters.removeFirst();
      if (!waiter.isCompleted) {
        waiter.complete(socket);
      }
      return;
    }

    _pendingConnections.add(socket);
  }

  Future<void> _sendNetworkMessage(
    Socket socket, {
    required String commandId,
    Map<String, dynamic>? payload,
  }) async {
    final message = <String, dynamic>{
      'messageId': 'srv-${DateTime.now().toUtc().microsecondsSinceEpoch}',
      'timestamp': DateTime.now().toUtc().millisecondsSinceEpoch ~/ 1000,
      'commandId': commandId,
      'payload': payload == null
          ? null
          : base64Encode(utf8.encode(jsonEncode(payload))),
    };

    final messageBytes = utf8.encode(jsonEncode(message));
    final lengthPrefix = ByteData(4)
      ..setInt32(0, messageBytes.length, Endian.big);

    socket.add(lengthPrefix.buffer.asUint8List());
    socket.add(messageBytes);
    await socket.flush();
  }
}

class _WireNetworkMessage {
  const _WireNetworkMessage({
    required this.messageId,
    required this.commandId,
    required this.payloadMap,
  });

  final String messageId;
  final String commandId;
  final Map<String, dynamic>? payloadMap;
}
