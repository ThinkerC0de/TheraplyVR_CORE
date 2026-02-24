class SessionAttachFailurePolicy {
  static const Set<String> _transientFailureReasonCodes = <String>{
    'ACK_TIMEOUT',
    'DISCONNECTED',
    'NO_ACTIVE_TCP_ROUTE',
    'TRANSPORT_CLOSED',
    'SOCKET_CLOSED',
    'TCP_LINK_LOST',
    'NO_RUNTIME_SIGNAL_TIMEOUT',
    'RUNTIME_SIGNAL_STALE',
    'APP_PAUSED',
    'APP_FOCUS_LOST',
    'TCP_CLIENT_DISCONNECTED',
  };

  static const Set<String> _silentAttachReasonCodes = <String>{
    'INITIAL_CONNECT',
    'AUTO_RECONNECT',
    'HANDOFF_KEEP_CURRENT',
    'HANDOFF_GATE_CLEARED',
  };

  static const Set<String> _alwaysPopupAttachReasonCodes = <String>{
    'COMMAND_PRECONDITION',
    'RECOVERY_UNDER_WINDOW',
    'HANDOFF_RESUME',
    'HANDOFF_START_NEW',
  };

  static bool shouldShowPopup({
    required String attachReasonCode,
    required String failureReasonCode,
    required bool isGameSetupWorkflow,
  }) {
    final attachReason = _normalize(attachReasonCode);
    final failureReason = _normalize(failureReasonCode);
    final transientFailure =
        _transientFailureReasonCodes.contains(failureReason);

    if (_alwaysPopupAttachReasonCodes.contains(attachReason)) {
      return true;
    }

    if (_silentAttachReasonCodes.contains(attachReason) && transientFailure) {
      return false;
    }

    // While browsing catalog/store we suppress transient transport noise.
    if (!isGameSetupWorkflow && transientFailure) {
      return false;
    }

    return true;
  }

  static String _normalize(String value) {
    return value.trim().toUpperCase();
  }
}
