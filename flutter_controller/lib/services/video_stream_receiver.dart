import 'dart:io';
import 'dart:async';
import 'dart:typed_data';

/// Receives video frames from Unity over UDP.
///
/// Protocol (per UDP packet):
///   [frameId: 4 bytes, little-endian uint32]
///   [fragmentIndex: 2 bytes, little-endian uint16]
///   [totalFragments: 2 bytes, little-endian uint16]
///   [payload: remaining bytes]
///
/// Unity sends JPEG-encoded frames, fragmented into MTU-safe UDP packets.
/// This receiver reassembles fragments and emits complete frames.
class VideoStreamReceiver {
  static const int _headerSize = 8;
  static const int _maxStaleFrames = 5;

  final int port;
  RawDatagramSocket? _socket;
  bool _isRunning = false;

  // Frame reassembly buffers
  final Map<int, List<Uint8List?>> _frameBuffers = {};
  final Map<int, int> _frameTotalFragments = {};
  int _lastCompletedFrameId = -1;

  // Output stream
  final _frameController = StreamController<Uint8List>.broadcast();
  Stream<Uint8List> get frames => _frameController.stream;

  // Stats
  int _framesCompleted = 0;
  int _packetsReceived = 0;
  int _framesDropped = 0;
  DateTime _startTime = DateTime.now();

  VideoStreamReceiver({this.port = 8081});

  /// Start listening for video packets on UDP port.
  Future<void> start() async {
    if (_isRunning) {
      print('[VideoReceiver] Already running');
      return;
    }

    try {
      _socket = await RawDatagramSocket.bind(InternetAddress.anyIPv4, port);
      _isRunning = true;
      _startTime = DateTime.now();

      print('[VideoReceiver] Listening on UDP port $port');

      _socket!.listen((event) {
        if (event == RawSocketEvent.read) {
          final datagram = _socket!.receive();
          if (datagram != null) {
            _handlePacket(datagram.data);
          }
        }
      });
    } catch (e) {
      print('[VideoReceiver] Failed to start: $e');
      _isRunning = false;
      rethrow;
    }
  }

  /// Handle a single incoming UDP packet.
  void _handlePacket(Uint8List packet) {
    if (packet.length <= _headerSize) return;

    _packetsReceived++;

    // Parse header
    final byteData = ByteData.sublistView(packet);
    final frameId = byteData.getUint32(0, Endian.little);
    final fragmentIndex = byteData.getUint16(4, Endian.little);
    final totalFragments = byteData.getUint16(6, Endian.little);

    // Skip old frames (already displayed a newer one)
    if (frameId <= _lastCompletedFrameId) return;

    // Extract payload
    final payload = packet.sublist(_headerSize);

    // Initialize buffer for this frame
    if (!_frameBuffers.containsKey(frameId)) {
      _frameBuffers[frameId] = List<Uint8List?>.filled(totalFragments, null);
      _frameTotalFragments[frameId] = totalFragments;
    }

    // Bounds check
    if (fragmentIndex >= totalFragments) return;

    // Store fragment
    _frameBuffers[frameId]![fragmentIndex] = payload;

    // Check if frame is complete
    if (_isFrameComplete(frameId)) {
      _assembleFrame(frameId);
    }

    // Cleanup stale incomplete frames
    _cleanupStaleFrames(frameId);
  }

  bool _isFrameComplete(int frameId) {
    final fragments = _frameBuffers[frameId];
    if (fragments == null) return false;
    return fragments.every((f) => f != null);
  }

  void _assembleFrame(int frameId) {
    final fragments = _frameBuffers[frameId]!;

    // Calculate total size
    int totalSize = 0;
    for (final f in fragments) {
      totalSize += f!.length;
    }

    // Concatenate
    final frame = Uint8List(totalSize);
    int offset = 0;
    for (final f in fragments) {
      frame.setRange(offset, offset + f!.length, f);
      offset += f.length;
    }

    // Update state
    _lastCompletedFrameId = frameId;
    _framesCompleted++;

    // Remove this frame's buffer
    _frameBuffers.remove(frameId);
    _frameTotalFragments.remove(frameId);

    // Emit frame
    if (!_frameController.isClosed) {
      _frameController.add(frame);
    }
  }

  /// Remove frames older than the latest completed frame.
  void _cleanupStaleFrames(int currentFrameId) {
    final staleIds = _frameBuffers.keys
        .where((id) => id < currentFrameId - _maxStaleFrames)
        .toList();

    for (final id in staleIds) {
      _frameBuffers.remove(id);
      _frameTotalFragments.remove(id);
      _framesDropped++;
    }
  }

  /// Get current statistics.
  Map<String, dynamic> getStats() {
    final elapsed = DateTime.now().difference(_startTime).inSeconds;
    return {
      'packetsReceived': _packetsReceived,
      'framesCompleted': _framesCompleted,
      'framesDropped': _framesDropped,
      'avgFps': elapsed > 0 ? (_framesCompleted / elapsed).toStringAsFixed(1) : '0',
      'bufferCount': _frameBuffers.length,
    };
  }

  /// Stop receiving and release resources.
  void dispose() {
    _isRunning = false;
    _socket?.close();
    _socket = null;
    _frameBuffers.clear();
    _frameTotalFragments.clear();
    if (!_frameController.isClosed) {
      _frameController.close();
    }
    print('[VideoReceiver] Disposed');
  }
}
