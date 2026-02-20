import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:flutter_controller/models/session_fsm_contract.dart';
import 'package:flutter_controller/models/therapy_session_record.dart';
import 'package:flutter_controller/services/firebase_service.dart';

class SessionJournalService {
  static const String _source = 'mobile_controller';
  static final CollectionReference<Map<String, dynamic>> _sessionsCollection =
      FirebaseService.firestore.collection('therapy_sessions');

  static Future<TherapySessionRecord?> fetchLatestForStudent({
    required String studentId,
  }) async {
    final normalizedStudentId = studentId.trim();
    if (normalizedStudentId.isEmpty) {
      return null;
    }

    QuerySnapshot<Map<String, dynamic>> snapshot;
    try {
      snapshot = await _sessionsCollection
          .where('studentId', isEqualTo: normalizedStudentId)
          .orderBy('updatedAtUnixMs', descending: true)
          .limit(1)
          .get();
    } catch (_) {
      // Fallback for environments without a ready index yet.
      snapshot = await _sessionsCollection
          .where('studentId', isEqualTo: normalizedStudentId)
          .limit(100)
          .get();
    }

    if (snapshot.docs.isEmpty) {
      return null;
    }

    final records = snapshot.docs
        .map((doc) => TherapySessionRecord.fromFirestore(doc.data()))
        .where((record) => record.sessionId.isNotEmpty)
        .toList();
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
    final existingStartedAt = _readStartedAt(existingData);
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

    final nowUtc = DateTime.now().toUtc();
    final payloadDetails = details == null
        ? <String, dynamic>{}
        : Map<String, dynamic>.from(details);

    final eventsCollection =
        _sessionsCollection.doc(normalizedSessionId).collection('events');

    await eventsCollection.add(<String, dynamic>{
      'sessionId': normalizedSessionId,
      'studentId': normalizedStudentId,
      'therapistId': normalizedTherapistId,
      'eventType': normalizedEventType,
      'gameId': gameId.trim(),
      'details': payloadDetails,
      'source': _source,
      'eventAtUtc': nowUtc.toIso8601String(),
      'eventAtUnixMs': nowUtc.millisecondsSinceEpoch,
      'createdAtUtc': nowUtc.toIso8601String(),
    });
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
