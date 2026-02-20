import 'dart:async';
import 'dart:collection';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter_controller/models/critical_command_envelope.dart';
import 'package:flutter_controller/models/runtime_status_signal.dart';
import 'package:flutter_controller/models/session_fsm_contract.dart';
import 'package:flutter_controller/models/session_recovery_policy.dart';
import 'package:flutter_controller/services/connection_service.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('save/resume regression policy matrix', () {
    test('different remote non-terminal states always require decision', () {
      const nonTerminalStates = <SessionLifecycleState>[
        SessionLifecycleState.inProgress,
        SessionLifecycleState.paused,
        SessionLifecycleState.interrupted,
      ];

      for (final state in nonTerminalStates) {
        final requiresDecision = SessionRecoveryPolicy.shouldRequireDecision(
          localSessionId: 'local-session',
          remoteSessionId: 'quest-session',
          remoteState: state,
          remoteRuntimeStatus: TherapistRuntimeStatus.connected,
        );

        expect(
          requiresDecision,
          isTrue,
          reason: 'Expected decision gate for non-terminal state ${state.name}',
        );
      }
    });

    test('created remote state does not require decision', () {
      final requiresDecision = SessionRecoveryPolicy.shouldRequireDecision(
        localSessionId: 'local-session',
        remoteSessionId: 'quest-session',
        remoteState: SessionLifecycleState.created,
        remoteRuntimeStatus: TherapistRuntimeStatus.connected,
      );

      expect(requiresDecision, isFalse);
    });

    test('terminal remote states never require decision', () {
      const terminalStates = <SessionLifecycleState>[
        SessionLifecycleState.completed,
        SessionLifecycleState.abortedByTherapist,
        SessionLifecycleState.failedTechnical,
      ];

      for (final state in terminalStates) {
        final requiresDecision = SessionRecoveryPolicy.shouldRequireDecision(
          localSessionId: 'local-session',
          remoteSessionId: 'quest-session',
          remoteState: state,
          remoteRuntimeStatus: TherapistRuntimeStatus.playing,
        );

        expect(
          requiresDecision,
          isFalse,
          reason: 'Expected no gate for terminal state ${state.name}',
        );
      }
    });

    test(
        'runtime fallback matrix is respected when remote state is unavailable',
        () {
      const runtimeExpectations = <TherapistRuntimeStatus?, bool>{
        TherapistRuntimeStatus.playing: true,
        TherapistRuntimeStatus.paused: true,
        TherapistRuntimeStatus.interrupted: true,
        TherapistRuntimeStatus.syncPending: false,
        TherapistRuntimeStatus.connected: false,
        null: false,
      };

      runtimeExpectations.forEach((runtimeStatus, expected) {
        final requiresDecision = SessionRecoveryPolicy.shouldRequireDecision(
          localSessionId: 'local-session',
          remoteSessionId: 'quest-session',
          remoteState: null,
          remoteRuntimeStatus: runtimeStatus,
        );

        expect(
          requiresDecision,
          expected,
          reason: 'Unexpected gate result for runtime status $runtimeStatus',
        );
      });
    });

    test('empty or same remote session id never requires decision', () {
      final emptyRemote = SessionRecoveryPolicy.shouldRequireDecision(
        localSessionId: 'local-session',
        remoteSessionId: '',
        remoteState: SessionLifecycleState.inProgress,
        remoteRuntimeStatus: TherapistRuntimeStatus.playing,
      );

      final sameRemote = SessionRecoveryPolicy.shouldRequireDecision(
        localSessionId: 'shared-session',
        remoteSessionId: 'shared-session',
        remoteState: SessionLifecycleState.interrupted,
        remoteRuntimeStatus: TherapistRuntimeStatus.interrupted,
      );

      expect(emptyRemote, isFalse);
      expect(sameRemote, isFalse);
    });
  });

  group('save/resume regression protocol flow', () {
    test('resume flow keeps attached Quest session id', () async {
      final server = await _SaveResumeMockServer.start();
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

      const questSessionId = 'quest-save-resume-001';
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

      await server.sendRuntimeStatusUpdate(
        client,
        sessionId: questSessionId,
        status: TherapistRuntimeStatus.interrupted.wireValue,
        reasonCode: 'AUTO_RECOVERY_APP_RESTART',
      );
      await _waitForCondition(() => activeSessionId == questSessionId);

      final resumeFuture = connection.sendCriticalCommand(
        commandId: CriticalCommandIds.resumeGame,
        sessionId: activeSessionId,
        payload: <String, dynamic>{'reason': 'TherapistResumeAfterRecovery'},
        ackTimeout: const Duration(milliseconds: 700),
        maxRetries: 2,
      );

      final resumeRequest = await inbox.nextMessage();
      expect(resumeRequest.commandId, CriticalCommandIds.resumeGame);
      expect(resumeRequest.payloadMap?['sessionId'], questSessionId);

      await server.sendCommandAck(
        client,
        requestMessageId: resumeRequest.messageId,
        requestCommandId: resumeRequest.commandId,
        sessionId: questSessionId,
        status: CommandAckStatus.ack,
        reasonCode: 'OK',
      );

      await resumeFuture;
    });

    test('start-new flow ends remote session before using new local session id',
        () async {
      final server = await _SaveResumeMockServer.start();
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

      const remoteSessionId = 'quest-active-session';
      final localSessionId =
          'mobile-new-session-${DateTime.now().toUtc().millisecondsSinceEpoch}';

      final endSessionFuture = connection.sendCriticalCommand(
        commandId: CriticalCommandIds.endSession,
        sessionId: remoteSessionId,
        payload: <String, dynamic>{'reason': 'TherapistEndedSession'},
        ackTimeout: const Duration(milliseconds: 700),
        maxRetries: 2,
      );

      final endSessionRequest = await inbox.nextMessage();
      expect(endSessionRequest.commandId, CriticalCommandIds.endSession);
      expect(endSessionRequest.payloadMap?['sessionId'], remoteSessionId);

      await server.sendCommandAck(
        client,
        requestMessageId: endSessionRequest.messageId,
        requestCommandId: endSessionRequest.commandId,
        sessionId: remoteSessionId,
        status: CommandAckStatus.ack,
        reasonCode: 'OK',
      );
      await endSessionFuture;

      final startGameFuture = connection.sendCriticalCommand(
        commandId: CriticalCommandIds.startGame,
        sessionId: localSessionId,
        payload: <String, dynamic>{'gameId': 'focus_ball'},
        ackTimeout: const Duration(milliseconds: 700),
        maxRetries: 2,
      );

      final startGameRequest = await inbox.nextMessage();
      expect(startGameRequest.commandId, CriticalCommandIds.startGame);
      expect(startGameRequest.payloadMap?['sessionId'], localSessionId);
      expect(startGameRequest.payloadMap?['sessionId'], isNot(remoteSessionId));

      await server.sendCommandAck(
        client,
        requestMessageId: startGameRequest.messageId,
        requestCommandId: startGameRequest.commandId,
        sessionId: localSessionId,
        status: CommandAckStatus.ack,
        reasonCode: 'OK',
      );

      await startGameFuture;
    });
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

class _SaveResumeMockServer {
  _SaveResumeMockServer._(this._serverSocket);

  final ServerSocket _serverSocket;
  final Queue<Socket> _pendingConnections = Queue<Socket>();
  final Queue<Completer<Socket>> _connectionWaiters =
      Queue<Completer<Socket>>();
  final List<Socket> _allClients = <Socket>[];
  StreamSubscription<Socket>? _acceptSubscription;

  int get port => _serverSocket.port;

  static Future<_SaveResumeMockServer> start() async {
    final serverSocket =
        await ServerSocket.bind(InternetAddress.loopbackIPv4, 0);
    final instance = _SaveResumeMockServer._(serverSocket);
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
