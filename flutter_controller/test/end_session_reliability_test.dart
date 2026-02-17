import 'dart:async';
import 'dart:collection';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter_controller/models/critical_command_envelope.dart';
import 'package:flutter_controller/services/connection_service.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('END_SESSION reliability', () {
    test('completes only after ACK verification', () async {
      final server = await _QuestAckMockServer.start();
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

      const sessionId = 'quest-session-end-ack';
      final sendFuture = connection.sendCriticalCommand(
        commandId: CriticalCommandIds.endSession,
        sessionId: sessionId,
        payload: <String, dynamic>{'reason': 'TherapistEndedSession'},
        ackTimeout: const Duration(milliseconds: 500),
        maxRetries: 2,
      );

      final firstRequest = await inbox.nextMessage();
      expect(firstRequest.commandId, CriticalCommandIds.endSession);
      expect(firstRequest.payloadMap, isNotNull);
      expect(firstRequest.payloadMap!['sessionId'], sessionId);

      var completed = false;
      Object? unexpectedError;
      unawaited(
        sendFuture.then((_) {
          completed = true;
        }).catchError((Object error) {
          unexpectedError = error;
        }),
      );

      await Future<void>.delayed(const Duration(milliseconds: 150));
      expect(completed, isFalse);
      expect(unexpectedError, isNull);

      await server.sendCommandAck(
        client,
        requestMessageId: firstRequest.messageId,
        requestCommandId: firstRequest.commandId,
        sessionId: sessionId,
        status: CommandAckStatus.ack,
        reasonCode: 'OK',
      );

      await sendFuture;
    });

    test('retries after NACK and succeeds on later ACK', () async {
      final server = await _QuestAckMockServer.start();
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

      const sessionId = 'quest-session-end-retry';
      final sendFuture = connection.sendCriticalCommand(
        commandId: CriticalCommandIds.endSession,
        sessionId: sessionId,
        payload: <String, dynamic>{'reason': 'TherapistEndedSession'},
        ackTimeout: const Duration(milliseconds: 500),
        maxRetries: 3,
      );

      final firstRequest = await inbox.nextMessage();
      expect(firstRequest.commandId, CriticalCommandIds.endSession);
      expect(firstRequest.payloadMap?['sessionId'], sessionId);

      await server.sendCommandAck(
        client,
        requestMessageId: firstRequest.messageId,
        requestCommandId: firstRequest.commandId,
        sessionId: sessionId,
        status: CommandAckStatus.nack,
        reasonCode: 'TEMPORARY_REJECT',
      );

      final retryRequest = await inbox.nextMessage();
      expect(retryRequest.commandId, CriticalCommandIds.endSession);
      expect(retryRequest.payloadMap?['sessionId'], sessionId);
      expect(retryRequest.messageId, isNot(firstRequest.messageId));

      await server.sendCommandAck(
        client,
        requestMessageId: retryRequest.messageId,
        requestCommandId: retryRequest.commandId,
        sessionId: sessionId,
        status: CommandAckStatus.ack,
        reasonCode: 'OK',
      );

      await sendFuture;
    });

    test('fails after max retries when END_SESSION keeps NACKing', () async {
      final server = await _QuestAckMockServer.start();
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

      const sessionId = 'quest-session-end-fail';
      final sendFuture = connection.sendCriticalCommand(
        commandId: CriticalCommandIds.endSession,
        sessionId: sessionId,
        payload: <String, dynamic>{'reason': 'TherapistEndedSession'},
        ackTimeout: const Duration(milliseconds: 500),
        maxRetries: 2,
      );
      final expectedFailure = expectLater(
        sendFuture,
        throwsA(
          predicate<Object>(
            (error) => error.toString().contains('SESSION_NOT_ACTIVE'),
          ),
        ),
      );

      final firstRequest = await inbox.nextMessage();
      expect(firstRequest.commandId, CriticalCommandIds.endSession);

      await server.sendCommandAck(
        client,
        requestMessageId: firstRequest.messageId,
        requestCommandId: firstRequest.commandId,
        sessionId: sessionId,
        status: CommandAckStatus.nack,
        reasonCode: 'SESSION_NOT_ACTIVE',
      );

      final secondRequest = await inbox.nextMessage();
      expect(secondRequest.commandId, CriticalCommandIds.endSession);

      await server.sendCommandAck(
        client,
        requestMessageId: secondRequest.messageId,
        requestCommandId: secondRequest.commandId,
        sessionId: sessionId,
        status: CommandAckStatus.nack,
        reasonCode: 'SESSION_NOT_ACTIVE',
      );

      await expectedFailure;
    });
  });
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

class _QuestAckMockServer {
  _QuestAckMockServer._(this._serverSocket);

  final ServerSocket _serverSocket;
  final Queue<Socket> _pendingConnections = Queue<Socket>();
  final Queue<Completer<Socket>> _connectionWaiters =
      Queue<Completer<Socket>>();
  final List<Socket> _allClients = <Socket>[];
  StreamSubscription<Socket>? _acceptSubscription;

  int get port => _serverSocket.port;

  static Future<_QuestAckMockServer> start() async {
    final serverSocket =
        await ServerSocket.bind(InternetAddress.loopbackIPv4, 0);
    final instance = _QuestAckMockServer._(serverSocket);
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
