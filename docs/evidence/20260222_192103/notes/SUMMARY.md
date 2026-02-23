# Evidence Summary (OPS-002)

Date: 2026-02-22  
Roadmap item:
- OPS-002 (Trace-integrated dataset export + pre-train handoff package SOP)

## Commands

1. `powershell -ExecutionPolicy Bypass -File .\scripts\unity_cli_validate.ps1 -Mode compile -LogDirectory .\docs\evidence\20260222_192103\commands`
   - output:
     - `docs/evidence/20260222_192103/commands/unity_cli_validate_compile.log`
     - `docs/evidence/20260222_192103/commands/unity_cli_compile.log`
   - result: PASS

2. `powershell -ExecutionPolicy Bypass -File .\scripts\unity_ops_dataset_trace_export_validate.ps1 -LogFile .\docs\evidence\20260222_192103\commands\unity_operational_dataset_trace_export_validation.log -ExportOutputDirectory C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260222_192103\artifacts\ops_dataset_trace_export -ExportName ops002_pretrain_export`
   - output:
     - `docs/evidence/20260222_192103/commands/unity_operational_dataset_trace_export_validation_runner.log`
     - `docs/evidence/20260222_192103/commands/unity_operational_dataset_trace_export_validation.log`
   - result: PASS

3. `powershell -ExecutionPolicy Bypass -File .\scripts\ops_pretrain_handoff_package.ps1 -ExportDirectory .\docs\evidence\20260222_192103\artifacts\ops_dataset_trace_export -HandoffOutputDirectory .\docs\evidence\20260222_192103\artifacts\ops_dataset_trace_export\handoff -PackageName ops002_pretrain_handoff -OperatorId ops_validation -SourceTracePath .\docs\evidence\20260222_192103\artifacts\ops_dataset_trace_export\session_trace_input.ndjson -SessionId validation_session -CreateZip`
   - output:
     - `docs/evidence/20260222_192103/commands/ops_pretrain_handoff_package.log`
   - result: PASS

4. `powershell -ExecutionPolicy Bypass -File .\scripts\unity_ops_dataset_trace_export_validate.ps1 -LogFile .\docs\evidence\20260222_192103\commands\unity_operational_dataset_trace_export_validation_provided.log -ExportOutputDirectory C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260222_192103\artifacts\ops_dataset_trace_export_provided -ExportName ops002_pretrain_export_provided -TraceInputPath C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260222_192103\artifacts\ops_dataset_trace_export\session_trace_input.ndjson -TraceSessionId validation_session`
   - output:
     - `docs/evidence/20260222_192103/commands/unity_operational_dataset_trace_export_validation_provided_runner.log`
     - `docs/evidence/20260222_192103/commands/unity_operational_dataset_trace_export_validation_provided.log`
   - result: PASS

## PASS markers

- compile marker:
  - `docs/evidence/20260222_192103/commands/unity_cli_validate_compile_markers.log`
  - `[OK] Step 'compile' passed. Log: .\docs\evidence\20260222_192103\commands\unity_cli_compile.log`

- operational dataset trace export marker:
  - `docs/evidence/20260222_192103/commands/unity_operational_dataset_trace_export_validation_markers.log`
  - `[OperationalDatasetTraceExportValidation] PASS: events=9; taskRuns=3; labels=3; summaries=3; sourceOfTruthViolations=0; ownershipViolations=0; taskRunConsistencyViolations=0; traceImported=9; traceLines=9; traceReason=TRACE_LOADED; traceSource=GENERATED_FIXTURE; exportReason=EXPORT_READY_FOR_TRAINING; qualityReason=DATASET_READY_FOR_TRAINING`

- operational dataset trace export marker (provided trace input path):
  - `docs/evidence/20260222_192103/commands/unity_operational_dataset_trace_export_validation_provided_markers.log`
  - `[OperationalDatasetTraceExportValidation] PASS: events=9; taskRuns=3; labels=3; summaries=3; sourceOfTruthViolations=0; ownershipViolations=0; taskRunConsistencyViolations=0; traceImported=9; traceLines=9; traceReason=TRACE_LOADED; traceSource=PROVIDED_TRACE; exportReason=EXPORT_READY_FOR_TRAINING; qualityReason=DATASET_READY_FOR_TRAINING`

## Exported artifacts

- `docs/evidence/20260222_192103/artifacts/ops_dataset_trace_export/session_trace_input.ndjson`
- `docs/evidence/20260222_192103/artifacts/ops_dataset_trace_export/canonical_events.ndjson`
- `docs/evidence/20260222_192103/artifacts/ops_dataset_trace_export/pretrain_manifest.json`
- `docs/evidence/20260222_192103/artifacts/ops_dataset_trace_export/operator_report.md`
- `docs/evidence/20260222_192103/artifacts/ops_dataset_trace_export_provided/pretrain_manifest.json`
- `docs/evidence/20260222_192103/artifacts/ops_dataset_trace_export_provided/operator_report.md`

## Handoff package artifacts

- `docs/evidence/20260222_192103/artifacts/ops_dataset_trace_export/handoff/ops002_pretrain_handoff/checksums.sha256`
- `docs/evidence/20260222_192103/artifacts/ops_dataset_trace_export/handoff/ops002_pretrain_handoff/handoff_manifest.json`
- `docs/evidence/20260222_192103/artifacts/ops_dataset_trace_export/handoff/ops002_pretrain_handoff/handoff_sop_checklist.md`
- `docs/evidence/20260222_192103/artifacts/ops_dataset_trace_export/handoff/ops002_pretrain_handoff.zip`

## Implemented files

- `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/TaskDatasetSessionTraceLoader.cs`
- `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/TaskDatasetSessionTraceLoader.cs.meta`
- `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/TaskDatasetPreTrainExporter.cs`
- `unity-quest-template/Assets/_TheraplyCore/Editor/Automation/OperationalDatasetTraceExportValidation.cs`
- `unity-quest-template/Assets/_TheraplyCore/Editor/Automation/OperationalDatasetTraceExportValidation.cs.meta`
- `scripts/unity_ops_dataset_trace_export_validate.ps1`
- `scripts/ops_pretrain_handoff_package.ps1`
- `docs/30-OPS-Pretrain-Handoff-SOP.md`
- `docs/29-Controller-Authority-Resilience-And-Data-Roadmap.md`
- `docs/08-Session-Resilience-Worklog.md`
