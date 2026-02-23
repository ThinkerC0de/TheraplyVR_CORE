# Evidence Summary (RDM-010)

Date: 2026-02-22  
Roadmap item:
- RDM-010 (Interaction bridge i event schema we wszystkich aktywnych grach)

## Commands

1. `powershell -ExecutionPolicy Bypass -File .\scripts\unity_cli_validate.ps1 -Mode compile`
   - output: `docs/evidence/20260222_154216/commands/unity_cli_validate_compile.log`
   - result: PASS

2. `Unity.exe -batchmode -nographics -quit -projectPath ... -executeMethod TheraplyCore.Editor.Automation.InteractionEventSchemaValidation.RunInteractionEventSchemaValidation`
   - output:
     - `docs/evidence/20260222_154216/commands/unity_interaction_event_schema_validation_runner.log`
     - `docs/evidence/20260222_154216/commands/unity_interaction_event_schema_validation.log`
   - result: PASS

## PASS markers

- compile marker:
  - `[OK] Step 'compile' passed.`
- interaction schema marker:
  - `docs/evidence/20260222_154216/commands/unity_interaction_event_schema_validation_markers.log`
  - `[InteractionEventSchemaValidation] PASS: events=19; maxSequence=19; gameIds=demo_cube_clicker,pulse_target_tap,smoke_test_game`

## Implemented files

- `unity-quest-template/Assets/_TheraplyCore/Interactions/InteractionEventBridge.cs`
- `unity-quest-template/Assets/_TheraplyCore/Interactions/InteractionEventBridge.cs.meta`
- `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/GameModuleBase.cs`
- `unity-quest-template/Assets/_TheraplyCore/Interactions/PointerTelemetryService.cs`
- `unity-quest-template/Assets/_Examples/Scripts/QuestPointerClickInteractor.cs`
- `unity-quest-template/Assets/_Examples/Scripts/DemoCubeGameModule.cs`
- `unity-quest-template/Assets/_Examples/Scripts/PulseTargetsGameModule.cs`
- `unity-quest-template/Assets/_TheraplyCore/Editor/Automation/InteractionEventSchemaValidation.cs`
- `unity-quest-template/Assets/_TheraplyCore/Editor/Automation/InteractionEventSchemaValidation.cs.meta`
- `docs/29-Controller-Authority-Resilience-And-Data-Roadmap.md`
- `docs/08-Session-Resilience-Worklog.md`
