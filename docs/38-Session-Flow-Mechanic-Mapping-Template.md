# Session Flow Mechanic Mapping Template

Date: 2026-02-25  
Purpose: convert any new gameplay idea into `GameDefinition` + actions/effects/policies without core rewrites.

## 1. Mechanic Intake Card

Use this card before implementation:

1. Mechanic name:
2. Player objective:
3. Required interactions:
4. Success condition:
5. Failure condition:
6. Time constraints:
7. Feedback requirements:
8. Scoring expectations:
9. Control mode requirement:
10. Telemetry fields required by analysis:

## 2. Interaction Mapping

Map each interaction to canonical action IDs.

| Intended Interaction | Action ID | Channel | Required Constraints |
| --- | --- | --- | --- |
| describe interaction A | `<action_id>` | `<channel_id>` | target/tool/time/order |
| describe interaction B | `<action_id>` | `<channel_id>` | target/tool/time/order |

Rules:

1. if existing action fits, reuse it.
2. if no action fits, define new action atomically and keep naming activity-oriented.
3. never fork core runtime per scene.

## 3. Step Graph Mapping

Define deterministic flow steps.

| Step ID | Node Type | Allowed Actions | Completion Rule | Next Success | Next Fail | Next Timeout |
| --- | --- | --- | --- | --- | --- | --- |
| `n_01` | `Action` | `...` | count/time/threshold | `n_02` | `n_fail` | `n_timeout` |
| `n_02` | `Condition/Branch/Action` | `...` | rule | `...` | `...` | `...` |

Rules:

1. one active node at a time.
2. every transition target exists.
3. branch logic is deterministic.

## 4. Binding Mapping

Bind scene resources via stable keys.

| Binding Type | Key | Scene Reference |
| --- | --- | --- |
| object | `target_primary` | `<scene object>` |
| zone | `zone_collect` | `<scene zone>` |
| audio | `sfx_correct` | `<audio source>` |
| timeline | `story_main` | `<playable director>` |

Rules:

1. no hardcoded scene names in runtime logic.
2. keys should remain stable across scene revisions.

## 5. Effects Mapping

Map lifecycle hooks to effect IDs.

| Trigger | Effect ID | Binding | Parameters |
| --- | --- | --- | --- |
| `onEnter` | `show_object` | `...` | `...` |
| `onAcceptedAction` | `play_sfx` | `...` | `...` |
| `onRejectedAction` | `emit_hint` | `...` | `...` |
| `onTimeout` | `play_audio` | `...` | `...` |
| `onExit` | `hide_object` | `...` | `...` |

## 6. Policy Mapping

Fill all policy areas explicitly.

| Policy | Value | Reason |
| --- | --- | --- |
| wrongActionPolicy | `warn/penalize/fail` | scene behavior |
| retryPolicy | `maxRetries/cooldown` | interaction cadence |
| timeoutPolicy | `defaultTimeoutSec` | task pacing |
| scoringPolicy | points/lives/threshold | desired challenge |
| difficultyPolicy | adaptive true/false | personalization |
| controlPolicy | mode | deployment context |
| telemetryPolicy | decision coverage requirements | data quality guarantees |

## 7. Telemetry Mapping (AI Readiness)

Define exact event requirements before implementation.

1. required action pair events:
   `action_received` + `action_evaluated`.
2. required terminal event:
   `session_terminal`.
3. mandatory envelope keys:
   `eventId`, `sessionId`, `taskRunId`, `attemptId`, `sequenceNumber`, `gameId`, `eventType`.
4. required reason codes for expected rejects/fails.
5. outbox replay behavior expected under reconnect.

## 8. Acceptance Gate

Mechanic mapping is ready only when:

1. definition validates,
2. graph path is deterministic,
3. interaction-to-action mapping is complete,
4. effect hooks cover player feedback,
5. telemetry contract is explicit and testable,
6. no new per-game core branch is needed.

## 9. Handoff Package

Before implementation handoff, produce:

1. completed intake card,
2. filled mapping tables,
3. draft `GameDefinition` payload,
4. expected reason code list,
5. required automation validations list.
