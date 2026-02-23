# Evidence Summary (OPS-003 trace collector lane)

Date: 2026-02-22  
Roadmap item:
- OPS-003 (trace intake automation: collect/prepare from Quest or local paths)

## Commands

1. `powershell -ExecutionPolicy Bypass -File .\scripts\ops_dataset_trace_collect.ps1 -OutputDirectory .\docs\evidence\20260222_202038\artifacts\ops_trace_collect -AllowNoDatasetEvents`
   - output:
     - `docs/evidence/20260222_202038/commands/ops_dataset_trace_collect.log`
     - `docs/evidence/20260222_202038/artifacts/ops_trace_collect/reports/trace_discovery_report.json`
   - result: PASS (`TRACE_CANDIDATES_NOT_FOUND` on currently connected Quest `RFCY9019KYF`)

2. `powershell -ExecutionPolicy Bypass -File .\scripts\ops_dataset_trace_collect.ps1 -OutputDirectory .\docs\evidence\20260222_202038\artifacts\ops_trace_collect_local_fixture -SkipQuestPull -LocalTracePaths .\docs\evidence\20260222_192103\artifacts\ops_dataset_trace_export\session_trace_input.ndjson`
   - output:
     - `docs/evidence/20260222_202038/commands/ops_dataset_trace_collect_local_fixture.log`
     - `docs/evidence/20260222_202038/artifacts/ops_trace_collect_local_fixture/reports/trace_discovery_report.json`
   - result: PASS (`TRACE_COLLECTED`, `readyForOpsTraceValidation=True`, `preparedSessionDatasetEvents=9`)

3. `powershell -ExecutionPolicy Bypass -File .\scripts\unity_ops_dataset_trace_export_validate.ps1 ... -TraceInputPath <collector_output> -TraceSessionId validation_session -RequireProvidedTrace`
   - output:
     - `docs/evidence/20260222_202038/commands/unity_operational_dataset_trace_export_validation_from_collector_runner.log`
   - result: BLOCKED (`another Unity instance is running with this project open`)

## Markers

- Quest autodiscovery marker:
  - `docs/evidence/20260222_202038/commands/ops_dataset_trace_collect_markers.log`
  - `selectionReason=TRACE_CANDIDATES_NOT_FOUND`

- Local fixture collector marker:
  - `docs/evidence/20260222_202038/commands/ops_dataset_trace_collect_local_fixture_markers.log`
  - `selectionReason=TRACE_COLLECTED`
  - `readyForOpsTraceValidation=True`
  - `preparedSessionDatasetEvents=9`

- Unity collector handoff marker (blocked):
  - `docs/evidence/20260222_202038/commands/unity_operational_dataset_trace_export_validation_from_collector_markers.log`
  - `another Unity instance is running with this project open`

## Artifacts

- collector reports:
  - `docs/evidence/20260222_202038/artifacts/ops_trace_collect/reports/trace_discovery_report.json`
  - `docs/evidence/20260222_202038/artifacts/ops_trace_collect/reports/trace_discovery_report.md`
  - `docs/evidence/20260222_202038/artifacts/ops_trace_collect_local_fixture/reports/trace_discovery_report.json`
  - `docs/evidence/20260222_202038/artifacts/ops_trace_collect_local_fixture/reports/trace_discovery_report.md`
- prepared trace output (collector, local fixture validation):
  - `docs/evidence/20260222_202038/artifacts/ops_trace_collect_local_fixture/prepared/dataset_trace_all_sessions.ndjson`
  - `docs/evidence/20260222_202038/artifacts/ops_trace_collect_local_fixture/prepared/dataset_trace_selected_session.ndjson`

## Implemented files

- `scripts/ops_dataset_trace_collect.ps1`
- `docs/30-OPS-Pretrain-Handoff-SOP.md`
- `docs/29-Controller-Authority-Resilience-And-Data-Roadmap.md`
- `docs/08-Session-Resilience-Worklog.md`
