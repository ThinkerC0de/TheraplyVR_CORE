# Evidence Summary (RDM-005 + RDM-006)

Date: 2026-02-22
Roadmap items:
- RDM-005 (`CommandJournal` + `IdempotencyGuard`)
- RDM-006 (E2E reconnect/kill/restart + deterministic `END_SESSION` reason codes)

## Commands

1. `powershell -ExecutionPolicy Bypass -File .\\scripts\\unity_cli_validate.ps1 -Mode compile`
   - output: `docs/evidence/20260222_131144/commands/unity_cli_validate_compile.log`
   - compile log: `docs/evidence/20260222_131144/commands/unity_cli_compile.log`
   - result: PASS

2. Unity batchmode execute method:
   - `TheraplyCore.Editor.Automation.CriticalCommandIdempotencyValidation.RunCriticalCommandIdempotencyValidation`
   - runner log: `docs/evidence/20260222_131144/commands/unity_critical_command_idempotency_runner.log`
   - unity log: `docs/evidence/20260222_131144/commands/unity_critical_command_idempotency_validation.log`
   - result: PASS (`EXIT 0`)

3. Unity batchmode execute method:
   - `TheraplyCore.Editor.Automation.CriticalEndSessionResilienceValidation.RunCriticalEndSessionResilienceValidation`
   - runner log: `docs/evidence/20260222_131144/commands/unity_critical_end_session_resilience_runner.log`
   - unity log: `docs/evidence/20260222_131144/commands/unity_critical_end_session_resilience_validation.log`
   - result: PASS (`EXIT 0`)

## PASS markers

- Compile:
  - `[DONE] Unity CLI validation completed successfully.`
- RDM-005 idempotency validation:
  - `[CriticalCommandIdempotencyValidation] PASS: firstRuntimeHandlerCalls=1; restartRuntimeHandlerCalls=1; firstMessageJournalRecords=4; secondMessageJournalRecords=2`
- RDM-006 END_SESSION resilience validation:
  - `[CriticalEndSessionResilienceValidation] PASS: first=APPLIED:OK; conflict=REJECTED:SESSION_OWNERSHIP_CONFLICT; reconnect=APPLIED:OK; duplicateAfterRestart=APPLIED:DUPLICATE_COMMAND; final=APPLIED:OK`

## Implemented files

- `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/CommandJournal.cs`
- `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/GameCommandBus.cs`
- `unity-quest-template/Assets/_TheraplyCore/Editor/Automation/CriticalCommandIdempotencyValidation.cs`
- `unity-quest-template/Assets/_TheraplyCore/Editor/Automation/CriticalEndSessionResilienceValidation.cs`
- `docs/29-Controller-Authority-Resilience-And-Data-Roadmap.md`
- `docs/08-Session-Resilience-Worklog.md`