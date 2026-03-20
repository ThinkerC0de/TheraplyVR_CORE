import 'package:flutter/material.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_controller/services/connection_service.dart';
import 'package:flutter_controller/services/webrtc_media_service.dart';
import 'package:flutter_webrtc/flutter_webrtc.dart';
import 'dart:async';

enum MediaPreviewState {
  initializing,
  waitingForStream,
  streaming,
  error,
}

/// Displays live media stream from Quest via WebRTC (signaling over TCP).
class MediaStreamWidget extends StatefulWidget {
  final ConnectionService connection;
  final String? deviceIP;
  final int port;
  final bool previewActive;
  final ValueChanged<MediaPreviewState>? onStateChanged;

  const MediaStreamWidget({
    super.key,
    required this.connection,
    this.deviceIP,
    this.port = 8081,
    this.previewActive = true,
    this.onStateChanged,
  });

  @override
  State<MediaStreamWidget> createState() => _MediaStreamWidgetState();
}

class _MediaStreamWidgetState extends State<MediaStreamWidget>
    with WidgetsBindingObserver {
  static const bool _suppressQuestMonitorDuringPtt = true;
  static const String _androidSpeakerOutputId = 'speakerphone';

  WebRTCMediaService? _webrtcService;
  StreamSubscription<MediaStream>? _streamSub;
  StreamSubscription<bool>? _connectionSub;
  RTCVideoRenderer? _renderer;
  String? _error;
  bool _rendererReady = false;
  bool _pttPressed = false;
  bool _questAudioEnabled = false;
  bool _previewSessionActive = false;
  AppLifecycleState? _appLifecycleState;
  MediaPreviewState _previewState = MediaPreviewState.initializing;

  bool get _effectivePreviewActive {
    final lifecycleState = _appLifecycleState;
    final appIsResumed =
        lifecycleState == null || lifecycleState == AppLifecycleState.resumed;
    return widget.previewActive && appIsResumed;
  }

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _initWebRTC();
  }

  @override
  void didUpdateWidget(covariant MediaStreamWidget oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.previewActive != widget.previewActive) {
      unawaited(_syncPreviewLifecycle(reasonCode: 'WIDGET_UPDATE'));
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    _appLifecycleState = state;
    unawaited(
        _syncPreviewLifecycle(reasonCode: 'APP_${state.name.toUpperCase()}'));
  }

  Future<void> _initWebRTC() async {
    _setPreviewState(MediaPreviewState.initializing);
    try {
      _renderer = RTCVideoRenderer();
      await _renderer!.initialize();
      if (!mounted) return;
      setState(() => _rendererReady = true);
      _setPreviewState(MediaPreviewState.waitingForStream);
    } catch (e) {
      if (mounted) {
        setState(() => _error = e.toString());
      }
      _setPreviewState(MediaPreviewState.error);
      return;
    }
    _webrtcService = WebRTCMediaService(widget.connection);
    _streamSub = _webrtcService!.onRemoteStream.listen((stream) {
      if (!mounted) {
        return;
      }

      if (!_effectivePreviewActive) {
        unawaited(_applyRemoteAudioState(stream, enabled: false));
        setState(() => _renderer?.srcObject = null);
        _setPreviewState(MediaPreviewState.waitingForStream);
        return;
      }

      unawaited(_applyRemoteAudioState(
        stream,
        enabled: _resolveEffectiveQuestAudioEnabled(),
      ));
      setState(() => _renderer?.srcObject = stream);
      _setPreviewState(MediaPreviewState.streaming);
    });

    _connectionSub = widget.connection.connectionStatus.listen((connected) {
      if (!mounted) return;
      if (!connected) {
        _previewSessionActive = false;
        unawaited(_setPtt(false));
        final srcObject = _renderer?.srcObject;
        if (srcObject is MediaStream) {
          unawaited(_applyRemoteAudioState(srcObject, enabled: false));
        }
        setState(() => _renderer?.srcObject = null);
        _setPreviewState(MediaPreviewState.waitingForStream);
        return;
      }

      unawaited(_syncPreviewLifecycle(reasonCode: 'TCP_CONNECTED'));
    });

    await _syncPreviewLifecycle(reasonCode: 'INIT');
  }

  void _setPreviewState(MediaPreviewState nextState) {
    if (_previewState == nextState) {
      return;
    }
    _previewState = nextState;
    widget.onStateChanged?.call(nextState);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _streamSub?.cancel();
    _connectionSub?.cancel();
    unawaited(_applyRendererAudioRouting(false));
    _webrtcService?.dispose();
    _renderer?.srcObject = null;
    _renderer?.dispose();
    super.dispose();
  }

  Future<void> _syncPreviewLifecycle({required String reasonCode}) async {
    final service = _webrtcService;
    if (!mounted || service == null) {
      return;
    }

    if (!_effectivePreviewActive) {
      _previewSessionActive = false;
      await _setPtt(false);
      final srcObject = _renderer?.srcObject;
      if (srcObject is MediaStream) {
        await _applyRemoteAudioState(srcObject, enabled: false);
      }
      if (mounted) {
        setState(() => _renderer?.srcObject = null);
      }
      _setPreviewState(MediaPreviewState.waitingForStream);
      await service.pausePreview(reasonCode: reasonCode);
      return;
    }

    if (mounted) {
      setState(() => _error = null);
    }
    _setPreviewState(MediaPreviewState.waitingForStream);

    if (_previewSessionActive) {
      return;
    }

    if (!widget.connection.isConnected) {
      await service.resumePreview(reasonCode: reasonCode);
      return;
    }

    _previewSessionActive = true;
    await service.resumePreview(reasonCode: reasonCode);
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
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Tooltip(
                        message: _questAudioEnabled
                            ? 'Mute Quest audio'
                            : 'Unmute Quest audio',
                        child: InkWell(
                          borderRadius: BorderRadius.circular(10),
                          onTap: _toggleQuestAudio,
                          child: AnimatedContainer(
                            duration: const Duration(milliseconds: 120),
                            padding: const EdgeInsets.all(10),
                            decoration: BoxDecoration(
                              color: _questAudioEnabled
                                  ? Colors.blueGrey.shade700
                                  : Colors.black54,
                              borderRadius: BorderRadius.circular(10),
                              border: Border.all(
                                color: _questAudioEnabled
                                    ? Colors.blue.shade300
                                    : Colors.white24,
                              ),
                            ),
                            child: Icon(
                              _questAudioEnabled
                                  ? Icons.volume_up
                                  : Icons.volume_off,
                              color: Colors.white,
                              size: 18,
                            ),
                          ),
                        ),
                      ),
                      const SizedBox(width: 8),
                      Listener(
                        behavior: HitTestBehavior.opaque,
                        onPointerDown: (_) => _setPtt(true),
                        onPointerUp: (_) => _setPtt(false),
                        onPointerCancel: (_) => _setPtt(false),
                        child: AnimatedContainer(
                          duration: const Duration(milliseconds: 100),
                          padding: const EdgeInsets.symmetric(
                              horizontal: 12, vertical: 10),
                          decoration: BoxDecoration(
                            color: _pttPressed ? Colors.red : Colors.black54,
                            borderRadius: BorderRadius.circular(10),
                            border: Border.all(
                              color: _pttPressed
                                  ? Colors.redAccent
                                  : Colors.white24,
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
                                _pttPressed
                                    ? (_resolveEffectiveQuestAudioEnabled(
                                        pttPressedOverride: true,
                                      )
                                        ? 'Talking...'
                                        : 'Talking... (monitor muted)')
                                    : 'Hold to Talk',
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
                    ],
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

    final stream = _renderer?.srcObject;
    if (stream is MediaStream && _suppressQuestMonitorDuringPtt) {
      await _applyRemoteAudioState(
        stream,
        enabled: _resolveEffectiveQuestAudioEnabled(
          pttPressedOverride: enabled,
        ),
      );
    }

    setState(() => _pttPressed = enabled);
    await _webrtcService?.setTalkbackEnabled(enabled);
  }

  Future<void> _toggleQuestAudio() async {
    final next = !_questAudioEnabled;
    final stream = _renderer?.srcObject;
    if (stream is MediaStream) {
      await _applyRemoteAudioState(
        stream,
        enabled: _resolveEffectiveQuestAudioEnabled(
          questAudioEnabledOverride: next,
        ),
      );
    }
    setState(() {
      _questAudioEnabled = next;
    });
  }

  bool _resolveEffectiveQuestAudioEnabled({
    bool? questAudioEnabledOverride,
    bool? pttPressedOverride,
  }) {
    final effectiveQuestAudioEnabled =
        questAudioEnabledOverride ?? _questAudioEnabled;
    final effectivePttPressed = pttPressedOverride ?? _pttPressed;
    if (_suppressQuestMonitorDuringPtt && effectivePttPressed) {
      return false;
    }
    return effectiveQuestAudioEnabled;
  }

  Future<void> _applyRemoteAudioState(MediaStream stream,
      {required bool enabled}) async {
    await _applyRendererAudioRouting(enabled);

    for (final track in stream.getAudioTracks()) {
      track.enabled = enabled;
      try {
        await Helper.setVolume(enabled ? 1.0 : 0.0, track);
      } catch (e) {
        debugPrint(
          '[MediaStreamWidget] Failed to set remote audio track volume: $e',
        );
      }
    }

    debugPrint(
      '[MediaStreamWidget] Quest audio ${enabled ? 'enabled' : 'disabled'} '
      '(tracks=${stream.getAudioTracks().length})',
    );
  }

  Future<void> _applyRendererAudioRouting(bool enabled) async {
    if (kIsWeb) {
      return;
    }

    if (defaultTargetPlatform == TargetPlatform.android) {
      try {
        await Helper.setSpeakerphoneOn(enabled);
      } catch (e) {
        debugPrint('[MediaStreamWidget] setSpeakerphoneOn failed: $e');
      }

      try {
        await _renderer?.audioOutput(_androidSpeakerOutputId);
      } catch (e) {
        debugPrint('[MediaStreamWidget] audioOutput(speakerphone) failed: $e');
      }
    }

    try {
      await _renderer?.setVolume(enabled ? 1.0 : 0.0);
    } catch (e) {
      debugPrint('[MediaStreamWidget] renderer.setVolume failed: $e');
    }
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
