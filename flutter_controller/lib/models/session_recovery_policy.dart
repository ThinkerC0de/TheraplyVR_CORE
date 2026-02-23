import 'package:flutter_controller/models/runtime_status_signal.dart';
import 'package:flutter_controller/models/session_fsm_contract.dart';

class SessionRecoveryPolicy {
  static bool shouldRequireDecision({
    required String localSessionId,
    required String remoteSessionId,
    required SessionLifecycleState? remoteState,
    required TherapistRuntimeStatus? remoteRuntimeStatus,
  }) {
    if (remoteSessionId.isEmpty || remoteSessionId == localSessionId) {
      return false;
    }

    if (remoteState != null) {
      return shouldGateBySessionState(remoteState);
    }

    switch (remoteRuntimeStatus) {
      case TherapistRuntimeStatus.playing:
      case TherapistRuntimeStatus.paused:
      case TherapistRuntimeStatus.interrupted:
        return true;
      case TherapistRuntimeStatus.syncPending:
      case TherapistRuntimeStatus.connected:
      case null:
        return false;
    }
  }

  static bool shouldGateBySessionState(SessionLifecycleState state) {
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

  static bool isTerminalState(SessionLifecycleState state) {
    return state == SessionLifecycleState.completed ||
        state == SessionLifecycleState.abortedByTherapist ||
        state == SessionLifecycleState.failedTechnical;
  }

  static bool shouldAdoptCreatedRolloverSession({
    required bool sessionAttachReady,
    required bool requiresSessionDecision,
    required String activeSessionId,
    required String incomingSessionId,
    required SessionLifecycleState? activeSessionState,
    required bool activeSessionRecentlyEnded,
  }) {
    if (!sessionAttachReady || requiresSessionDecision) {
      return false;
    }

    final normalizedActiveSessionId = activeSessionId.trim();
    final normalizedIncomingSessionId = incomingSessionId.trim();
    if (normalizedActiveSessionId.isEmpty ||
        normalizedIncomingSessionId.isEmpty ||
        normalizedActiveSessionId == normalizedIncomingSessionId) {
      return false;
    }

    final activeStateIsTerminal =
        activeSessionState != null && isTerminalState(activeSessionState);
    return activeStateIsTerminal || activeSessionRecentlyEnded;
  }
}
