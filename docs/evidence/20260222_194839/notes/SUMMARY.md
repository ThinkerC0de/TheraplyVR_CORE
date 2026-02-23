# Evidence Summary (OPS-003)

Date: 2026-02-22  
Roadmap item:
- OPS-003 (Real-trace-required validation mode + operator intake ACK trail)

## Commands

1. `powershell -ExecutionPolicy Bypass -File .\scripts\unity_ops_dataset_trace_export_validate.ps1 -LogFile .\docs\evidence\20260222_194839\commands\unity_operational_dataset_trace_export_validation_real_trace.log -ExportOutputDirectory C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260222_194839\artifacts\ops_dataset_trace_export_real_trace -ExportName ops003_pretrain_export_real_trace -TraceInputPath C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260222_192103\artifacts\ops_dataset_trace_export\session_trace_input.ndjson -TraceSessionId validation_session -RequireProvidedTrace`
   - output:
     - `docs/evidence/20260222_194839/commands/unity_operational_dataset_trace_export_validation_real_trace_runner.log`
     - `docs/evidence/20260222_194839/commands/unity_operational_dataset_trace_export_validation_real_trace.log`
   - result: PASS

2. `powershell -ExecutionPolicy Bypass -File .\scripts\ops_pretrain_handoff_package.ps1 -ExportDirectory .\docs\evidence\20260222_194839\artifacts\ops_dataset_trace_export_real_trace -HandoffOutputDirectory .\docs\evidence\20260222_194839\artifacts\ops_dataset_trace_export_real_trace\handoff -PackageName ops003_pretrain_handoff -OperatorId ops_validation -SourceTracePath C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260222_192103\artifacts\ops_dataset_trace_export\session_trace_input.ndjson -SessionId validation_session -CreateZip`
   - output:
     - `docs/evidence/20260222_194839/commands/ops_pretrain_handoff_package.log`
   - result: PASS

3. `powershell -ExecutionPolicy Bypass -File .\scripts\ops_pretrain_handoff_ack_confirm.ps1 -HandoffPackageDirectory .\docs\evidence\20260222_194839\artifacts\ops_dataset_trace_export_real_trace\handoff\ops003_pretrain_handoff -IntakeTicketId OPS003-INTAKE-20260222 -IntakeOperatorId ops_intake_validation -AckStatus ACKNOWLEDGED -AckNotes "Validated on provided trace path in OPS-003 lane."`
   - output:
     - `docs/evidence/20260222_194839/commands/ops_pretrain_handoff_ack_confirm.log`
   - result: PASS

4. `powershell -ExecutionPolicy Bypass -File .\scripts\unity_ops_dataset_trace_export_validate.ps1 -RequireProvidedTrace`
   - output:
     - `docs/evidence/20260222_194839/commands/unity_ops_dataset_trace_require_provided_guard.log`
   - result: EXPECTED_FAIL (`TraceInputPath is required when -RequireProvidedTrace is set.`)

## PASS markers

- operational dataset trace export marker:
  - `docs/evidence/20260222_194839/commands/unity_operational_dataset_trace_export_validation_real_trace_markers.log`
  - `[OperationalDatasetTraceExportValidation] PASS: ... traceSource=PROVIDED_TRACE ... requireProvidedTrace=TRUE`

- provided-trace guard marker:
  - `docs/evidence/20260222_194839/commands/unity_ops_dataset_trace_require_provided_guard_markers.log`
  - `EXPECTED_FAIL`

- intake ACK marker:
  - `docs/evidence/20260222_194839/commands/ops_pretrain_handoff_ack_confirm_markers.log`
  - `intakeAckStatus=ACKNOWLEDGED`
  - `ackTrailCount=1`

## Exported artifacts

- `docs/evidence/20260222_194839/artifacts/ops_dataset_trace_export_real_trace/canonical_events.ndjson`
- `docs/evidence/20260222_194839/artifacts/ops_dataset_trace_export_real_trace/pretrain_manifest.json`
- `docs/evidence/20260222_194839/artifacts/ops_dataset_trace_export_real_trace/operator_report.md`

## Handoff + ACK artifacts

- `docs/evidence/20260222_194839/artifacts/ops_dataset_trace_export_real_trace/handoff/ops003_pretrain_handoff/checksums.sha256`
- `docs/evidence/20260222_194839/artifacts/ops_dataset_trace_export_real_trace/handoff/ops003_pretrain_handoff/handoff_manifest.json`
- `docs/evidence/20260222_194839/artifacts/ops_dataset_trace_export_real_trace/handoff/ops003_pretrain_handoff/handoff_sop_checklist.md`
- `docs/evidence/20260222_194839/artifacts/ops_dataset_trace_export_real_trace/handoff/ops003_pretrain_handoff/intake_ack_template.json`
- `docs/evidence/20260222_194839/artifacts/ops_dataset_trace_export_real_trace/handoff/ops003_pretrain_handoff/intake_acknowledgement.json`
- `docs/evidence/20260222_194839/artifacts/ops_dataset_trace_export_real_trace/handoff/ops003_pretrain_handoff/intake_acknowledgement.md`
- `docs/evidence/20260222_194839/artifacts/ops_dataset_trace_export_real_trace/handoff/ops003_pretrain_handoff.zip`

## Notes

- Workspace does not currently contain a Quest-captured production-like trace path outside existing validation fixtures.
- OPS-003 flow is fully validated for provided-trace-required mode and ACK trail persistence; final production run should be repeated with real Quest trace input path.

## Implemented files

- `unity-quest-template/Assets/_TheraplyCore/Editor/Automation/OperationalDatasetTraceExportValidation.cs`
- `scripts/unity_ops_dataset_trace_export_validate.ps1`
- `scripts/ops_pretrain_handoff_package.ps1`
- `scripts/ops_pretrain_handoff_ack_confirm.ps1`
- `docs/30-OPS-Pretrain-Handoff-SOP.md`
- `docs/29-Controller-Authority-Resilience-And-Data-Roadmap.md`
- `docs/08-Session-Resilience-Worklog.md`
