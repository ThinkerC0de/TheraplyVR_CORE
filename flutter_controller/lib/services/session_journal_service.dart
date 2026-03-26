import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_controller/models/game_run_record.dart';
import 'package:flutter_controller/models/session_fsm_contract.dart';
import 'package:flutter_controller/models/session_ownership.dart';
import 'package:flutter_controller/models/therapy_session_record.dart';
import 'package:flutter_controller/services/firebase_service.dart';

class SessionTimelineEvent {
  final String eventId;
  final String timelineEventId;
  final String sessionId;
  final String studentId;
  final String therapistId;
  final String ownerKey;
  final String sessionKey;
  final String eventType;
  final String gameId;
  final String source;
  final DateTime? eventAtUtc;
  final int eventAtUnixMs;
  final Map<String, dynamic> details;

  const SessionTimelineEvent({
    required this.eventId,
    required this.timelineEventId,
    required this.sessionId,
    required this.studentId,
    required this.therapistId,
    required this.ownerKey,
    required this.sessionKey,
    required this.eventType,
    required this.gameId,
    required this.source,
    required this.eventAtUtc,
    required this.eventAtUnixMs,
    required this.details,
  });

  bool get isTherapistNote =>
      eventType == SessionJournalService.therapistTimelineNoteEventType;

  String get noteText {
    final rawText = details['noteText'] ?? details['text'] ?? details['note'];
    return rawText?.toString().trim() ?? '';
  }

  factory SessionTimelineEvent.fromFirestore({
    required String eventId,
    required Map<String, dynamic> json,
  }) {
    final details = json['details'] is Map<String, dynamic>
        ? Map<String, dynamic>.from(json['details'] as Map<String, dynamic>)
        : <String, dynamic>{};
    final eventAtUtc = _parseDateTime(json['eventAtUtc']) ??
        _parseDateTimeFromUnixMs(json['eventAtUnixMs']);
    final eventAtUnixMs = SessionJournalService._asInt(json['eventAtUnixMs']);

    return SessionTimelineEvent(
      eventId: eventId.trim(),
      timelineEventId:
          (json['timelineEventId'] as String? ?? '').trim().isNotEmpty
              ? (json['timelineEventId'] as String).trim()
              : eventId.trim(),
      sessionId: (json['sessionId'] as String? ?? '').trim(),
      studentId: (json['studentId'] as String? ?? '').trim(),
      therapistId: (json['therapistId'] as String? ?? '').trim(),
      ownerKey: (json['ownerKey'] as String? ?? '').trim(),
      sessionKey: (json['sessionKey'] as String? ?? '').trim(),
      eventType: (json['eventType'] as String? ?? '').trim(),
      gameId: (json['gameId'] as String? ?? '').trim(),
      source: (json['source'] as String? ?? '').trim(),
      eventAtUtc: eventAtUtc,
      eventAtUnixMs: eventAtUnixMs,
      details: details,
    );
  }

  static DateTime? _parseDateTime(dynamic value) {
    if (value is String && value.isNotEmpty) {
      return DateTime.tryParse(value)?.toUtc();
    }
    return null;
  }

  static DateTime? _parseDateTimeFromUnixMs(dynamic value) {
    final unixMs = SessionJournalService._asInt(value);
    if (unixMs <= 0) {
      return null;
    }
    return DateTime.fromMillisecondsSinceEpoch(unixMs, isUtc: true);
  }
}

class SessionJournalService {
  static const String _source = 'mobile_controller';
  static const String therapistTimelineNoteEventType =
      'THERAPIST_TIMELINE_NOTE';
  static FirebaseFirestore? _firestoreOverride;
  static FirebaseFirestore get _firestore =>
      _firestoreOverride ?? FirebaseService.firestore;
  static CollectionReference<Map<String, dynamic>> get _sessionsCollection =>
      _firestore.collection('therapy_sessions');

  @visibleForTesting
  static void setFirestoreInstanceForTesting(FirebaseFirestore firestore) {
    _firestoreOverride = firestore;
  }

  @visibleForTesting
  static void clearTestingOverrides() {
    _firestoreOverride = null;
  }

  static Future<TherapySessionRecord?> fetchLatestForStudent({
    required String studentId,
    required String therapistId,
  }) async {
    final normalizedStudentId = studentId.trim();
    final normalizedTherapistId = therapistId.trim();
    if (normalizedStudentId.isEmpty || normalizedTherapistId.isEmpty) {
      return null;
    }
    final normalizedOwnerKey = SessionOwnership.ownerKey(
      therapistId: normalizedTherapistId,
      studentId: normalizedStudentId,
    );
    if (normalizedOwnerKey.isEmpty) {
      return null;
    }

    QuerySnapshot<Map<String, dynamic>> snapshot;
    try {
      snapshot = await _sessionsCollection
          .where('therapistId', isEqualTo: normalizedTherapistId)
          .where('studentId', isEqualTo: normalizedStudentId)
          .orderBy('updatedAtUnixMs', descending: true)
          .limit(1)
          .get();
    } catch (_) {
      // Fallback for environments without a composite index yet.
      snapshot = await _sessionsCollection
          .where('therapistId', isEqualTo: normalizedTherapistId)
          .where('studentId', isEqualTo: normalizedStudentId)
          .limit(200)
          .get();
    }

    if (snapshot.docs.isEmpty) {
      return null;
    }

    final records = snapshot.docs
        .map((doc) => TherapySessionRecord.fromFirestore(doc.data()))
        .where((record) => record.sessionId.isNotEmpty)
        .where((record) {
      if (record.ownerKey.isNotEmpty) {
        return record.ownerKey == normalizedOwnerKey;
      }
      return record.studentId == normalizedStudentId &&
          record.therapistId == normalizedTherapistId;
    }).toList();
    if (records.isEmpty) {
      return null;
    }

    records.sort((a, b) {
      if (a.updatedAtUnixMs == b.updatedAtUnixMs) {
        return b.sessionId.compareTo(a.sessionId);
      }
      return b.updatedAtUnixMs.compareTo(a.updatedAtUnixMs);
    });

    return records.first;
  }

  static Future<TherapySessionRecord?> fetchLatestUnfinishedForTherapist({
    required String therapistId,
  }) async {
    final normalizedTherapistId = therapistId.trim();
    if (normalizedTherapistId.isEmpty) {
      return null;
    }

    QuerySnapshot<Map<String, dynamic>> snapshot;
    try {
      snapshot = await _sessionsCollection
          .where('therapistId', isEqualTo: normalizedTherapistId)
          .where('unfinished', isEqualTo: true)
          .orderBy('updatedAtUnixMs', descending: true)
          .limit(1)
          .get();
    } catch (_) {
      // Fallback for environments without a ready index yet.
      snapshot = await _sessionsCollection
          .where('therapistId', isEqualTo: normalizedTherapistId)
          .limit(200)
          .get();
    }

    if (snapshot.docs.isEmpty) {
      return null;
    }

    final records = snapshot.docs
        .map((doc) => TherapySessionRecord.fromFirestore(doc.data()))
        .where((record) => record.sessionId.isNotEmpty)
        .where((record) => record.therapistId == normalizedTherapistId)
        .where((record) => record.requiresHandoffDecision)
        .toList(growable: false);

    if (records.isEmpty) {
      return null;
    }

    records.sort((a, b) {
      if (a.updatedAtUnixMs == b.updatedAtUnixMs) {
        return b.sessionId.compareTo(a.sessionId);
      }
      return b.updatedAtUnixMs.compareTo(a.updatedAtUnixMs);
    });

    return records.first;
  }

  static Future<void> upsertSessionState({
    required String sessionId,
    required String studentId,
    required String therapistId,
    required SessionLifecycleState state,
    String latestGameId = '',
    String reasonCode = '',
    Map<String, dynamic>? metadata,
  }) async {
    final normalizedSessionId = sessionId.trim();
    final normalizedStudentId = studentId.trim();
    final normalizedTherapistId = therapistId.trim();
    if (normalizedSessionId.isEmpty ||
        normalizedStudentId.isEmpty ||
        normalizedTherapistId.isEmpty) {
      return;
    }
    final normalizedOwnerKey = SessionOwnership.ownerKey(
      therapistId: normalizedTherapistId,
      studentId: normalizedStudentId,
    );
    final normalizedSessionKey = SessionOwnership.sessionKey(
      therapistId: normalizedTherapistId,
      studentId: normalizedStudentId,
      sessionId: normalizedSessionId,
    );
    if (normalizedOwnerKey.isEmpty || normalizedSessionKey.isEmpty) {
      return;
    }

    final nowUtc = DateTime.now().toUtc();
    final nowMs = nowUtc.millisecondsSinceEpoch;
    final isTerminal = _isTerminalState(state);
    final unfinished = _isUnfinishedState(state);
    final payloadMetadata = metadata == null
        ? <String, dynamic>{}
        : Map<String, dynamic>.from(metadata);

    final docRef = _sessionsCollection.doc(normalizedSessionId);
    final existingSnapshot = await docRef.get();
    final existingData = existingSnapshot.data();
    final existingOwnerKey =
        (existingData?['ownerKey'] as String? ?? '').trim();
    if (existingOwnerKey.isNotEmpty && existingOwnerKey != normalizedOwnerKey) {
      throw StateError(
        'Session ownership conflict for $normalizedSessionId: '
        'stored=$existingOwnerKey incoming=$normalizedOwnerKey',
      );
    }

    final existingStudentId =
        (existingData?['studentId'] as String? ?? '').trim();
    final existingTherapistId =
        (existingData?['therapistId'] as String? ?? '').trim();
    if (existingOwnerKey.isEmpty &&
        existingStudentId.isNotEmpty &&
        existingTherapistId.isNotEmpty) {
      final resolvedLegacyOwnerKey = SessionOwnership.ownerKey(
        therapistId: existingTherapistId,
        studentId: existingStudentId,
      );
      if (resolvedLegacyOwnerKey.isNotEmpty &&
          resolvedLegacyOwnerKey != normalizedOwnerKey) {
        throw StateError(
          'Session ownership conflict for $normalizedSessionId: '
          'stored=$resolvedLegacyOwnerKey incoming=$normalizedOwnerKey',
        );
      }
    }

    final existingStartedAt = _readStartedAt(existingData);
    final existingInterruptedAt = _readInterruptedAt(existingData);
    final existingState = SessionLifecycleState.tryParse(
      (existingData?['state'] as String? ?? '').trim(),
    );
    final startedAt = existingStartedAt ?? nowUtc;
    final createdAtMs = existingData != null
        ? _asInt(existingData['createdAtUnixMs']) == 0
            ? nowMs
            : _asInt(existingData['createdAtUnixMs'])
        : nowMs;

    final patch = <String, dynamic>{
      'sessionId': normalizedSessionId,
      'studentId': normalizedStudentId,
      'therapistId': normalizedTherapistId,
      'ownerKey': normalizedOwnerKey,
      'sessionKey': normalizedSessionKey,
      'state': state.wireValue,
      'unfinished': unfinished,
      'isTerminal': isTerminal,
      'latestGameId': latestGameId.trim(),
      'reasonCode': reasonCode.trim(),
      'source': _source,
      'metadata': payloadMetadata,
      'updatedAtUtc': nowUtc.toIso8601String(),
      'updatedAtUnixMs': nowMs,
      'createdAtUtc':
          DateTime.fromMillisecondsSinceEpoch(createdAtMs, isUtc: true)
              .toIso8601String(),
      'createdAtUnixMs': createdAtMs,
      'startedAtUtc': startedAt.toIso8601String(),
      'startedAtUnixMs': startedAt.millisecondsSinceEpoch,
    };

    if (state == SessionLifecycleState.interrupted) {
      final shouldResetInterruptedAt =
          existingState != SessionLifecycleState.interrupted;
      final interruptedAt =
          shouldResetInterruptedAt ? nowUtc : (existingInterruptedAt ?? nowUtc);
      patch['interruptedAtUtc'] = interruptedAt.toIso8601String();
      patch['interruptedAtUnixMs'] = interruptedAt.millisecondsSinceEpoch;
    }

    if (isTerminal) {
      patch['endedAtUtc'] = nowUtc.toIso8601String();
      patch['endedAtUnixMs'] = nowMs;
    }

    await docRef.set(patch, SetOptions(merge: true));
  }

  static Future<void> markSessionCompletedByTherapist({
    required String sessionId,
    required String studentId,
    required String therapistId,
    String latestGameId = '',
    String reasonCode = 'THERAPIST_CONFIRMED_END',
    Map<String, dynamic>? metadata,
  }) {
    return upsertSessionState(
      sessionId: sessionId,
      studentId: studentId,
      therapistId: therapistId,
      state: SessionLifecycleState.completed,
      latestGameId: latestGameId,
      reasonCode: reasonCode,
      metadata: metadata,
    );
  }

  static Future<void> markSessionAbortedByTherapist({
    required String sessionId,
    required String studentId,
    required String therapistId,
    String latestGameId = '',
    String reasonCode = 'THERAPIST_ABORTED_SESSION',
    Map<String, dynamic>? metadata,
  }) {
    return upsertSessionState(
      sessionId: sessionId,
      studentId: studentId,
      therapistId: therapistId,
      state: SessionLifecycleState.abortedByTherapist,
      latestGameId: latestGameId,
      reasonCode: reasonCode,
      metadata: metadata,
    );
  }

  static Future<void> appendSessionEvent({
    required String sessionId,
    required String studentId,
    required String therapistId,
    required String eventType,
    String gameId = '',
    String source = _source,
    String? timelineEventId,
    DateTime? eventAtUtc,
    Map<String, dynamic>? details,
  }) async {
    final normalizedSessionId = sessionId.trim();
    final normalizedStudentId = studentId.trim();
    final normalizedTherapistId = therapistId.trim();
    final normalizedEventType = eventType.trim();
    if (normalizedSessionId.isEmpty ||
        normalizedStudentId.isEmpty ||
        normalizedTherapistId.isEmpty ||
        normalizedEventType.isEmpty) {
      return;
    }
    final normalizedOwnerKey = SessionOwnership.ownerKey(
      therapistId: normalizedTherapistId,
      studentId: normalizedStudentId,
    );
    final normalizedSessionKey = SessionOwnership.sessionKey(
      therapistId: normalizedTherapistId,
      studentId: normalizedStudentId,
      sessionId: normalizedSessionId,
    );
    if (normalizedOwnerKey.isEmpty || normalizedSessionKey.isEmpty) {
      return;
    }

    final nowUtc = DateTime.now().toUtc();
    final resolvedEventAtUtc = (eventAtUtc ?? nowUtc).toUtc();
    final normalizedSource = source.trim().isEmpty ? _source : source.trim();
    final payloadDetails = details == null
        ? <String, dynamic>{}
        : Map<String, dynamic>.from(details);
    final providedTimelineEventId = timelineEventId?.trim() ?? '';
    final resolvedTimelineEventId = providedTimelineEventId.isNotEmpty
        ? providedTimelineEventId
        : buildTimelineEventId(
            sessionId: normalizedSessionId,
            eventType: normalizedEventType,
            source: normalizedSource,
            eventAtUnixMs: resolvedEventAtUtc.millisecondsSinceEpoch,
            details: payloadDetails,
            discriminator: 'mobile',
          );

    final sessionRef = _sessionsCollection.doc(normalizedSessionId);
    final existingSessionSnapshot = await sessionRef.get();
    final existingSessionData = existingSessionSnapshot.data();
    final existingOwnerKey =
        (existingSessionData?['ownerKey'] as String? ?? '').trim();
    if (existingOwnerKey.isNotEmpty && existingOwnerKey != normalizedOwnerKey) {
      throw StateError(
        'Session ownership conflict for event append $normalizedSessionId: '
        'stored=$existingOwnerKey incoming=$normalizedOwnerKey',
      );
    }
    if (existingSessionData == null) {
      await sessionRef.set(
        <String, dynamic>{
          'sessionId': normalizedSessionId,
          'studentId': normalizedStudentId,
          'therapistId': normalizedTherapistId,
          'ownerKey': normalizedOwnerKey,
          'sessionKey': normalizedSessionKey,
          'state': SessionLifecycleState.created.wireValue,
          'unfinished': true,
          'isTerminal': false,
          'latestGameId': gameId.trim(),
          'reasonCode': 'TIMELINE_EVENT_SEEDED',
          'source': normalizedSource,
          'metadata': <String, dynamic>{
            'seededByTimeline': true,
          },
          'createdAtUtc': nowUtc.toIso8601String(),
          'createdAtUnixMs': nowUtc.millisecondsSinceEpoch,
          'updatedAtUtc': nowUtc.toIso8601String(),
          'updatedAtUnixMs': nowUtc.millisecondsSinceEpoch,
          'startedAtUtc': resolvedEventAtUtc.toIso8601String(),
          'startedAtUnixMs': resolvedEventAtUtc.millisecondsSinceEpoch,
        },
        SetOptions(merge: true),
      );
    }

    final eventsCollection = sessionRef.collection('events');
    final payload = <String, dynamic>{
      'sessionId': normalizedSessionId,
      'studentId': normalizedStudentId,
      'therapistId': normalizedTherapistId,
      'ownerKey': normalizedOwnerKey,
      'sessionKey': normalizedSessionKey,
      'eventType': normalizedEventType,
      'timelineEventId': resolvedTimelineEventId,
      'gameId': gameId.trim(),
      'details': payloadDetails,
      'source': normalizedSource,
      'eventAtUtc': resolvedEventAtUtc.toIso8601String(),
      'eventAtUnixMs': resolvedEventAtUtc.millisecondsSinceEpoch,
      'createdAtUtc': nowUtc.toIso8601String(),
    };

    if (providedTimelineEventId.isNotEmpty) {
      await eventsCollection
          .doc(resolvedTimelineEventId)
          .set(payload, SetOptions(merge: true));
      return;
    }

    await eventsCollection.add(payload);
  }

  static Future<void> appendTherapistTimelineNote({
    required String sessionId,
    required String studentId,
    required String therapistId,
    required String noteText,
    String gameId = '',
    bool fromQuickTemplate = false,
  }) async {
    final normalizedNoteText = noteText.trim();
    if (normalizedNoteText.isEmpty) {
      return;
    }

    await appendSessionEvent(
      sessionId: sessionId,
      studentId: studentId,
      therapistId: therapistId,
      eventType: therapistTimelineNoteEventType,
      gameId: gameId,
      details: <String, dynamic>{
        'noteText': normalizedNoteText,
        'noteSource': fromQuickTemplate ? 'quick_template' : 'manual_input',
      },
    );
  }

  static Stream<List<SessionTimelineEvent>> watchSessionTimeline({
    required String sessionId,
    int limit = 60,
  }) {
    final normalizedSessionId = sessionId.trim();
    if (normalizedSessionId.isEmpty) {
      return Stream<List<SessionTimelineEvent>>.value(
        const <SessionTimelineEvent>[],
      );
    }

    final resolvedLimit = limit <= 0 ? 1 : limit;
    final resolvedFetchLimit = resolvedLimit * 4;
    return _sessionsCollection
        .doc(normalizedSessionId)
        .collection('events')
        .orderBy('eventAtUnixMs', descending: true)
        .limit(resolvedFetchLimit)
        .snapshots()
        .map((snapshot) {
      final events = snapshot.docs
          .map(
            (doc) => SessionTimelineEvent.fromFirestore(
                eventId: doc.id, json: doc.data()),
          )
          .toList(growable: false);
      return _mergeAndSortTimeline(events, limit: resolvedLimit);
    });
  }

  static Future<List<SessionTimelineEvent>> fetchSessionTimeline({
    required String sessionId,
    int limit = 60,
  }) async {
    final normalizedSessionId = sessionId.trim();
    if (normalizedSessionId.isEmpty) {
      return const <SessionTimelineEvent>[];
    }

    final resolvedLimit = limit <= 0 ? 1 : limit;
    final resolvedFetchLimit = resolvedLimit * 4;
    final snapshot = await _sessionsCollection
        .doc(normalizedSessionId)
        .collection('events')
        .orderBy('eventAtUnixMs', descending: true)
        .limit(resolvedFetchLimit)
        .get();

    final events = snapshot.docs
        .map(
          (doc) => SessionTimelineEvent.fromFirestore(
              eventId: doc.id, json: doc.data()),
        )
        .toList(growable: false);
    return _mergeAndSortTimeline(events, limit: resolvedLimit);
  }

  static Stream<List<GameRunRecord>> watchGameRuns({
    required String sessionId,
  }) {
    final normalizedSessionId = sessionId.trim();
    if (normalizedSessionId.isEmpty) {
      return const Stream.empty();
    }
    return _sessionsCollection
        .doc(normalizedSessionId)
        .collection('game_runs')
        .snapshots()
        .map((snap) {
      final runs = snap.docs
          .map((doc) => GameRunRecord.fromFirestore(doc.id, doc.data()))
          .toList(growable: false);
      runs.sort((a, b) {
        final startCompare = a.sortAnchorUnixMs.compareTo(b.sortAnchorUnixMs);
        if (startCompare != 0) {
          return startCompare;
        }

        final endCompare = a.endedAtUnixMs.compareTo(b.endedAtUnixMs);
        if (endCompare != 0) {
          return endCompare;
        }

        return a.gameRunId.compareTo(b.gameRunId);
      });
      return runs;
    });
  }

  static Future<List<GameRunRecord>> fetchGameRuns({
    required String sessionId,
  }) async {
    final normalizedSessionId = sessionId.trim();
    if (normalizedSessionId.isEmpty) {
      return const <GameRunRecord>[];
    }

    final snapshot = await _sessionsCollection
        .doc(normalizedSessionId)
        .collection('game_runs')
        .get();

    final runs = snapshot.docs
        .map((doc) => GameRunRecord.fromFirestore(doc.id, doc.data()))
        .toList(growable: false);
    runs.sort((a, b) {
      final startCompare = a.sortAnchorUnixMs.compareTo(b.sortAnchorUnixMs);
      if (startCompare != 0) {
        return startCompare;
      }

      final endCompare = a.endedAtUnixMs.compareTo(b.endedAtUnixMs);
      if (endCompare != 0) {
        return endCompare;
      }

      return a.gameRunId.compareTo(b.gameRunId);
    });
    return runs;
  }

  static String buildTimelineEventId({
    required String sessionId,
    required String eventType,
    required String source,
    required int eventAtUnixMs,
    Map<String, dynamic>? details,
    String discriminator = '',
  }) {
    final normalizedSessionId = sessionId.trim();
    final normalizedEventType = eventType.trim().toUpperCase();
    final normalizedSource = source.trim().toLowerCase();
    final normalizedDiscriminator = discriminator.trim().toLowerCase();
    final detailsSignature = _stableMapSignature(details);
    final input = [
      normalizedSessionId,
      normalizedEventType,
      normalizedSource,
      eventAtUnixMs.toString(),
      normalizedDiscriminator,
      detailsSignature,
    ].join('|');
    final hash = _fnv1a64(input);
    return 'tl-$hash';
  }

  static List<SessionTimelineEvent> _mergeAndSortTimeline(
    List<SessionTimelineEvent> events, {
    required int limit,
  }) {
    final dedupedByTimelineEventId = <String, SessionTimelineEvent>{};
    for (final event in events) {
      final key = event.timelineEventId.trim().isNotEmpty
          ? event.timelineEventId.trim()
          : event.eventId.trim();
      if (key.isEmpty) {
        continue;
      }

      final existing = dedupedByTimelineEventId[key];
      if (existing == null) {
        dedupedByTimelineEventId[key] = event;
        continue;
      }

      final existingTimestamp =
          existing.eventAtUnixMs > 0 ? existing.eventAtUnixMs : 0;
      final candidateTimestamp =
          event.eventAtUnixMs > 0 ? event.eventAtUnixMs : 0;
      if (candidateTimestamp > existingTimestamp) {
        dedupedByTimelineEventId[key] = event;
        continue;
      }

      if (candidateTimestamp == existingTimestamp &&
          event.eventId.compareTo(existing.eventId) > 0) {
        dedupedByTimelineEventId[key] = event;
      }
    }

    final merged = dedupedByTimelineEventId.values.toList(growable: false);
    merged.sort((a, b) {
      final timestampCompare = b.eventAtUnixMs.compareTo(a.eventAtUnixMs);
      if (timestampCompare != 0) {
        return timestampCompare;
      }
      return b.eventId.compareTo(a.eventId);
    });

    if (merged.length <= limit) {
      return merged;
    }
    return merged.sublist(0, limit);
  }

  static String _stableMapSignature(Map<String, dynamic>? details) {
    if (details == null || details.isEmpty) {
      return '';
    }

    final keys = details.keys.toList(growable: false)..sort();
    final parts = <String>[];
    for (final key in keys) {
      final normalizedKey = key.trim();
      final value = details[key];
      parts.add('$normalizedKey=${_stableValueSignature(value)}');
    }
    return parts.join(';');
  }

  static String _stableValueSignature(dynamic value) {
    if (value == null) {
      return 'null';
    }

    if (value is Map) {
      final normalized = <String, dynamic>{};
      for (final entry in value.entries) {
        final key = entry.key?.toString() ?? '';
        normalized[key] = entry.value;
      }
      return '{${_stableMapSignature(normalized)}}';
    }

    if (value is List) {
      final parts = value.map((entry) => _stableValueSignature(entry));
      return '[${parts.join(',')}]';
    }

    return value.toString().trim();
  }

  static String _fnv1a64(String input) {
    const int fnvOffset = 0xcbf29ce484222325;
    const int fnvPrime = 0x100000001b3;
    var hash = fnvOffset;
    final codeUnits = input.codeUnits;
    for (final codeUnit in codeUnits) {
      hash ^= codeUnit;
      hash = (hash * fnvPrime) & 0xFFFFFFFFFFFFFFFF;
    }
    return hash.toRadixString(16).padLeft(16, '0');
  }

  static bool _isTerminalState(SessionLifecycleState state) {
    switch (state) {
      case SessionLifecycleState.completed:
      case SessionLifecycleState.abortedByTherapist:
      case SessionLifecycleState.failedTechnical:
        return true;
      case SessionLifecycleState.created:
      case SessionLifecycleState.inProgress:
      case SessionLifecycleState.paused:
      case SessionLifecycleState.interrupted:
        return false;
    }
  }

  static bool _isUnfinishedState(SessionLifecycleState state) {
    switch (state) {
      case SessionLifecycleState.inProgress:
      case SessionLifecycleState.paused:
      case SessionLifecycleState.interrupted:
        return true;
      case SessionLifecycleState.created:
      case SessionLifecycleState.completed:
      case SessionLifecycleState.abortedByTherapist:
      case SessionLifecycleState.failedTechnical:
        return false;
    }
  }

  static DateTime? _readStartedAt(Map<String, dynamic>? data) {
    if (data == null) {
      return null;
    }
    final startedAtUtcRaw = data['startedAtUtc'];
    if (startedAtUtcRaw is String && startedAtUtcRaw.isNotEmpty) {
      final parsed = DateTime.tryParse(startedAtUtcRaw)?.toUtc();
      if (parsed != null) {
        return parsed;
      }
    }

    final startedAtMs = _asInt(data['startedAtUnixMs']);
    if (startedAtMs > 0) {
      return DateTime.fromMillisecondsSinceEpoch(startedAtMs, isUtc: true);
    }
    return null;
  }

  static DateTime? _readInterruptedAt(Map<String, dynamic>? data) {
    if (data == null) {
      return null;
    }

    final interruptedAtUtcRaw = data['interruptedAtUtc'];
    if (interruptedAtUtcRaw is String && interruptedAtUtcRaw.isNotEmpty) {
      final parsed = DateTime.tryParse(interruptedAtUtcRaw)?.toUtc();
      if (parsed != null) {
        return parsed;
      }
    }

    final interruptedAtMs = _asInt(data['interruptedAtUnixMs']);
    if (interruptedAtMs > 0) {
      return DateTime.fromMillisecondsSinceEpoch(interruptedAtMs, isUtc: true);
    }
    return null;
  }

  static int _asInt(dynamic value) {
    if (value is int) {
      return value;
    }
    if (value is num) {
      return value.toInt();
    }
    if (value is String) {
      return int.tryParse(value) ?? 0;
    }
    return 0;
  }
}
