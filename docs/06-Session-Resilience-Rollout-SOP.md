# Session Resilience Rollout Checklist and Incident SOP

## Purpose
Define release gates and incident handling for session resilience features delivered in phases P0-P4.

Primary goals:
- Keep `no-loss sessions` at or above `99.9%`.
- Prevent duplicate or hidden sessions during reconnect/restart.
- Keep support diagnosis fast using `sessionId`, `messageId`, and reconciliation data.

## Scope
- Unity Quest runtime + Flutter controller release process.
- Operational monitoring during rollout.
- Incident triage, containment, and recovery for session resilience failures.

## Roles
- Release Owner: executes rollout checklist and go/no-go decisions.
- Incident Commander: leads active incident response.
- Runtime Engineer: Unity-side diagnosis and mitigations.
- Mobile Engineer: Flutter-side diagnosis and mitigations.
- Support Liaison: therapist/customer communication and case updates.

## Pre-Rollout Checklist
All items must be complete before any staged rollout begins.

| Area | Required check | Evidence |
|---|---|---|
| Build and tests | `flutter analyze`, `flutter test`, `flutter build apk --debug` pass on release candidate branch | CI or terminal logs |
| Chaos suite | `chaos_fault_matrix_test.dart` fully passing | Test report |
| Save/resume suite | `save_resume_regression_test.dart` fully passing | Test report |
| Command reliability | Critical command ACK/NACK retry behavior verified for `START/Pause/Resume/Stop/END_SESSION` | Automated test logs |
| Recovery behavior | Confirm restart recovery maps non-terminal sessions to `INTERRUPTED` | Test or QA evidence |
| Reconciliation path | Manual re-sync flow (`MANUAL_RESYNC`) returns report with before/after deltas | Runtime log + report payload |
| Monitoring visibility | Dashboard includes `sync lag`, `pending outbox`, `last ack`, `last snapshot` | Dashboard screenshot/config |
| Runbook readiness | This SOP reviewed by release owner + support | Review sign-off |

## Rollout Plan
Use a staged rollout. Do not skip stages.

1. Stage 0 (internal canary)
   - One internal therapist account and one Quest device.
   - Run at least 20 sessions including forced reconnect and restart paths.
2. Stage 1 (limited beta)
   - Small therapist subset (up to 10% of active devices).
   - Minimum observation window: 24 hours.
3. Stage 2 (broad rollout)
   - Expand to remaining devices only if go/no-go criteria pass.

## Go / No-Go Criteria
All must pass before progressing to next stage.

| Metric | Go threshold | No-go trigger |
|---|---|---|
| No-loss sessions | `>= 99.9%` | `< 99.9%` |
| ACK timeout rate (critical commands) | `< 0.5%` | `>= 0.5%` |
| Duplicate session lock conflicts | No unexpected spikes vs baseline | Spike sustained over 30 min |
| Outbox pending age | `P95 < 10 min` | `P95 >= 10 min` |
| Crash context rate (`crash_context`) | No sustained increase vs baseline | Sustained increase over 30 min |
| Manual re-sync success | `>= 99%` successful reports | `< 99%` successful reports |

## Operational Signals to Watch
During rollout and incident response, always capture these fields:
- `sessionId`
- `messageId` and `commandId` for command failures
- ACK status and `reasonCode`
- `pendingQueueSize` and sync/outbox counters
- `SESSION_WATCHDOG_HEARTBEAT` health fields (`healthy`, `healthCode`)
- Reconciliation summary (`missing on server`, `missing on device`)
- Crash report identifiers from `crash_context`

## Incident Severity
| Severity | Definition | Initial target |
|---|---|---|
| SEV-1 | Active data loss risk or widespread inability to control/finish sessions | Stabilize within 15 min |
| SEV-2 | Partial degradation with workaround (manual re-sync/retry) | Stabilize within 60 min |
| SEV-3 | Isolated issues without ongoing data risk | Stabilize within 1 business day |

## First 15 Minutes (SEV-1/SEV-2)
1. Assign Incident Commander and open incident channel.
2. Freeze rollout progression immediately.
3. Collect minimum context for at least 3 affected sessions:
   - `sessionId`, device ID, app versions, timestamps.
   - Last critical commands and ACK/NACK reason codes.
   - Outbox and reconciliation status.
4. Decide containment:
   - Keep canary only, or rollback to previous known-good release.
5. Publish first status update to support and stakeholders.

## Fault-Specific Runbooks
### 1) ACK timeout / NACK spike
1. Check if failures are command-specific (`END_SESSION` only vs all critical commands).
2. Compare timeout trend against network/connectivity metrics.
3. If spike persists:
   - Increase retry observation sample.
   - Pause rollout.
   - Collect raw command envelopes and ACK payloads for failing sessions.

### 2) Outbox backlog / sync lag growth
1. Verify `pending outbox` growth and `P95` pending age.
2. Trigger `MANUAL_RESYNC` for representative impacted sessions.
3. If backlog continues to grow:
   - Treat as SEV-2 (or SEV-1 if data at risk).
   - Hold rollout and evaluate rollback.

### 3) Save/resume regression
1. Validate remote session gate behavior (`Resume` vs `Start New`) on affected account.
2. Confirm resumed command uses active Quest `sessionId`.
3. Generate reconciliation report and compare missing sequence deltas.
4. If mismatch/duplication is confirmed, escalate to SEV-1 and rollback.

### 4) Crash-context spike
1. Group by `healthCode`, exception signature, scene/build.
2. Verify if crashes cluster around startup restore or reconnect windows.
3. If sustained spike and user impact is broad, rollback and open hotfix track.

### 5) Duplicate session lock conflicts
1. Confirm if conflicts are expected (patient switch race) or unexpected.
2. Audit `sessionId` transitions from runtime status and session state updates.
3. If conflicts are unexpected and persistent, pause rollout and investigate gate logic.

## Communication Templates
### Support update (short)
`We are investigating session resilience instability affecting [scope]. Current mitigation: [action]. Next update in [time].`

### Stakeholder update (short)
`Rollout status: [paused/rolling back/active]. Impact: [SEV, user scope]. Data safety status: [known safe / under validation].`

## Recovery and Exit Criteria
Incident can be closed only when all are true:
1. Trigger condition has returned below no-go threshold.
2. At least 1 hour of stable monitoring after mitigation (or next business day for SEV-3).
3. Reconciliation checks show no unresolved missing-event growth.
4. Follow-up tasks are logged with owners and due dates.

## Post-Incident Checklist
1. Record timeline with exact UTC timestamps.
2. Document root cause and why guardrails did or did not catch it.
3. Add regression test or alert rule for the failure mode.
4. Update this SOP if escalation or mitigation steps changed.
