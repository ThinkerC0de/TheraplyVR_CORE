import 'package:flutter_controller/models/guided_session_continuation_policy.dart';
import 'package:flutter_controller/models/guided_session_plan.dart';
import 'package:flutter_controller/models/session_fsm_contract.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('GuidedSessionContinuationEvaluator', () {
    test('allows start when no unfinished session', () {
      final decision = GuidedSessionContinuationEvaluator.evaluate(
        policy: GuidedSessionContinuationPolicy.manual,
        hasUnfinishedSession: false,
        sessionRecentlyEnded: false,
        sessionState: null,
        nowUtc: DateTime.utc(2026, 2, 25, 12),
        interruptedAtUtc: null,
        recoveryWindowMinutes: 60,
      );

      expect(decision, GuidedSessionContinuationDecision.allowStartNew);
    });

    test('manual policy requires decision for unfinished session', () {
      final decision = GuidedSessionContinuationEvaluator.evaluate(
        policy: GuidedSessionContinuationPolicy.manual,
        hasUnfinishedSession: true,
        sessionRecentlyEnded: false,
        sessionState: SessionLifecycleState.interrupted,
        nowUtc: DateTime.utc(2026, 2, 25, 12),
        interruptedAtUtc: DateTime.utc(2026, 2, 25, 11, 50),
        recoveryWindowMinutes: 60,
      );

      expect(decision, GuidedSessionContinuationDecision.requireManualDecision);
    });

    test('resume always auto-continues unfinished session', () {
      final decision = GuidedSessionContinuationEvaluator.evaluate(
        policy: GuidedSessionContinuationPolicy.resumeAlways,
        hasUnfinishedSession: true,
        sessionRecentlyEnded: false,
        sessionState: SessionLifecycleState.interrupted,
        nowUtc: DateTime.utc(2026, 2, 25, 12),
        interruptedAtUtc: DateTime.utc(2026, 2, 25, 9),
        recoveryWindowMinutes: 60,
      );

      expect(
        decision,
        GuidedSessionContinuationDecision.autoContinueUnfinished,
      );
    });

    test('resume-under-window auto-continues interrupted only inside window',
        () {
      final insideWindow = GuidedSessionContinuationEvaluator.evaluate(
        policy: GuidedSessionContinuationPolicy.resumeUnderRecoveryWindow,
        hasUnfinishedSession: true,
        sessionRecentlyEnded: false,
        sessionState: SessionLifecycleState.interrupted,
        nowUtc: DateTime.utc(2026, 2, 25, 12),
        interruptedAtUtc: DateTime.utc(2026, 2, 25, 11, 30),
        recoveryWindowMinutes: 60,
      );
      final outsideWindow = GuidedSessionContinuationEvaluator.evaluate(
        policy: GuidedSessionContinuationPolicy.resumeUnderRecoveryWindow,
        hasUnfinishedSession: true,
        sessionRecentlyEnded: false,
        sessionState: SessionLifecycleState.interrupted,
        nowUtc: DateTime.utc(2026, 2, 25, 12),
        interruptedAtUtc: DateTime.utc(2026, 2, 25, 10, 30),
        recoveryWindowMinutes: 60,
      );

      expect(
        insideWindow,
        GuidedSessionContinuationDecision.autoContinueUnfinished,
      );
      expect(
        outsideWindow,
        GuidedSessionContinuationDecision.requireManualDecision,
      );
    });
  });
}
