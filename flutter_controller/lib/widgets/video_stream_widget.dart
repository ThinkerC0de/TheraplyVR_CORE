import 'package:flutter/material.dart';
import 'package:flutter_controller/services/connection_service.dart';
import 'package:flutter_controller/services/webrtc_video_service.dart';
import 'package:flutter_webrtc/flutter_webrtc.dart';
import 'dart:async';

/// Displays live video stream from Quest via WebRTC (signaling over TCP).
class VideoStreamWidget extends StatefulWidget {
  final ConnectionService connection;
  final String? deviceIP;
  final int port;

  const VideoStreamWidget({
    super.key,
    required this.connection,
    this.deviceIP,
    this.port = 8081,
  });

  @override
  State<VideoStreamWidget> createState() => _VideoStreamWidgetState();
}

class _VideoStreamWidgetState extends State<VideoStreamWidget> {
  WebRTCVideoService? _webrtcService;
  StreamSubscription<MediaStream>? _streamSub;
  StreamSubscription<bool>? _connectionSub;
  RTCVideoRenderer? _renderer;
  String? _error;
  bool _rendererReady = false;

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
    _webrtcService = WebRTCVideoService(widget.connection);
    _webrtcService!.start();
    _streamSub = _webrtcService!.onRemoteStream.listen((stream) {
      if (mounted) {
        setState(() => _renderer?.srcObject = stream);
      }
    });

    _connectionSub = widget.connection.connectionStatus.listen((connected) {
      if (!mounted) return;
      if (!connected) {
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
                    objectFit: RTCVideoViewObjectFit.RTCVideoViewObjectFitContain,
                  )
                else
                  _buildWaiting(),
              ],
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildWaiting() {
    return const Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          SizedBox(
            width: 40,
            height: 40,
            child: CircularProgressIndicator(color: Colors.white54, strokeWidth: 2),
          ),
          SizedBox(height: 16),
          Text(
            'Waiting for video stream...',
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
