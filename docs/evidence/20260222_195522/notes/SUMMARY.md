# Evidence Summary (OPS-003 real Quest trace attempt)

Date: 2026-02-22  
Roadmap item:
- OPS-003 (Real-trace-required run + intake ACK trail)

## Commands

1. `adb devices`
   - output:
     - `docs/evidence/20260222_195522/commands/adb_devices.log`
   - result: PASS (`2G0YC5ZF8P009T` connected)

2. `adb -s 2G0YC5ZF8P009T shell find /sdcard/Android/data -type f -name '*.ndjson'`
   - output:
     - `docs/evidence/20260222_195522/commands/adb_find_ndjson.log`
   - result: PASS (only `events.ndjson` and `crash_reports.ndjson` in `com.DefaultCompany.unityquesttemplate`)

3. `adb -s 2G0YC5ZF8P009T pull /sdcard/Android/data/com.DefaultCompany.unityquesttemplate/files/session_resilience/events.ndjson .\docs\evidence\20260222_195522\artifacts\quest_trace\quest_events.ndjson`
   - output:
     - `docs/evidence/20260222_195522/commands/adb_pull_quest_events_ndjson.log`
   - result: PASS

4. Local inspection of pulled trace (`eventType` and `sessionId` distribution)
   - output:
     - `docs/evidence/20260222_195522/commands/quest_events_eventtype_counts.log`
     - `docs/evidence/20260222_195522/commands/quest_events_session_counts.log`
   - result: PASS

5. `powershell -ExecutionPolicy Bypass -File .\scripts\unity_ops_dataset_trace_export_validate.ps1 -LogFile .\docs\evidence\20260222_195522\commands\unity_operational_dataset_trace_export_validation_quest_real_trace.log -ExportOutputDirectory C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260222_195522\artifacts\ops_dataset_trace_export_quest_real_trace -ExportName ops003_pretrain_export_quest_real_trace -TraceInputPath C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260222_195522\artifacts\quest_trace\quest_events.ndjson -RequireProvidedTrace`
   - output:
     - `docs/evidence/20260222_195522/commands/unity_operational_dataset_trace_export_validation_quest_real_trace_runner.log`
     - `docs/evidence/20260222_195522/commands/unity_operational_dataset_trace_export_validation_quest_real_trace.log`
   - result: EXPECTED_FAIL (`TRACE_NO_DATASET_EVENTS`)

## Markers

- `docs/evidence/20260222_195522/commands/unity_operational_dataset_trace_export_validation_quest_real_trace_markers.log`
  - `EXPECTED_FAIL`
- `docs/evidence/20260222_195522/commands/unity_operational_dataset_trace_export_validation_quest_real_trace_unity_markers.log`
  - `[OperationalDatasetTraceExportValidation] FAIL: ... reason=TRACE_NO_DATASET_EVENTS`

## Real trace diagnostics

- Pulled Quest trace path:
  - `docs/evidence/20260222_195522/artifacts/quest_trace/quest_events.ndjson`
- Top-level `eventType` values are runtime/demo events (e.g. `demo_cube_clicked`, `pointer_shot`, `game_start`, `session_*`).
- Trace does not contain dataset lane event envelope (`eventType=interaction_event` with `payloadJson.eventType` in `TASK_OUTCOME_SUMMARY`, `TASK_LABEL_GENERATED`, `ADAPTIVE_DIFFICULTY_ADJUSTED`), therefore OPS export lane correctly fails with `TRACE_NO_DATASET_EVENTS`.

## Intake ACK trail status

- ACK flow implementation and PASS evidence already exists:
  - `docs/evidence/20260222_194839/notes/SUMMARY.md`
- OPS-003 remains blocked only by missing Quest trace with dataset-compatible events.
