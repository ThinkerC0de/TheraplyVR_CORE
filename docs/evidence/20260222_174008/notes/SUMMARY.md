# Evidence Summary (RDM-013)

Date: 2026-02-22  
Roadmap item:
- RDM-013 (Adaptive difficulty + label pipeline)

## Commands

1. `powershell -ExecutionPolicy Bypass -File .\scripts\unity_cli_validate.ps1 -Mode compile`
   - output:
     - `docs/evidence/20260222_174008/commands/unity_cli_validate_compile.log`
     - `docs/evidence/20260222_174008/commands/unity_cli_compile.log`
   - result: PASS

2. `Unity.exe -batchmode -nographics -quit -projectPath ... -executeMethod TheraplyCore.Editor.Automation.AdaptiveDifficultyLabelPipelineValidation.RunAdaptiveDifficultyLabelPipelineValidation`
   - output:
     - `docs/evidence/20260222_174008/commands/unity_adaptive_difficulty_label_pipeline_validation_runner.log`
     - `docs/evidence/20260222_174008/commands/unity_adaptive_difficulty_label_pipeline_validation.log`
   - result: PASS

3. `flutter test test/therapist_session_settings_test.dart test/therapist_session_settings_service_test.dart`
   - output: `docs/evidence/20260222_174008/commands/flutter_controller_settings_tests.log`
   - result: PASS

4. `flutter analyze`
   - output: `docs/evidence/20260222_174008/commands/flutter_controller_flutter_analyze.log`
   - result: PASS

## PASS markers

- compile marker:
  - `[OK] Step 'compile' passed.`
- adaptive difficulty + label pipeline marker:
  - `docs/evidence/20260222_174008/commands/unity_adaptive_difficulty_label_pipeline_validation_markers.log`
  - `[AdaptiveDifficultyLabelPipelineValidation] PASS: events=4; adaptiveEvents=2; labelEvents=2; maxSequence=4; eventTypes=ADAPTIVE_DIFFICULTY_ADJUSTED,TASK_LABEL_GENERATED`

## Implemented files

- `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/AdaptiveDifficultyController.cs`
- `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/AdaptiveDifficultyController.cs.meta`
- `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/TaskLabelPipeline.cs`
- `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/TaskLabelPipeline.cs.meta`
- `unity-quest-template/Assets/_Examples/Scripts/PulseTargetsGameModule.cs`
- `unity-quest-template/Assets/_TheraplyCore/Editor/Automation/AdaptiveDifficultyLabelPipelineValidation.cs`
- `unity-quest-template/Assets/_TheraplyCore/Editor/Automation/AdaptiveDifficultyLabelPipelineValidation.cs.meta`
- `flutter_controller/lib/models/therapist_session_settings.dart`
- `flutter_controller/lib/screens/control_screen.dart`
- `flutter_controller/lib/screens/students_screen.dart`
- `flutter_controller/test/therapist_session_settings_test.dart`
- `flutter_controller/test/therapist_session_settings_service_test.dart`
- `docs/29-Controller-Authority-Resilience-And-Data-Roadmap.md`
- `docs/08-Session-Resilience-Worklog.md`
