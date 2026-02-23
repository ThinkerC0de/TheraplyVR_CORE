import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_controller/models/parent_progress_snapshot.dart';
import 'package:flutter_controller/models/session_ownership.dart';
import 'package:flutter_controller/models/therapy_session_record.dart';
import 'package:flutter_controller/services/firebase_service.dart';

class ParentProgressService {
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

  static Future<ParentProgressSnapshot> fetchSnapshot({
    required String studentId,
    required String therapistId,
    int limit = 40,
  }) async {
    final normalizedStudentId = studentId.trim();
    final normalizedTherapistId = therapistId.trim();
    if (normalizedStudentId.isEmpty || normalizedTherapistId.isEmpty) {
      return ParentProgressSnapshot.empty;
    }

    final normalizedOwnerKey = SessionOwnership.ownerKey(
      therapistId: normalizedTherapistId,
      studentId: normalizedStudentId,
    );
    if (normalizedOwnerKey.isEmpty) {
      return ParentProgressSnapshot.empty;
    }

    final resolvedLimit = limit <= 0 ? 1 : limit;
    QuerySnapshot<Map<String, dynamic>> snapshot;

    try {
      snapshot = await _sessionsCollection
          .where('ownerKey', isEqualTo: normalizedOwnerKey)
          .orderBy('updatedAtUnixMs', descending: true)
          .limit(resolvedLimit)
          .get();
    } catch (_) {
      snapshot = await _sessionsCollection
          .where('studentId', isEqualTo: normalizedStudentId)
          .limit(200)
          .get();
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
      return ParentProgressSnapshot.empty;
    }

    records.sort((a, b) {
      if (a.updatedAtUnixMs == b.updatedAtUnixMs) {
        return b.sessionId.compareTo(a.sessionId);
      }
      return b.updatedAtUnixMs.compareTo(a.updatedAtUnixMs);
    });

    if (records.length > resolvedLimit) {
      records.removeRange(resolvedLimit, records.length);
    }

    var terminalSessions = 0;
    var unfinishedSessions = 0;
    for (final record in records) {
      if (record.isTerminal) {
        terminalSessions += 1;
      } else if (record.requiresHandoffDecision) {
        unfinishedSessions += 1;
      }
    }

    final latest = records.first;
    return ParentProgressSnapshot(
      totalSessions: records.length,
      terminalSessions: terminalSessions,
      unfinishedSessions: unfinishedSessions,
      lastSessionId: latest.sessionId,
      lastGameId: latest.latestGameId,
      lastState: latest.stateWire,
      lastUpdatedAtUtc: latest.updatedAtUtc,
    );
  }
}
