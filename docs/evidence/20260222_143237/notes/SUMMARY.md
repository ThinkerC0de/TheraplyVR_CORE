# Evidence Summary (RDM-006 ACK transport addendum)

Date: 2026-02-22
Roadmap item:
- RDM-006 (ACK transport verification over real TCP route outside batchmode)

## Commands

1. `powershell -ExecutionPolicy Bypass -File .\\scripts\\unity_cli_validate.ps1 -Mode compile`
   - output: `docs/evidence/20260222_143237/commands/unity_cli_validate_compile.log`
   - compile log: `docs/evidence/20260222_143237/commands/unity_cli_compile.log`
   - result: PASS

2. Unity non-batch execute method:
   - `TheraplyCore.Editor.Automation.CriticalEndSessionAckTransportValidation.RunCriticalEndSessionAckTransportValidation`
   - runner log: `docs/evidence/20260222_143237/commands/unity_critical_end_session_ack_transport_non_batch_runner.log`
   - unity log: `docs/evidence/20260222_143237/commands/unity_critical_end_session_ack_transport_non_batch.log`
   - result: PASS (`EXIT 0`)

## PASS markers

- non-batch mode marker:
  - `BatchMode: 0`
- validation marker:
  - `[CriticalEndSessionAckTransportValidation] PASS: first=ACK:OK; conflict=NACK:SESSION_OWNERSHIP_CONFLICT; reconnect=ACK:OK; duplicateAfterRestart=ACK:DUPLICATE_COMMAND; final=ACK:OK; transport=tcp; mode=non-batch`
- runner exit marker:
  - `[EXIT] 0`
