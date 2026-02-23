# Evidence Summary (OPS-003 real Quest trace closure run)

Date: 2026-02-23  
Roadmap item:
- OPS-003 (Real-trace-required run + operator intake ACK trail)

## Commands

1. `powershell -ExecutionPolicy Bypass -File .\scripts\ops_dataset_trace_collect.ps1 -OutputDirectory .\docs\evidence\20260223_101542\artifacts\ops_trace_collect -AllowNoDatasetEvents`
   - output:
     - `docs/evidence/20260223_101542/commands/ops_dataset_trace_collect.log`
   - result: PASS (`readyForOpsTraceValidation=TRUE`, `preparedSessionDatasetEvents=6`)

2. `powershell -ExecutionPolicy Bypass -File .\scripts\unity_ops_dataset_trace_export_validate.ps1 -TraceInputPath C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260223_101542\artifacts\ops_trace_collect\prepared\dataset_trace_all_sessions.ndjson -TraceSessionId mobile-dLxoPFss0wCV6nezOZfb-1771838008060 -RequireProvidedTrace -ExportOutputDirectory .\docs\evidence\20260223_101542\artifacts\ops_dataset_trace_export_real_trace -ExportName ops003_real_trace`
   - output:
     - `docs/evidence/20260223_101542/commands/unity_operational_dataset_trace_export_validation_real_trace.log`
   - result: FAIL (`INSUFFICIENT_EVENT_COUNT`, `totalEvents=6`, `readyForTraining=false`)

3. `powershell -ExecutionPolicy Bypass -File .\scripts\ops_pretrain_handoff_package.ps1 -ExportDirectory .\docs\evidence\20260223_101542\artifacts\ops_dataset_trace_export_real_trace -HandoffOutputDirectory .\docs\evidence\20260223_101542\artifacts\ops_pretrain_handoff -PackageName ops003_pretrain_handoff_real_trace -OperatorId ops_cli -SourceTracePath C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260223_101542\artifacts\ops_trace_collect\prepared\dataset_trace_all_sessions.ndjson -SessionId mobile-dLxoPFss0wCV6nezOZfb-1771838008060`
   - output:
     - `docs/evidence/20260223_101542/commands/ops_pretrain_handoff_package.log`
   - result: PASS

4. `powershell -ExecutionPolicy Bypass -File .\scripts\ops_pretrain_handoff_ack_confirm.ps1 -HandoffPackageDirectory .\docs\evidence\20260223_101542\artifacts\ops_pretrain_handoff\ops003_pretrain_handoff_real_trace -IntakeTicketId OPS-003-REALTRACE-20260223-01 -IntakeOperatorId ops_cli -AckStatus NEEDS_INFO -AckNotes "Trace replay validated, but pretrain export is not ready: INSUFFICIENT_EVENT_COUNT (totalEvents=6). Collect additional gameplay events and rerun OPS-003."`
   - output:
     - `docs/evidence/20260223_101542/commands/ops_pretrain_handoff_ack_confirm.log`
   - result: PASS (`intakeAckStatus=NEEDS_INFO`, `ackTrailCount=1`)

## Result

- `OK` Real Quest trace auto-discovery and selection completed with dataset-compatible envelope.
- `OK` OPS-003 required-trace lane executed on real trace and produced operational artifacts.
- `OK` Operator intake ACK trail persisted in handoff package (`intakeAck`, `ackTrail`).
- `PARTIAL` Training-readiness gate blocked close-to-training outcome for this run:
  - `reasonCode=INSUFFICIENT_EVENT_COUNT`
  - `readyForTraining=false`
  - gate defaults still unmet:
    - `minEvents=9` (observed `6`)
    - `minTaskRuns=3` (observed `1`)

## Artifacts

- collector report:
  - `docs/evidence/20260223_101542/artifacts/ops_trace_collect/reports/trace_discovery_report.json`
  - `docs/evidence/20260223_101542/artifacts/ops_trace_collect/reports/trace_discovery_report.md`
- prepared trace:
  - `docs/evidence/20260223_101542/artifacts/ops_trace_collect/prepared/dataset_trace_all_sessions.ndjson`
  - `docs/evidence/20260223_101542/artifacts/ops_trace_collect/prepared/dataset_trace_selected_session.ndjson`
- export artifacts:
  - `docs/evidence/20260223_101542/artifacts/ops_dataset_trace_export_real_trace/canonical_events.ndjson`
  - `docs/evidence/20260223_101542/artifacts/ops_dataset_trace_export_real_trace/pretrain_manifest.json`
  - `docs/evidence/20260223_101542/artifacts/ops_dataset_trace_export_real_trace/operator_report.md`
- handoff + ACK artifacts:
  - `docs/evidence/20260223_101542/artifacts/ops_pretrain_handoff/ops003_pretrain_handoff_real_trace/checksums.sha256`
  - `docs/evidence/20260223_101542/artifacts/ops_pretrain_handoff/ops003_pretrain_handoff_real_trace/handoff_manifest.json`
  - `docs/evidence/20260223_101542/artifacts/ops_pretrain_handoff/ops003_pretrain_handoff_real_trace/intake_ack_template.json`
  - `docs/evidence/20260223_101542/artifacts/ops_pretrain_handoff/ops003_pretrain_handoff_real_trace/intake_acknowledgement.json`
  - `docs/evidence/20260223_101542/artifacts/ops_pretrain_handoff/ops003_pretrain_handoff_real_trace/intake_acknowledgement.md`

## Implemented file (this run)

- `scripts/unity_ops_dataset_trace_export_validate.ps1` (path normalization for output/log/trace input)
- `docs/29-Controller-Authority-Resilience-And-Data-Roadmap.md`
- `docs/08-Session-Resilience-Worklog.md`
