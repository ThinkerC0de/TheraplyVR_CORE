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

    test('watchSessionTimeline emits empty list for blank session id',
        () async {
      final stream = SessionJournalService.watchSessionTimeline(sessionId: ' ');
      await expectLater(stream, emits(isEmpty));
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
}) {
  final eventAtUtc = DateTime.fromMillisecondsSinceEpoch(
    eventAtUnixMs,
    isUtc: true,
  ).toIso8601String();

  return <String, dynamic>{
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
}
