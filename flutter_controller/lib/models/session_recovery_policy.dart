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
      return !isTerminalState(remoteState);
    }

    switch (remoteRuntimeStatus) {
      case TherapistRuntimeStatus.playing:
      case TherapistRuntimeStatus.paused:
      case TherapistRuntimeStatus.interrupted:
      case TherapistRuntimeStatus.syncPending:
        return true;
      case TherapistRuntimeStatus.connected:
      case null:
        return false;
    }
  }

  static bool isTerminalState(SessionLifecycleState state) {
    return state == SessionLifecycleState.completed ||
        state == SessionLifecycleState.abortedByTherapist ||
        state == SessionLifecycleState.failedTechnical;
  }
}
