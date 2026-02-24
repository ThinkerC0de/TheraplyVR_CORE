import 'dart:async';
import 'dart:convert';
import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
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
  RTCRtpSender? _localMicSender;
  bool _talkbackEnabled = false;
  bool _talkbackDesiredEnabled = false;
  bool _audioSessionReady = false;
  bool _audioSessionApiUnsupported = false;
  bool _stunFallbackArmed = false;
  bool _usingLanOnlyThisPeer = false;
  bool _connectedInCurrentPeer = false;
  Timer? _lanProbeTimer;
  Timer? _talkbackStatsTimer;
  Map<String, _OfferAudioMline> _offerAudioMlinesByMid = {};

  bool get talkbackEnabled => _talkbackEnabled;

  static bool get _supportsEnsureAudioSession {
    if (kIsWeb) {
      return false;
    }

    return defaultTargetPlatform == TargetPlatform.iOS ||
        defaultTargetPlatform == TargetPlatform.macOS;
  }

  static bool get _supportsNativeMicrophoneMute {
    if (kIsWeb) {
      return false;
    }

    return defaultTargetPlatform == TargetPlatform.iOS ||
        defaultTargetPlatform == TargetPlatform.macOS;
  }

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
    unawaited(_ensureAudioSessionReady());

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

      final offerAudioMlines = _parseOfferAudioMlines(sdp);
      _offerAudioMlinesByMid = {
        for (final item in offerAudioMlines) item.mid: item,
      };
      if (offerAudioMlines.isEmpty) {
        print('[WebRTCMedia] ⚠️ Offer does not include any audio m-line.');
      } else {
        final summary = offerAudioMlines
            .map((item) => 'mid=${item.mid},dir=${item.direction}')
            .join(' | ');
        print('[WebRTCMedia] Offer audio m-lines: $summary');
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

      await _ensureAudioSessionReady();
      await _ensureTalkbackTrack();

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

  Future<void> _ensureAudioSessionReady() async {
    if (_audioSessionReady || _audioSessionApiUnsupported) {
      return;
    }

    if (!_supportsEnsureAudioSession) {
      _audioSessionReady = true;
      return;
    }

    try {
      await Helper.ensureAudioSession();
      _audioSessionReady = true;
      print('[WebRTCMedia] ✅ Audio session ready');
    } on MissingPluginException {
      _audioSessionApiUnsupported = true;
      _audioSessionReady = true;
      print(
          '[WebRTCMedia] ensureAudioSession not available on this platform build - skipping.');
    } catch (e) {
      print('[WebRTCMedia] Audio session setup failed: $e');
    }
  }

  Future<void> _ensureTalkbackTrack() async {
    if (_peerConnection == null) return;

    // Recreate per-peer to avoid sender reuse issues after renegotiation/reconnect.
    await _disposeTalkbackTrack();
    await _ensureAudioSessionReady();

    try {
      try {
        _localMicStream = await navigator.mediaDevices.getUserMedia({
          'audio': {
            'echoCancellation': true,
            'noiseSuppression': true,
            'autoGainControl': true,
          },
          'video': false,
        });
      } catch (_) {
        // Fallback for platforms/devices that reject advanced constraints.
        _localMicStream = await navigator.mediaDevices.getUserMedia({
          'audio': true,
          'video': false,
        });
      }

      if (_localMicStream == null ||
          _localMicStream!.getAudioTracks().isEmpty) {
        print('[WebRTCMedia] ⚠️ No microphone track available');
        return;
      }

      _localMicTrack = _localMicStream!.getAudioTracks().first;
      await _clearLegacyAndroidGlobalMicMute();
      final attachedToOfferAudioMline =
          await _attachTalkbackTrackToOfferAudioMline();
      if (!attachedToOfferAudioMline) {
        _localMicSender = await _peerConnection!.addTrack(
          _localMicTrack!,
          _localMicStream!,
        );
      }
      await _applyTalkbackMuteState(!_talkbackDesiredEnabled);
      _talkbackEnabled = _talkbackDesiredEnabled;
      print(
        '[WebRTCMedia] 🎤 Talkback track added (PTT ready, desired=${_talkbackDesiredEnabled ? 'ON' : 'OFF'})',
      );
    } catch (e) {
      print('[WebRTCMedia] Talkback init failed: $e');
    }
  }

  Future<void> _applyTalkbackMuteState(bool muted) async {
    if (_localMicTrack == null) {
      return;
    }

    if (_supportsNativeMicrophoneMute) {
      try {
        await Helper.setMicrophoneMute(muted, _localMicTrack!);
      } catch (e) {
        print('[WebRTCMedia] setMicrophoneMute failed: $e');
      }
    }

    // Keep explicit track.enabled state in sync for deterministic behavior across devices.
    _localMicTrack!.enabled = !muted;
  }

  Future<void> _clearLegacyAndroidGlobalMicMute() async {
    if (kIsWeb ||
        defaultTargetPlatform != TargetPlatform.android ||
        _localMicTrack == null) {
      return;
    }

    try {
      // Best-effort recovery path for older builds that used global AudioManager mic mute.
      await Helper.setMicrophoneMute(false, _localMicTrack!);
      print(
          '[WebRTCMedia] Android global microphone mute reset (legacy compatibility).');
    } catch (_) {
      // No-op: not critical for normal track.enabled-based PTT flow.
    }
  }

  void _startTalkbackStatsLogging() {
    _stopTalkbackStatsLogging();
    _talkbackStatsTimer = Timer.periodic(const Duration(seconds: 2), (_) {
      unawaited(_logTalkbackSenderStats());
    });
  }

  void _stopTalkbackStatsLogging() {
    _talkbackStatsTimer?.cancel();
    _talkbackStatsTimer = null;
  }

  Future<void> _logTalkbackSenderStats() async {
    if (!_talkbackEnabled || _localMicSender == null) {
      return;
    }

    try {
      final reports = await _localMicSender!.getStats();
      var outboundLogged = false;
      for (final report in reports) {
        final type = report.type.toLowerCase();
        if (type != 'outbound-rtp') {
          continue;
        }

        final values = report.values;
        final kind = (values['kind'] ?? values['mediaType'] ?? '')
            .toString()
            .toLowerCase();
        if (kind.isNotEmpty && kind != 'audio') {
          continue;
        }

        final packetsSent = values['packetsSent'];
        final bytesSent = values['bytesSent'];
        print(
          '[WebRTCMedia] 🎤 Outbound audio RTP: packetsSent=$packetsSent, bytesSent=$bytesSent, trackEnabled=${_localMicTrack?.enabled}',
        );
        outboundLogged = true;
      }

      if (!outboundLogged) {
        print(
          '[WebRTCMedia] 🎤 Talkback sender stats had no outbound-rtp audio entries (trackEnabled=${_localMicTrack?.enabled}).',
        );
      }
    } catch (e) {
      print('[WebRTCMedia] Failed to read talkback sender stats: $e');
    }
  }

  Future<bool> _attachTalkbackTrackToOfferAudioMline() async {
    if (_peerConnection == null || _localMicTrack == null) {
      return false;
    }

    try {
      final transceivers = await _peerConnection!.getTransceivers();
      RTCRtpTransceiver? fallbackAudioTransceiver;

      for (final transceiver in transceivers) {
        final mid = transceiver.mid;
        final offerAudioMline = _offerAudioMlinesByMid[mid];
        final receiverTrack = transceiver.receiver.track;
        final senderTrack = transceiver.sender.track;
        final hasAudioKind = receiverTrack?.kind == 'audio' ||
            senderTrack?.kind == 'audio' ||
            offerAudioMline != null;

        if (!hasAudioKind) {
          continue;
        }

        fallbackAudioTransceiver ??= transceiver;

        if (offerAudioMline == null ||
            !offerAudioMline.remoteCanReceiveLocalAudio) {
          continue;
        }

        final preferredDirection = offerAudioMline.direction == 'recvonly'
            ? TransceiverDirection.SendOnly
            : TransceiverDirection.SendRecv;
        try {
          await transceiver.setDirection(preferredDirection);
        } catch (e) {
          print(
            '[WebRTCMedia] Failed to set transceiver direction on mid=$mid: $e',
          );
        }

        await transceiver.sender.replaceTrack(_localMicTrack);
        _localMicSender = transceiver.sender;
        print(
          '[WebRTCMedia] 🎤 Talkback bound to offer audio m-line mid=$mid (offerDir=${offerAudioMline.direction})',
        );
        return true;
      }

      if (fallbackAudioTransceiver != null) {
        final mid = fallbackAudioTransceiver.mid;
        await fallbackAudioTransceiver.sender.replaceTrack(_localMicTrack);
        _localMicSender = fallbackAudioTransceiver.sender;
        print(
          '[WebRTCMedia] ⚠️ Fallback talkback bind used on audio transceiver mid=$mid (offer mapping unavailable).',
        );
        return true;
      }

      if (_offerAudioMlinesByMid.isNotEmpty) {
        final unsupported = _offerAudioMlinesByMid.values
            .where((item) => !item.remoteCanReceiveLocalAudio)
            .map((item) => 'mid=${item.mid},dir=${item.direction}')
            .join(' | ');
        if (unsupported.isNotEmpty) {
          print(
            '[WebRTCMedia] ⚠️ Offer has audio m-lines that cannot carry mobile uplink: $unsupported',
          );
        }
      }

      print(
        '[WebRTCMedia] ⚠️ No suitable audio transceiver found for talkback uplink.',
      );
    } catch (e) {
      print('[WebRTCMedia] Failed to bind talkback transceiver: $e');
    }

    return false;
  }

  static List<_OfferAudioMline> _parseOfferAudioMlines(String sdp) {
    final lines = sdp.split(RegExp(r'\r?\n'));
    final items = <_OfferAudioMline>[];
    String? currentMedia;
    String? currentMid;
    var sessionDirection = 'sendrecv';
    var currentDirection = sessionDirection;

    void flushCurrent() {
      final mid = currentMid;
      if (currentMedia != 'audio' || mid == null || mid.isEmpty) {
        return;
      }
      items.add(_OfferAudioMline(mid: mid, direction: currentDirection));
    }

    for (final rawLine in lines) {
      final line = rawLine.trim();
      if (line.isEmpty) {
        continue;
      }

      if (line.startsWith('m=')) {
        flushCurrent();
        final mediaParts = line.substring(2).split(' ');
        currentMedia = mediaParts.isEmpty ? null : mediaParts.first;
        currentMid = null;
        currentDirection = sessionDirection;
        continue;
      }

      if (line == 'a=sendrecv' ||
          line == 'a=sendonly' ||
          line == 'a=recvonly' ||
          line == 'a=inactive') {
        final direction = line.substring(2);
        if (currentMedia == null) {
          sessionDirection = direction;
        } else {
          currentDirection = direction;
        }
        continue;
      }

      if (line.startsWith('a=mid:')) {
        currentMid = line.substring('a=mid:'.length);
      }
    }

    flushCurrent();
    return items;
  }

  Future<void> setTalkbackEnabled(bool enabled) async {
    _talkbackDesiredEnabled = enabled;

    if (enabled && _localMicTrack == null) {
      await _ensureTalkbackTrack();
    }

    if (_localMicTrack == null) {
      _talkbackEnabled = false;
      return;
    }

    await _applyTalkbackMuteState(!enabled);

    if (_localMicSender != null &&
        _localMicSender!.track?.id != _localMicTrack!.id) {
      try {
        await _localMicSender!.replaceTrack(_localMicTrack);
      } catch (e) {
        print('[WebRTCMedia] Failed to rebind mic track to sender: $e');
      }
    }

    _talkbackEnabled = enabled;
    if (_talkbackEnabled) {
      _startTalkbackStatsLogging();
    } else {
      _stopTalkbackStatsLogging();
    }
    print(
      '[WebRTCMedia] ${enabled ? '🎙️ Talkback ON' : '🔇 Talkback OFF'} (trackEnabled=${_localMicTrack!.enabled})',
    );
  }

  Future<void> _disposeTalkbackTrack() async {
    try {
      if (_supportsNativeMicrophoneMute && _localMicTrack != null) {
        await Helper.setMicrophoneMute(true, _localMicTrack!);
      }
    } catch (_) {}
    try {
      await _localMicSender?.replaceTrack(null);
    } catch (_) {}
    _localMicSender = null;
    try {
      await _localMicTrack?.stop();
    } catch (_) {}
    _localMicTrack = null;
    try {
      await _localMicStream?.dispose();
    } catch (_) {}
    _localMicStream = null;
    _talkbackEnabled = false;
    _stopTalkbackStatsLogging();
  }

  void stop() {
    _messageSub?.cancel();
    _messageSub = null;
    _cancelLanProbeTimer();
    _stopTalkbackStatsLogging();
    _stunFallbackArmed = false;
    _usingLanOnlyThisPeer = false;
    _connectedInCurrentPeer = false;
    _peerConnection?.close();
    _peerConnection = null;
    _offerAudioMlinesByMid = {};
    _pendingRemoteCandidates.clear();
    _talkbackDesiredEnabled = false;
    unawaited(_disposeTalkbackTrack());
  }

  void dispose() {
    _disposed = true;
    stop();
    _remoteStreamController.close();
  }
}

class _OfferAudioMline {
  _OfferAudioMline({
    required this.mid,
    required this.direction,
  });

  final String mid;
  final String direction;

  bool get remoteCanReceiveLocalAudio =>
      direction == 'recvonly' || direction == 'sendrecv';
}
