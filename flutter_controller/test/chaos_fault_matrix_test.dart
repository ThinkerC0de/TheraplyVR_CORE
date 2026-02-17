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
  group('chaos fault matrix', () {
    test(
      'network toggle: in-flight critical command retries across reconnect',
      () async {
        final server = await _ChaosMockServer.start();
        addTearDown(server.dispose);

        final connection = ConnectionService();
        addTearDown(connection.dispose);

        final connected = await connection.connect(
          InternetAddress.loopbackIPv4.address,
          server.port,
        );
        expect(connected, isTrue);

        final firstClient = await server.nextClient();
        final firstInbox = _FramedMessageInbox(firstClient);
        addTearDown(firstInbox.dispose);

        const sessionId = 'chaos-network-toggle-session';
        final sendFuture = connection.sendCriticalCommand(
          commandId: CriticalCommandIds.pauseGame,
          sessionId: sessionId,
          payload: <String, dynamic>{'reason': 'ChaosNetworkToggle'},
          ackTimeout: const Duration(milliseconds: 350),
          maxRetries: 4,
        );

        final firstAttempt = await firstInbox.nextMessage();
        expect(firstAttempt.commandId, CriticalCommandIds.pauseGame);
        expect(firstAttempt.payloadMap?['sessionId'], sessionId);

        await firstClient.close();
        firstClient.destroy();
        await _waitForCondition(() => !connection.isConnected);

        final reconnectFuture = connection.reconnect(
          maxAttempts: 6,
          baseDelay: const Duration(milliseconds: 80),
        );

        final secondClient = await server.nextClient();
        final secondInbox = _FramedMessageInbox(secondClient);
        addTearDown(secondInbox.dispose);

        expect(await reconnectFuture, isTrue);

        final retryAttempt = await secondInbox.nextMessage(
          timeout: const Duration(seconds: 6),
        );
        expect(retryAttempt.commandId, CriticalCommandIds.pauseGame);
        expect(retryAttempt.messageId, isNot(firstAttempt.messageId));
        expect(retryAttempt.payloadMap?['sessionId'], sessionId);

        await server.sendCommandAck(
          secondClient,
          requestMessageId: retryAttempt.messageId,
          requestCommandId: retryAttempt.commandId,
          sessionId: sessionId,
          status: CommandAckStatus.ack,
          reasonCode: 'OK',
        );

        await sendFuture;
      },
    );

    test(
      'app kill: pending command fails and post-restart command succeeds',
      () async {
        final server = await _ChaosMockServer.start();
        addTearDown(server.dispose);

        final connection = ConnectionService();
        addTearDown(connection.dispose);

        final connected = await connection.connect(
          InternetAddress.loopbackIPv4.address,
          server.port,
        );
        expect(connected, isTrue);

        final firstClient = await server.nextClient();
        final firstInbox = _FramedMessageInbox(firstClient);
        addTearDown(firstInbox.dispose);

        const sessionId = 'chaos-app-kill-session';
        final pendingCommand = connection.sendCriticalCommand(
          commandId: CriticalCommandIds.endSession,
          sessionId: sessionId,
          payload: <String, dynamic>{'reason': 'ChaosAppKill'},
          ackTimeout: const Duration(milliseconds: 300),
          maxRetries: 2,
        );

        final inFlightRequest = await firstInbox.nextMessage();
        expect(inFlightRequest.commandId, CriticalCommandIds.endSession);

        connection.dispose();

        await expectLater(
          pendingCommand,
          throwsA(
            predicate<Object>(
              (error) => error.toString().contains('failed after'),
            ),
          ),
        );

        final restartedConnection = ConnectionService();
        addTearDown(restartedConnection.dispose);

        final reconnected = await restartedConnection.connect(
          InternetAddress.loopbackIPv4.address,
          server.port,
        );
        expect(reconnected, isTrue);

        final restartedClient = await server.nextClient();
        final restartedInbox = _FramedMessageInbox(restartedClient);
        addTearDown(restartedInbox.dispose);

        final resumedCommand = restartedConnection.sendCriticalCommand(
          commandId: CriticalCommandIds.resumeGame,
          sessionId: sessionId,
          payload: <String, dynamic>{'reason': 'PostRestartControl'},
          ackTimeout: const Duration(milliseconds: 700),
          maxRetries: 2,
        );

        final resumedRequest = await restartedInbox.nextMessage();
        expect(resumedRequest.commandId, CriticalCommandIds.resumeGame);

        await server.sendCommandAck(
          restartedClient,
          requestMessageId: resumedRequest.messageId,
          requestCommandId: resumedRequest.commandId,
          sessionId: sessionId,
          status: CommandAckStatus.ack,
          reasonCode: 'OK',
        );

        await resumedCommand;
      },
    );

    test(
      'device reboot: reconnect and runtime attach keep session control',
      () async {
        final server = await _ChaosMockServer.start();
        addTearDown(server.dispose);

        final connection = ConnectionService();
        addTearDown(connection.dispose);

        const questSessionId = 'chaos-device-reboot-session';
        var activeSessionId = 'mobile-fallback-session';

        final subscription = connection.messages.listen((message) {
          final runtimeUpdate = RuntimeStatusUpdateSignal.tryFromNetworkMessage(
            message,
          );
          if (runtimeUpdate != null && runtimeUpdate.sessionId.isNotEmpty) {
            activeSessionId = runtimeUpdate.sessionId;
          }
        });
        addTearDown(subscription.cancel);

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
        firstClient.destroy();
        await _waitForCondition(() => !connection.isConnected);

        final reconnectFuture = connection.reconnect(
          maxAttempts: 8,
          baseDelay: const Duration(milliseconds: 80),
        );
        final rebootedClient = await server.nextClient();
        final rebootedInbox = _FramedMessageInbox(rebootedClient);
        addTearDown(rebootedInbox.dispose);

        expect(await reconnectFuture, isTrue);

        await server.sendRuntimeStatusUpdate(
          rebootedClient,
          sessionId: questSessionId,
          status: TherapistRuntimeStatus.interrupted.wireValue,
          reasonCode: 'DEVICE_REBOOT_RECOVERED',
        );
        await _waitForCondition(() => activeSessionId == questSessionId);

        final commandFuture = connection.sendCriticalCommand(
          commandId: CriticalCommandIds.resumeGame,
          sessionId: activeSessionId,
          payload: <String, dynamic>{'reason': 'ChaosDeviceRebootRecovery'},
          ackTimeout: const Duration(milliseconds: 700),
          maxRetries: 2,
        );

        final commandRequest = await rebootedInbox.nextMessage();
        expect(commandRequest.commandId, CriticalCommandIds.resumeGame);
        expect(commandRequest.payloadMap?['sessionId'], questSessionId);

        await server.sendCommandAck(
          rebootedClient,
          requestMessageId: commandRequest.messageId,
          requestCommandId: commandRequest.commandId,
          sessionId: questSessionId,
          status: CommandAckStatus.ack,
          reasonCode: 'OK',
        );

        await commandFuture;
      },
    );

    test('delayed ACK: stale ACK is ignored and retry ACK succeeds', () async {
      final server = await _ChaosMockServer.start();
      addTearDown(server.dispose);

      final connection = ConnectionService();
      addTearDown(connection.dispose);

      final connected = await connection.connect(
        InternetAddress.loopbackIPv4.address,
        server.port,
      );
      expect(connected, isTrue);

      final client = await server.nextClient();
      final inbox = _FramedMessageInbox(client);
      addTearDown(inbox.dispose);

      const sessionId = 'chaos-delayed-ack-session';
      final sendFuture = connection.sendCriticalCommand(
        commandId: CriticalCommandIds.stopGame,
        sessionId: sessionId,
        payload: <String, dynamic>{'reason': 'ChaosDelayedAck'},
        ackTimeout: const Duration(milliseconds: 180),
        maxRetries: 3,
      );

      final firstAttempt = await inbox.nextMessage();
      expect(firstAttempt.commandId, CriticalCommandIds.stopGame);

      await Future<void>.delayed(const Duration(milliseconds: 260));

      final retryAttempt = await inbox.nextMessage();
      expect(retryAttempt.commandId, CriticalCommandIds.stopGame);
      expect(retryAttempt.messageId, isNot(firstAttempt.messageId));

      var completed = false;
      Object? unexpectedError;
      unawaited(
        sendFuture.then((_) {
          completed = true;
        }).catchError((Object error) {
          unexpectedError = error;
        }),
      );

      await server.sendCommandAck(
        client,
        requestMessageId: firstAttempt.messageId,
        requestCommandId: firstAttempt.commandId,
        sessionId: sessionId,
        status: CommandAckStatus.ack,
        reasonCode: 'LATE_OK',
      );

      await Future<void>.delayed(const Duration(milliseconds: 90));
      expect(completed, isFalse);
      expect(unexpectedError, isNull);

      await server.sendCommandAck(
        client,
        requestMessageId: retryAttempt.messageId,
        requestCommandId: retryAttempt.commandId,
        sessionId: sessionId,
        status: CommandAckStatus.ack,
        reasonCode: 'OK',
      );

      await sendFuture;
    });

    test(
      'duplicate commands: repeated command remains isolated under duplicate ACK noise',
      () async {
        final server = await _ChaosMockServer.start();
        addTearDown(server.dispose);

        final connection = ConnectionService();
        addTearDown(connection.dispose);

        final connected = await connection.connect(
          InternetAddress.loopbackIPv4.address,
          server.port,
        );
        expect(connected, isTrue);

        final client = await server.nextClient();
        final inbox = _FramedMessageInbox(client);
        addTearDown(inbox.dispose);

        const sessionId = 'chaos-duplicate-command-session';

        final firstFuture = connection.sendCriticalCommand(
          commandId: CriticalCommandIds.pauseGame,
          sessionId: sessionId,
          payload: <String, dynamic>{'reason': 'FirstDuplicateIssue'},
          ackTimeout: const Duration(milliseconds: 900),
          maxRetries: 1,
        );

        final firstRequest = await inbox.nextMessage();
        expect(firstRequest.commandId, CriticalCommandIds.pauseGame);

        await server.sendCommandAck(
          client,
          requestMessageId: firstRequest.messageId,
          requestCommandId: firstRequest.commandId,
          sessionId: sessionId,
          status: CommandAckStatus.ack,
          reasonCode: 'OK',
        );
        await firstFuture;

        await server.sendCommandAck(
          client,
          requestMessageId: firstRequest.messageId,
          requestCommandId: firstRequest.commandId,
          sessionId: sessionId,
          status: CommandAckStatus.ack,
          reasonCode: 'DUPLICATE_ACK',
        );

        final secondFuture = connection.sendCriticalCommand(
          commandId: CriticalCommandIds.pauseGame,
          sessionId: sessionId,
          payload: <String, dynamic>{'reason': 'SecondDuplicateIssue'},
          ackTimeout: const Duration(milliseconds: 900),
          maxRetries: 1,
        );

        final secondRequest = await inbox.nextMessage();
        expect(secondRequest.commandId, CriticalCommandIds.pauseGame);
        expect(secondRequest.messageId, isNot(firstRequest.messageId));

        var secondCompleted = false;
        Object? secondUnexpectedError;
        unawaited(
          secondFuture.then((_) {
            secondCompleted = true;
          }).catchError((Object error) {
            secondUnexpectedError = error;
          }),
        );

        await Future<void>.delayed(const Duration(milliseconds: 100));
        expect(secondCompleted, isFalse);
        expect(secondUnexpectedError, isNull);

        await server.sendCommandAck(
          client,
          requestMessageId: secondRequest.messageId,
          requestCommandId: secondRequest.commandId,
          sessionId: sessionId,
          status: CommandAckStatus.ack,
          reasonCode: 'OK',
        );

        await secondFuture;
      },
    );
  });
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

class _FramedMessageInbox {
  _FramedMessageInbox(this._socket) {
    _subscription = _socket.listen(
      _handleChunk,
      onError: _handleError,
      onDone: _handleDone,
      cancelOnError: true,
    );
  }

  final Socket _socket;
  final List<int> _buffer = <int>[];
  final Queue<_WireNetworkMessage> _ready = Queue<_WireNetworkMessage>();
  final Queue<Completer<_WireNetworkMessage>> _waiters =
      Queue<Completer<_WireNetworkMessage>>();
  late final StreamSubscription<List<int>> _subscription;
  Object? _terminalError;

  Future<_WireNetworkMessage> nextMessage({
    Duration timeout = const Duration(seconds: 5),
  }) {
    if (_ready.isNotEmpty) {
      return Future<_WireNetworkMessage>.value(_ready.removeFirst());
    }

    if (_terminalError != null) {
      return Future<_WireNetworkMessage>.error(_terminalError!);
    }

    final completer = Completer<_WireNetworkMessage>();
    _waiters.add(completer);

    return completer.future.timeout(
      timeout,
      onTimeout: () {
        _waiters.remove(completer);
        throw TimeoutException('Timed out waiting for framed network message');
      },
    );
  }

  Future<void> dispose() async {
    await _subscription.cancel();
    _failAllWaiters(StateError('Framed inbox disposed'));
  }

  void _handleChunk(List<int> chunk) {
    if (_terminalError != null) {
      return;
    }

    _buffer.addAll(chunk);

    while (true) {
      final parsed = _tryParseOne();
      if (parsed == null) {
        return;
      }

      if (_waiters.isNotEmpty) {
        final waiter = _waiters.removeFirst();
        if (!waiter.isCompleted) {
          waiter.complete(parsed);
        }
      } else {
        _ready.add(parsed);
      }
    }
  }

  _WireNetworkMessage? _tryParseOne() {
    if (_buffer.length < 4) {
      return null;
    }

    final messageLength = ByteData.sublistView(
      Uint8List.fromList(_buffer.sublist(0, 4)),
    ).getInt32(0, Endian.big);

    if (messageLength <= 0 || messageLength > 10 * 1024 * 1024) {
      throw FormatException('Invalid framed message length: $messageLength');
    }

    if (_buffer.length < 4 + messageLength) {
      return null;
    }

    final messageBytes = _buffer.sublist(4, 4 + messageLength);
    _buffer.removeRange(0, 4 + messageLength);

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

  void _handleError(Object error, StackTrace stackTrace) {
    _terminalError = error;
    _failAllWaiters(error);
  }

  void _handleDone() {
    final error = const SocketException(
      'Socket closed before receiving expected framed message',
    );
    _terminalError = error;
    _failAllWaiters(error);
  }

  void _failAllWaiters(Object error) {
    while (_waiters.isNotEmpty) {
      final waiter = _waiters.removeFirst();
      if (!waiter.isCompleted) {
        waiter.completeError(error);
      }
    }
  }
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

class _ChaosMockServer {
  _ChaosMockServer._(this._serverSocket);

  final ServerSocket _serverSocket;
  final Queue<Socket> _pendingConnections = Queue<Socket>();
  final Queue<Completer<Socket>> _connectionWaiters =
      Queue<Completer<Socket>>();
  final List<Socket> _allClients = <Socket>[];
  StreamSubscription<Socket>? _acceptSubscription;

  int get port => _serverSocket.port;

  static Future<_ChaosMockServer> start() async {
    final serverSocket =
        await ServerSocket.bind(InternetAddress.loopbackIPv4, 0);
    final instance = _ChaosMockServer._(serverSocket);
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
