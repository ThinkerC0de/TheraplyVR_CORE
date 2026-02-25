# Session Flow Scene Authoring Checklist

Date: 2026-02-25  
Purpose: standard checklist for creating any new scene using only core session-flow runtime.

## 1. Definition Readiness

1. `GameDefinition.gameId` is stable, descriptive, and framework-neutral.
2. `displayName` is present and human-readable.
3. `commentVersion` contains date + short change note.
4. `controlMode` is explicitly set (`remote_only`, `local_only`, `hybrid`).
5. `channels` contains only required channel IDs and no duplicates.
6. `policies` are set for timeout, retry, scoring, safety, control, and telemetry.
7. `taskGraph.entryNodeId` points to an existing node.
8. every node has unique `nodeId`.
9. every `Action` node has explicit `allowedActions`.
10. every transition target (`nextOnSuccess`, `nextOnFail`, `nextOnTimeout`) points to an existing node or terminal intent.

## 2. Binding Readiness

1. all required object bindings exist in `bindings.objects`.
2. all required zone bindings exist in `bindings.zones`.
3. all required audio bindings exist in `bindings.audio`.
4. timeline bindings are present when `timeline` channel/actions are used.
5. binding keys are stable and not tied to game-specific naming patterns.
6. no missing binding keys in action/effect parameters.

## 3. Action Coverage

1. each intended player interaction maps to an existing action ID.
2. each action has channel-compatible constraints (`requiredChannelId`, target/tool/time constraints).
3. no per-scene runtime code branches are required to accept/reject actions.
4. rejection cases are intentional and mapped to explicit `reasonCode` expectations.
5. if interaction is continuous, completion criteria are explicit (`count`, `duration`, `threshold`).

## 4. Effects Coverage

1. `onEnter` effects for each step are defined where needed.
2. `onAcceptedAction` and `onRejectedAction` effects are defined where feedback is required.
3. `onTimeout` effects are defined for timed steps.
4. `onExit` effects clean up spawned/temporary state.
5. no effect relies on hardcoded scene object names outside binding registry.

## 5. Scoring and Difficulty

1. scoring policy values are explicit (`lives`, points, threshold).
2. adaptive difficulty policy is explicitly enabled or disabled.
3. adaptive changes are observable in telemetry (`adaptive_difficulty_updated` or equivalent).
4. fail/success conditions are deterministic and tied to graph state.

## 6. Telemetry and Data Quality

1. every action attempt emits `action_received`.
2. every action attempt emits exactly one `action_evaluated`.
3. terminal sessions emit `session_terminal`.
4. canonical envelope keys are present for all flow events.
5. `eventId` is unique and suitable for idempotent dedupe.
6. outbox is enabled for durable delivery and retry.
7. export quality gates pass (`mandatory keys`, `sequence continuity`, `decision coverage`, `terminal coverage`).

## 7. Control Modes

1. chosen `controlMode` is tested in expected operating mode.
2. disallowed source (`remote` or `local`) is rejected with explicit reason code.
3. pause/resume/stop behavior is validated against session FSM.
4. local fallback behavior is verified when configured (`hybrid`, `local_only`).

## 8. Smoke Validation (Required Before Merge)

1. run contract validation.
2. run task graph integration validation.
3. run adapter integration validation.
4. run telemetry quality gate validation.
5. run outbox resilience validation.
6. run smoke template validation.

## 9. Release Gate

Scene is release-ready only if:

1. all checklist sections are complete,
2. no per-game core runtime edits were introduced,
3. all required automation validations pass,
4. docs for definition and operator flow are updated.
