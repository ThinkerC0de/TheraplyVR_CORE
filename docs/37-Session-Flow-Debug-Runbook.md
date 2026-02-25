# Session Flow Debug Runbook

Date: 2026-02-25  
Purpose: triage and resolve session-flow runtime issues using canonical signals and reason codes.

## 1. Quick Triage

1. confirm issue scope: `definition`, `interaction`, `transition`, `control`, `telemetry`, or `outbox`.
2. capture identifiers:
   `sessionId`, `taskRunId`, `attemptId`, `eventId`, `flowId`, `stepId`, `actionId`.
3. collect latest reason codes from canonical events:
   `action_evaluated.reasonCode`, `flow_failed.reasonCode`, `session_terminal.reasonCode`.
4. check whether issue reproduces in smoke template validation.

## 2. Mandatory Checks

1. `GameDefinition` validates successfully.
2. active control mode matches expected source (`remote/local/hybrid`).
3. `TaskGraphRunner` active node is deterministic and expected.
4. action channel is enabled in definition.
5. action constraints are satisfiable in current node.
6. no unresolved action decision pairs remain.

## 3. Common Failure Patterns

### A. Action Rejected Unexpectedly

Check:

1. `ACTION_NOT_ALLOWED_IN_NODE`
2. `CHANNEL_ID_MISMATCH`
3. `TARGET_ID_MISMATCH`
4. `CONTROL_MODE_MISMATCH`
5. input value min/max violations

Fix:

1. align `allowedActions` with incoming `actionId/channelId`.
2. align target bindings and normalized IDs.
3. align control policy with actual control source.

### B. Step Does Not Transition

Check:

1. current node type (`Action`, `Condition`, `Branch`, `Timer`).
2. transition fields (`nextOnSuccess`, `nextOnFail`, `nextOnTimeout`).
3. plugin `Apply` result flags (`stepCompleted`, `stepFailed`, `stepTimedOut`, `nextNodeId`).

Fix:

1. complete missing transition target(s).
2. remove contradictory constraints.
3. verify timeout and branch policy values.

### C. Session Closes Incorrectly

Check:

1. session FSM transitions.
2. `session_terminal` presence and state.
3. unresolved decision count in terminal payload.

Fix:

1. ensure action pair completeness before close.
2. ensure terminal states only emitted on canonical end paths.

### D. Data Missing After Disconnect/Reconnect

Check:

1. outbox stats (`pending`, `inFlight`, `failed`, `synced`, `replayed`, `retryCount`).
2. retry scheduling after offline phase.
3. replay drain on reconnect.

Fix:

1. ensure append-first durability is enabled.
2. ensure outbox sync loop is running.
3. validate dedupe by `eventId` at ingest.

## 4. Canonical Debug Sequence

For one failing attempt, verify strict order:

1. `flow_started`
2. `step_entered`
3. `action_received`
4. `action_evaluated`
5. `step_entered` (next) or `flow_failed`
6. `session_terminal` (on close)

Any missing link is a blocking defect.

## 5. Required Automation Suite

Run these validations in order:

1. `SessionFlowContractsValidation`
2. `TaskGraphRuntimeIntegrationValidation`
3. `AdapterIntegrationValidation`
4. `CanonicalFlowTelemetryQualityGateValidation`
5. `SessionFlowOutboxResilienceValidation`
6. `SessionFlowSmokeTemplateValidation`

One-shot command (recommended):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\unity_session_flow_validation_pack.ps1
```

Optional flags:

- `-SkipCompile` skips Unity compile precheck.
- `-EvidenceRoot <path>` writes summary and logs to a specific evidence directory.
- `-LogDirectory <path>` writes command logs to a specific directory.

Default output:

- summary: `docs/evidence/<timestamp>/SUMMARY.md`
- logs: `docs/evidence/<timestamp>/commands/*.log`

## 6. Operator Escalation Thresholds

Escalate immediately when:

1. `FLOW_ACTION_DECISION_COVERAGE_FAILED`
2. `FLOW_SEQUENCE_GAPS_DETECTED`
3. `FLOW_SESSION_TERMINAL_MISSING`
4. repeatable `CONTROL_*_NOT_ALLOWED` in expected mode
5. outbox backlog cannot drain after reconnect window

## 7. Debug Artifact Bundle

When raising a ticket, attach:

1. definition snapshot (`GameDefinition` JSON/asset export),
2. canonical event sample around failure,
3. outbox statistics before and after retry cycle,
4. validation result files from `Temp/CliValidation`,
5. exact reason codes and reproduction steps.
