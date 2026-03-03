import 'dart:convert';
import 'dart:math' as math;
import 'dart:typed_data';

import 'package:archive/archive.dart';

class VrlPoseSample {
  final double px;
  final double py;
  final double pz;
  final double rx;
  final double ry;
  final double rz;
  final double rw;

  const VrlPoseSample({
    required this.px,
    required this.py,
    required this.pz,
    required this.rx,
    required this.ry,
    required this.rz,
    required this.rw,
  });
}

class VrlTraceFrame {
  final int index;
  final double monotonicSec;
  final VrlPoseSample? head;
  final VrlPoseSample? left;
  final VrlPoseSample? right;

  const VrlTraceFrame({
    required this.index,
    required this.monotonicSec,
    required this.head,
    required this.left,
    required this.right,
  });
}

class VrlTraceData {
  final int schemaRevision;
  final int sampleRateHz;
  final String sessionId;
  final String gameId;
  final String flowId;
  final String traceId;
  final int declaredFrameCount;
  final List<VrlTraceFrame> frames;
  final bool compressedSource;
  final int sourceByteLength;
  final int decodedByteLength;

  const VrlTraceData({
    required this.schemaRevision,
    required this.sampleRateHz,
    required this.sessionId,
    required this.gameId,
    required this.flowId,
    required this.traceId,
    required this.declaredFrameCount,
    required this.frames,
    required this.compressedSource,
    required this.sourceByteLength,
    required this.decodedByteLength,
  });

  double get durationSec {
    if (frames.length <= 1) {
      return 0;
    }
    final first = frames.first.monotonicSec;
    final last = frames.last.monotonicSec;
    return math.max(0, last - first);
  }
}

class VrlTraceCodec {
  static VrlTraceData decode({
    required String base64Payload,
    required String encoding,
  }) {
    final normalizedPayload = base64Payload.trim();
    if (normalizedPayload.isEmpty) {
      throw const FormatException('VRL payload is empty.');
    }

    final sourceBytes = base64Decode(normalizedPayload);
    final compressedHint = _isGzipEncoding(encoding);
    final decodedBytes = _maybeInflateGzip(
      sourceBytes,
      compressedHint: compressedHint,
    );
    final reader = _VrlReader(decodedBytes);

    final magic = String.fromCharCodes(reader.readBytes(3));
    if (magic != 'VRL') {
      throw FormatException('Unsupported VRL magic: $magic');
    }

    final schemaRevision = reader.readUint8();
    final sampleRateHz = reader.readUint16();
    final sessionId = reader.readString();
    final gameId = reader.readString();
    final flowId = reader.readString();
    final traceId = reader.readString();
    final declaredFrameCount = reader.readInt32();
    if (declaredFrameCount < 0) {
      throw const FormatException('VRL frame count cannot be negative.');
    }

    final frames = <VrlTraceFrame>[];
    for (var i = 0; i < declaredFrameCount; i += 1) {
      final presenceMask = reader.readUint8();
      final index = reader.readInt32();
      final monotonicSec = reader.readFloat32();
      final head = (presenceMask & 0x1) != 0 ? reader.readPose() : null;
      final left = (presenceMask & 0x2) != 0 ? reader.readPose() : null;
      final right = (presenceMask & 0x4) != 0 ? reader.readPose() : null;
      frames.add(
        VrlTraceFrame(
          index: index,
          monotonicSec: monotonicSec,
          head: head,
          left: left,
          right: right,
        ),
      );
    }

    return VrlTraceData(
      schemaRevision: schemaRevision,
      sampleRateHz: sampleRateHz,
      sessionId: sessionId,
      gameId: gameId,
      flowId: flowId,
      traceId: traceId,
      declaredFrameCount: declaredFrameCount,
      frames: frames,
      compressedSource: compressedHint || _looksLikeGzip(sourceBytes),
      sourceByteLength: sourceBytes.length,
      decodedByteLength: decodedBytes.length,
    );
  }

  static bool _isGzipEncoding(String encoding) {
    final normalized = encoding.trim().toLowerCase();
    if (normalized.isEmpty) {
      return false;
    }
    return normalized.contains('gzip');
  }

  static bool _looksLikeGzip(Uint8List bytes) {
    return bytes.length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b;
  }

  static Uint8List _maybeInflateGzip(
    Uint8List sourceBytes, {
    required bool compressedHint,
  }) {
    if (!compressedHint && !_looksLikeGzip(sourceBytes)) {
      return sourceBytes;
    }

    try {
      final inflated = GZipDecoder().decodeBytes(sourceBytes);
      return Uint8List.fromList(inflated);
    } catch (error) {
      throw FormatException('Failed to decode gzip VRL payload: $error');
    }
  }
}

class _VrlReader {
  final Uint8List _bytes;
  final ByteData _view;
  int _offset = 0;

  _VrlReader(this._bytes) : _view = ByteData.sublistView(_bytes);

  Uint8List readBytes(int length) {
    _ensure(length);
    final data = Uint8List.sublistView(_bytes, _offset, _offset + length);
    _offset += length;
    return data;
  }

  int readUint8() {
    _ensure(1);
    final value = _view.getUint8(_offset);
    _offset += 1;
    return value;
  }

  int readUint16() {
    _ensure(2);
    final value = _view.getUint16(_offset, Endian.little);
    _offset += 2;
    return value;
  }

  int readInt32() {
    _ensure(4);
    final value = _view.getInt32(_offset, Endian.little);
    _offset += 4;
    return value;
  }

  double readFloat32() {
    _ensure(4);
    final value = _view.getFloat32(_offset, Endian.little);
    _offset += 4;
    return value.toDouble();
  }

  String readString() {
    final length = readInt32();
    if (length < 0) {
      throw const FormatException('VRL string length cannot be negative.');
    }
    if (length == 0) {
      return '';
    }
    final bytes = readBytes(length);
    return utf8.decode(bytes, allowMalformed: true);
  }

  VrlPoseSample readPose() {
    return VrlPoseSample(
      px: _halfToDouble(readUint16()),
      py: _halfToDouble(readUint16()),
      pz: _halfToDouble(readUint16()),
      rx: _halfToDouble(readUint16()),
      ry: _halfToDouble(readUint16()),
      rz: _halfToDouble(readUint16()),
      rw: _halfToDouble(readUint16()),
    );
  }

  void _ensure(int length) {
    if (_offset + length > _bytes.length) {
      throw const FormatException('Unexpected end of VRL payload.');
    }
  }

  static double _halfToDouble(int value) {
    final sign = (value & 0x8000) == 0 ? 1.0 : -1.0;
    final exponent = (value >> 10) & 0x1f;
    final mantissa = value & 0x3ff;

    if (exponent == 0) {
      if (mantissa == 0) {
        return sign == 1.0 ? 0.0 : -0.0;
      }
      return sign * math.pow(2.0, -14).toDouble() * (mantissa / 1024.0);
    }

    if (exponent == 0x1f) {
      if (mantissa == 0) {
        return sign * double.infinity;
      }
      return double.nan;
    }

    return sign *
        math.pow(2.0, exponent - 15).toDouble() *
        (1.0 + (mantissa / 1024.0));
  }
}
