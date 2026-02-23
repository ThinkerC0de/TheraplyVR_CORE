# OPS-002 Pre-Train Handoff Checklist

- generatedUtc: 2026-02-22T18:49:20.3980077Z
- operatorId: ops_validation
- exportName: ops003_pretrain_export_real_trace
- exportMode: DURABLE_SESSION_TRACE
- readyForTraining: True
- reasonCode: EXPORT_READY_FOR_TRAINING

## Required Files

- [x] canonical_events.ndjson
- [x] pretrain_manifest.json
- [x] operator_report.md
- [x] checksums.sha256
- [x] handoff_manifest.json
- [x] intake_ack_template.json
- [ ] intake_acknowledgement.json (after intake confirmation)

## Operator Steps

1. Verify eadyForTraining=true in pretrain_manifest.json and handoff_manifest.json.
2. Verify file checksums against checksums.sha256 before transfer.
3. Attach package to training intake ticket and include easonCode.
4. Confirm intake ACK via scripts/ops_pretrain_handoff_ack_confirm.ps1 and commit intake_acknowledgement.json.
5. Record handoff acknowledgement in session worklog with package path + intake ticket.
