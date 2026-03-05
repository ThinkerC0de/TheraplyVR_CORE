import 'dart:async';
import 'dart:convert';

import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:firebase_storage/firebase_storage.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_controller/services/connection_service.dart';
import 'package:flutter_controller/services/firebase_service.dart';
import 'package:flutter_controller/services/session_journal_service.dart';

/// Handles game telemetry forwarded from Quest via TCP.
///
/// Quest sends two command types:
///   QUEST_GAME_EVENT       — game_start | game_end | session_start | session_stop
///   QUEST_INTERACTION_BATCH — all interactions from a completed game run
///
/// On game_end this service writes to Firestore:
///   therapy_sessions/{sessionId}/game_runs/{gameRunId}
///   therapy_sessions/{sessionId}/interactions/{eventId}  (batch)
///   therapy_sessions/{sessionId}/events/{id}             (timeline via SessionJournalService)
class GameDataService {
  static const _gameEventCmd = 'QUEST_GAME_EVENT';
  static const _interactionBatchCmd = 'QUEST_INTERACTION_BATCH';
  static const _motionTraceCmd = 'QUEST_MOTION_TRACE';

  static FirebaseFirestore get _firestore => FirebaseService.firestore;

  final ConnectionService _connection;

  // Active session context — set when mobile creates/attaches a session.
  String _sessionId = '';
  String _studentId = '';
  String _therapistId = '';

  // Tracks current in-progress game run (cleared on game_end).
  String? _activeGameRunId;
  String? _activeGameId;

  // Count of game_runs successfully written this session (reset on attachSession).
  int _gameRunsWritten = 0;

  /// True if at least one game_run was written for the current session.
  bool get hasGameData => _gameRunsWritten > 0;

  // Pending interaction batch for the current game run (arrives before game_end on the wire).
  // Map: gameRunId → list of interaction maps.
  final Map<String, List<Map<String, dynamic>>> _pendingInteractions = {};

  StreamSubscription<Map<String, dynamic>>? _messageSub;

  GameDataService(this._connection) {
    _messageSub = _connection.messages.listen(_handleMessage);
  }

  /// Call when a session is attached (mobile created/joined).
  void attachSession({
    required String sessionId,
    required String studentId,
    required String therapistId,
  }) {
    _sessionId = sessionId.trim();
    _studentId = studentId.trim();
    _therapistId = therapistId.trim();
    _pendingInteractions.clear();
    _activeGameRunId = null;
    _activeGameId = null;
    _gameRunsWritten = 0;
    print('[GameData] attachSession: sid=$_sessionId uid=$_therapistId student=$_studentId');
  }

  /// Call when the session ends or is cleared.
  void detachSession() {
    _sessionId = '';
    _studentId = '';
    _therapistId = '';
    _pendingInteractions.clear();
  }

  Future<void> dispose() async {
    await _messageSub?.cancel();
    _messageSub = null;
  }

  // ─── TCP message handling ─────────────────────────────────────────────────

  void _handleMessage(Map<String, dynamic> message) {
    final commandId = message['commandId'] as String? ?? '';
    if (commandId != _gameEventCmd &&
        commandId != _interactionBatchCmd &&
        commandId != _motionTraceCmd) return;

    print('[GameData] 📥 $commandId sid=$_sessionId');
    final payload = _decodePayload(message['payload']);
    if (payload == null) {
      print('[GameData] ❌ payload decode failed for $commandId');
      return;
    }

    if (commandId == _gameEventCmd) {
      _handleGameEvent(payload).catchError((Object e, StackTrace st) {
        print('[GameData] ❌ handleGameEvent error: $e\n$st');
      });
    } else if (commandId == _interactionBatchCmd) {
      _handleInteractionBatch(payload);
    } else {
      _handleMotionTrace(payload).catchError((Object e, StackTrace st) {
        print('[GameData] ❌ handleMotionTrace error: $e\n$st');
      });
    }
  }

  Future<void> _handleGameEvent(Map<String, dynamic> payload) async {
    final eventType = payload['eventType'] as String? ?? '';
    final sessionId = (payload['sessionId'] as String? ?? '').trim();

    // If Quest sends a session ID that differs from ours, update context.
    if (sessionId.isNotEmpty && sessionId != _sessionId && _sessionId.isEmpty) {
      _sessionId = sessionId;
    }

    if (_sessionId.isEmpty) return;

    switch (eventType) {
      case 'game_start':
        await _onGameStart(payload);
        break;
      case 'game_end':
        await _onGameEnd(payload);
        break;
      case 'session_start':
        await _onSessionStart(payload);
        break;
      case 'session_stop':
        await _onSessionStop(payload);
        break;
    }
  }

  void _handleInteractionBatch(Map<String, dynamic> payload) {
    final gameRunId = (payload['gameRunId'] as String? ?? '').trim();
    if (gameRunId.isEmpty) return;

    final rawEvents = payload['events'];
    if (rawEvents is! List) return;

    final events = rawEvents
        .whereType<Map<String, dynamic>>()
        .toList();

    if (events.isNotEmpty) {
      _pendingInteractions[gameRunId] = events;
      if (kDebugMode) {
        debugPrint('[GameData] Buffered ${events.length} interactions for gameRunId=$gameRunId');
      }
    }
  }

  // ─── Event handlers ───────────────────────────────────────────────────────

  Future<void> _onGameStart(Map<String, dynamic> payload) async {
    final gameId = payload['gameId'] as String? ?? '';
    final gameRunId = (payload['gameRunId'] as String? ?? '').trim();
    final occurredAtUtc = payload['occurredAtUtc'] as String?;

    _activeGameRunId = gameRunId.isNotEmpty ? gameRunId : null;
    _activeGameId = gameId.isNotEmpty ? gameId : null;

    if (kDebugMode) {
      debugPrint('[GameData] game_start: gameId=$gameId gameRunId=$gameRunId');
    }

    // Write high-level timeline event immediately.
    await _appendTimeline(
      eventType: 'game_started',
      gameId: gameId,
      occurredAtUtc: occurredAtUtc,
      details: {'gameRunId': gameRunId},
    );
  }

  Future<void> _onGameEnd(Map<String, dynamic> payload) async {
    final gameRunId = (payload['gameRunId'] as String? ?? '').trim();
    final gameId = payload['gameId'] as String? ?? '';
    final finalState = payload['finalState'] as String? ?? 'completed';
    final durationSec = (payload['durationSec'] as num?)?.toInt() ?? 0;
    final hitCount = (payload['hitCount'] as num?)?.toInt() ?? 0;
    final missCount = (payload['missCount'] as num?)?.toInt() ?? 0;
    final interactionCount = (payload['interactionCount'] as num?)?.toInt() ?? 0;
    final occurredAtUtc = payload['occurredAtUtc'] as String?;

    if (gameRunId.isEmpty) return;

    // Clear active run tracker — run is ending.
    if (_activeGameRunId == gameRunId) {
      _activeGameRunId = null;
      _activeGameId = null;
    }

    print('[GameData] game_end: sid=$_sessionId runId=$gameRunId state=$finalState hits=$hitCount misses=$missCount');

    final sessionRef = _firestore
        .collection('therapy_sessions')
        .doc(_sessionId);

    // 1. Write game_runs document.
    try {
      await sessionRef.collection('game_runs').doc(gameRunId).set({
        'gameId': gameId,
        'startedAtUtc': null,
        'endedAtUtc': occurredAtUtc,
        'finalState': finalState,
        'durationSec': durationSec,
        // motionTraceUrl is written separately by _handleMotionTrace (merge-safe).
        'summary': {
          'interactionCount': interactionCount,
          'hitCount': hitCount,
          'missCount': missCount,
          'avgReactionTimeMs': null,
        },
      }, SetOptions(merge: true));
      _gameRunsWritten++;
      print('[GameData] ✅ game_runs/$gameRunId written (total=$_gameRunsWritten)');
    } catch (e) {
      print('[GameData] ❌ game_runs write failed: $e');
    }

    // 2. Batch write interactions (if received before game_end).
    final interactions = _pendingInteractions.remove(gameRunId);
    if (interactions != null && interactions.isNotEmpty) {
      try {
        await _writeInteractionsBatch(gameRunId, interactions);
        print('[GameData] ✅ interactions written: ${interactions.length}');
      } catch (e) {
        print('[GameData] ❌ interactions write failed: $e');
      }
    } else {
      print('[GameData] ⚠️ no pending interactions for $gameRunId');
    }

    // 3. Update session summary totals.
    try {
      await sessionRef.set({
        'summary': {
          'totalInteractions': FieldValue.increment(interactionCount),
          'totalHits': FieldValue.increment(hitCount),
          'totalMisses': FieldValue.increment(missCount),
          'gamesPlayed': FieldValue.increment(1),
        },
      }, SetOptions(merge: true));
      print('[GameData] ✅ session summary updated');
    } catch (e) {
      print('[GameData] ❌ session summary update failed: $e');
    }

    // 4. High-level timeline event.
    await _appendTimeline(
      eventType: finalState == 'completed' ? 'game_completed' : 'game_interrupted',
      gameId: gameId,
      occurredAtUtc: occurredAtUtc,
      details: {
        'gameRunId': gameRunId,
        'finalState': finalState,
        'durationSec': durationSec,
        'hitCount': hitCount,
        'missCount': missCount,
        'interactionCount': interactionCount,
      },
    );
  }

  Future<void> _onSessionStart(Map<String, dynamic> payload) async {
    final occurredAtUtc = payload['occurredAtUtc'] as String?;
    await _appendTimeline(
      eventType: 'session_start',
      occurredAtUtc: occurredAtUtc,
    );
  }

  Future<void> _onSessionStop(Map<String, dynamic> payload) async {
    final reason = payload['finalState'] as String?
        ?? payload['reasonCode'] as String?
        ?? '';
    final occurredAtUtc = payload['occurredAtUtc'] as String?;
    await _appendTimeline(
      eventType: 'session_stop',
      occurredAtUtc: occurredAtUtc,
      details: reason.isNotEmpty ? {'reason': reason} : const {},
    );
  }

  // ─── Motion trace upload ──────────────────────────────────────────────────

  Future<void> _handleMotionTrace(Map<String, dynamic> payload) async {
    if (_sessionId.isEmpty) return;

    final gameRunId = (payload['gameRunId'] as String? ?? '').trim();
    final traceId = (payload['traceId'] as String? ?? '').trim();
    final inlineStatus = payload['inlinePayloadStatus'] as String? ?? '';
    final tracePayloadBase64 = payload['tracePayloadBase64'] as String? ?? '';
    final encoding = payload['encoding'] as String? ?? 'none';
    final frameCount = (payload['frameCount'] as num?)?.toInt() ?? 0;

    print('[GameData] motion_trace: runId=$gameRunId traceId=$traceId '
        'status=$inlineStatus frames=$frameCount');

    if (gameRunId.isEmpty || traceId.isEmpty) return;

    if (inlineStatus != 'INCLUDED' || tracePayloadBase64.isEmpty) {
      print('[GameData] ⚠️ motion_trace: no inline payload (status=$inlineStatus) — skipping upload');
      return;
    }

    Uint8List bytes;
    try {
      bytes = base64Decode(tracePayloadBase64);
    } catch (e) {
      print('[GameData] ❌ motion_trace base64 decode failed: $e');
      return;
    }

    // Upload to Firebase Storage.
    final ext = encoding == 'vrl_gzip' ? '.vrl.gz' : '.vrl';
    final storagePath = 'motion_traces/$_sessionId/$gameRunId/$traceId$ext';
    try {
      final ref = FirebaseStorage.instance.ref(storagePath);
      final metadata = SettableMetadata(
        contentType: encoding == 'vrl_gzip' ? 'application/gzip' : 'application/octet-stream',
        customMetadata: {
          'sessionId': _sessionId,
          'gameRunId': gameRunId,
          'traceId': traceId,
          'encoding': encoding,
          'frameCount': '$frameCount',
        },
      );
      await ref.putData(bytes, metadata);
      final downloadUrl = await ref.getDownloadURL();

      // Update game_run with the Storage URL (merge — safe regardless of order vs game_end).
      await _firestore
          .collection('therapy_sessions')
          .doc(_sessionId)
          .collection('game_runs')
          .doc(gameRunId)
          .set({'motionTraceUrl': downloadUrl}, SetOptions(merge: true));

      print('[GameData] ✅ motion_trace uploaded: $storagePath → $downloadUrl');
    } catch (e) {
      print('[GameData] ❌ motion_trace upload failed: $e');
    }
  }

  // ─── Firestore helpers ────────────────────────────────────────────────────

  Future<void> _writeInteractionsBatch(
    String gameRunId,
    List<Map<String, dynamic>> interactions,
  ) async {
    const maxPerBatch = 499;
    final sessionRef = _firestore
        .collection('therapy_sessions')
        .doc(_sessionId);

    for (var offset = 0; offset < interactions.length; offset += maxPerBatch) {
      final chunk = interactions.skip(offset).take(maxPerBatch).toList();
      final batch = _firestore.batch();

      for (final event in chunk) {
        final eventId = event['eventId'] as String? ?? '';
        if (eventId.isEmpty) continue;

        batch.set(
          sessionRef.collection('interactions').doc(eventId),
          {
            'occurredAtUtc': event['occurredAtUtc'] ?? '',
            'sequenceNo': (event['sequenceNo'] as num?)?.toInt() ?? 0,
            'gameRunId': gameRunId,
            'interactionType': event['interactionType'] ?? '',
            'actionOutcome': event['actionOutcome'] ?? '',
            'targetId': event['targetId'] ?? '',
            'targetName': event['targetName'] ?? '',
            'inputHand': event['inputHand'] ?? '',
            'inputSource': event['inputSource'] ?? '',
            'inputValue': (event['inputValue'] as num?)?.toDouble(),
            'sourceComponent': event['sourceComponent'] ?? '',
            'reasonCode': event['reasonCode'] ?? '',
          },
        );
      }

      await batch.commit();
    }

    if (kDebugMode) {
      debugPrint('[GameData] Wrote ${interactions.length} interactions for gameRunId=$gameRunId');
    }
  }

  Future<void> _appendTimeline({
    required String eventType,
    String gameId = '',
    String? occurredAtUtc,
    Map<String, dynamic> details = const {},
  }) async {
    if (_sessionId.isEmpty || _studentId.isEmpty || _therapistId.isEmpty) return;

    await SessionJournalService.appendSessionEvent(
      sessionId: _sessionId,
      studentId: _studentId,
      therapistId: _therapistId,
      eventType: eventType,
      gameId: gameId,
      source: 'quest_runtime',
      eventAtUtc: occurredAtUtc != null ? DateTime.tryParse(occurredAtUtc)?.toUtc() : null,
      details: details.isEmpty ? null : details,
    );
  }

  // ─── Public helpers for control_screen ───────────────────────────────────

  /// Writes an 'interrupted' game_run document for the current active run.
  /// Called when Quest disconnects mid-game.
  Future<void> markActiveRunInterrupted() async {
    final runId = _activeGameRunId;
    final gameId = _activeGameId;
    if (runId == null || _sessionId.isEmpty) return;

    print('[GameData] markActiveRunInterrupted: runId=$runId');
    try {
      await _firestore
          .collection('therapy_sessions')
          .doc(_sessionId)
          .collection('game_runs')
          .doc(runId)
          .set({
        'gameId': gameId ?? '',
        'startedAtUtc': null,
        'endedAtUtc': DateTime.now().toUtc().toIso8601String(),
        'finalState': 'interrupted',
        'durationSec': 0,
        'summary': {
          'interactionCount': 0,
          'hitCount': 0,
          'missCount': 0,
          'avgReactionTimeMs': null,
        },
      }, SetOptions(merge: true));
      _gameRunsWritten++;
      _activeGameRunId = null;
      _activeGameId = null;
      print('[GameData] ✅ interrupted game_run written');
    } catch (e) {
      print('[GameData] ❌ interrupted game_run write failed: $e');
    }
  }

  /// Deletes the session document from Firestore if no game_runs were written.
  /// Safe to call after EndSession when therapist never played any game.
  Future<void> deleteSessionIfEmpty() async {
    if (_sessionId.isEmpty || _gameRunsWritten > 0) return;

    print('[GameData] deleteSessionIfEmpty: deleting empty session $_sessionId');
    try {
      // Delete subcollection events (timeline) first, then the session doc.
      final sessionRef = _firestore.collection('therapy_sessions').doc(_sessionId);
      final events = await sessionRef.collection('events').get();
      final batch = _firestore.batch();
      for (final doc in events.docs) {
        batch.delete(doc.reference);
      }
      batch.delete(sessionRef);
      await batch.commit();
      print('[GameData] ✅ empty session deleted: $_sessionId');
    } catch (e) {
      print('[GameData] ❌ empty session delete failed: $e');
    }
  }

  // ─── Payload decoding ─────────────────────────────────────────────────────

  static Map<String, dynamic>? _decodePayload(dynamic payload) {
    if (payload is! String || payload.isEmpty) return null;
    try {
      final payloadString = utf8.decode(base64Decode(payload));
      final decoded = jsonDecode(payloadString);
      return decoded is Map<String, dynamic> ? decoded : null;
    } catch (_) {
      return null;
    }
  }
}
