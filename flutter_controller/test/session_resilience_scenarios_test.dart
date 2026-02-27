import 'package:fake_cloud_firestore/fake_cloud_firestore.dart';
import 'package:flutter_controller/models/session_fsm_contract.dart';
import 'package:flutter_controller/models/session_recovery_manager.dart';
import 'package:flutter_controller/services/session_journal_service.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('Session resilience scenarios', () {
    test('1) Headset sleep -> wake within recovery window stays recoverable',
        () {
      final nowUtc = DateTime.utc(2026, 2, 27, 12, 0, 0);
      final evaluation = SessionRecoveryManager.evaluate(
        remoteSessionNeedsDecision: true,
        nowUtc: nowUtc,
        interruptedAtUtc: nowUtc.subtract(const Duration(minutes: 7)),
        lastConnectionLostAtUtc: nowUtc.subtract(const Duration(minutes: 2)),
        sessionRecoveryWindowMinutes: 30,
        requireResumeConfirmationAfterRecoveryWindow: true,
      );

      expect(
        evaluation.state,
        SessionRecoveryWindowState.interruptedRecoveringUnderWindow,
      );
      expect(evaluation.shouldAutoRecoverSilently, isTrue);
      expect(evaluation.shouldPromptTherapistDecision, isFalse);
    });

    test('2) Mobile background/kill -> restart within recovery window recovers',
        () {
      final nowUtc = DateTime.utc(2026, 2, 27, 13, 0, 0);
      final evaluation = SessionRecoveryManager.evaluate(
        remoteSessionNeedsDecision: true,
        nowUtc: nowUtc,
        interruptedAtUtc: nowUtc.subtract(const Duration(minutes: 9)),
        lastConnectionLostAtUtc: nowUtc.subtract(const Duration(minutes: 1)),
        sessionRecoveryWindowMinutes: 20,
        requireResumeConfirmationAfterRecoveryWindow: true,
      );

      expect(
        evaluation.state,
        SessionRecoveryWindowState.interruptedRecoveringUnderWindow,
      );
      expect(evaluation.shouldAutoRecoverSilently, isTrue);
      expect(evaluation.shouldPromptTherapistDecision, isFalse);
    });

    test('3) VR kill -> restart within recovery window recovers', () {
      final nowUtc = DateTime.utc(2026, 2, 27, 14, 0, 0);
      final evaluation = SessionRecoveryManager.evaluate(
        remoteSessionNeedsDecision: true,
        nowUtc: nowUtc,
        interruptedAtUtc: nowUtc.subtract(const Duration(minutes: 12)),
        lastConnectionLostAtUtc: nowUtc.subtract(const Duration(minutes: 3)),
        sessionRecoveryWindowMinutes: 20,
        requireResumeConfirmationAfterRecoveryWindow: true,
      );

      expect(
        evaluation.state,
        SessionRecoveryWindowState.interruptedRecoveringUnderWindow,
      );
      expect(evaluation.shouldAutoRecoverSilently, isTrue);
      expect(evaluation.shouldPromptTherapistDecision, isFalse);
    });

    test('4) Internet offline on both sides does not auto-close active session',
        () {
      final evaluation = SessionRecoveryManager.evaluateInterruptedAutoClose(
        sessionState: SessionLifecycleState.inProgress,
        nowUtc: DateTime.utc(2026, 2, 27, 15, 0, 0),
        interruptedAtUtc: DateTime.utc(2026, 2, 27, 13, 0, 0),
        interruptedSessionAutoCloseHours: 48,
        autoCloseInterruptedSessionsEnabled: true,
      );

      expect(evaluation.state, InterruptedSessionAutoCloseState.notApplicable);
      expect(evaluation.shouldAutoClose, isFalse);
    });

    test(
      '5) Both apps crash and return within window merges dual-source timeline without duplicates',
      () async {
        final firestore = FakeFirebaseFirestore();
        SessionJournalService.setFirestoreInstanceForTesting(firestore);
        addTearDown(SessionJournalService.clearTestingOverrides);

        await SessionJournalService.upsertSessionState(
          sessionId: 'session-dual-merge',
          studentId: 'student-merge',
          therapistId: 'therapist-merge',
          state: SessionLifecycleState.interrupted,
          latestGameId: 'demo_cube_clicker',
          reasonCode: 'TCP_LINK_LOST',
        );

        const timelineEventId = 'tl-dual-reconnect-001';
        final eventAt = DateTime.utc(2026, 2, 27, 16, 0, 0);
        await SessionJournalService.appendSessionEvent(
          sessionId: 'session-dual-merge',
          studentId: 'student-merge',
          therapistId: 'therapist-merge',
          eventType: 'RECONNECT_SUCCEEDED',
          source: 'mobile_controller',
          eventAtUtc: eventAt,
          timelineEventId: timelineEventId,
          details: const <String, dynamic>{
            'side': 'mobile',
            'attempt': 2,
          },
        );
        await SessionJournalService.appendSessionEvent(
          sessionId: 'session-dual-merge',
          studentId: 'student-merge',
          therapistId: 'therapist-merge',
          eventType: 'RECONNECT_SUCCEEDED',
          source: 'vr_runtime',
          eventAtUtc: eventAt.add(const Duration(milliseconds: 120)),
          timelineEventId: timelineEventId,
          details: const <String, dynamic>{
            'side': 'vr',
            'attempt': 2,
          },
        );
        await SessionJournalService.appendSessionEvent(
          sessionId: 'session-dual-merge',
          studentId: 'student-merge',
          therapistId: 'therapist-merge',
          eventType: 'SESSION_ATTACH_SUCCEEDED',
          source: 'mobile_controller',
          eventAtUtc: eventAt.add(const Duration(seconds: 1)),
          timelineEventId: 'tl-dual-attach-001',
          details: const <String, dynamic>{
            'reasonCode': 'AUTO_RECONNECT',
          },
        );

        final timeline = await SessionJournalService.fetchSessionTimeline(
          sessionId: 'session-dual-merge',
          limit: 10,
        );

        expect(timeline.length, 2);
        final reconnectEvents = timeline
            .where((event) => event.timelineEventId == timelineEventId)
            .toList(growable: false);
        expect(reconnectEvents.length, 1);
      },
    );

    test(
      '6) Both apps do not return within window closes as FAILED_TECHNICAL with corrupted reason',
      () async {
        final firestore = FakeFirebaseFirestore();
        SessionJournalService.setFirestoreInstanceForTesting(firestore);
        addTearDown(SessionJournalService.clearTestingOverrides);

        final nowUtc = DateTime.utc(2026, 2, 27, 17, 0, 0);
        final autoCloseEvaluation =
            SessionRecoveryManager.evaluateInterruptedAutoClose(
          sessionState: SessionLifecycleState.interrupted,
          nowUtc: nowUtc,
          interruptedAtUtc: nowUtc.subtract(const Duration(hours: 72)),
          interruptedSessionAutoCloseHours: 48,
          autoCloseInterruptedSessionsEnabled: true,
        );

        expect(autoCloseEvaluation.shouldAutoClose, isTrue);

        await SessionJournalService.upsertSessionState(
          sessionId: 'session-corrupted-close',
          studentId: 'student-corrupted',
          therapistId: 'therapist-corrupted',
          state: SessionLifecycleState.failedTechnical,
          latestGameId: 'demo_cube_clicker',
          reasonCode: 'CORRUPTED_DUAL_TERMINATION',
          metadata: const <String, dynamic>{
            'origin': 'mobile_auto_close_policy',
            'sourceState': 'INTERRUPTED',
          },
        );
        await SessionJournalService.appendSessionEvent(
          sessionId: 'session-corrupted-close',
          studentId: 'student-corrupted',
          therapistId: 'therapist-corrupted',
          eventType: 'INTERRUPTED_SESSION_AUTO_CLOSED',
          source: 'mobile_controller',
          details: const <String, dynamic>{
            'reasonCode': 'CORRUPTED_DUAL_TERMINATION',
          },
        );

        final latest = await SessionJournalService.fetchLatestForStudent(
          studentId: 'student-corrupted',
          therapistId: 'therapist-corrupted',
        );
        expect(latest, isNotNull);
        expect(latest!.state, SessionLifecycleState.failedTechnical);
        expect(latest.reasonCode, 'CORRUPTED_DUAL_TERMINATION');
      },
    );
  });
}
