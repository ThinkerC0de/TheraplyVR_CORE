# Evidence Summary (RDM-011)

Date: 2026-02-22  
Roadmap item:
- RDM-011 (Tool telemetry stack)

## Commands

1. `powershell -ExecutionPolicy Bypass -File .\scripts\unity_cli_validate.ps1 -Mode compile`
   - output: `docs/evidence/20260222_165305/commands/unity_cli_validate_compile.log`
   - result: PASS

2. `Unity.exe -batchmode -nographics -quit -projectPath ... -executeMethod TheraplyCore.Editor.Automation.ToolTelemetryStackValidation.RunToolTelemetryStackValidation`
   - output:
     - `docs/evidence/20260222_165305/commands/unity_tool_telemetry_stack_validation_runner.log`
     - `docs/evidence/20260222_165305/commands/unity_tool_telemetry_stack_validation.log`
   - result: PASS

## PASS markers

- compile marker:
  - `[OK] Step 'compile' passed.`
- tool telemetry stack marker:
  - `docs/evidence/20260222_165305/commands/unity_tool_telemetry_stack_validation_markers.log`
  - `[ToolTelemetryStackValidation] PASS: events=6; toolEvents=6; maxSequence=6; eventTypes=TOOL_GRIP_END,TOOL_GRIP_HOLD,TOOL_GRIP_START,TOOL_IMPACT_HIT,TOOL_IMPACT_INVALID,TOOL_IMPACT_MISS`

## Implemented files

- `unity-quest-template/Assets/_TheraplyCore/Interactions/InteractionEventBridge.cs`
- `unity-quest-template/Assets/_TheraplyCore/Interactions/TargetValidationZone.cs`
- `unity-quest-template/Assets/_TheraplyCore/Interactions/TargetValidationZone.cs.meta`
- `unity-quest-template/Assets/_TheraplyCore/Interactions/ToolGripTracker.cs`
- `unity-quest-template/Assets/_TheraplyCore/Interactions/ToolGripTracker.cs.meta`
- `unity-quest-template/Assets/_TheraplyCore/Interactions/ToolImpactProbe.cs`
- `unity-quest-template/Assets/_TheraplyCore/Interactions/ToolImpactProbe.cs.meta`
- `unity-quest-template/Assets/_Examples/Scripts/QuestPointerClickInteractor.cs`
- `unity-quest-template/Assets/_Examples/Scripts/DemoCubeGameModule.cs`
- `unity-quest-template/Assets/_Examples/Scripts/PulseTargetsGameModule.cs`
- `unity-quest-template/Assets/_TheraplyCore/Editor/Automation/ToolTelemetryStackValidation.cs`
- `unity-quest-template/Assets/_TheraplyCore/Editor/Automation/ToolTelemetryStackValidation.cs.meta`
- `docs/29-Controller-Authority-Resilience-And-Data-Roadmap.md`
- `docs/08-Session-Resilience-Worklog.md`
