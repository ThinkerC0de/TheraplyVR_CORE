import 'package:flutter/material.dart';
import 'package:flutter_controller/services/connection_service.dart';
import 'package:flutter_controller/services/webrtc_media_service.dart';
import 'package:flutter_webrtc/flutter_webrtc.dart';
import 'dart:async';

/// Displays live media stream from Quest via WebRTC (signaling over TCP).
class MediaStreamWidget extends StatefulWidget {
  final ConnectionService connection;
  final String? deviceIP;
  final int port;

  const MediaStreamWidget({
    super.key,
    required this.connection,
    this.deviceIP,
    this.port = 8081,
  });

  @override
  State<MediaStreamWidget> createState() => _MediaStreamWidgetState();
}

class _MediaStreamWidgetState extends State<MediaStreamWidget> {
  static const bool _playQuestAudioOnMobile = false;

  WebRTCMediaService? _webrtcService;
  StreamSubscription<MediaStream>? _streamSub;
  StreamSubscription<bool>? _connectionSub;
  RTCVideoRenderer? _renderer;
  String? _error;
  bool _rendererReady = false;
  bool _pttPressed = false;

  @override
  void initState() {
    super.initState();
    _initWebRTC();
  }

  Future<void> _initWebRTC() async {
    try {
      _renderer = RTCVideoRenderer();
      await _renderer!.initialize();
      if (!mounted) return;
      setState(() => _rendererReady = true);
    } catch (e) {
      if (mounted) setState(() => _error = e.toString());
      return;
    }
    _webrtcService = WebRTCMediaService(widget.connection);
    _webrtcService!.start();
    _streamSub = _webrtcService!.onRemoteStream.listen((stream) {
      if (mounted) {
        if (!_playQuestAudioOnMobile) {
          for (final track in stream.getAudioTracks()) {
            track.enabled = false;
          }
        }
        setState(() => _renderer?.srcObject = stream);
      }
    });

    _connectionSub = widget.connection.connectionStatus.listen((connected) {
      if (!mounted) return;
      if (!connected) {
        unawaited(_setPtt(false));
        setState(() => _renderer?.srcObject = null);
      }
    });
  }

  @override
  void dispose() {
    _streamSub?.cancel();
    _connectionSub?.cancel();
    _webrtcService?.dispose();
    _renderer?.srcObject = null;
    _renderer?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Expanded(
          child: Container(
            decoration: BoxDecoration(
              color: Colors.black,
              borderRadius: BorderRadius.circular(8),
            ),
            clipBehavior: Clip.antiAlias,
            child: Stack(
              fit: StackFit.expand,
              children: [
                if (_error != null)
                  _buildError()
                else if (_rendererReady && _renderer != null)
                  RTCVideoView(
                    _renderer!,
                    objectFit:
                        RTCVideoViewObjectFit.RTCVideoViewObjectFitContain,
                  )
                else
                  _buildWaiting(),
                Positioned(
                  right: 12,
                  bottom: 12,
                  child: GestureDetector(
                    onTapDown: (_) => _setPtt(true),
                    onTapUp: (_) => _setPtt(false),
                    onTapCancel: () => _setPtt(false),
                    child: AnimatedContainer(
                      duration: const Duration(milliseconds: 100),
                      padding: const EdgeInsets.symmetric(
                          horizontal: 12, vertical: 10),
                      decoration: BoxDecoration(
                        color: _pttPressed ? Colors.red : Colors.black54,
                        borderRadius: BorderRadius.circular(10),
                        border: Border.all(
                          color:
                              _pttPressed ? Colors.redAccent : Colors.white24,
                        ),
                      ),
                      child: Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Icon(
                            _pttPressed ? Icons.mic : Icons.mic_none,
                            color: Colors.white,
                            size: 18,
                          ),
                          const SizedBox(width: 6),
                          Text(
                            _pttPressed ? 'Talking...' : 'Hold to Talk',
                            style: const TextStyle(
                              color: Colors.white,
                              fontSize: 12,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ],
    );
  }

  Future<void> _setPtt(bool enabled) async {
    if (_pttPressed == enabled) return;
    setState(() => _pttPressed = enabled);
    await _webrtcService?.setTalkbackEnabled(enabled);
  }

  Widget _buildWaiting() {
    return const Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          SizedBox(
            width: 40,
            height: 40,
            child: CircularProgressIndicator(
                color: Colors.white54, strokeWidth: 2),
          ),
          SizedBox(height: 16),
          Text(
            'Waiting for media stream...',
            style: TextStyle(color: Colors.white54, fontSize: 14),
          ),
          SizedBox(height: 4),
          Text(
            'WebRTC over TCP',
            style: TextStyle(color: Colors.white30, fontSize: 12),
          ),
        ],
      ),
    );
  }

  Widget _buildError() {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.error_outline, color: Colors.red, size: 48),
            const SizedBox(height: 12),
            Text(
              _error ?? 'Unknown error',
              style: const TextStyle(color: Colors.white70, fontSize: 13),
              textAlign: TextAlign.center,
              maxLines: 3,
            ),
          ],
        ),
      ),
    );
  }
}
