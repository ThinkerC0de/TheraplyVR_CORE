# OPS-002 Dataset Export Operator Report

- generatedUtc: 2026-02-22T18:25:06.1083676Z
- exportName: ops002_pretrain_export_provided
- exportMode: DURABLE_SESSION_TRACE
- outputDirectory: C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260222_192103\artifacts\ops_dataset_trace_export_provided
- readyForTraining: TRUE
- reasonCode: EXPORT_READY_FOR_TRAINING

## Artifact Paths

- canonicalDataset: `C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260222_192103\artifacts\ops_dataset_trace_export_provided\canonical_events.ndjson`
- preTrainManifest: `C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260222_192103\artifacts\ops_dataset_trace_export_provided\pretrain_manifest.json`
- operatorReport: `C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260222_192103\artifacts\ops_dataset_trace_export_provided\operator_report.md`

## Trace Load

- loaded: TRUE
- reasonCode: TRACE_LOADED
- durableTracePath: C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260222_192103\artifacts\ops_dataset_trace_export\session_trace_input.ndjson
- sessionIdFilter: validation_session
- totalLines: 9
- importedEvents: 9
- skippedBySession: 0
- skippedUnsupportedEventType: 0
- invalidLines: 0

## Sanity

- sourceOfTruthViolations: 0
- ownershipViolations: 0
- taskRunConsistencyViolations: 0
- invalidRecords: 0

## Quality Gate

- readyForTraining: TRUE
- reasonCode: DATASET_READY_FOR_TRAINING
- qualityScore: 1.000
- labelCoverageRatio: 1.000
- summaryCoverageRatio: 1.000
- averageLabelConfidence: 0.859

## Task Runs

| taskRunId | summaries | labels | adaptiveEvents | ownerKey | sessionKey |
| --- | ---: | ---: | ---: | --- | --- |
| 9e98ca83-6d63-47f1-9fff-5a425aa65359 | 1 | 1 | 1 | validation_therapist|validation_student | validation_therapist|validation_student|validation_session |
| bc1e22ef-4370-4640-97b2-40594b92b73f | 1 | 1 | 1 | validation_therapist|validation_student | validation_therapist|validation_student|validation_session |
| e7a70add-dcd6-49a8-a274-6257ff828e92 | 1 | 1 | 1 | validation_therapist|validation_student | validation_therapist|validation_student|validation_session |
