# Error Code Catalog

## Purpose
Provide stable operator-facing error ids (`E-xxxx`) without changing command wire contracts.

- wire protocol stays on existing `reasonCode` values,
- UI/logs can show both:
  - `errorId` for operator runbooks,
  - `reasonCode` for engineering diagnostics.

## Source Of Truth
- `contracts/error_catalog.json`

## Compatibility Rule
- do not rename existing `reasonCode` values on the wire,
- add new entries by extending the catalog,
- unknown reason codes map to fallback:
  - `E-0000 (UNMAPPED_REASON_CODE)`.

## Recommended Display Format
- `E-1201 (SESSION_LOCK_CONFLICT) Active session lock conflict.`

## Maintenance Flow
1. Add/modify mapping in `contracts/error_catalog.json`.
2. Sync Flutter mapping helper (`flutter_controller/lib/models/ops_error_catalog.dart`).
3. Add/adjust tests (`flutter_controller/test/ops_error_catalog_test.dart`).
4. Reference the new code in operator evidence notes when incident occurs.

## Coverage
Mapping now includes transport, envelope, session lock/ownership, handler failures, login entitlement gates, and runtime/presence operator signals:
- transport: `ACK_TIMEOUT`, `DISCONNECTED`, `NO_ACTIVE_TCP_ROUTE`, `TRANSPORT_CLOSED`, `SOCKET_CLOSED`
- envelope: `ENVELOPE_*`
- session: `SESSION_LOCK_CONFLICT`, `SESSION_OWNERSHIP_CONFLICT`, `SESSION_OWNERSHIP_MISSING`, `SESSION_NOT_ACTIVE`
- handler/idempotency: `NO_HANDLER`, `DESERIALIZE_FAILED`, `HANDLER_EXCEPTION`, `COMMAND_IN_PROGRESS`, `DUPLICATE_COMMAND`, `UNSPECIFIED`, `TEMPORARY_REJECT`
- entitlement/login: `ENTITLEMENT_RECORD_REQUIRED`, `APP_LICENSE_INACTIVE`, `ENTITLEMENT_BACKEND_UNAVAILABLE`, `ROLE_UNDEFINED`, `LEGACY_FALLBACK_*`, `APP_LICENSE_ACTIVE`
- runtime/presence: `APP_PAUSED`, `APP_RESUMED`, `APP_FOCUS_LOST`, `APP_FOCUS_GAINED`, `APP_QUIT`, `TCP_CLIENT_CONNECTED`, `NO_RUNTIME_SIGNAL_TIMEOUT`, `RUNTIME_SIGNAL_STALE`, `TCP_LINK_LOST`, `INITIAL_CONNECT`, `AUTO_RECONNECT`
- runtime/manual resync: `SESSION_ATTACH`, `SESSION_ID_REQUIRED`, `FIREBASE_DATA_SERVICE_UNAVAILABLE`, `MANUAL_RESYNC_EXCEPTION`, `UNINITIALIZED`, `ACTIVE_GAME_COMPLETED`, `ACTIVE_GAME_FAILED`, `TCP_CLIENT_DISCONNECTED`
- runtime attach internal failures: `SESSION_ATTACH_CONTEXT_MISSING`, `SESSION_ATTACH_SESSION_ID_REQUIRED`, `SESSION_ATTACH_OWNER_CONFLICT`, `SESSION_ATTACH_RESTORE_FAILED`
- runtime game-action internal failures: `START_GAME_*`, `PAUSE_GAME_*`, `RESUME_GAME_*`, `STOP_GAME_*`, `END_SESSION_STOP_FAILED`
- workflow decision markers: `ACTIVE_SESSION_MATCHED`, `COMMAND_PRECONDITION`, `HANDOFF_*`, `THERAPIST_*`, `RUNTIME_GATE_CLEARED`, `RECOVERY_UNDER_WINDOW`, `REMOTE_STATE_TERMINAL`, `SESSION_RECENTLY_TERMINAL`, `UNKNOWN`
- adaptive/sequence/trace pipeline: `ADAPTIVE_*`, `KEEP_DIFFICULTY*`, `TARGET_*`, `STEP_TIMEOUT`, `ACTION_AFTER_TIMEOUT`, `TASK_LABEL_GENERATED`, `TRACE_*`, `DATASET_EMPTY`, `RESTORE_SNAPSHOT`

## Scope Clarification
- `E-xxxx` is mandatory for operator-facing failure/signal reasons that can appear in mobile UI, runbooks, or ops evidence.
- Not every debug/telemetry string in the repo is a catalog code; internal-only diagnostics can remain unmapped unless promoted to operator workflow.
- Coverage/audit policy: `docs/32-Error-Code-Coverage-Policy.md`.
- Current scoped audit target (`flutter_controller/lib`, `_TheraplyCore/Games/Runtime`, `_TheraplyCore/Games/Contracts`, `scripts`) is fully mapped for detected `reasonCode` literals and runtime `InvalidOperationException("CODE")` identifiers.
