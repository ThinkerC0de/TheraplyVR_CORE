import 'package:flutter_controller/models/guided_session_plan.dart';
import 'package:flutter_controller/models/session_fsm_contract.dart';
import 'package:flutter_controller/models/therapist_session_settings.dart';

enum GuidedSessionContinuationDecision {
  allowStartNew,
  autoContinueUnfinished,
  requireManualDecision,
}

class GuidedSessionContinuationEvaluator {
  static GuidedSessionContinuationDecision evaluate({
    required GuidedSessionContinuationPolicy policy,
    required bool hasUnfinishedSession,
    required bool sessionRecentlyEnded,
    required SessionLifecycleState? sessionState,
    required DateTime nowUtc,
    required DateTime? interruptedAtUtc,
    required int recoveryWindowMinutes,
  }) {
    if (!hasUnfinishedSession || sessionRecentlyEnded) {
      return GuidedSessionContinuationDecision.allowStartNew;
    }

    switch (policy) {
      case GuidedSessionContinuationPolicy.manual:
        return GuidedSessionContinuationDecision.requireManualDecision;
      case GuidedSessionContinuationPolicy.resumeAlways:
        return GuidedSessionContinuationDecision.autoContinueUnfinished;
      case GuidedSessionContinuationPolicy.resumeUnderRecoveryWindow:
        if (sessionState == SessionLifecycleState.inProgress ||
            sessionState == SessionLifecycleState.paused) {
          return GuidedSessionContinuationDecision.autoContinueUnfinished;
        }

        if (sessionState != SessionLifecycleState.interrupted ||
            interruptedAtUtc == null) {
          return GuidedSessionContinuationDecision.requireManualDecision;
        }

        final windowMinutes = recoveryWindowMinutes.clamp(
          TherapistSessionSettings.minSessionRecoveryWindowMinutes,
          TherapistSessionSettings.maxSessionRecoveryWindowMinutes,
        );
        final age = nowUtc.toUtc().difference(interruptedAtUtc.toUtc());
        if (!age.isNegative && age <= Duration(minutes: windowMinutes)) {
          return GuidedSessionContinuationDecision.autoContinueUnfinished;
        }
        return GuidedSessionContinuationDecision.requireManualDecision;
    }
  }
}
