import 'dart:async';
import 'dart:convert';
import 'dart:math' as math;

import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:firebase_storage/firebase_storage.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_controller/models/session_ownership.dart';
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
  static const _interactionPreviewCmd = 'QUEST_INTERACTION_PREVIEW';
  static const _interactionBatchCmd = 'QUEST_INTERACTION_BATCH';
  static const _motionTraceCmd = 'QUEST_MOTION_TRACE';
  static const _interactionTimelineEventType = 'OBJECT_INTERACTION';

  static FirebaseFirestore get _firestore => FirebaseService.firestore;

  final ConnectionService _connection;

  // Active session context — set when mobile creates/attaches a session.
  String _sessionId = '';
  String _studentId = '';
  String _therapistId = '';

  // Tracks current in-progress game run (cleared on game_end).
  String? _activeGameRunId;
  String? _activeGameId;
  DateTime? _activeGameRunStartedAtUtc;

  // Tracks unique game_runs written for the current session.
  final Set<String> _writtenGameRunIds = <String>{};

  /// True if at least one game_run was written for the current session.
  bool get hasGameData => _writtenGameRunIds.isNotEmpty;
  final Map<String, SessionTimelineEvent> _liveTimelinePreviewById =
      <String, SessionTimelineEvent>{};
  final StreamController<List<SessionTimelineEvent>>
      _liveTimelinePreviewController =
      StreamController<List<SessionTimelineEvent>>.broadcast();
  Stream<List<SessionTimelineEvent>> get liveTimelinePreviewStream =>
      _liveTimelinePreviewController.stream;
  List<SessionTimelineEvent> get liveTimelinePreviewEvents =>
      _sortedLiveTimelinePreviewEvents();

  // Pending interaction batch for the current game run (arrives before game_end on the wire).
  // Map: gameRunId → list of interaction maps.
  final Map<String, List<Map<String, dynamic>>> _pendingInteractions = {};
  final Map<String, _CompletedGameRunContext>
      _completedRunsAwaitingInteractions = <String, _CompletedGameRunContext>{};

  StreamSubscription<Map<String, dynamic>>? _messageSub;
  Future<void> Function(String sessionId)? onJournalCheckpointCommitted;

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
    _completedRunsAwaitingInteractions.clear();
    _activeGameRunId = null;
    _activeGameId = null;
    _activeGameRunStartedAtUtc = null;
    _writtenGameRunIds.clear();
    _clearLiveTimelinePreview();
    print(
        '[GameData] attachSession: sid=$_sessionId uid=$_therapistId student=$_studentId');
  }

  /// Call when the session ends or is cleared.
  void detachSession() {
    _sessionId = '';
    _studentId = '';
    _therapistId = '';
    _pendingInteractions.clear();
    _completedRunsAwaitingInteractions.clear();
    _activeGameRunId = null;
    _activeGameId = null;
    _activeGameRunStartedAtUtc = null;
    _clearLiveTimelinePreview();
  }

  Future<void> dispose() async {
    await _messageSub?.cancel();
    _messageSub = null;
    _clearLiveTimelinePreview();
    await _liveTimelinePreviewController.close();
  }

  // ─── TCP message handling ─────────────────────────────────────────────────

  void _handleMessage(Map<String, dynamic> message) {
    final commandId = message['commandId'] as String? ?? '';
    if (commandId != _gameEventCmd &&
        commandId != _interactionPreviewCmd &&
        commandId != _interactionBatchCmd &&
        commandId != _motionTraceCmd) {
      return;
    }

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
    } else if (commandId == _interactionPreviewCmd) {
      _handleInteractionPreview(payload);
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
        .map((event) {
          if (event is Map<String, dynamic>) {
            return Map<String, dynamic>.from(event);
          }
          if (event is Map) {
            return Map<String, dynamic>.from(event);
          }
          return null;
        })
        .whereType<Map<String, dynamic>>()
        .toList(growable: false);

    if (events.isEmpty) {
      return;
    }

    final completedContext =
        _completedRunsAwaitingInteractions.remove(gameRunId);
    if (completedContext != null) {
      _persistLateInteractionBatch(
        gameRunId: gameRunId,
        context: completedContext,
        events: events,
      ).catchError((Object e, StackTrace st) {
        print('[GameData] ❌ late interaction batch error: $e\n$st');
      });
      return;
    }

    _pendingInteractions[gameRunId] = events;
    if (kDebugMode) {
      debugPrint(
          '[GameData] Buffered ${events.length} interactions for gameRunId=$gameRunId');
    }
  }

  void _handleInteractionPreview(Map<String, dynamic> payload) {
    final previewSessionId = (payload['sessionId'] as String? ?? '').trim();
    if (previewSessionId.isEmpty) {
      return;
    }
    if (_sessionId.isNotEmpty && previewSessionId != _sessionId) {
      return;
    }
    if (_studentId.trim().isEmpty || _therapistId.trim().isEmpty) {
      return;
    }

    final rawEvent = payload['event'];
    if (rawEvent is! Map && rawEvent is! Map<String, dynamic>) {
      return;
    }

    final event = rawEvent is Map<String, dynamic>
        ? Map<String, dynamic>.from(rawEvent)
        : Map<String, dynamic>.from(rawEvent as Map);
    final eventId = (event['eventId'] as String? ?? '').trim();
    if (eventId.isEmpty) {
      return;
    }

    final gameRunId = (payload['gameRunId'] as String? ?? '').trim();
    final occurredAt =
        _tryParseUtc((event['occurredAtUtc'] as String?)?.trim()) ??
            DateTime.now().toUtc();
    final targetAppearedAtUtc =
        (event['targetAppearedAtUtc'] as String? ?? '').trim();
    final targetAppearedAt = _tryParseUtc(targetAppearedAtUtc);
    final timelineEventId = 'interaction-$eventId';

    _liveTimelinePreviewById[timelineEventId] = SessionTimelineEvent(
      eventId: eventId,
      timelineEventId: timelineEventId,
      sessionId: previewSessionId,
      studentId: _studentId.trim(),
      therapistId: _therapistId.trim(),
      ownerKey: SessionOwnership.ownerKey(
        therapistId: _therapistId,
        studentId: _studentId,
      ),
      sessionKey: SessionOwnership.sessionKey(
        therapistId: _therapistId,
        studentId: _studentId,
        sessionId: previewSessionId,
      ),
      eventType: _interactionTimelineEventType,
      gameId: (event['gameId'] as String? ?? '').trim(),
      source: 'quest_runtime',
      eventAtUtc: occurredAt,
      eventAtUnixMs: occurredAt.millisecondsSinceEpoch,
      details: _buildInteractionTimelineDetails(
        eventId: eventId,
        gameRunId: gameRunId,
        sequenceNo: (event['sequenceNo'] as num?)?.toInt() ?? 0,
        interactionEventType: (event['eventType'] as String? ?? '').trim(),
        interactionType: (event['interactionType'] as String? ?? '').trim(),
        actionOutcome: (event['actionOutcome'] as String? ?? '').trim(),
        reasonCode: (event['reasonCode'] as String? ?? '').trim(),
        targetId: (event['targetId'] as String? ?? '').trim(),
        targetName: (event['targetName'] as String? ?? '').trim(),
        targetCategory: (event['targetCategory'] as String? ?? '').trim(),
        targetInstanceId: (event['targetInstanceId'] as String? ?? '').trim(),
        targetAppearedAtUtc: targetAppearedAtUtc,
        targetAppearedAtUnixMs:
            targetAppearedAt?.millisecondsSinceEpoch ?? 0,
        targetAppearedAtElapsedSec:
            _asDoubleOrNull(event['targetAppearedAtElapsedSec']),
        responseSec: _resolveInteractionResponseSec(
          explicitResponseSec: _asDoubleOrNull(event['responseSec']),
          occurredAtUtc: occurredAt,
          targetAppearedAtUtc: targetAppearedAt,
        ),
        inputHand: (event['inputHand'] as String? ?? '').trim(),
        inputSource: (event['inputSource'] as String? ?? '').trim(),
        inputValue: _asDoubleOrNull(event['inputValue']),
        sourceComponent: (event['sourceComponent'] as String? ?? '').trim(),
      )
        ..['previewState'] = 'LIVE_PENDING'
        ..['persisted'] = false,
    );
    _publishLiveTimelinePreview();
  }

  // ─── Event handlers ───────────────────────────────────────────────────────

  Future<void> _onGameStart(Map<String, dynamic> payload) async {
    final gameId = payload['gameId'] as String? ?? '';
    final gameRunId = (payload['gameRunId'] as String? ?? '').trim();
    final occurredAtUtc = payload['occurredAtUtc'] as String?;
    final startedAtUtc = _resolveOccurredAtUtc(occurredAtUtc);

    _activeGameRunId = gameRunId.isNotEmpty ? gameRunId : null;
    _activeGameId = gameId.isNotEmpty ? gameId : null;
    _activeGameRunStartedAtUtc = startedAtUtc;

    if (kDebugMode) {
      debugPrint('[GameData] game_start: gameId=$gameId gameRunId=$gameRunId');
    }
  }

  Future<void> _onGameEnd(Map<String, dynamic> payload) async {
    final gameRunId = (payload['gameRunId'] as String? ?? '').trim();
    final gameId = payload['gameId'] as String? ?? '';
    final finalState = payload['finalState'] as String? ?? 'completed';
    final durationSec = (payload['durationSec'] as num?)?.toInt() ?? 0;
    final reportedHitCount = (payload['hitCount'] as num?)?.toInt() ?? 0;
    final reportedMissCount = (payload['missCount'] as num?)?.toInt() ?? 0;
    final reportedInteractionCount =
        (payload['interactionCount'] as num?)?.toInt() ?? 0;
    final occurredAtUtc = payload['occurredAtUtc'] as String?;
    final endedAtUtc = _resolveOccurredAtUtc(occurredAtUtc);
    final startedAtUtc =
        (_activeGameRunId == gameRunId && _activeGameRunStartedAtUtc != null)
            ? _activeGameRunStartedAtUtc!
            : endedAtUtc;

    if (gameRunId.isEmpty) return;

    // Clear active run tracker — run is ending.
    if (_activeGameRunId == gameRunId) {
      _activeGameRunId = null;
      _activeGameId = null;
      _activeGameRunStartedAtUtc = null;
    }

    final interactions = _pendingInteractions.remove(gameRunId);
    final interactionSummary = _summarizeInteractions(interactions);
    final interactionCount = math.max(
      reportedInteractionCount,
      interactionSummary.interactionCount,
    );
    final hitCount = math.max(
      reportedHitCount,
      interactionSummary.hitCount,
    );
    final missCount = math.max(
      reportedMissCount,
      interactionSummary.missCount,
    );

    print(
        '[GameData] game_end: sid=$_sessionId runId=$gameRunId state=$finalState hits=$hitCount misses=$missCount interactions=$interactionCount');

    final sessionRef =
        _firestore.collection('therapy_sessions').doc(_sessionId);

    // 1. Write game_runs document.
    try {
      await sessionRef.collection('game_runs').doc(gameRunId).set({
        'gameId': gameId,
        'startedAtUtc': startedAtUtc.toIso8601String(),
        'startedAtUnixMs': startedAtUtc.millisecondsSinceEpoch,
        'endedAtUtc': endedAtUtc.toIso8601String(),
        'endedAtUnixMs': endedAtUtc.millisecondsSinceEpoch,
        'finalState': finalState,
        'durationSec': durationSec,
        'updatedAtUtc': DateTime.now().toUtc().toIso8601String(),
        'updatedAtUnixMs': DateTime.now().toUtc().millisecondsSinceEpoch,
        // motionTraceUrl is written separately by _handleMotionTrace (merge-safe).
        'summary': {
          'interactionCount': interactionCount,
          'hitCount': hitCount,
          'missCount': missCount,
          'avgReactionTimeMs': null,
        },
      }, SetOptions(merge: true));
      _markGameRunWritten(gameRunId);
      print(
        '[GameData] ✅ game_runs/$gameRunId written (total=${_writtenGameRunIds.length})',
      );
    } catch (e) {
      print('[GameData] ❌ game_runs write failed: $e');
    }

    // 2. Batch write interactions (if received before game_end).
    if (interactions != null && interactions.isNotEmpty) {
      try {
        await _writeInteractionsBatch(
          sessionId: _sessionId,
          gameRunId: gameRunId,
          interactions: interactions,
        );
        print('[GameData] ✅ interactions written: ${interactions.length}');
      } catch (e) {
        print('[GameData] ❌ interactions write failed: $e');
      }
    } else {
      print('[GameData] ⚠️ no pending interactions for $gameRunId');
      _completedRunsAwaitingInteractions[gameRunId] = _CompletedGameRunContext(
        sessionId: _sessionId,
      );
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
      eventType:
          finalState == 'completed' ? 'game_completed' : 'game_interrupted',
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

    final sessionId = _sessionId.trim();
    if (sessionId.isNotEmpty) {
      try {
        await onJournalCheckpointCommitted?.call(sessionId);
      } catch (e) {
        print('[GameData] ❌ journal checkpoint callback failed: $e');
      }
    }
  }

  Future<void> _onSessionStart(Map<String, dynamic> payload) async {
    if (kDebugMode) {
      debugPrint('[GameData] session_start received (local-only)');
    }
  }

  Future<void> _onSessionStop(Map<String, dynamic> payload) async {
    final reason = payload['finalState'] as String? ??
        payload['reasonCode'] as String? ??
        '';
    if (kDebugMode) {
      debugPrint(
          '[GameData] session_stop received (local-only): reason=$reason');
    }
  }

  // ─── Motion trace upload ──────────────────────────────────────────────────

  Future<void> _handleMotionTrace(Map<String, dynamic> payload) async {
    if (_sessionId.isEmpty) return;

    final gameRunId = (payload['gameRunId'] as String? ?? '').trim();
    final traceId = (payload['traceId'] as String? ?? '').trim();
    final traceGameId = (payload['gameId'] as String? ?? '').trim();
    final inlineStatus = payload['inlinePayloadStatus'] as String? ?? '';
    final tracePayloadBase64 = payload['tracePayloadBase64'] as String? ?? '';
    final encoding = payload['encoding'] as String? ?? 'none';
    final frameCount = (payload['frameCount'] as num?)?.toInt() ?? 0;

    print('[GameData] motion_trace: runId=$gameRunId traceId=$traceId '
        'status=$inlineStatus frames=$frameCount');

    if (gameRunId.isEmpty || traceId.isEmpty) return;

    if (inlineStatus != 'INCLUDED' || tracePayloadBase64.isEmpty) {
      print(
          '[GameData] ⚠️ motion_trace: no inline payload (status=$inlineStatus) — skipping upload');
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
        contentType: encoding == 'vrl_gzip'
            ? 'application/gzip'
            : 'application/octet-stream',
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
          .set({
        'gameId': traceGameId.isNotEmpty
            ? traceGameId
            : (_activeGameRunId == gameRunId ? (_activeGameId ?? '') : ''),
        'startedAtUtc': _activeGameRunId == gameRunId &&
                _activeGameRunStartedAtUtc != null
            ? _activeGameRunStartedAtUtc!.toIso8601String()
            : null,
        'startedAtUnixMs': _activeGameRunId == gameRunId &&
                _activeGameRunStartedAtUtc != null
            ? _activeGameRunStartedAtUtc!.millisecondsSinceEpoch
            : null,
        'updatedAtUtc': DateTime.now().toUtc().toIso8601String(),
        'updatedAtUnixMs': DateTime.now().toUtc().millisecondsSinceEpoch,
        'motionTraceUrl': downloadUrl,
      }, SetOptions(merge: true));

      print('[GameData] ✅ motion_trace uploaded: $storagePath → $downloadUrl');
    } catch (e) {
      print('[GameData] ❌ motion_trace upload failed: $e');
    }
  }

  // ─── Firestore helpers ────────────────────────────────────────────────────

  Future<void> _writeInteractionsBatch({
    required String sessionId,
    required String gameRunId,
    required List<Map<String, dynamic>> interactions,
  }) async {
    const maxPerBatch = 240;
    final sessionRef = _firestore.collection('therapy_sessions').doc(sessionId);
    final ownerKey = SessionOwnership.ownerKey(
      therapistId: _therapistId,
      studentId: _studentId,
    );
    final sessionKey = SessionOwnership.sessionKey(
      therapistId: _therapistId,
      studentId: _studentId,
      sessionId: sessionId,
    );
    final canWriteTimeline = _studentId.trim().isNotEmpty &&
        _therapistId.trim().isNotEmpty &&
        ownerKey.isNotEmpty &&
        sessionKey.isNotEmpty;

    for (var offset = 0; offset < interactions.length; offset += maxPerBatch) {
      final chunk = interactions.skip(offset).take(maxPerBatch).toList();
      final batch = _firestore.batch();
      final batchCreatedAtUtc = DateTime.now().toUtc();

      for (final event in chunk) {
        final eventId = event['eventId'] as String? ?? '';
        if (eventId.isEmpty) continue;
        final interactionEventType = (event['eventType'] as String? ?? '').trim();
        final gameId = (event['gameId'] as String? ?? '').trim();
        final occurredAt =
            _tryParseUtc((event['occurredAtUtc'] as String?)?.trim());
        final occurredAtUtc = occurredAt?.toIso8601String() ?? '';
        final occurredAtUnixMs = occurredAt?.millisecondsSinceEpoch ?? 0;
        final targetId = (event['targetId'] as String? ?? '').trim();
        final targetName = (event['targetName'] as String? ?? '').trim();
        final targetInstanceId =
            (event['targetInstanceId'] as String? ?? '').trim();
        final targetCategory = (event['targetCategory'] as String? ?? '').trim();
        final targetAppearedAtUtc =
            (event['targetAppearedAtUtc'] as String? ?? '').trim();
        final targetAppearedAt = _tryParseUtc(targetAppearedAtUtc);
        final targetAppearedAtUnixMs =
            targetAppearedAt?.millisecondsSinceEpoch ?? 0;
        final targetAppearedAtElapsedSec =
            _asDoubleOrNull(event['targetAppearedAtElapsedSec']);
        final responseSec = _resolveInteractionResponseSec(
          explicitResponseSec: _asDoubleOrNull(event['responseSec']),
          occurredAtUtc: occurredAt,
          targetAppearedAtUtc: targetAppearedAt,
        );
        final actionOutcome = (event['actionOutcome'] as String? ?? '').trim();
        final reasonCode = (event['reasonCode'] as String? ?? '').trim();
        final interactionType =
            (event['interactionType'] as String? ?? '').trim();
        final inputHand = (event['inputHand'] as String? ?? '').trim();
        final inputSource = (event['inputSource'] as String? ?? '').trim();
        final inputValue = _asDoubleOrNull(event['inputValue']);
        final sourceComponent =
            (event['sourceComponent'] as String? ?? '').trim();
        final sequenceNo = (event['sequenceNo'] as num?)?.toInt() ?? 0;

        batch.set(
          sessionRef.collection('interactions').doc(eventId),
          {
            'occurredAtUtc': occurredAtUtc,
            'occurredAtUnixMs': occurredAtUnixMs,
            'eventType': interactionEventType,
            'gameId': gameId,
            'sequenceNo': sequenceNo,
            'gameRunId': gameRunId,
            'interactionType': interactionType,
            'actionOutcome': actionOutcome,
            'targetId': targetId,
            'targetName': targetName,
            'targetInstanceId': targetInstanceId,
            'targetCategory': targetCategory,
            'targetAppearedAtUtc': targetAppearedAtUtc,
            'targetAppearedAtUnixMs': targetAppearedAtUnixMs,
            'targetAppearedAtElapsedSec': targetAppearedAtElapsedSec,
            'responseSec': responseSec,
            'inputHand': inputHand,
            'inputSource': inputSource,
            'inputValue': inputValue,
            'sourceComponent': sourceComponent,
            'reasonCode': reasonCode,
          },
        );

        if (canWriteTimeline) {
          final timelineEventId = 'interaction-$eventId';
          final eventAtUtc = occurredAt ?? batchCreatedAtUtc;
          batch.set(
            sessionRef.collection('events').doc(timelineEventId),
            {
              'sessionId': sessionId,
              'studentId': _studentId.trim(),
              'therapistId': _therapistId.trim(),
              'ownerKey': ownerKey,
              'sessionKey': sessionKey,
              'eventType': _interactionTimelineEventType,
              'timelineEventId': timelineEventId,
              'gameId': gameId,
              'details': _buildInteractionTimelineDetails(
                eventId: eventId,
                gameRunId: gameRunId,
                sequenceNo: sequenceNo,
                interactionEventType: interactionEventType,
                interactionType: interactionType,
                actionOutcome: actionOutcome,
                reasonCode: reasonCode,
                targetId: targetId,
                targetName: targetName,
                targetCategory: targetCategory,
                targetInstanceId: targetInstanceId,
                targetAppearedAtUtc: targetAppearedAtUtc,
                targetAppearedAtUnixMs: targetAppearedAtUnixMs,
                targetAppearedAtElapsedSec: targetAppearedAtElapsedSec,
                responseSec: responseSec,
                inputHand: inputHand,
                inputSource: inputSource,
                inputValue: inputValue,
                sourceComponent: sourceComponent,
              ),
              'source': 'quest_runtime',
              'eventAtUtc': eventAtUtc.toIso8601String(),
              'eventAtUnixMs': eventAtUtc.millisecondsSinceEpoch,
              'createdAtUtc': batchCreatedAtUtc.toIso8601String(),
            },
            SetOptions(merge: true),
          );
        }
      }

      await batch.commit();
    }

    if (kDebugMode) {
      debugPrint(
          '[GameData] Wrote ${interactions.length} interactions for gameRunId=$gameRunId');
    }
  }

  Future<void> _persistLateInteractionBatch({
    required String gameRunId,
    required _CompletedGameRunContext context,
    required List<Map<String, dynamic>> events,
  }) async {
    final sessionId = context.sessionId.trim();
    if (sessionId.isEmpty) {
      return;
    }

    final sessionRef = _firestore.collection('therapy_sessions').doc(sessionId);
    final interactionSummary = _summarizeInteractions(events);

    await _writeInteractionsBatch(
      sessionId: sessionId,
      gameRunId: gameRunId,
      interactions: events,
    );

    final runSnapshot =
        await sessionRef.collection('game_runs').doc(gameRunId).get();
    final runData = runSnapshot.data() ?? <String, dynamic>{};
    final summary = Map<String, dynamic>.from(
        runData['summary'] as Map? ?? const <String, dynamic>{});
    final existingInteractionCount = _asInt(summary['interactionCount']);
    final existingHitCount = _asInt(summary['hitCount']);
    final existingMissCount = _asInt(summary['missCount']);
    final resolvedInteractionCount = math.max(
      existingInteractionCount,
      interactionSummary.interactionCount,
    );
    final resolvedHitCount = math.max(
      existingHitCount,
      interactionSummary.hitCount,
    );
    final resolvedMissCount = math.max(
      existingMissCount,
      interactionSummary.missCount,
    );

    final interactionDelta =
        resolvedInteractionCount - existingInteractionCount;
    final hitDelta = resolvedHitCount - existingHitCount;
    final missDelta = resolvedMissCount - existingMissCount;
    final nowUtc = DateTime.now().toUtc();

    await sessionRef.collection('game_runs').doc(gameRunId).set({
      'updatedAtUtc': nowUtc.toIso8601String(),
      'updatedAtUnixMs': nowUtc.millisecondsSinceEpoch,
      'summary': {
        'interactionCount': resolvedInteractionCount,
        'hitCount': resolvedHitCount,
        'missCount': resolvedMissCount,
        'avgReactionTimeMs': summary['avgReactionTimeMs'],
      },
    }, SetOptions(merge: true));

    if (interactionDelta > 0 || hitDelta > 0 || missDelta > 0) {
      await sessionRef.set({
        'summary': {
          'totalInteractions': FieldValue.increment(interactionDelta),
          'totalHits': FieldValue.increment(hitDelta),
          'totalMisses': FieldValue.increment(missDelta),
        },
      }, SetOptions(merge: true));
    }

    print(
      '[GameData] ✅ late interactions reconciled: runId=$gameRunId '
      'total=${events.length} deltaInteractions=$interactionDelta '
      'deltaHits=$hitDelta deltaMisses=$missDelta',
    );

    try {
      await onJournalCheckpointCommitted?.call(sessionId);
    } catch (e) {
      print('[GameData] ❌ journal checkpoint callback failed: $e');
    }
  }

  Future<void> _appendTimeline({
    required String eventType,
    String gameId = '',
    String? occurredAtUtc,
    Map<String, dynamic> details = const {},
  }) async {
    if (_sessionId.isEmpty || _studentId.isEmpty || _therapistId.isEmpty) {
      return;
    }

    await SessionJournalService.appendSessionEvent(
      sessionId: _sessionId,
      studentId: _studentId,
      therapistId: _therapistId,
      eventType: eventType,
      gameId: gameId,
      source: 'quest_runtime',
      eventAtUtc: occurredAtUtc != null
          ? DateTime.tryParse(occurredAtUtc)?.toUtc()
          : null,
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
      final nowUtc = DateTime.now().toUtc();
      final startedAtUtc = _activeGameRunStartedAtUtc ?? nowUtc;
      await _firestore
          .collection('therapy_sessions')
          .doc(_sessionId)
          .collection('game_runs')
          .doc(runId)
          .set({
        'gameId': gameId ?? '',
        'startedAtUtc': startedAtUtc.toIso8601String(),
        'startedAtUnixMs': startedAtUtc.millisecondsSinceEpoch,
        'endedAtUtc': nowUtc.toIso8601String(),
        'endedAtUnixMs': nowUtc.millisecondsSinceEpoch,
        'finalState': 'interrupted',
        'durationSec': 0,
        'updatedAtUtc': nowUtc.toIso8601String(),
        'updatedAtUnixMs': nowUtc.millisecondsSinceEpoch,
        'summary': {
          'interactionCount': 0,
          'hitCount': 0,
          'missCount': 0,
          'avgReactionTimeMs': null,
        },
      }, SetOptions(merge: true));
      _markGameRunWritten(runId);
      _activeGameRunId = null;
      _activeGameId = null;
      _activeGameRunStartedAtUtc = null;
      print(
        '[GameData] ✅ interrupted game_run written (total=${_writtenGameRunIds.length})',
      );
      final sessionId = _sessionId.trim();
      if (sessionId.isNotEmpty) {
        try {
          await onJournalCheckpointCommitted?.call(sessionId);
        } catch (e) {
          print('[GameData] ❌ journal checkpoint callback failed: $e');
        }
      }
    } catch (e) {
      print('[GameData] ❌ interrupted game_run write failed: $e');
    }
  }

  /// Deletes the session document from Firestore if no game_runs were written.
  /// Safe to call after EndSession when therapist never played any game.
  Future<void> deleteSessionIfEmpty() async {
    if (_sessionId.isEmpty || _writtenGameRunIds.isNotEmpty) {
      return;
    }

    print(
        '[GameData] deleteSessionIfEmpty: deleting empty session $_sessionId');
    try {
      // Delete subcollection events (timeline) first, then the session doc.
      final sessionRef =
          _firestore.collection('therapy_sessions').doc(_sessionId);
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

  void _markGameRunWritten(String gameRunId) {
    final normalizedGameRunId = gameRunId.trim();
    if (normalizedGameRunId.isEmpty) {
      return;
    }
    _writtenGameRunIds.add(normalizedGameRunId);
  }

  DateTime _resolveOccurredAtUtc(String? occurredAtUtc) {
    final normalized = occurredAtUtc?.trim() ?? '';
    if (normalized.isNotEmpty) {
      final parsed = DateTime.tryParse(normalized)?.toUtc();
      if (parsed != null) {
        return parsed;
      }
    }
    return DateTime.now().toUtc();
  }

  DateTime? _tryParseUtc(String? value) {
    final normalized = value?.trim() ?? '';
    if (normalized.isEmpty) {
      return null;
    }
    return DateTime.tryParse(normalized)?.toUtc();
  }

  double? _asDoubleOrNull(dynamic value) {
    if (value == null) {
      return null;
    }
    if (value is double) {
      return value.isFinite ? value : null;
    }
    if (value is int) {
      return value.toDouble();
    }
    if (value is num) {
      final resolved = value.toDouble();
      return resolved.isFinite ? resolved : null;
    }
    if (value is String) {
      final parsed = double.tryParse(value.trim());
      return parsed != null && parsed.isFinite ? parsed : null;
    }
    return null;
  }

  double? _resolveInteractionResponseSec({
    required double? explicitResponseSec,
    required DateTime? occurredAtUtc,
    required DateTime? targetAppearedAtUtc,
  }) {
    if (explicitResponseSec != null && explicitResponseSec.isFinite) {
      return math.max(0, explicitResponseSec).toDouble();
    }
    if (occurredAtUtc == null || targetAppearedAtUtc == null) {
      return null;
    }
    final deltaMs = occurredAtUtc.millisecondsSinceEpoch -
        targetAppearedAtUtc.millisecondsSinceEpoch;
    return math.max(0, deltaMs) / 1000.0;
  }

  Map<String, dynamic> _buildInteractionTimelineDetails({
    required String eventId,
    required String gameRunId,
    required int sequenceNo,
    required String interactionEventType,
    required String interactionType,
    required String actionOutcome,
    required String reasonCode,
    required String targetId,
    required String targetName,
    required String targetCategory,
    required String targetInstanceId,
    required String targetAppearedAtUtc,
    required int targetAppearedAtUnixMs,
    required double? targetAppearedAtElapsedSec,
    required double? responseSec,
    required String inputHand,
    required String inputSource,
    required double? inputValue,
    required String sourceComponent,
  }) {
    final details = <String, dynamic>{
      'eventId': eventId,
      'gameRunId': gameRunId,
      'sequenceNo': sequenceNo,
      'interactionEventType': interactionEventType,
      'interactionType': interactionType,
      'actionOutcome': actionOutcome,
      'reasonCode': reasonCode,
      'targetId': targetId,
      'targetName': targetName,
      'targetCategory': targetCategory,
      'targetInstanceId': targetInstanceId,
      'inputHand': inputHand,
      'inputSource': inputSource,
      'sourceComponent': sourceComponent,
    };

    if (targetAppearedAtUtc.isNotEmpty) {
      details['targetAppearedAtUtc'] = targetAppearedAtUtc;
    }
    if (targetAppearedAtUnixMs > 0) {
      details['targetAppearedAtUnixMs'] = targetAppearedAtUnixMs;
    }
    if (targetAppearedAtElapsedSec != null) {
      details['targetAppearedAtElapsedSec'] = targetAppearedAtElapsedSec;
    }
    if (responseSec != null) {
      details['responseSec'] = responseSec;
    }
    if (inputValue != null) {
      details['inputValue'] = inputValue;
    }

    return details;
  }

  void _clearLiveTimelinePreview() {
    _liveTimelinePreviewById.clear();
    _publishLiveTimelinePreview();
  }

  void _publishLiveTimelinePreview() {
    if (_liveTimelinePreviewController.isClosed) {
      return;
    }
    _liveTimelinePreviewController.add(_sortedLiveTimelinePreviewEvents());
  }

  List<SessionTimelineEvent> _sortedLiveTimelinePreviewEvents() {
    final events = _liveTimelinePreviewById.values.toList(growable: false);
    events.sort((a, b) {
      final timestampCompare = b.eventAtUnixMs.compareTo(a.eventAtUnixMs);
      if (timestampCompare != 0) {
        return timestampCompare;
      }
      return b.eventId.compareTo(a.eventId);
    });
    return events;
  }

  _InteractionBatchSummary _summarizeInteractions(
    List<Map<String, dynamic>>? interactions,
  ) {
    if (interactions == null || interactions.isEmpty) {
      return const _InteractionBatchSummary(
        interactionCount: 0,
        hitCount: 0,
        missCount: 0,
      );
    }

    var hitCount = 0;
    var missCount = 0;
    for (final event in interactions) {
      final actionOutcome =
          (event['actionOutcome'] as String? ?? '').trim().toUpperCase();
      if (actionOutcome == 'CORRECT') {
        hitCount++;
      } else if (actionOutcome == 'INCORRECT') {
        missCount++;
      }
    }

    return _InteractionBatchSummary(
      interactionCount: interactions.length,
      hitCount: hitCount,
      missCount: missCount,
    );
  }

  int _asInt(dynamic value) {
    if (value is int) {
      return value;
    }
    if (value is num) {
      return value.toInt();
    }
    if (value is String) {
      return int.tryParse(value.trim()) ?? 0;
    }
    return 0;
  }
}

class _CompletedGameRunContext {
  final String sessionId;

  const _CompletedGameRunContext({
    required this.sessionId,
  });
}

class _InteractionBatchSummary {
  final int interactionCount;
  final int hitCount;
  final int missCount;

  const _InteractionBatchSummary({
    required this.interactionCount,
    required this.hitCount,
    required this.missCount,
  });
}
