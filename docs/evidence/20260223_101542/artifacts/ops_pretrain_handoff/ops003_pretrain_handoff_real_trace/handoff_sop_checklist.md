# OPS-002 Pre-Train Handoff Checklist

- generatedUtc: 2026-02-23T09:30:43.6026699Z
- operatorId: ops_cli
- exportName: ops003_real_trace
- exportMode: DURABLE_SESSION_TRACE
- readyForTraining: False
- reasonCode: INSUFFICIENT_EVENT_COUNT

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
