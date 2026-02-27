import 'package:flutter_controller/models/session_fsm_contract.dart';
import 'package:flutter_controller/models/therapist_session_settings.dart';

enum SessionRecoveryWindowState {
  notApplicable,
  interruptedRecoveringUnderWindow,
  interruptedOverWindowNeedsTherapistDecision,
}

enum InterruptedSessionAutoCloseState {
  notApplicable,
  disabledBySettings,
  interruptedAgeUnknown,
  interruptedUnderWindow,
  interruptedOverWindowAutoClose,
}

class SessionRecoveryEvaluation {
  final SessionRecoveryWindowState state;
  final bool shouldAutoRecoverSilently;
  final bool shouldPromptTherapistDecision;
  final Duration? connectionLossAge;

  const SessionRecoveryEvaluation({
    required this.state,
    required this.shouldAutoRecoverSilently,
    required this.shouldPromptTherapistDecision,
    required this.connectionLossAge,
  });
}

class InterruptedSessionAutoCloseEvaluation {
  final InterruptedSessionAutoCloseState state;
  final bool shouldAutoClose;
  final Duration? interruptedAge;
  final int autoCloseWindowHours;

  const InterruptedSessionAutoCloseEvaluation({
    required this.state,
    required this.shouldAutoClose,
    required this.interruptedAge,
    required this.autoCloseWindowHours,
  });
}

class SessionRecoveryManager {
  static SessionRecoveryEvaluation evaluate({
    required bool remoteSessionNeedsDecision,
    required DateTime nowUtc,
    required DateTime? interruptedAtUtc,
    required DateTime? lastConnectionLostAtUtc,
    required int sessionRecoveryWindowMinutes,
    required bool requireResumeConfirmationAfterRecoveryWindow,
  }) {
    if (!remoteSessionNeedsDecision) {
      return const SessionRecoveryEvaluation(
        state: SessionRecoveryWindowState.notApplicable,
        shouldAutoRecoverSilently: false,
        shouldPromptTherapistDecision: false,
        connectionLossAge: null,
      );
    }

    final recoveryAnchorUtc = interruptedAtUtc ?? lastConnectionLostAtUtc;
    if (recoveryAnchorUtc == null) {
      return const SessionRecoveryEvaluation(
        state: SessionRecoveryWindowState
            .interruptedOverWindowNeedsTherapistDecision,
        shouldAutoRecoverSilently: false,
        shouldPromptTherapistDecision: true,
        connectionLossAge: null,
      );
    }

    final windowMinutes = sessionRecoveryWindowMinutes.clamp(
      TherapistSessionSettings.minSessionRecoveryWindowMinutes,
      TherapistSessionSettings.maxSessionRecoveryWindowMinutes,
    );
    final rawAge = nowUtc.toUtc().difference(recoveryAnchorUtc.toUtc());
    final age = rawAge.isNegative ? Duration.zero : rawAge;
    final isUnderWindow = age <= Duration(minutes: windowMinutes);

    if (isUnderWindow) {
      return SessionRecoveryEvaluation(
        state: SessionRecoveryWindowState.interruptedRecoveringUnderWindow,
        shouldAutoRecoverSilently: true,
        shouldPromptTherapistDecision: false,
        connectionLossAge: age,
      );
    }

    if (!requireResumeConfirmationAfterRecoveryWindow) {
      return SessionRecoveryEvaluation(
        state: SessionRecoveryWindowState
            .interruptedOverWindowNeedsTherapistDecision,
        shouldAutoRecoverSilently: true,
        shouldPromptTherapistDecision: false,
        connectionLossAge: age,
      );
    }

    return SessionRecoveryEvaluation(
      state: SessionRecoveryWindowState
          .interruptedOverWindowNeedsTherapistDecision,
      shouldAutoRecoverSilently: false,
      shouldPromptTherapistDecision: true,
      connectionLossAge: age,
    );
  }

  static InterruptedSessionAutoCloseEvaluation evaluateInterruptedAutoClose({
    required SessionLifecycleState? sessionState,
    required DateTime nowUtc,
    required DateTime? interruptedAtUtc,
    required int interruptedSessionAutoCloseHours,
    required bool autoCloseInterruptedSessionsEnabled,
  }) {
    final windowHours = interruptedSessionAutoCloseHours.clamp(
      TherapistSessionSettings.minInterruptedSessionAutoCloseHours,
      TherapistSessionSettings.maxInterruptedSessionAutoCloseHours,
    );

    if (sessionState != SessionLifecycleState.interrupted) {
      return InterruptedSessionAutoCloseEvaluation(
        state: InterruptedSessionAutoCloseState.notApplicable,
        shouldAutoClose: false,
        interruptedAge: null,
        autoCloseWindowHours: windowHours,
      );
    }

    if (!autoCloseInterruptedSessionsEnabled) {
      return InterruptedSessionAutoCloseEvaluation(
        state: InterruptedSessionAutoCloseState.disabledBySettings,
        shouldAutoClose: false,
        interruptedAge: null,
        autoCloseWindowHours: windowHours,
      );
    }

    if (interruptedAtUtc == null) {
      return InterruptedSessionAutoCloseEvaluation(
        state: InterruptedSessionAutoCloseState.interruptedAgeUnknown,
        shouldAutoClose: false,
        interruptedAge: null,
        autoCloseWindowHours: windowHours,
      );
    }

    final rawAge = nowUtc.toUtc().difference(interruptedAtUtc.toUtc());
    final age = rawAge.isNegative ? Duration.zero : rawAge;
    final shouldAutoClose = age >= Duration(hours: windowHours);

    return InterruptedSessionAutoCloseEvaluation(
      state: shouldAutoClose
          ? InterruptedSessionAutoCloseState.interruptedOverWindowAutoClose
          : InterruptedSessionAutoCloseState.interruptedUnderWindow,
      shouldAutoClose: shouldAutoClose,
      interruptedAge: age,
      autoCloseWindowHours: windowHours,
    );
  }
}
