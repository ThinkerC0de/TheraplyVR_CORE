import 'dart:io';
import 'dart:convert';
import 'dart:async';
import 'package:flutter/foundation.dart';
import 'package:flutter_controller/models/critical_command_envelope.dart';
import 'discovery_service.dart';

class ConnectionService {
  Socket? _socket;
  bool _isConnected = false;
  bool _isDisconnecting = false;
  Future<bool>? _connectInFlight;
  Future<void> _writeQueue = Future<void>.value();
  String? _lastIp;
  int? _lastPort;

  final StreamController<Map<String, dynamic>> _messageController =
      StreamController<Map<String, dynamic>>.broadcast();
  final StreamController<bool> _connectionController =
      StreamController<bool>.broadcast();

  // Discovery service reference (for automatic pause/resume)
  DiscoveryService? _discoveryService;

  // Buffer for incomplete messages
  final List<int> _receiveBuffer = [];
  Map<String, dynamic>? _bufferedWebRtcOffer;
  final List<Map<String, dynamic>> _bufferedWebRtcCandidates = [];
  final Map<String, Completer<CriticalCommandAck>> _pendingCriticalAcks = {};
  static const int _maxBufferedWebRtcCandidates = 64;

  Stream<Map<String, dynamic>> get messages => _messageController.stream;
  Stream<bool> get connectionStatus => _connectionController.stream;
  bool get isConnected => _isConnected;

  /// Set discovery service for automatic pause/resume
  void setDiscoveryService(DiscoveryService discoveryService) {
    _discoveryService = discoveryService;
  }

  Future<bool> connect(String ip, int port) async {
    _lastIp = ip;
    _lastPort = port;

    if (_isConnected && _socket != null) {
      return true;
    }

    if (_connectInFlight != null) {
      return _connectInFlight!;
    }

    _connectInFlight = _connectInternal(ip, port);
    final result = await _connectInFlight!;
    _connectInFlight = null;
    return result;
  }

  Future<bool> _connectInternal(String ip, int port) async {
    try {
      print('[Connection] 🔌 Connecting to $ip:$port');

      final socket = await Socket.connect(
        ip,
        port,
        timeout: const Duration(seconds: 5),
      );
      _socket = socket;

      // TCP no delay for low latency
      socket.setOption(SocketOption.tcpNoDelay, true);

      _isConnected = true;
      if (!_connectionController.isClosed) {
        _connectionController.add(true);
      }

      // Pause UDP discovery when TCP connected
      _discoveryService?.pauseScanning();

      print('[Connection] ✅ Connected successfully');

      // Listen for incoming messages
      socket.listen(
        _handleData,
        onError: (error) {
          if (!identical(_socket, socket)) {
            print('[Connection] ℹ️ Ignoring stale socket error callback');
            return;
          }
          print('[Connection] ❌ Error: $error');
          unawaited(disconnect());
        },
        onDone: () {
          if (!identical(_socket, socket)) {
            print('[Connection] ℹ️ Ignoring stale socket close callback');
            return;
          }
          print(
              '[Connection] 🔌 Socket closed (onDone) - peer or app closed connection');
          unawaited(disconnect());
        },
        cancelOnError: false,
      );

      return true;
    } catch (e) {
      print('[Connection] ❌ Failed to connect: $e');
      _isConnected = false;
      if (!_connectionController.isClosed) {
        _connectionController.add(false);
      }
      return false;
    }
  }

  Future<bool> reconnect({
    int maxAttempts = 8,
    Duration baseDelay = const Duration(milliseconds: 300),
  }) async {
    final ip = _lastIp;
    final port = _lastPort;
    if (ip == null || port == null) {
      print('[Connection] ⚠️ Cannot reconnect - no last endpoint');
      return false;
    }

    if (_isConnected && _socket != null) {
      return true;
    }

    for (int attempt = 1; attempt <= maxAttempts; attempt++) {
      final ok = await connect(ip, port);
      if (ok) {
        print('[Connection] ✅ Reconnected on attempt $attempt');
        return true;
      }

      final ms = baseDelay.inMilliseconds * attempt;
      await Future.delayed(Duration(milliseconds: ms));
    }

    print('[Connection] ❌ Reconnect failed after $maxAttempts attempts');
    return false;
  }

  void _handleData(Uint8List data) {
    _receiveBuffer.addAll(data);

    while (_receiveBuffer.length >= 4) {
      try {
        // Read 4-byte length prefix (big-endian)
        final lengthBytes = _receiveBuffer.sublist(0, 4);
        final messageLength =
            ByteData.sublistView(Uint8List.fromList(lengthBytes))
                .getInt32(0, Endian.big);

        // Validate length
        if (messageLength <= 0 || messageLength > 10 * 1024 * 1024) {
          print(
              '[Connection] ❌ Invalid message length: $messageLength - clearing buffer');
          _receiveBuffer.clear();
          disconnect();
          return;
        }

        // Wait for complete message
        if (_receiveBuffer.length < 4 + messageLength) {
          break; // Need more data
        }

        // Extract message bytes
        final messageBytes = _receiveBuffer.sublist(4, 4 + messageLength);
        _receiveBuffer.removeRange(0, 4 + messageLength);

        // Decode JSON
        final messageJson = utf8.decode(messageBytes);

        try {
          final message = jsonDecode(messageJson) as Map<String, dynamic>;
          _handleCriticalAckIfNeeded(message);
          _bufferWebRtcSignalingIfNeeded(message);
          if (kDebugMode) {
            print('[Connection] 📥 Received: ${message['commandId']}');
          }
          if (!_messageController.isClosed) {
            _messageController.add(message);
          }
        } catch (e) {
          print('[Connection] ⚠️ Failed to parse JSON: $e');
          print(
              '[Connection] JSON preview: ${messageJson.substring(0, messageJson.length > 100 ? 100 : messageJson.length)}');
        }
      } catch (e) {
        print('[Connection] ⚠️ Error processing message: $e');
        _receiveBuffer.clear();
        break;
      }
    }
  }

  List<Map<String, dynamic>> drainBufferedWebRtcSignaling() {
    final drained = <Map<String, dynamic>>[];
    if (_bufferedWebRtcOffer != null) {
      drained.add(_bufferedWebRtcOffer!);
    }
    drained.addAll(_bufferedWebRtcCandidates);
    _bufferedWebRtcOffer = null;
    _bufferedWebRtcCandidates.clear();
    return drained;
  }

  void _bufferWebRtcSignalingIfNeeded(Map<String, dynamic> message) {
    final commandId = message['commandId'];
    if (commandId == 'WEBRTC_OFFER') {
      _bufferedWebRtcOffer = message;
      _bufferedWebRtcCandidates.clear();
      return;
    }

    if (commandId == 'WEBRTC_ICE_CANDIDATE') {
      _bufferedWebRtcCandidates.add(message);
      if (_bufferedWebRtcCandidates.length > _maxBufferedWebRtcCandidates) {
        _bufferedWebRtcCandidates.removeAt(0);
      }
    }
  }

  Future<void> sendCommand(
    String commandId,
    Map<String, dynamic>? payload, {
    String? messageId,
    DateTime? timestampUtc,
  }) async {
    if (!_isConnected || _socket == null || _isDisconnecting) {
      print('[Connection] ⚠️ Cannot send - not connected');
      return;
    }

    try {
      final issuedAt = (timestampUtc ?? DateTime.now().toUtc()).toUtc();

      // Create message matching Unity's NetworkMessageJson structure
      final message = {
        'messageId':
            messageId ?? DateTime.now().millisecondsSinceEpoch.toString(),
        'timestamp': (issuedAt.millisecondsSinceEpoch / 1000).floor(),
        'commandId': commandId,
        // CRITICAL: Unity expects payload as Base64 string!
        'payload': payload != null
            ? base64Encode(utf8.encode(jsonEncode(payload)))
            : null,
      };

      final messageJson = jsonEncode(message);
      final messageBytes = utf8.encode(messageJson);

      // Send length prefix (4 bytes, big-endian)
      final lengthBytes = ByteData(4)
        ..setInt32(0, messageBytes.length, Endian.big);
      final framedMessage = Uint8List(4 + messageBytes.length);
      framedMessage.setAll(0, lengthBytes.buffer.asUint8List());
      framedMessage.setAll(4, messageBytes);

      await _enqueueWrite(framedMessage);

      print('[Connection] 📤 Sent: $commandId (${messageBytes.length} bytes)');
    } catch (e) {
      print('[Connection] ❌ Send failed: $e');
      unawaited(disconnect());
    }
  }

  Future<void> _enqueueWrite(Uint8List framedMessage) async {
    Future<void> writeTask() async {
      if (!_isConnected || _isDisconnecting) {
        throw StateError('DISCONNECTED');
      }

      final socket = _socket;
      if (socket == null) {
        throw StateError('DISCONNECTED');
      }

      socket.add(framedMessage);
      await socket.flush();
    }

    final pending = _writeQueue.then((_) => writeTask());
    _writeQueue = pending.catchError((_) {});
    await pending;
  }

  Future<void> sendCriticalCommand({
    required String commandId,
    required String sessionId,
    Map<String, dynamic>? payload,
    DateTime? expiresAtUtc,
    Duration ackTimeout = const Duration(seconds: 3),
    int maxRetries = 3,
  }) async {
    String lastReasonCode = 'UNKNOWN';
    int attempt = 0;

    while (attempt < maxRetries) {
      attempt++;

      final envelope = CriticalCommandEnvelope.create(
        sessionId: sessionId,
        commandId: commandId,
        payload: payload,
        expiresAtUtc: expiresAtUtc,
      );

      final ackCompleter = Completer<CriticalCommandAck>();
      _pendingCriticalAcks[envelope.messageId] = ackCompleter;

      await sendCommand(
        commandId,
        envelope.toJson(),
        messageId: envelope.messageId,
        timestampUtc: DateTime.parse(envelope.issuedAtUtc).toUtc(),
      );

      try {
        final ack = await ackCompleter.future.timeout(ackTimeout);
        if (ack.isAck) {
          return;
        }

        lastReasonCode =
            ack.reasonCode.isNotEmpty ? ack.reasonCode : CommandAckStatus.nack;
        print(
            '[Connection] ⚠️ NACK for $commandId (attempt $attempt/$maxRetries): $lastReasonCode');
      } on TimeoutException {
        lastReasonCode = 'ACK_TIMEOUT';
        print(
            '[Connection] ⚠️ ACK timeout for $commandId (attempt $attempt/$maxRetries)');
      } finally {
        _pendingCriticalAcks.remove(envelope.messageId);
      }

      if (attempt < maxRetries) {
        await Future.delayed(Duration(milliseconds: 250 * attempt));
      }
    }

    if (_isTransportFailureReason(lastReasonCode)) {
      print(
          '[Connection] ⚠️ Terminal transport failure for $commandId -> forcing disconnect');
      await disconnect();
    }

    throw Exception(
      'Critical command $commandId failed after $maxRetries attempts (reason=$lastReasonCode)',
    );
  }

  bool _isTransportFailureReason(String reasonCode) {
    final normalized = reasonCode.trim().toUpperCase();
    if (normalized.isEmpty) {
      return false;
    }

    return normalized == 'DISCONNECTED' ||
        normalized == 'ACK_TIMEOUT' ||
        normalized == 'NO_ACTIVE_TCP_ROUTE' ||
        normalized == 'TRANSPORT_CLOSED' ||
        normalized == 'SOCKET_CLOSED';
  }

  void _handleCriticalAckIfNeeded(Map<String, dynamic> message) {
    final ack = CriticalCommandAck.tryFromNetworkMessage(message);
    if (ack == null || ack.messageId.isEmpty) {
      return;
    }

    final pending = _pendingCriticalAcks.remove(ack.messageId);
    if (pending != null && !pending.isCompleted) {
      pending.complete(ack);
    }
  }

  void _failPendingCriticalAcks(String reasonCode) {
    if (_pendingCriticalAcks.isEmpty) {
      return;
    }

    final keys = _pendingCriticalAcks.keys.toList();
    for (final key in keys) {
      final pending = _pendingCriticalAcks.remove(key);
      if (pending == null || pending.isCompleted) {
        continue;
      }

      pending.complete(
        CriticalCommandAck(
          messageId: key,
          commandId: '',
          sessionId: '',
          status: CommandAckStatus.nack,
          reasonCode: reasonCode,
          processedAtUtc: DateTime.now().toUtc().toIso8601String(),
        ),
      );
    }
  }

  Future<void> disconnect() async {
    if (_isDisconnecting) return;

    final socket = _socket;
    if (socket == null) {
      _isConnected = false;
      _failPendingCriticalAcks('DISCONNECTED');
      return;
    }

    _isDisconnecting = true;
    _socket = null;
    _isConnected = false;
    _writeQueue = Future<void>.value();
    _receiveBuffer.clear();
    _failPendingCriticalAcks('DISCONNECTED');
    if (!_connectionController.isClosed) {
      _connectionController.add(false);
    }
    _discoveryService?.resumeScanning();

    try {
      // Gracefully close the socket
      await socket.close();
    } catch (e) {
      // Ignore errors during disconnect
      print('[Connection] ⚠️ Error during disconnect: $e');
    } finally {
      _isDisconnecting = false;
    }

    print('[Connection] 🔌 Disconnected');
  }

  void dispose() {
    unawaited(disconnect());
    if (!_messageController.isClosed) {
      _messageController.close();
    }
    if (!_connectionController.isClosed) {
      _connectionController.close();
    }
  }
}
