# OPS-002 Dataset Export Operator Report

- generatedUtc: 2026-02-27T19:00:46.6731911Z
- exportName: ops_dataset_trace_export_validation_20260227_190046
- exportMode: DURABLE_SESSION_TRACE
- outputDirectory: C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260227_195736\artifacts\ops_dataset_trace_export
- readyForTraining: TRUE
- reasonCode: EXPORT_READY_FOR_TRAINING

## Artifact Paths

- canonicalDataset: `C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260227_195736\artifacts\ops_dataset_trace_export\canonical_events.ndjson`
- preTrainManifest: `C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260227_195736\artifacts\ops_dataset_trace_export\pretrain_manifest.json`
- operatorReport: `C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260227_195736\artifacts\ops_dataset_trace_export\operator_report.md`

## Trace Load

- loaded: TRUE
- reasonCode: TRACE_LOADED
- durableTracePath: C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\20260227_195736\artifacts\ops_dataset_trace_export\session_trace_input.ndjson
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
- missingMandatoryKeyViolations: 0
- sequenceGapViolations: 0
- unresolvedActionDecisionViolations: 0
- duplicateActionDecisionViolations: 0
- missingSessionTerminalViolations: 0
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
| 3e4d7bba-7959-4ae0-98f9-62d616cd2b1e | 1 | 1 | 1 | validation_therapist|validation_student | validation_therapist|validation_student|validation_session |
| 7d1f4c7b-b877-4856-81a5-e319db13a965 | 1 | 1 | 1 | validation_therapist|validation_student | validation_therapist|validation_student|validation_session |
| dab7d521-997c-4644-b90e-bf7ce022156e | 1 | 1 | 1 | validation_therapist|validation_student | validation_therapist|validation_student|validation_session |
