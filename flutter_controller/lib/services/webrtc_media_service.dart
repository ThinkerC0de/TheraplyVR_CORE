import 'dart:async';
import 'dart:convert';
import 'package:flutter/foundation.dart';
import 'package:flutter_webrtc/flutter_webrtc.dart';
import 'connection_service.dart';

/// Handles WebRTC signaling over TCP: receives offer, creates answer, exchanges ICE candidates.
/// Emits the remote MediaStream when track is received (for display).
class WebRTCMediaService {
  WebRTCMediaService(this._connection);

  static const bool _preferLanFirst = true;
  static const Duration _lanProbeTimeout = Duration(seconds: 4);
  static const List<Map<String, dynamic>> _stunIceServers =
      <Map<String, dynamic>>[
    <String, dynamic>{'urls': 'stun:stun.l.google.com:19302'},
    <String, dynamic>{'urls': 'stun:stun1.l.google.com:19302'},
  ];

  final ConnectionService _connection;
  RTCPeerConnection? _peerConnection;
  final List<Map<String, dynamic>> _pendingRemoteCandidates = [];
  StreamSubscription<Map<String, dynamic>>? _messageSub;
  final _remoteStreamController = StreamController<MediaStream>.broadcast();
  Stream<MediaStream> get onRemoteStream => _remoteStreamController.stream;
  bool _disposed = false;
  MediaStream? _localMicStream;
  MediaStreamTrack? _localMicTrack;
  bool _talkbackEnabled = false;
  bool _stunFallbackArmed = false;
  bool _usingLanOnlyThisPeer = false;
  bool _connectedInCurrentPeer = false;
  Timer? _lanProbeTimer;

  bool get talkbackEnabled => _talkbackEnabled;

  /// Extract payload string from message
  /// Unity sends payload as Base64 string in JSON
  static String _payloadString(Map<String, dynamic> message) {
    final p = message['payload'];
    if (p == null) return '';

    // Unity sends payload as Base64 string
    if (p is String) {
      try {
        // Decode Base64 -> bytes -> UTF-8 string
        return utf8.decode(base64Decode(p));
      } catch (e) {
        print('[WebRTCMedia] Failed to decode Base64 payload: $e');
        // Fallback: treat as plain string
        return p;
      }
    }

    // Legacy: if payload is byte array (shouldn't happen with Unity)
    if (p is List) {
      try {
        return utf8.decode(Uint8List.fromList(List<int>.from(p)));
      } catch (e) {
        print('[WebRTCMedia] Failed to decode byte array payload: $e');
        return '';
      }
    }

    return '';
  }

  void start() {
    if (_messageSub != null) return;
    _messageSub = _connection.messages.listen(_onMessage);
    print('[WebRTCMedia] Listening for WEBRTC_OFFER / WEBRTC_ICE_CANDIDATE');

    // Replay signaling messages that may have arrived before this service subscribed.
    final buffered = _connection.drainBufferedWebRtcSignaling();
    for (final message in buffered) {
      _onMessage(message);
    }
  }

  void _onMessage(Map<String, dynamic> message) {
    try {
      final commandId = message['commandId'] as String?;
      if (commandId == null) return;
      switch (commandId) {
        case 'WEBRTC_OFFER':
          print('[WebRTCMedia] 📥 Received WEBRTC_OFFER');
          _handleOffer(_payloadString(message));
          break;
        case 'WEBRTC_ICE_CANDIDATE':
          _handleIceCandidate(_payloadString(message));
          break;
      }
    } catch (e, st) {
      print('[WebRTCMedia] Error in _onMessage: $e');
      if (kDebugMode) print(st);
      // Do not rethrow – avoid closing the TCP connection on WebRTC parse errors
    }
  }

  Future<void> _handleOffer(String offerJson) async {
    if (offerJson.isEmpty) {
      print('[WebRTCMedia] ⚠️ Empty offer payload');
      return;
    }

    try {
      print('[WebRTCMedia] Parsing offer JSON (${offerJson.length} chars)');
      final offerMap = jsonDecode(offerJson) as Map<String, dynamic>;
      final sdp = offerMap['sdp'] as String?;
      if (sdp == null) {
        print('[WebRTCMedia] ⚠️ No SDP in offer');
        return;
      }

      final iceMode = (offerMap['iceMode'] as String?)?.toUpperCase();
      final serverRequestsStun = iceMode == 'STUN';
      if (serverRequestsStun && !_stunFallbackArmed) {
        _stunFallbackArmed = true;
        print(
            '[WebRTCMedia] Server requested STUN mode for this renegotiation.');
      }

      final useStun =
          serverRequestsStun || !_preferLanFirst || _stunFallbackArmed;
      _usingLanOnlyThisPeer = !useStun;
      _connectedInCurrentPeer = false;

      print(
        '[WebRTCMedia] Creating peer connection (${_usingLanOnlyThisPeer ? 'LAN-first' : 'STUN'} mode)...',
      );
      final offer =
          RTCSessionDescription(sdp, offerMap['type'] as String? ?? 'offer');

      _peerConnection?.close();
      _peerConnection = await createPeerConnection(
        _buildPeerConnectionConfig(useStun: useStun),
      );

      if (_usingLanOnlyThisPeer) {
        _armLanProbeTimer();
      } else {
        _cancelLanProbeTimer();
      }

      await _ensureTalkbackTrack();

      _peerConnection!.onTrack = (event) {
        final kind = event.track.kind;
        print('[WebRTCMedia] 📥 Received $kind track');
        if (event.streams.isNotEmpty && !_disposed) {
          _remoteStreamController.add(event.streams.first);
        }
      };

      _peerConnection!.onIceCandidate = (candidate) {
        _sendIceCandidate(candidate);
      };

      _peerConnection!.onIceConnectionState = (state) {
        print('[WebRTCMedia] ICE Connection State: $state');
        switch (state) {
          case RTCIceConnectionState.RTCIceConnectionStateConnected:
          case RTCIceConnectionState.RTCIceConnectionStateCompleted:
            _connectedInCurrentPeer = true;
            _cancelLanProbeTimer();
            break;
          case RTCIceConnectionState.RTCIceConnectionStateFailed:
            _activateStunFallback('ICE_FAILED');
            break;
          case RTCIceConnectionState.RTCIceConnectionStateDisconnected:
            if (_usingLanOnlyThisPeer && !_connectedInCurrentPeer) {
              _activateStunFallback('ICE_DISCONNECTED');
            }
            break;
          default:
            break;
        }
      };

      _peerConnection!.onConnectionState = (state) {
        print('[WebRTCMedia] Peer Connection State: $state');
        switch (state) {
          case RTCPeerConnectionState.RTCPeerConnectionStateConnected:
            _connectedInCurrentPeer = true;
            _cancelLanProbeTimer();
            break;
          case RTCPeerConnectionState.RTCPeerConnectionStateFailed:
            _activateStunFallback('PEER_FAILED');
            break;
          default:
            break;
        }
      };

      print('[WebRTCMedia] Setting remote description...');
      await _peerConnection!.setRemoteDescription(offer);

      print('[WebRTCMedia] Creating answer...');
      final answer = await _peerConnection!.createAnswer();

      print('[WebRTCMedia] Setting local description...');
      await _peerConnection!.setLocalDescription(answer);

      final answerPayload = {'type': 'answer', 'sdp': answer.sdp};
      await _connection.sendCommand('WEBRTC_ANSWER', answerPayload);
      print('[WebRTCMedia] ✅ Sent WEBRTC_ANSWER');

      // Apply ICE candidates that arrived before peer connection was ready.
      if (_pendingRemoteCandidates.isNotEmpty) {
        for (final candidate
            in List<Map<String, dynamic>>.from(_pendingRemoteCandidates)) {
          await _addRemoteCandidate(candidate);
        }
        _pendingRemoteCandidates.clear();
      }
    } catch (e, st) {
      print('[WebRTCMedia] Handle offer error: $e');
      if (kDebugMode) print(st);
    }
  }

  Map<String, dynamic> _buildPeerConnectionConfig({required bool useStun}) {
    return <String, dynamic>{
      'iceServers': useStun ? _stunIceServers : const <Map<String, dynamic>>[],
    };
  }

  void _armLanProbeTimer() {
    _cancelLanProbeTimer();
    _lanProbeTimer = Timer(_lanProbeTimeout, () {
      if (_disposed || !_usingLanOnlyThisPeer || _connectedInCurrentPeer) {
        return;
      }

      _activateStunFallback('LAN_PROBE_TIMEOUT');
    });
  }

  void _cancelLanProbeTimer() {
    _lanProbeTimer?.cancel();
    _lanProbeTimer = null;
  }

  void _activateStunFallback(String reasonCode) {
    if (_stunFallbackArmed) {
      return;
    }

    _stunFallbackArmed = true;
    _cancelLanProbeTimer();
    print(
      '[WebRTCMedia] LAN-first path failed ($reasonCode). Arming STUN fallback and requesting renegotiation.',
    );
    unawaited(_requestStunFallbackRenegotiation(reasonCode));
  }

  Future<void> _requestStunFallbackRenegotiation(String reasonCode) async {
    try {
      await _connection
          .sendCommand('WEBRTC_STUN_FALLBACK_REQUEST', <String, dynamic>{
        'reasonCode': reasonCode,
      });
      print('[WebRTCMedia] 📤 Sent WEBRTC_STUN_FALLBACK_REQUEST ($reasonCode)');
    } catch (e) {
      print('[WebRTCMedia] Failed to request STUN fallback renegotiation: $e');
    }
  }

  Future<void> _handleIceCandidate(String candidateJson) async {
    if (candidateJson.isEmpty) return;
    try {
      final map = jsonDecode(candidateJson) as Map<String, dynamic>;

      if (_peerConnection == null) {
        _pendingRemoteCandidates.add(map);
        return;
      }

      await _addRemoteCandidate(map);
    } catch (e) {
      print('[WebRTCMedia] Handle ICE candidate error: $e');
    }
  }

  Future<void> _addRemoteCandidate(Map<String, dynamic> map) async {
    if (_peerConnection == null) return;
    final c = RTCIceCandidate(
      map['candidate'] as String? ?? '',
      map['sdpMid'] as String? ?? '',
      map['sdpMLineIndex'] as int? ?? 0,
    );
    await _peerConnection!.addCandidate(c);
    print('[WebRTCMedia] ✅ Added ICE candidate');
  }

  Future<void> _sendIceCandidate(RTCIceCandidate candidate) async {
    await _connection.sendCommand('WEBRTC_ICE_CANDIDATE', {
      'candidate': candidate.candidate,
      'sdpMid': candidate.sdpMid,
      'sdpMLineIndex': candidate.sdpMLineIndex ?? 0,
    });
    print('[WebRTCMedia] 📤 Sent ICE candidate');
  }

  Future<void> _ensureTalkbackTrack() async {
    if (_peerConnection == null) return;

    // Recreate per-peer to avoid sender reuse issues after renegotiation/reconnect.
    await _disposeTalkbackTrack();

    try {
      _localMicStream = await navigator.mediaDevices.getUserMedia({
        'audio': true,
        'video': false,
      });

      if (_localMicStream == null ||
          _localMicStream!.getAudioTracks().isEmpty) {
        print('[WebRTCMedia] ⚠️ No microphone track available');
        return;
      }

      _localMicTrack = _localMicStream!.getAudioTracks().first;
      _localMicTrack!.enabled = false; // push-to-talk: disabled by default
      await _peerConnection!.addTrack(_localMicTrack!, _localMicStream!);
      print('[WebRTCMedia] 🎤 Talkback track added (PTT ready)');
    } catch (e) {
      print('[WebRTCMedia] Talkback init failed: $e');
    }
  }

  Future<void> setTalkbackEnabled(bool enabled) async {
    if (enabled && _localMicTrack == null) {
      await _ensureTalkbackTrack();
    }

    if (_localMicTrack == null) {
      _talkbackEnabled = false;
      return;
    }

    _localMicTrack!.enabled = enabled;
    _talkbackEnabled = enabled;
    print('[WebRTCMedia] ${enabled ? '🎙️ Talkback ON' : '🔇 Talkback OFF'}');
  }

  Future<void> _disposeTalkbackTrack() async {
    try {
      _localMicTrack?.enabled = false;
      _localMicTrack?.stop();
    } catch (_) {}
    _localMicTrack = null;
    try {
      await _localMicStream?.dispose();
    } catch (_) {}
    _localMicStream = null;
    _talkbackEnabled = false;
  }

  void stop() {
    _messageSub?.cancel();
    _messageSub = null;
    _cancelLanProbeTimer();
    _stunFallbackArmed = false;
    _usingLanOnlyThisPeer = false;
    _connectedInCurrentPeer = false;
    _peerConnection?.close();
    _peerConnection = null;
    _pendingRemoteCandidates.clear();
    unawaited(_disposeTalkbackTrack());
  }

  void dispose() {
    _disposed = true;
    stop();
    _remoteStreamController.close();
  }
}
