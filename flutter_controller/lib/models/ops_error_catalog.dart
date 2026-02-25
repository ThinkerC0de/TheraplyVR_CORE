class OpsErrorCatalogEntry {
  final String errorId;
  final String reasonCode;
  final String description;

  const OpsErrorCatalogEntry({
    required this.errorId,
    required this.reasonCode,
    required this.description,
  });
}

class OpsErrorCatalog {
  static const OpsErrorCatalogEntry unknown = OpsErrorCatalogEntry(
    errorId: 'E-0000',
    reasonCode: 'UNKNOWN',
    description:
        'Unknown failure. Check runtime logs and docs/31-Error-Code-Catalog.md.',
  );

  static final Map<String, OpsErrorCatalogEntry> _entriesByReasonCode =
      <String, OpsErrorCatalogEntry>{
    'ACK_TIMEOUT': const OpsErrorCatalogEntry(
      errorId: 'E-1001',
      reasonCode: 'ACK_TIMEOUT',
      description: 'Critical command ACK timeout.',
    ),
    'DISCONNECTED': const OpsErrorCatalogEntry(
      errorId: 'E-1002',
      reasonCode: 'DISCONNECTED',
      description: 'Transport disconnected.',
    ),
    'NO_ACTIVE_TCP_ROUTE': const OpsErrorCatalogEntry(
      errorId: 'E-1003',
      reasonCode: 'NO_ACTIVE_TCP_ROUTE',
      description: 'No active TCP route.',
    ),
    'TRANSPORT_CLOSED': const OpsErrorCatalogEntry(
      errorId: 'E-1004',
      reasonCode: 'TRANSPORT_CLOSED',
      description: 'Transport closed.',
    ),
    'SOCKET_CLOSED': const OpsErrorCatalogEntry(
      errorId: 'E-1005',
      reasonCode: 'SOCKET_CLOSED',
      description: 'Socket closed.',
    ),
    'ENVELOPE_COMMAND_ID_MISMATCH': const OpsErrorCatalogEntry(
      errorId: 'E-1101',
      reasonCode: 'ENVELOPE_COMMAND_ID_MISMATCH',
      description: 'Envelope command mismatch.',
    ),
    'ENVELOPE_MESSAGE_ID_MISMATCH': const OpsErrorCatalogEntry(
      errorId: 'E-1102',
      reasonCode: 'ENVELOPE_MESSAGE_ID_MISMATCH',
      description: 'Envelope message id mismatch.',
    ),
    'ENVELOPE_INVALID_ISSUED_AT': const OpsErrorCatalogEntry(
      errorId: 'E-1103',
      reasonCode: 'ENVELOPE_INVALID_ISSUED_AT',
      description: 'Envelope issuedAt is invalid.',
    ),
    'ENVELOPE_INVALID_EXPIRES_AT': const OpsErrorCatalogEntry(
      errorId: 'E-1104',
      reasonCode: 'ENVELOPE_INVALID_EXPIRES_AT',
      description: 'Envelope expiresAt is invalid.',
    ),
    'ENVELOPE_EXPIRED': const OpsErrorCatalogEntry(
      errorId: 'E-1105',
      reasonCode: 'ENVELOPE_EXPIRED',
      description: 'Envelope expired.',
    ),
    'SESSION_LOCK_CONFLICT': const OpsErrorCatalogEntry(
      errorId: 'E-1201',
      reasonCode: 'SESSION_LOCK_CONFLICT',
      description: 'Active session lock conflict.',
    ),
    'SESSION_CLOSURE_REQUIRED': const OpsErrorCatalogEntry(
      errorId: 'E-1204',
      reasonCode: 'SESSION_CLOSURE_REQUIRED',
      description: 'Previous session must be explicitly ended before new start.',
    ),
    'SESSION_OWNERSHIP_CONFLICT': const OpsErrorCatalogEntry(
      errorId: 'E-1202',
      reasonCode: 'SESSION_OWNERSHIP_CONFLICT',
      description: 'Session ownership conflict.',
    ),
    'SESSION_OWNERSHIP_MISSING': const OpsErrorCatalogEntry(
      errorId: 'E-1203',
      reasonCode: 'SESSION_OWNERSHIP_MISSING',
      description: 'Session ownership metadata missing.',
    ),
    'NO_HANDLER': const OpsErrorCatalogEntry(
      errorId: 'E-1301',
      reasonCode: 'NO_HANDLER',
      description: 'No command handler on Quest runtime.',
    ),
    'DESERIALIZE_FAILED': const OpsErrorCatalogEntry(
      errorId: 'E-1302',
      reasonCode: 'DESERIALIZE_FAILED',
      description: 'Payload deserialization failed.',
    ),
    'HANDLER_EXCEPTION': const OpsErrorCatalogEntry(
      errorId: 'E-1303',
      reasonCode: 'HANDLER_EXCEPTION',
      description: 'Quest handler exception.',
    ),
    'COMMAND_IN_PROGRESS': const OpsErrorCatalogEntry(
      errorId: 'E-1304',
      reasonCode: 'COMMAND_IN_PROGRESS',
      description: 'Command already in progress.',
    ),
    'DUPLICATE_COMMAND': const OpsErrorCatalogEntry(
      errorId: 'E-1305',
      reasonCode: 'DUPLICATE_COMMAND',
      description: 'Duplicate command idempotency hit.',
    ),
    'UNSPECIFIED': const OpsErrorCatalogEntry(
      errorId: 'E-1306',
      reasonCode: 'UNSPECIFIED',
      description: 'Unspecified ACK/NACK reason.',
    ),
    'SESSION_NOT_ACTIVE': const OpsErrorCatalogEntry(
      errorId: 'E-1307',
      reasonCode: 'SESSION_NOT_ACTIVE',
      description: 'Session is not active.',
    ),
    'TEMPORARY_REJECT': const OpsErrorCatalogEntry(
      errorId: 'E-1308',
      reasonCode: 'TEMPORARY_REJECT',
      description: 'Temporary command rejection.',
    ),
    'ENTITLEMENT_RECORD_REQUIRED': const OpsErrorCatalogEntry(
      errorId: 'E-2001',
      reasonCode: 'ENTITLEMENT_RECORD_REQUIRED',
      description: 'Entitlement profile is required and missing.',
    ),
    'APP_LICENSE_INACTIVE': const OpsErrorCatalogEntry(
      errorId: 'E-2002',
      reasonCode: 'APP_LICENSE_INACTIVE',
      description: 'App license is inactive or expired.',
    ),
    'ENTITLEMENT_BACKEND_UNAVAILABLE': const OpsErrorCatalogEntry(
      errorId: 'E-2003',
      reasonCode: 'ENTITLEMENT_BACKEND_UNAVAILABLE',
      description: 'Entitlement backend is unavailable.',
    ),
    'ROLE_UNDEFINED': const OpsErrorCatalogEntry(
      errorId: 'E-2004',
      reasonCode: 'ROLE_UNDEFINED',
      description: 'Operator role is undefined or unsupported.',
    ),
    'LEGACY_FALLBACK_NO_RECORD': const OpsErrorCatalogEntry(
      errorId: 'E-2005',
      reasonCode: 'LEGACY_FALLBACK_NO_RECORD',
      description:
          'Legacy fallback used because entitlement record is missing.',
    ),
    'LEGACY_FALLBACK_FIRESTORE_ERROR': const OpsErrorCatalogEntry(
      errorId: 'E-2006',
      reasonCode: 'LEGACY_FALLBACK_FIRESTORE_ERROR',
      description: 'Legacy fallback used due to entitlement backend error.',
    ),
    'APP_LICENSE_ACTIVE': const OpsErrorCatalogEntry(
      errorId: 'E-2007',
      reasonCode: 'APP_LICENSE_ACTIVE',
      description: 'App license is active.',
    ),
    'APP_PAUSED': const OpsErrorCatalogEntry(
      errorId: 'E-2101',
      reasonCode: 'APP_PAUSED',
      description: 'Headset app paused/backgrounded.',
    ),
    'APP_RESUMED': const OpsErrorCatalogEntry(
      errorId: 'E-2102',
      reasonCode: 'APP_RESUMED',
      description: 'Headset app resumed.',
    ),
    'APP_FOCUS_LOST': const OpsErrorCatalogEntry(
      errorId: 'E-2103',
      reasonCode: 'APP_FOCUS_LOST',
      description: 'Headset app focus lost.',
    ),
    'APP_FOCUS_GAINED': const OpsErrorCatalogEntry(
      errorId: 'E-2104',
      reasonCode: 'APP_FOCUS_GAINED',
      description: 'Headset app focus regained.',
    ),
    'APP_QUIT': const OpsErrorCatalogEntry(
      errorId: 'E-2105',
      reasonCode: 'APP_QUIT',
      description: 'Headset app quitting.',
    ),
    'TCP_CLIENT_CONNECTED': const OpsErrorCatalogEntry(
      errorId: 'E-2106',
      reasonCode: 'TCP_CLIENT_CONNECTED',
      description: 'TCP client connected.',
    ),
    'NO_RUNTIME_SIGNAL_TIMEOUT': const OpsErrorCatalogEntry(
      errorId: 'E-2107',
      reasonCode: 'NO_RUNTIME_SIGNAL_TIMEOUT',
      description: 'No runtime signal received before timeout.',
    ),
    'RUNTIME_SIGNAL_STALE': const OpsErrorCatalogEntry(
      errorId: 'E-2108',
      reasonCode: 'RUNTIME_SIGNAL_STALE',
      description: 'Runtime signal became stale.',
    ),
    'TCP_LINK_LOST': const OpsErrorCatalogEntry(
      errorId: 'E-2109',
      reasonCode: 'TCP_LINK_LOST',
      description: 'TCP link lost.',
    ),
    'INITIAL_CONNECT': const OpsErrorCatalogEntry(
      errorId: 'E-2110',
      reasonCode: 'INITIAL_CONNECT',
      description: 'Initial controller connect.',
    ),
    'AUTO_RECONNECT': const OpsErrorCatalogEntry(
      errorId: 'E-2111',
      reasonCode: 'AUTO_RECONNECT',
      description: 'Auto reconnect in progress.',
    ),
    'SESSION_ATTACH': const OpsErrorCatalogEntry(
      errorId: 'E-2201',
      reasonCode: 'SESSION_ATTACH',
      description: 'Session attach handshake.',
    ),
    'SESSION_ID_REQUIRED': const OpsErrorCatalogEntry(
      errorId: 'E-2202',
      reasonCode: 'SESSION_ID_REQUIRED',
      description: 'Session id is required.',
    ),
    'FIREBASE_DATA_SERVICE_UNAVAILABLE': const OpsErrorCatalogEntry(
      errorId: 'E-2203',
      reasonCode: 'FIREBASE_DATA_SERVICE_UNAVAILABLE',
      description: 'Firebase data service is unavailable.',
    ),
    'MANUAL_RESYNC_EXCEPTION': const OpsErrorCatalogEntry(
      errorId: 'E-2204',
      reasonCode: 'MANUAL_RESYNC_EXCEPTION',
      description: 'Manual resync failed with an exception.',
    ),
    'UNINITIALIZED': const OpsErrorCatalogEntry(
      errorId: 'E-2205',
      reasonCode: 'UNINITIALIZED',
      description: 'Runtime is uninitialized.',
    ),
    'ACTIVE_GAME_COMPLETED': const OpsErrorCatalogEntry(
      errorId: 'E-2206',
      reasonCode: 'ACTIVE_GAME_COMPLETED',
      description: 'Active game completed.',
    ),
    'ACTIVE_GAME_FAILED': const OpsErrorCatalogEntry(
      errorId: 'E-2207',
      reasonCode: 'ACTIVE_GAME_FAILED',
      description: 'Active game failed.',
    ),
    'TCP_CLIENT_DISCONNECTED': const OpsErrorCatalogEntry(
      errorId: 'E-2208',
      reasonCode: 'TCP_CLIENT_DISCONNECTED',
      description: 'TCP client disconnected.',
    ),
    'SESSION_ATTACH_CONTEXT_MISSING': const OpsErrorCatalogEntry(
      errorId: 'E-2301',
      reasonCode: 'SESSION_ATTACH_CONTEXT_MISSING',
      description: 'Session attach context is missing.',
    ),
    'SESSION_ATTACH_SESSION_ID_REQUIRED': const OpsErrorCatalogEntry(
      errorId: 'E-2302',
      reasonCode: 'SESSION_ATTACH_SESSION_ID_REQUIRED',
      description: 'Session attach requires session id.',
    ),
    'SESSION_ATTACH_OWNER_CONFLICT': const OpsErrorCatalogEntry(
      errorId: 'E-2303',
      reasonCode: 'SESSION_ATTACH_OWNER_CONFLICT',
      description: 'Session attach ownership conflict.',
    ),
    'SESSION_ATTACH_RESTORE_FAILED': const OpsErrorCatalogEntry(
      errorId: 'E-2304',
      reasonCode: 'SESSION_ATTACH_RESTORE_FAILED',
      description: 'Session attach restore failed.',
    ),
    'START_GAME_NO_ACTIVE_GAME': const OpsErrorCatalogEntry(
      errorId: 'E-2305',
      reasonCode: 'START_GAME_NO_ACTIVE_GAME',
      description: 'Start game requested without active game.',
    ),
    'START_GAME_FAILED': const OpsErrorCatalogEntry(
      errorId: 'E-2306',
      reasonCode: 'START_GAME_FAILED',
      description: 'Start game failed.',
    ),
    'PAUSE_GAME_NO_ACTIVE_GAME': const OpsErrorCatalogEntry(
      errorId: 'E-2307',
      reasonCode: 'PAUSE_GAME_NO_ACTIVE_GAME',
      description: 'Pause requested with no active game.',
    ),
    'PAUSE_GAME_FAILED': const OpsErrorCatalogEntry(
      errorId: 'E-2308',
      reasonCode: 'PAUSE_GAME_FAILED',
      description: 'Pause game failed.',
    ),
    'RESUME_GAME_NO_ACTIVE_GAME': const OpsErrorCatalogEntry(
      errorId: 'E-2309',
      reasonCode: 'RESUME_GAME_NO_ACTIVE_GAME',
      description: 'Resume requested with no active game.',
    ),
    'RESUME_GAME_START_FROM_SAVE_FAILED': const OpsErrorCatalogEntry(
      errorId: 'E-2310',
      reasonCode: 'RESUME_GAME_START_FROM_SAVE_FAILED',
      description: 'Resume from saved state failed.',
    ),
    'RESUME_GAME_FAILED': const OpsErrorCatalogEntry(
      errorId: 'E-2311',
      reasonCode: 'RESUME_GAME_FAILED',
      description: 'Resume game failed.',
    ),
    'STOP_GAME_NO_ACTIVE_GAME': const OpsErrorCatalogEntry(
      errorId: 'E-2312',
      reasonCode: 'STOP_GAME_NO_ACTIVE_GAME',
      description: 'Stop requested with no active game.',
    ),
    'STOP_GAME_FAILED': const OpsErrorCatalogEntry(
      errorId: 'E-2313',
      reasonCode: 'STOP_GAME_FAILED',
      description: 'Stop game failed.',
    ),
    'END_SESSION_STOP_FAILED': const OpsErrorCatalogEntry(
      errorId: 'E-2314',
      reasonCode: 'END_SESSION_STOP_FAILED',
      description: 'End session failed while stopping game.',
    ),
    'START_GAME_GAME_NOT_ENTITLED': const OpsErrorCatalogEntry(
      errorId: 'E-2315',
      reasonCode: 'START_GAME_GAME_NOT_ENTITLED',
      description: 'Start game blocked by entitlement.',
    ),
    'START_GAME_ENTITLEMENT_PROFILE_UNKNOWN': const OpsErrorCatalogEntry(
      errorId: 'E-2316',
      reasonCode: 'START_GAME_ENTITLEMENT_PROFILE_UNKNOWN',
      description: 'Start game blocked by unknown entitlement profile.',
    ),
    'INSTALL_GAME_GAME_NOT_ENTITLED': const OpsErrorCatalogEntry(
      errorId: 'E-2317',
      reasonCode: 'INSTALL_GAME_GAME_NOT_ENTITLED',
      description: 'Install blocked by entitlement.',
    ),
    'INSTALL_GAME_ENTITLEMENT_PROFILE_UNKNOWN': const OpsErrorCatalogEntry(
      errorId: 'E-2318',
      reasonCode: 'INSTALL_GAME_ENTITLEMENT_PROFILE_UNKNOWN',
      description: 'Install blocked by unknown entitlement profile.',
    ),
    'ACTION_AFTER_TIMEOUT': const OpsErrorCatalogEntry(
      errorId: 'E-2401',
      reasonCode: 'ACTION_AFTER_TIMEOUT',
      description: 'Action observed after timeout.',
    ),
    'ADAPTIVE_DISABLED': const OpsErrorCatalogEntry(
      errorId: 'E-2402',
      reasonCode: 'ADAPTIVE_DISABLED',
      description: 'Adaptive difficulty is disabled.',
    ),
    'DATASET_EMPTY': const OpsErrorCatalogEntry(
      errorId: 'E-2403',
      reasonCode: 'DATASET_EMPTY',
      description: 'Dataset is empty.',
    ),
    'DECREASE_DIFFICULTY': const OpsErrorCatalogEntry(
      errorId: 'E-2404',
      reasonCode: 'DECREASE_DIFFICULTY',
      description: 'Adaptive decision: decrease difficulty.',
    ),
    'INCREASE_DIFFICULTY': const OpsErrorCatalogEntry(
      errorId: 'E-2405',
      reasonCode: 'INCREASE_DIFFICULTY',
      description: 'Adaptive decision: increase difficulty.',
    ),
    'KEEP_DIFFICULTY': const OpsErrorCatalogEntry(
      errorId: 'E-2406',
      reasonCode: 'KEEP_DIFFICULTY',
      description: 'Adaptive decision: keep difficulty.',
    ),
    'KEEP_DIFFICULTY_MIXED_SIGNALS': const OpsErrorCatalogEntry(
      errorId: 'E-2407',
      reasonCode: 'KEEP_DIFFICULTY_MIXED_SIGNALS',
      description: 'Adaptive decision: keep difficulty due to mixed signals.',
    ),
    'NO_PENDING_STEP': const OpsErrorCatalogEntry(
      errorId: 'E-2408',
      reasonCode: 'NO_PENDING_STEP',
      description: 'No pending step.',
    ),
    'RESTORE_SNAPSHOT': const OpsErrorCatalogEntry(
      errorId: 'E-2409',
      reasonCode: 'RESTORE_SNAPSHOT',
      description: 'Session restored from snapshot.',
    ),
    'STEP_TIMEOUT': const OpsErrorCatalogEntry(
      errorId: 'E-2410',
      reasonCode: 'STEP_TIMEOUT',
      description: 'Sequence step timeout.',
    ),
    'TARGET_MATCHED': const OpsErrorCatalogEntry(
      errorId: 'E-2411',
      reasonCode: 'TARGET_MATCHED',
      description: 'Target matched.',
    ),
    'TARGET_MISMATCH': const OpsErrorCatalogEntry(
      errorId: 'E-2412',
      reasonCode: 'TARGET_MISMATCH',
      description: 'Target mismatch.',
    ),
    'TASK_LABEL_GENERATED': const OpsErrorCatalogEntry(
      errorId: 'E-2413',
      reasonCode: 'TASK_LABEL_GENERATED',
      description: 'Task label generated.',
    ),
    'TRACE_CANDIDATES_NOT_FOUND': const OpsErrorCatalogEntry(
      errorId: 'E-2414',
      reasonCode: 'TRACE_CANDIDATES_NOT_FOUND',
      description: 'No trace candidates found.',
    ),
    'TRACE_FILE_NOT_FOUND': const OpsErrorCatalogEntry(
      errorId: 'E-2415',
      reasonCode: 'TRACE_FILE_NOT_FOUND',
      description: 'Trace file not found.',
    ),
    'TRACE_PATH_REQUIRED': const OpsErrorCatalogEntry(
      errorId: 'E-2416',
      reasonCode: 'TRACE_PATH_REQUIRED',
      description: 'Trace path is required.',
    ),
    'TRACE_UNINITIALIZED': const OpsErrorCatalogEntry(
      errorId: 'E-2417',
      reasonCode: 'TRACE_UNINITIALIZED',
      description: 'Trace loader uninitialized.',
    ),
    'UNKNOWN': const OpsErrorCatalogEntry(
      errorId: 'E-2418',
      reasonCode: 'UNKNOWN',
      description: 'Unknown reason code.',
    ),
    'ACTIVE_SESSION_MATCHED': const OpsErrorCatalogEntry(
      errorId: 'E-2501',
      reasonCode: 'ACTIVE_SESSION_MATCHED',
      description: 'Active session matched.',
    ),
    'COMMAND_PRECONDITION': const OpsErrorCatalogEntry(
      errorId: 'E-2502',
      reasonCode: 'COMMAND_PRECONDITION',
      description: 'Command precondition not met.',
    ),
    'CONTEXT_NOT_ENTRY_OR_RECONNECT': const OpsErrorCatalogEntry(
      errorId: 'E-2503',
      reasonCode: 'CONTEXT_NOT_ENTRY_OR_RECONNECT',
      description: 'Context is not entry/reconnect.',
    ),
    'END_SESSION_AND_ATTACH_OK': const OpsErrorCatalogEntry(
      errorId: 'E-2504',
      reasonCode: 'END_SESSION_AND_ATTACH_OK',
      description: 'End session and attach completed.',
    ),
    'END_SESSION_OVERRIDE': const OpsErrorCatalogEntry(
      errorId: 'E-2505',
      reasonCode: 'END_SESSION_OVERRIDE',
      description: 'End session override used.',
    ),
    'HANDOFF_GATE_CLEARED': const OpsErrorCatalogEntry(
      errorId: 'E-2506',
      reasonCode: 'HANDOFF_GATE_CLEARED',
      description: 'Handoff gate cleared.',
    ),
    'HANDOFF_KEEP_CURRENT': const OpsErrorCatalogEntry(
      errorId: 'E-2507',
      reasonCode: 'HANDOFF_KEEP_CURRENT',
      description: 'Handoff decision: keep current.',
    ),
    'HANDOFF_RESUME': const OpsErrorCatalogEntry(
      errorId: 'E-2508',
      reasonCode: 'HANDOFF_RESUME',
      description: 'Handoff decision: resume.',
    ),
    'HANDOFF_START_NEW': const OpsErrorCatalogEntry(
      errorId: 'E-2509',
      reasonCode: 'HANDOFF_START_NEW',
      description: 'Handoff decision: start new.',
    ),
    'PERSISTED_GATE_CLEARED': const OpsErrorCatalogEntry(
      errorId: 'E-2510',
      reasonCode: 'PERSISTED_GATE_CLEARED',
      description: 'Persisted gate cleared.',
    ),
    'RECOVERY_UNDER_WINDOW': const OpsErrorCatalogEntry(
      errorId: 'E-2511',
      reasonCode: 'RECOVERY_UNDER_WINDOW',
      description: 'Recovery is under allowed window.',
    ),
    'REMOTE_STATE_TERMINAL': const OpsErrorCatalogEntry(
      errorId: 'E-2512',
      reasonCode: 'REMOTE_STATE_TERMINAL',
      description: 'Remote state is terminal.',
    ),
    'REVIEW_DIALOG_OPENED': const OpsErrorCatalogEntry(
      errorId: 'E-2513',
      reasonCode: 'REVIEW_DIALOG_OPENED',
      description: 'Review dialog opened.',
    ),
    'RUNTIME_GATE_CLEARED': const OpsErrorCatalogEntry(
      errorId: 'E-2514',
      reasonCode: 'RUNTIME_GATE_CLEARED',
      description: 'Runtime gate cleared.',
    ),
    'SESSION_RECENTLY_TERMINAL': const OpsErrorCatalogEntry(
      errorId: 'E-2515',
      reasonCode: 'SESSION_RECENTLY_TERMINAL',
      description: 'Session recently reached terminal state.',
    ),
    'THERAPIST_CONFIRMED_END': const OpsErrorCatalogEntry(
      errorId: 'E-2516',
      reasonCode: 'THERAPIST_CONFIRMED_END',
      description: 'Therapist confirmed end session.',
    ),
    'THERAPIST_CONTINUE_UNFINISHED': const OpsErrorCatalogEntry(
      errorId: 'E-2517',
      reasonCode: 'THERAPIST_CONTINUE_UNFINISHED',
      description: 'Therapist chose continue unfinished.',
    ),
    'THERAPIST_KEEP_CURRENT': const OpsErrorCatalogEntry(
      errorId: 'E-2518',
      reasonCode: 'THERAPIST_KEEP_CURRENT',
      description: 'Therapist chose keep current.',
    ),
    'THERAPIST_REVIEW_REQUESTED': const OpsErrorCatalogEntry(
      errorId: 'E-2519',
      reasonCode: 'THERAPIST_REVIEW_REQUESTED',
      description: 'Therapist requested review.',
    ),
    'THERAPIST_START_NEW_DECISION': const OpsErrorCatalogEntry(
      errorId: 'E-2520',
      reasonCode: 'THERAPIST_START_NEW_DECISION',
      description: 'Therapist decision: start new.',
    ),
  };

  static final RegExp _reasonPattern = RegExp(
    r'\breason=([A-Za-z0-9_]+)\b',
    caseSensitive: false,
  );

  static OpsErrorCatalogEntry? lookupByReasonCode(String reasonCode) {
    final normalized = reasonCode.trim().toUpperCase();
    if (normalized.isEmpty) {
      return null;
    }
    return _entriesByReasonCode[normalized];
  }

  static String? tryBuildKnownReasonTag(String reasonCode) {
    final entry = lookupByReasonCode(reasonCode);
    if (entry == null) {
      return null;
    }
    return '${entry.errorId} (${entry.reasonCode})';
  }

  static String buildReasonTag(String reasonCode) {
    final normalized = reasonCode.trim().toUpperCase();
    if (normalized.isEmpty) {
      return '${unknown.errorId} (${unknown.reasonCode})';
    }

    final knownTag = tryBuildKnownReasonTag(normalized);
    if (knownTag != null) {
      return knownTag;
    }

    return 'E-0000 ($normalized)';
  }

  static String buildReasonSummary(String reasonCode) {
    final normalized = reasonCode.trim().toUpperCase();
    if (normalized.isEmpty) {
      return '${unknown.errorId} (${unknown.reasonCode}) ${unknown.description}';
    }

    final entry = lookupByReasonCode(normalized);
    if (entry != null) {
      return '${entry.errorId} (${entry.reasonCode}) ${entry.description}';
    }

    return 'E-0000 ($normalized) Unmapped reason code. See docs/31-Error-Code-Catalog.md.';
  }

  static String? tryExtractReasonCode(Object error) {
    final match = _reasonPattern.firstMatch(error.toString());
    if (match == null) {
      return null;
    }

    final value = (match.group(1) ?? '').trim().toUpperCase();
    if (value.isEmpty) {
      return null;
    }
    return value;
  }

  static String buildOperatorSummary({
    required Object error,
    String fallbackReasonCode = '',
  }) {
    final extractedReasonCode = tryExtractReasonCode(error);
    final resolvedReasonCode =
        extractedReasonCode == null || extractedReasonCode.trim().isEmpty
            ? fallbackReasonCode.trim().toUpperCase()
            : extractedReasonCode;

    return buildReasonSummary(resolvedReasonCode);
  }
}
