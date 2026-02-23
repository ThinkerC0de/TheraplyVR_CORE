# Evidence Summary (RDM-014)

Date: 2026-02-22  
Roadmap item:
- RDM-014 (Dataset quality validation before training)

## Commands

1. `powershell -ExecutionPolicy Bypass -File .\scripts\unity_cli_validate.ps1 -Mode compile`
   - output:
     - `docs/evidence/20260222_183204/commands/unity_cli_validate_compile.log`
     - `docs/evidence/20260222_183204/commands/unity_cli_compile.log`
   - result: PASS

2. `Unity.exe -batchmode -nographics -quit -projectPath ... -executeMethod TheraplyCore.Editor.Automation.DatasetQualityTrainingValidation.RunDatasetQualityTrainingValidation`
   - output:
     - `docs/evidence/20260222_183204/commands/unity_dataset_quality_training_validation_runner.log`
     - `docs/evidence/20260222_183204/commands/unity_dataset_quality_training_validation.log`
   - result: PASS

## PASS markers

- compile marker:
  - `[OK] Step 'compile' passed.`
- dataset quality training marker:
  - `docs/evidence/20260222_183204/commands/unity_dataset_quality_training_validation_markers.log`
  - `[DatasetQualityTrainingValidation] PASS: events=9; taskRuns=3; labels=3; summaries=3; adaptiveEvents=3; labelCoverage=1.000; avgConfidence=0.859; qualityScore=1.000; reason=DATASET_READY_FOR_TRAINING`

## Implemented files

- `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/TaskDatasetQualityGate.cs`
- `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/TaskDatasetQualityGate.cs.meta`
- `unity-quest-template/Assets/_TheraplyCore/Editor/Automation/DatasetQualityTrainingValidation.cs`
- `unity-quest-template/Assets/_TheraplyCore/Editor/Automation/DatasetQualityTrainingValidation.cs.meta`
- `docs/29-Controller-Authority-Resilience-And-Data-Roadmap.md`
- `docs/08-Session-Resilience-Worklog.md`
