# Board Demo Execution Checklist

Date: 2026-03-02  
Goal: deliver one stable board demo package without entering high-risk implementation paths.

## Demo Promise (Scope Freeze)

Show only:
1. Therapist login -> student select -> session start on one known-safe student profile.
2. Game start/pause/stop/end from mobile control panel (no hidden operator shortcuts).
3. Session resilience behavior: reconnect under window, then over-window therapist decision.
4. Canonical reason codes (`E/W/I`) and action telemetry for each therapist/player interaction.
5. CMS grant flow on domain, catalog row with real `packageUri`, and board-safe package download proof.

Do not promise in board demo:
1. Full production OTA installer lifecycle on Quest.
2. Dynamic runtime loading of new executable game modules from downloaded bundles.
3. Any per-game runtime logic branch in core session flow.
4. Any fallback "manual DB patching" as part of live board story.

## Guardrails (No-Corner Rules)

1. No per-game branching in core runtime.
2. No class/file renames that risk compatibility during demo hardening.
3. One lane at a time. Next lane starts only after validation gate is green.
4. Every lane ends with evidence, commit, and rollback note.
5. If a lane opens a P0 regression, stop and fix before moving on.

## Ordered Lanes

| Lane | Scope | Exit Gate (GO) | Stop Condition (NO-GO) |
| --- | --- | --- | --- |
| BD-001 | Freeze script + acceptance path | Demo script approved and frozen | Scope still changing daily |
| BD-002 | Runtime UX blockers | `Connected` reflects real readiness; `End Session` returns to student selection | Any repeatable blocked-flow in control screen |
| BD-003 | Settings/rules consistency | Therapist settings save path works under active Firestore rules | Silent settings write failures |
| BD-004 | CMS on domain + catalog wiring | Admin console available on domain, grants visible in mobile flow, catalog entries use real URLs | CMS deploy unstable or catalog mismatch |
| BD-005 | Package download proof (board-safe) | Runtime can prove `packageUri` reachability (status/size/hash header) and publish telemetry evidence | Probe causes runtime instability |
| BD-006 | Full rehearsal + evidence pack | All mandatory validations pass + manual board script pass | Any P0/P1 open issue in board script |
| BD-007 | Release candidate cut | `board-demo-rc` tag + rollback instructions ready | Missing rollback or missing evidence |

## BD-001 Frozen Board Script (5-7 Minutes)

### Locked Environment

1. Devices:
   - Therapist phone: Android app (control authority).
   - Student headset: Quest runtime (single headset path only).
   - Admin laptop: CMS `/admin` on target domain.
2. Network:
   - One dedicated Wi-Fi SSID for all demo devices.
   - No hotspot switching during the run.
3. Demo profile:
   - `demoProfileId`: `board-demo-20260302`
   - `recoveryWindowSec`: `90`
   - `autoEndOnRecoveryTimeout`: `false` (therapist must decide on over-window case)
   - `allowUpdateConfigDuringDemo`: `false` (freeze for board script)

### Locked Story Timeline

| Time | Operator Action | Expected Screen/Behavior | Evidence Expectation |
| --- | --- | --- | --- |
| 00:00-00:40 | Open therapist app, sign in, select frozen student profile | Therapist lands in control flow with session controls available | `session_select_student` logged with canonical actor/session ids |
| 00:40-01:30 | Start session and start game with frozen config | Runtime enters active game state without extra config prompt | `session_start` and `game_start` with same config fingerprint |
| 01:30-02:10 | Pause and resume once | Control panel and runtime states stay synchronized | `game_pause`, `game_resume` reason codes emitted as `I` |
| 02:10-03:20 | Simulate temporary disconnect (<90s), recover | `Connected` state changes to degraded/recovering, then back to healthy after reconnect | Disconnect and recovery events are logged; no false healthy state |
| 03:20-04:10 | Press `STOP`, then `START` without config update | Session restarts with identical config payload | `game_stop` then `game_start` with unchanged config hash |
| 04:10-05:00 | Simulate disconnect >90s and choose therapist `End Session` | Over-window decision prompt appears; `End Session` always returns to student selection | `session_end` + reason code for recovery timeout path |
| 05:00-06:20 | Open CMS on domain, show granted game and catalog `packageUri`, run package download proof | CMS grant visibility matches mobile list; probe shows HTTP reachability metadata only | `package_probe_result` telemetry with status/size headers, no runtime module load |
| 06:20-06:40 | Close with frozen-scope statement | Presenter explicitly confirms board-safe scope and excluded items | Checklist sign-off with GO/NO-GO outcome |

### BD-001 GO / NO-GO Criteria

GO only if all are true:
1. Full timeline executes in 5-7 minutes without improvisation.
2. Every scripted action emits telemetry event with canonical reason code.
3. `STOP -> START` reuses identical config when no `UPDATE_CONFIG` occurred.
4. `Connected` never shows healthy when control or stream path is effectively unavailable.
5. `End Session` always returns to student selection from over-window flow.
6. Presenter can show catalog `packageUri` and probe result without dynamic runtime loading.

NO-GO if any occurs:
1. Any step needs manual DB edits, code hotfix, or app restart mid-demo.
2. Missing telemetry for a scripted interaction.
3. Runtime dead-end on control/back/end-session flow.
4. Any accidental entry into full OTA/dynamic module loading path.
5. Timeline exceeds 7 minutes due to flow instability.

### Explicit "Show / Do Not Show" for Board

Show:
1. One deterministic therapist-to-student session path.
2. Canonical status/reason behavior, including disconnect recovery logic.
3. CMS domain hosting and catalog-driven `packageUri`.
4. Board-safe package download proof (HTTP reachability evidence only).

Do not show:
1. Installing new executable game code into runtime during session.
2. Multi-headset orchestration scenarios outside frozen script.
3. Any non-canonical telemetry path or ad-hoc reason code.
4. Experimental or unfinished admin/runtime screens not in the timeline.

## Lane Task Details

### BD-001 Freeze Script
1. Use the frozen timeline above as the only board story.
2. Keep config `board-demo-20260302` unchanged for all rehearsals.
3. Reject scope additions unless they replace, not extend, one frozen step.

### BD-002 Runtime UX Blockers
1. Fix status coherence: "connected" must not be shown as healthy when control/preview path is effectively unavailable.
2. Fix end-session path so both popup close and explicit `End Session` deterministically exit to student selection.
3. Verify no command dead-end in control screen back navigation.

### BD-003 Settings and Access Rules
1. Align therapist settings persistence target with Firestore write permissions.
2. Keep deterministic fallback defaults when settings read/write fails.
3. Capture negative test evidence for unauthorized write attempts.

### BD-004 CMS + Domain + Catalog
1. Build and deploy admin hosting bundle (`/admin`) to target domain.
2. Seed/update `game_catalog` with valid `deliveryMode`, `targetContentVersion`, `packageUri`, `thumbnailUrl`.
3. Validate grant flow in CMS and visibility in mobile game list.
4. Add one prepared "board demo game" catalog row with stable URL.

### BD-005 Package Download Proof (Board-Safe)
1. Implement optional install probe command path:
   - resolve `packageUri`,
   - perform HTTP probe (HEAD/GET with short timeout),
   - publish result telemetry (`statusCode`, `contentLength`, `reasonCode`).
2. Keep existing install simulation lifecycle unchanged as fallback.
3. Add kill-switch flag so probe can be disabled instantly.

### BD-006 Rehearsal and Evidence
1. Run mandatory validations:
   - `scripts/unity_session_flow_validation_pack.ps1 -SkipCompile`
   - `flutter analyze`
   - `flutter test`
   - `scripts/unity_ops_dataset_trace_export_validate.ps1`
2. Run board script end-to-end on target devices.
3. Save evidence in timestamped folder and update summary.

### BD-007 RC and Rollback
1. Tag validated commit (`board-demo-rc-YYYYMMDD`).
2. Prepare rollback steps:
   - previous stable tag,
   - config rollback points,
   - known-safe CMS seed snapshot.
3. Re-run smoke after tagging.

## Board Demo Ready Definition

All must be true:
1. Lanes `BD-001` through `BD-007` are green.
2. No open P0/P1 issues in frozen board script.
3. CMS grant -> mobile visibility -> runtime proof path is reproducible twice in a row.
4. Recovery-window and over-window therapist decision path is reproducible.
5. Evidence package is complete and linked to RC tag.
