import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_controller/models/session_fsm_contract.dart';
import 'package:flutter_controller/models/session_ownership.dart';
import 'package:flutter_controller/models/therapy_session_record.dart';
import 'package:flutter_controller/services/firebase_service.dart';

class SessionTimelineEvent {
  final String eventId;
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
          .where('ownerKey', isEqualTo: normalizedOwnerKey)
          .orderBy('updatedAtUnixMs', descending: true)
          .limit(1)
          .get();
    } catch (_) {
      // Fallback for environments without a ready index yet.
      snapshot = await _sessionsCollection
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

  static Future<void> appendSessionEvent({
    required String sessionId,
    required String studentId,
    required String therapistId,
    required String eventType,
    String gameId = '',
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
    final payloadDetails = details == null
        ? <String, dynamic>{}
        : Map<String, dynamic>.from(details);

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

    final eventsCollection = sessionRef.collection('events');

    await eventsCollection.add(<String, dynamic>{
      'sessionId': normalizedSessionId,
      'studentId': normalizedStudentId,
      'therapistId': normalizedTherapistId,
      'ownerKey': normalizedOwnerKey,
      'sessionKey': normalizedSessionKey,
      'eventType': normalizedEventType,
      'gameId': gameId.trim(),
      'details': payloadDetails,
      'source': _source,
      'eventAtUtc': nowUtc.toIso8601String(),
      'eventAtUnixMs': nowUtc.millisecondsSinceEpoch,
      'createdAtUtc': nowUtc.toIso8601String(),
    });
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
    return _sessionsCollection
        .doc(normalizedSessionId)
        .collection('events')
        .orderBy('eventAtUnixMs', descending: true)
        .limit(resolvedLimit)
        .snapshots()
        .map((snapshot) {
      return snapshot.docs
          .map(
            (doc) => SessionTimelineEvent.fromFirestore(
                eventId: doc.id, json: doc.data()),
          )
          .toList(growable: false);
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
    final snapshot = await _sessionsCollection
        .doc(normalizedSessionId)
        .collection('events')
        .orderBy('eventAtUnixMs', descending: true)
        .limit(resolvedLimit)
        .get();

    return snapshot.docs
        .map(
          (doc) => SessionTimelineEvent.fromFirestore(
              eventId: doc.id, json: doc.data()),
        )
        .toList(growable: false);
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
