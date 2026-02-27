import 'package:fake_cloud_firestore/fake_cloud_firestore.dart';
import 'package:flutter_controller/models/session_fsm_contract.dart';
import 'package:flutter_controller/models/session_ownership.dart';
import 'package:flutter_controller/services/session_journal_service.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('SessionJournalService timeline', () {
    late FakeFirebaseFirestore firestore;

    setUp(() {
      firestore = FakeFirebaseFirestore();
      SessionJournalService.setFirestoreInstanceForTesting(firestore);
    });

    tearDown(() {
      SessionJournalService.clearTestingOverrides();
    });

    test('appendTherapistTimelineNote writes note payload', () async {
      await SessionJournalService.upsertSessionState(
        sessionId: 'session-a',
        studentId: 'student-a',
        therapistId: 'therapist-a',
        state: SessionLifecycleState.created,
        latestGameId: 'demo_cube_clicker',
      );

      await SessionJournalService.appendTherapistTimelineNote(
        sessionId: 'session-a',
        studentId: 'student-a',
        therapistId: 'therapist-a',
        noteText: 'Need hydration break',
        gameId: 'demo_cube_clicker',
        fromQuickTemplate: true,
      );

      final snapshot = await firestore
          .collection('therapy_sessions')
          .doc('session-a')
          .collection('events')
          .get();
      expect(snapshot.docs, hasLength(1));

      final payload = snapshot.docs.first.data();
      expect(
        payload['eventType'],
        SessionJournalService.therapistTimelineNoteEventType,
      );
      expect(payload['gameId'], 'demo_cube_clicker');

      final details =
          Map<String, dynamic>.from(payload['details'] as Map<String, dynamic>);
      expect(details['noteText'], 'Need hydration break');
      expect(details['noteSource'], 'quick_template');
    });

    test('fetchSessionTimeline returns latest events first and applies limit',
        () async {
      final ownerKey = SessionOwnership.ownerKey(
        therapistId: 'therapist-b',
        studentId: 'student-b',
      );
      final sessionKey = SessionOwnership.sessionKey(
        therapistId: 'therapist-b',
        studentId: 'student-b',
        sessionId: 'session-b',
      );

      final eventsRef = firestore
          .collection('therapy_sessions')
          .doc('session-b')
          .collection('events');
      await eventsRef.doc('evt-1').set(
            _timelinePayload(
              sessionId: 'session-b',
              studentId: 'student-b',
              therapistId: 'therapist-b',
              ownerKey: ownerKey,
              sessionKey: sessionKey,
              eventType: 'GAME_STARTED',
              eventAtUnixMs: 1000,
            ),
          );
      await eventsRef.doc('evt-2').set(
            _timelinePayload(
              sessionId: 'session-b',
              studentId: 'student-b',
              therapistId: 'therapist-b',
              ownerKey: ownerKey,
              sessionKey: sessionKey,
              eventType: 'GAME_PAUSED',
              eventAtUnixMs: 3000,
            ),
          );
      await eventsRef.doc('evt-3').set(
            _timelinePayload(
              sessionId: 'session-b',
              studentId: 'student-b',
              therapistId: 'therapist-b',
              ownerKey: ownerKey,
              sessionKey: sessionKey,
              eventType: 'GAME_RESUMED',
              eventAtUnixMs: 2000,
            ),
          );

      final timeline = await SessionJournalService.fetchSessionTimeline(
        sessionId: 'session-b',
        limit: 2,
      );

      expect(timeline, hasLength(2));
      expect(timeline[0].eventType, 'GAME_PAUSED');
      expect(timeline[1].eventType, 'GAME_RESUMED');
    });

    test('fetchSessionTimeline deduplicates entries by timelineEventId',
        () async {
      final ownerKey = SessionOwnership.ownerKey(
        therapistId: 'therapist-z',
        studentId: 'student-z',
      );
      final sessionKey = SessionOwnership.sessionKey(
        therapistId: 'therapist-z',
        studentId: 'student-z',
        sessionId: 'session-z',
      );

      final eventsRef = firestore
          .collection('therapy_sessions')
          .doc('session-z')
          .collection('events');
      await eventsRef.doc('evt-older').set(
            _timelinePayload(
              sessionId: 'session-z',
              studentId: 'student-z',
              therapistId: 'therapist-z',
              ownerKey: ownerKey,
              sessionKey: sessionKey,
              eventType: 'RECONNECT_ATTEMPT',
              eventAtUnixMs: 1000,
              timelineEventId: 'tl-fixed-1',
            ),
          );
      await eventsRef.doc('evt-newer').set(
            _timelinePayload(
              sessionId: 'session-z',
              studentId: 'student-z',
              therapistId: 'therapist-z',
              ownerKey: ownerKey,
              sessionKey: sessionKey,
              eventType: 'RECONNECT_ATTEMPT',
              eventAtUnixMs: 1500,
              timelineEventId: 'tl-fixed-1',
            ),
          );
      await eventsRef.doc('evt-unique').set(
            _timelinePayload(
              sessionId: 'session-z',
              studentId: 'student-z',
              therapistId: 'therapist-z',
              ownerKey: ownerKey,
              sessionKey: sessionKey,
              eventType: 'SESSION_ATTACH_SUCCEEDED',
              eventAtUnixMs: 2000,
              timelineEventId: 'tl-fixed-2',
            ),
          );

      final timeline = await SessionJournalService.fetchSessionTimeline(
        sessionId: 'session-z',
        limit: 10,
      );

      expect(timeline, hasLength(2));
      expect(timeline[0].timelineEventId, 'tl-fixed-2');
      expect(timeline[0].eventAtUnixMs, 2000);
      expect(timeline[1].timelineEventId, 'tl-fixed-1');
      expect(timeline[1].eventAtUnixMs, 1500);
    });

    test('watchSessionTimeline emits empty list for blank session id',
        () async {
      final stream = SessionJournalService.watchSessionTimeline(sessionId: ' ');
      await expectLater(stream, emits(isEmpty));
    });

    test('fetchLatestUnfinishedForTherapist returns latest unfinished session',
        () async {
      await firestore
          .collection('therapy_sessions')
          .doc('session-old')
          .set(_sessionPayload(
            sessionId: 'session-old',
            studentId: 'student-a',
            therapistId: 'therapist-a',
            state: SessionLifecycleState.inProgress.wireValue,
            unfinished: true,
            updatedAtUnixMs: 1000,
          ));
      await firestore
          .collection('therapy_sessions')
          .doc('session-new')
          .set(_sessionPayload(
            sessionId: 'session-new',
            studentId: 'student-b',
            therapistId: 'therapist-a',
            state: SessionLifecycleState.paused.wireValue,
            unfinished: true,
            updatedAtUnixMs: 2000,
          ));
      await firestore
          .collection('therapy_sessions')
          .doc('session-terminal')
          .set(_sessionPayload(
            sessionId: 'session-terminal',
            studentId: 'student-c',
            therapistId: 'therapist-a',
            state: SessionLifecycleState.completed.wireValue,
            unfinished: false,
            updatedAtUnixMs: 3000,
          ));

      final latest =
          await SessionJournalService.fetchLatestUnfinishedForTherapist(
        therapistId: 'therapist-a',
      );

      expect(latest, isNotNull);
      expect(latest!.sessionId, 'session-new');
      expect(latest.studentId, 'student-b');
    });

    test('fetchLatestUnfinishedForTherapist returns null when none active',
        () async {
      await firestore
          .collection('therapy_sessions')
          .doc('session-complete')
          .set(_sessionPayload(
            sessionId: 'session-complete',
            studentId: 'student-a',
            therapistId: 'therapist-a',
            state: SessionLifecycleState.completed.wireValue,
            unfinished: false,
            updatedAtUnixMs: 1000,
          ));

      final latest =
          await SessionJournalService.fetchLatestUnfinishedForTherapist(
        therapistId: 'therapist-a',
      );

      expect(latest, isNull);
    });
  });
}

Map<String, dynamic> _timelinePayload({
  required String sessionId,
  required String studentId,
  required String therapistId,
  required String ownerKey,
  required String sessionKey,
  required String eventType,
  required int eventAtUnixMs,
  String timelineEventId = '',
}) {
  final eventAtUtc = DateTime.fromMillisecondsSinceEpoch(
    eventAtUnixMs,
    isUtc: true,
  ).toIso8601String();

  final payload = <String, dynamic>{
    'sessionId': sessionId,
    'studentId': studentId,
    'therapistId': therapistId,
    'ownerKey': ownerKey,
    'sessionKey': sessionKey,
    'eventType': eventType,
    'gameId': 'demo_cube_clicker',
    'source': 'mobile_controller',
    'details': <String, dynamic>{},
    'eventAtUtc': eventAtUtc,
    'eventAtUnixMs': eventAtUnixMs,
    'createdAtUtc': eventAtUtc,
  };
  if (timelineEventId.trim().isNotEmpty) {
    payload['timelineEventId'] = timelineEventId.trim();
  }
  return payload;
}

Map<String, dynamic> _sessionPayload({
  required String sessionId,
  required String studentId,
  required String therapistId,
  required String state,
  required bool unfinished,
  required int updatedAtUnixMs,
}) {
  final updatedAtUtc = DateTime.fromMillisecondsSinceEpoch(
    updatedAtUnixMs,
    isUtc: true,
  );
  final ownerKey = SessionOwnership.ownerKey(
    therapistId: therapistId,
    studentId: studentId,
  );
  final sessionKey = SessionOwnership.sessionKey(
    therapistId: therapistId,
    studentId: studentId,
    sessionId: sessionId,
  );

  return <String, dynamic>{
    'sessionId': sessionId,
    'studentId': studentId,
    'therapistId': therapistId,
    'ownerKey': ownerKey,
    'sessionKey': sessionKey,
    'state': state,
    'unfinished': unfinished,
    'isTerminal': !unfinished,
    'latestGameId': 'demo_cube_clicker',
    'reasonCode': '',
    'updatedAtUtc': updatedAtUtc.toIso8601String(),
    'updatedAtUnixMs': updatedAtUnixMs,
    'createdAtUtc': updatedAtUtc.toIso8601String(),
    'createdAtUnixMs': updatedAtUnixMs,
    'startedAtUtc': updatedAtUtc.toIso8601String(),
    'startedAtUnixMs': updatedAtUnixMs,
    'metadata': <String, dynamic>{},
  };
}
