import 'package:flutter_controller/models/session_recovery_manager.dart';
import 'package:flutter_controller/models/session_fsm_contract.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('SessionRecoveryManager', () {
    test('returns notApplicable when remote session does not need decision',
        () {
      final nowUtc = DateTime.utc(2026, 2, 22, 0, 0, 0);
      final evaluation = SessionRecoveryManager.evaluate(
        remoteSessionNeedsDecision: false,
        nowUtc: nowUtc,
        lastConnectionLostAtUtc: nowUtc.subtract(const Duration(minutes: 10)),
        sessionRecoveryWindowMinutes: 60,
        requireResumeConfirmationAfterRecoveryWindow: true,
      );

      expect(evaluation.state, SessionRecoveryWindowState.notApplicable);
      expect(evaluation.shouldAutoRecoverSilently, isFalse);
      expect(evaluation.shouldPromptTherapistDecision, isFalse);
      expect(evaluation.connectionLossAge, isNull);
    });

    test('auto-recovers silently when connection loss is under recovery window',
        () {
      final nowUtc = DateTime.utc(2026, 2, 21, 12, 0, 0);
      final evaluation = SessionRecoveryManager.evaluate(
        remoteSessionNeedsDecision: true,
        nowUtc: nowUtc,
        lastConnectionLostAtUtc: nowUtc.subtract(const Duration(minutes: 3)),
        sessionRecoveryWindowMinutes: 10,
        requireResumeConfirmationAfterRecoveryWindow: true,
      );

      expect(
        evaluation.state,
        SessionRecoveryWindowState.interruptedRecoveringUnderWindow,
      );
      expect(evaluation.shouldAutoRecoverSilently, isTrue);
      expect(evaluation.shouldPromptTherapistDecision, isFalse);
    });

    test('requires therapist decision when window is exceeded', () {
      final nowUtc = DateTime.utc(2026, 2, 21, 12, 0, 0);
      final evaluation = SessionRecoveryManager.evaluate(
        remoteSessionNeedsDecision: true,
        nowUtc: nowUtc,
        lastConnectionLostAtUtc: nowUtc.subtract(const Duration(minutes: 61)),
        sessionRecoveryWindowMinutes: 60,
        requireResumeConfirmationAfterRecoveryWindow: true,
      );

      expect(
        evaluation.state,
        SessionRecoveryWindowState.interruptedOverWindowNeedsTherapistDecision,
      );
      expect(evaluation.shouldAutoRecoverSilently, isFalse);
      expect(evaluation.shouldPromptTherapistDecision, isTrue);
    });

    test(
        'auto-recovers even over window when confirmation after window is disabled',
        () {
      final nowUtc = DateTime.utc(2026, 2, 21, 12, 0, 0);
      final evaluation = SessionRecoveryManager.evaluate(
        remoteSessionNeedsDecision: true,
        nowUtc: nowUtc,
        lastConnectionLostAtUtc: nowUtc.subtract(const Duration(minutes: 61)),
        sessionRecoveryWindowMinutes: 60,
        requireResumeConfirmationAfterRecoveryWindow: false,
      );

      expect(
        evaluation.state,
        SessionRecoveryWindowState.interruptedOverWindowNeedsTherapistDecision,
      );
      expect(evaluation.shouldAutoRecoverSilently, isTrue);
      expect(evaluation.shouldPromptTherapistDecision, isFalse);
    });

    test('requires therapist decision when loss timestamp is missing', () {
      final nowUtc = DateTime.utc(2026, 2, 21, 12, 0, 0);
      final evaluation = SessionRecoveryManager.evaluate(
        remoteSessionNeedsDecision: true,
        nowUtc: nowUtc,
        lastConnectionLostAtUtc: null,
        sessionRecoveryWindowMinutes: 60,
        requireResumeConfirmationAfterRecoveryWindow: true,
      );

      expect(
        evaluation.state,
        SessionRecoveryWindowState.interruptedOverWindowNeedsTherapistDecision,
      );
      expect(evaluation.shouldAutoRecoverSilently, isFalse);
      expect(evaluation.shouldPromptTherapistDecision, isTrue);
    });

    test('clamps recovery window to documented minimum', () {
      final nowUtc = DateTime.utc(2026, 2, 22, 0, 0, 0);
      final evaluation = SessionRecoveryManager.evaluate(
        remoteSessionNeedsDecision: true,
        nowUtc: nowUtc,
        lastConnectionLostAtUtc: nowUtc.subtract(const Duration(minutes: 6)),
        sessionRecoveryWindowMinutes: 0,
        requireResumeConfirmationAfterRecoveryWindow: true,
      );

      expect(
        evaluation.state,
        SessionRecoveryWindowState.interruptedOverWindowNeedsTherapistDecision,
      );
      expect(evaluation.shouldAutoRecoverSilently, isFalse);
      expect(evaluation.shouldPromptTherapistDecision, isTrue);
    });

    test('clamps recovery window to documented maximum', () {
      final nowUtc = DateTime.utc(2026, 2, 22, 0, 0, 0);
      final evaluation = SessionRecoveryManager.evaluate(
        remoteSessionNeedsDecision: true,
        nowUtc: nowUtc,
        lastConnectionLostAtUtc: nowUtc.subtract(const Duration(minutes: 241)),
        sessionRecoveryWindowMinutes: 9999,
        requireResumeConfirmationAfterRecoveryWindow: true,
      );

      expect(
        evaluation.state,
        SessionRecoveryWindowState.interruptedOverWindowNeedsTherapistDecision,
      );
      expect(evaluation.shouldAutoRecoverSilently, isFalse);
      expect(evaluation.shouldPromptTherapistDecision, isTrue);
    });

    test('treats future loss timestamp as zero age under-window recovery', () {
      final nowUtc = DateTime.utc(2026, 2, 22, 0, 0, 0);
      final evaluation = SessionRecoveryManager.evaluate(
        remoteSessionNeedsDecision: true,
        nowUtc: nowUtc,
        lastConnectionLostAtUtc: nowUtc.add(const Duration(minutes: 3)),
        sessionRecoveryWindowMinutes: 60,
        requireResumeConfirmationAfterRecoveryWindow: true,
      );

      expect(
        evaluation.state,
        SessionRecoveryWindowState.interruptedRecoveringUnderWindow,
      );
      expect(evaluation.shouldAutoRecoverSilently, isTrue);
      expect(evaluation.shouldPromptTherapistDecision, isFalse);
      expect(evaluation.connectionLossAge, Duration.zero);
    });
  });

  group('SessionRecoveryManager interrupted auto-close', () {
    test('returns notApplicable when state is not interrupted', () {
      final evaluation = SessionRecoveryManager.evaluateInterruptedAutoClose(
        sessionState: SessionLifecycleState.inProgress,
        nowUtc: DateTime.utc(2026, 2, 22, 0, 0, 0),
        interruptedAtUtc: DateTime.utc(2026, 2, 21, 0, 0, 0),
        interruptedSessionAutoCloseHours: 48,
        autoCloseInterruptedSessionsEnabled: true,
      );

      expect(evaluation.state, InterruptedSessionAutoCloseState.notApplicable);
      expect(evaluation.shouldAutoClose, isFalse);
    });

    test('does not auto-close when setting is disabled', () {
      final evaluation = SessionRecoveryManager.evaluateInterruptedAutoClose(
        sessionState: SessionLifecycleState.interrupted,
        nowUtc: DateTime.utc(2026, 2, 22, 0, 0, 0),
        interruptedAtUtc: DateTime.utc(2026, 2, 19, 0, 0, 0),
        interruptedSessionAutoCloseHours: 48,
        autoCloseInterruptedSessionsEnabled: false,
      );

      expect(
        evaluation.state,
        InterruptedSessionAutoCloseState.disabledBySettings,
      );
      expect(evaluation.shouldAutoClose, isFalse);
    });

    test('does not auto-close when interrupted age is below threshold', () {
      final nowUtc = DateTime.utc(2026, 2, 22, 0, 0, 0);
      final evaluation = SessionRecoveryManager.evaluateInterruptedAutoClose(
        sessionState: SessionLifecycleState.interrupted,
        nowUtc: nowUtc,
        interruptedAtUtc: nowUtc.subtract(const Duration(hours: 12)),
        interruptedSessionAutoCloseHours: 48,
        autoCloseInterruptedSessionsEnabled: true,
      );

      expect(
        evaluation.state,
        InterruptedSessionAutoCloseState.interruptedUnderWindow,
      );
      expect(evaluation.shouldAutoClose, isFalse);
      expect(evaluation.interruptedAge, const Duration(hours: 12));
    });

    test('auto-closes when interrupted age reaches configured threshold', () {
      final nowUtc = DateTime.utc(2026, 2, 22, 0, 0, 0);
      final evaluation = SessionRecoveryManager.evaluateInterruptedAutoClose(
        sessionState: SessionLifecycleState.interrupted,
        nowUtc: nowUtc,
        interruptedAtUtc: nowUtc.subtract(const Duration(hours: 48)),
        interruptedSessionAutoCloseHours: 48,
        autoCloseInterruptedSessionsEnabled: true,
      );

      expect(
        evaluation.state,
        InterruptedSessionAutoCloseState.interruptedOverWindowAutoClose,
      );
      expect(evaluation.shouldAutoClose, isTrue);
      expect(evaluation.interruptedAge, const Duration(hours: 48));
    });

    test('normalizes future interrupted timestamp to zero age', () {
      final nowUtc = DateTime.utc(2026, 2, 22, 0, 0, 0);
      final evaluation = SessionRecoveryManager.evaluateInterruptedAutoClose(
        sessionState: SessionLifecycleState.interrupted,
        nowUtc: nowUtc,
        interruptedAtUtc: nowUtc.add(const Duration(hours: 2)),
        interruptedSessionAutoCloseHours: 48,
        autoCloseInterruptedSessionsEnabled: true,
      );

      expect(
        evaluation.state,
        InterruptedSessionAutoCloseState.interruptedUnderWindow,
      );
      expect(evaluation.shouldAutoClose, isFalse);
      expect(evaluation.interruptedAge, Duration.zero);
    });
  });
}
