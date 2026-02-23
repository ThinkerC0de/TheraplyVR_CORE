# RDM-004 DurableEventOutbox validation summary (2026-02-22)

## Scope
- Unity durable local outbox hardening (`DurableEventOutbox`) with explicit outbox metrics (`pending`, `inFlight`, `failed`, `replayed`).
- Replay path validation for offline -> restart durable store -> reconnect.

## Commands
- `powershell -ExecutionPolicy Bypass -File .\scripts\unity_cli_validate.ps1 -Mode compile`
  - PASS
  - logs:
    - `docs/evidence/20260222_120301/commands/unity_cli_validate_compile.log`
    - `docs/evidence/20260222_120301/commands/unity_cli_compile.log`
- `Unity.exe -batchmode -nographics -projectPath unity-quest-template -executeMethod TheraplyCore.Editor.Automation.FirebaseNetworkValidation.RunFirebaseNetworkValidation`
  - Phase markers captured in log:
    - online: `accepted=12, outboxSynced=12, pending=0, inFlight=0`
    - offline: `failureDelta=1, retryDelta=4, pending=0, failed=4`
    - restart: `pending=16, inFlight=0, failed=0, replayed=0`
    - reconnect sync marker: `Outbox synced 16 events (duplicates acknowledged: 12)`
  - log:
    - `docs/evidence/20260222_120301/commands/unity_firebase_network_validation.log`
  - execution caveat:
    - no-quit mode did not auto-exit; command wrapper timed out and Unity process was terminated after markers were captured.
    - note: `docs/evidence/20260222_120301/commands/unity_firebase_network_validation_timeout_note.txt`

## Result
- Compile validation PASS.
- Runtime log confirms offline append + post-restart replay flush path with outbox failure/replay signal visibility.
