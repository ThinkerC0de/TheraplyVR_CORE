# Evidence Summary (OPS-001)

Date: 2026-02-22  
Roadmap item:
- OPS-001 (Operacyjny lane pod trening i rollout)

## Commands

1. `powershell -ExecutionPolicy Bypass -File .\scripts\unity_cli_validate.ps1 -Mode compile`
   - output:
     - `docs/evidence/20260222_185857/commands/unity_cli_validate_compile.log`
     - `docs/evidence/20260222_185857/commands/unity_cli_compile.log`
   - result: PASS

2. `powershell -ExecutionPolicy Bypass -File .\scripts\unity_ops_dataset_export_validate.ps1 -LogFile docs/evidence/20260222_185857/commands/unity_operational_dataset_export_validation.log -ExportOutputDirectory C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260222_185857\artifacts\ops_dataset_export -ExportName ops001_pretrain_export`
   - output:
     - `docs/evidence/20260222_185857/commands/unity_operational_dataset_export_validation_runner.log`
     - `docs/evidence/20260222_185857/commands/unity_operational_dataset_export_validation.log`
   - result: PASS

## PASS markers

- compile marker:
  - `[OK] Step 'compile' passed.`
- operational dataset export marker:
  - `docs/evidence/20260222_185857/commands/unity_operational_dataset_export_validation_markers.log`
  - `[OperationalDatasetExportValidation] PASS: events=9; taskRuns=3; labels=3; summaries=3; sourceOfTruthViolations=0; ownershipViolations=0; taskRunConsistencyViolations=0; exportReason=EXPORT_READY_FOR_TRAINING; qualityReason=DATASET_READY_FOR_TRAINING`

## Exported operator artifacts

- `docs/evidence/20260222_185857/artifacts/ops_dataset_export/canonical_events.ndjson`
- `docs/evidence/20260222_185857/artifacts/ops_dataset_export/pretrain_manifest.json`
- `docs/evidence/20260222_185857/artifacts/ops_dataset_export/operator_report.md`

## Implemented files

- `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/TaskDatasetPreTrainExporter.cs`
- `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/TaskDatasetPreTrainExporter.cs.meta`
- `unity-quest-template/Assets/_TheraplyCore/Editor/Automation/OperationalDatasetExportValidation.cs`
- `unity-quest-template/Assets/_TheraplyCore/Editor/Automation/OperationalDatasetExportValidation.cs.meta`
- `scripts/unity_ops_dataset_export_validate.ps1`
- `docs/29-Controller-Authority-Resilience-And-Data-Roadmap.md`
- `docs/08-Session-Resilience-Worklog.md`
