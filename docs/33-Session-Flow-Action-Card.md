# Session Flow + Action Catalog Card

This document defines the core workflow for building new scenes without adding per-game logic to the framework.

## Scope

- Goal: build games from reusable actions and effects.
- Goal: keep one deterministic flow engine that controls what the player can do now.
- Goal: log every attempt for ML and post-session analysis.
- Non-goal: port legacy code as-is.

## Direction Coverage Audit (This Chat)

| Requirement From Direction | Coverage |
| --- | --- |
| Per-activity core (not per-game) | Covered by `ActionPlugin`, atom/action catalog, and `GameDefinition` contracts |
| No version suffixes in names | Covered in Design Rules (`V2/V3` forbidden in names) |
| Session controls what is allowed now | Covered by `TaskGraphRunner` + `ActionGate` + node policies |
| Scene can drive show/spawn/hide/play/etc. | Covered by effect catalog and lifecycle hooks |
| Full interaction telemetry for ML (`what/when/how/why`) | Covered by canonical schema + decision events + reason codes |
| Hard data robustness (anti data drift/loss) | Covered by hard-data guarantees and export quality gates |
| Mobile controller mode and local mode | Covered by `ControlRuntime` and `controlPolicy` |
| Runtime domains (Session/Scene/TaskGraph/Interaction/Effect/Scoring/Telemetry/Control) | Covered in runtime domains checklist |
| Definition layer (`config + graph + bindings + policies`) | Covered by `GameDefinition` contract |
| `channels`, `conditions`, `effects`, `policies` | Covered by dedicated catalogs |
| `ActionPlugin` and `GameDefinition` | Covered by explicit contracts |

Open implementation items (expected):

- runtime scaffolding classes are specified but not all implemented yet.
- action adapter/plugin coverage includes pointer/tool/hand/grab/gaze/breath/audio-source/dual-hand/pose-path/timeline/sequence channels.

## Design Rules

- Use activity-oriented naming. Do not use game-specific names.
- Do not use version suffixes in class or file names (`V2`, `V3`). Keep version notes in comments/changelog fields.
- Keep game modules thin: bind scene objects to the flow, do not hardcode task logic in custom scripts.
- Enforce gate-first execution: every player action is validated by the current step before it changes state.
- Treat canonical event telemetry as source of truth. Optional compact traces are supplementary artifacts.

## Runtime Model

`SessionFlowRunner` executes ordered `FlowStep` nodes.

`ActionGate` evaluates whether an incoming action is allowed in the active step.

`ActionValidator` checks target/tool/sequence/timing rules and returns `accepted` or `rejected` with `reasonCode`.

`TransitionEngine` moves to next step by `success`, `fail`, `timeout`, or explicit branch conditions.

`EffectRunner` performs visual/audio/spawn/animation commands on step lifecycle hooks.

`TelemetryLedger` records all flow events and action decisions in canonical schema.

## Runtime Domains Coverage (Requested Checklist)

| Runtime Domain | Scope | Core Components | Status |
| --- | --- | --- | --- |
| `SessionRuntime` | Session FSM and lifecycle (`start/pause/resume/stop`) with remote or local control | `SessionFlowRunner`, session FSM bridge, control mode policy | Defined in this card; implementation pending |
| `SceneRuntime` | Scene load/unload/additive, spawn, teleport anchor, full reset | `SceneRuntimeController`, `FlowBindingRegistry`, `EffectRunner` | Defined in this card; implementation pending |
| `TaskGraphRuntime` | Graph execution (`Action/Condition/Branch/Timer/Complete/Fail`) | `TaskGraphRunner`, `TransitionEngine`, `ActionGate`, `ActionValidator` | Defined in this card; implementation pending |
| `InteractionRuntime` | Input channels and normalization | adapters + `ActionAdapterRegistry` | Partial in code, full contract defined |
| `EffectRuntime` | Narrator/audio/haptics/vfx/ui hint | `EffectRunner` + effect plugins | Partial in code, full contract defined |
| `ScoringRuntime` | points/errors/lives/success thresholds/adaptive difficulty | `ScoringRuntime`, policy evaluators | Concept defined; implementation pending |
| `TelemetryRuntime` | canonical append-only log + outbox + retries + dedupe | `TelemetryLedger`, existing event store/outbox path | Partial in code, hard-data rules now defined |
| `ControlRuntime` | mobile controller mode and local mode | `ControlRuntimeGateway` + control mode policy | Concept defined; implementation pending |

`SessionRuntime` canonical state path:

- `CREATED -> IN_PROGRESS -> PAUSED/INTERRUPTED -> IN_PROGRESS -> COMPLETED`
- terminal technical paths: `ABORTED_BY_THERAPIST`, `FAILED_TECHNICAL`

`SceneRuntime` minimum operations:

- `LoadScene(sceneId, mode)` where `mode` is `single` or `additive`
- `UnloadScene(sceneId)`
- `ResetScene(sceneId)` (restores initial binding state and despawns runtime entities)
- `Spawn(bindingKey, prefabKey, spawnPointKey)`
- `Despawn(instanceId or bindingKey)`
- `TeleportAnchor(anchorKey, poseKey)`

`ScoringRuntime` minimum outputs:

- `scoreTotal`
- `correctCount`
- `wrongCount`
- `livesRemaining`
- `successThresholdReached`
- `adaptiveDifficultyState`

## Definition Layer (Config + Graph + Bindings + Policies)

`GameDefinition` is the top-level runtime definition.  
It is the only thing a new scene should author.

```json
{
  "gameId": "sample_training_session",
  "displayName": "Color Catch Session",
  "commentVersion": "2026-02-24: initial definition",
  "controlMode": "hybrid",
  "config": {
    "difficulty": "normal",
    "timeLimitSec": 180,
    "targetCount": 20
  },
  "bindings": {
    "objects": {},
    "zones": {},
    "audio": {},
    "timeline": {}
  },
  "channels": [
    { "channelId": "pointer", "enabled": true },
    { "channelId": "tool_impact", "enabled": true },
    { "channelId": "hand_contact", "enabled": false }
  ],
  "policies": {
    "wrongActionPolicy": "warn",
    "retryPolicy": { "maxRetries": 3, "cooldownSec": 0.5 },
    "timeoutPolicy": { "defaultTimeoutSec": 15.0 },
    "scoringPolicy": { "lives": 3, "pointsPerCorrect": 1, "pointsPerWrong": -1 },
    "difficultyPolicy": { "adaptiveEnabled": true },
    "telemetryPolicy": { "requireDecisionForEveryAction": true }
  },
  "taskGraph": {
    "entryNodeId": "n1",
    "nodes": []
  }
}
```

`controlMode` allowed values:

- `remote_only` (requires mobile controller)
- `local_only` (runs without mobile controller)
- `hybrid` (uses remote when available, otherwise local fallback)

### `GameDefinition` Subcontracts

| Part | Contract |
| --- | --- |
| `config` | tunable parameters only; no flow logic |
| `taskGraph` | deterministic node graph |
| `bindings` | scene references by stable keys |
| `policies` | behavior and safety rules |
| `channels` | enabled input sources for this scene |

## TaskGraphRuntime Contract

`TaskGraphRunner` executes one active node at a time.

### Node Types

| Node Type | Purpose |
| --- | --- |
| `Action` | waits for action intent and validates it |
| `Condition` | evaluates pure predicate and routes |
| `Branch` | explicit branch by score/flags/context |
| `Timer` | waits fixed duration then routes |
| `Complete` | marks flow success and closes |
| `Fail` | marks flow failure and closes |

### Node Contract

Each node contains:

- `nodeId`
- `nodeType`
- `allowedActions` (for `Action` nodes)
- `conditions` (for `Condition` nodes)
- `timeoutSec` (optional)
- `onEnterEffects`
- `onExitEffects`
- `nextOnSuccess`
- `nextOnFail`
- `nextOnTimeout`

Execution rules:

- one active node at a time,
- all incoming actions are checked by `ActionGate`,
- each action produces exactly one decision event (`accepted` or `rejected`),
- every transition emits telemetry with causality fields.

## ActionPlugin Contract

`ActionPlugin` is the extension point for reusable actions without per-game core edits.

```csharp
public interface IActionPlugin
{
    string ActionId { get; }
    string ChannelId { get; }

    bool TryCreateIntent(
        IReadOnlyDictionary<string, object> rawInput,
        out ActionIntent intent);

    ActionValidationResult Validate(
        ActionIntent intent,
        ActionContext context,
        AllowedActionDefinition allowedAction);

    ActionApplyResult Apply(
        ActionIntent intent,
        ActionContext context,
        AllowedActionDefinition allowedAction);
}
```

Registration model:

- `ActionPluginRegistry` registers plugins by `actionId`.
- `ActionAdapterRegistry` routes channel input to plugins.
- `TaskGraphRunner` calls plugin validate/apply through `ActionGate`.

## Channel Catalog

| Channel ID | Purpose | Typical Source |
| --- | --- | --- |
| `pointer` | ray + click selection | controller trigger + raycast |
| `tool_impact` | tool collision events | wand/stick collider |
| `hand_contact` | direct hand touch/hit | hand collider |
| `hand_grab` | object pick/release | XR grab interaction |
| `gaze` | dwell and focus checks | head pose/raycast |
| `breath` | inhale/hold/exhale signal | mic amplitude/sensor |
| `audio_source` | sound localization choice | pointer/hand selection after cue |
| `dual_hand` | synchronized left-right actions | both controllers/hands |
| `pose_path` | pose hold and path-follow checks | tracked transforms/anchors |
| `sequence` | sequence replay and pair matching | sequence adapters and step events |
| `timeline` | passive segment watch state | playable director callbacks |

## Policy Catalog

| Policy | What It Controls |
| --- | --- |
| `wrongActionPolicy` | ignore/warn/penalize/fail behavior |
| `retryPolicy` | retries and cooldown between attempts |
| `timeoutPolicy` | step/node timeout behavior |
| `branchPolicy` | deterministic branching precedence |
| `scoringPolicy` | points, errors, lives, success thresholds |
| `difficultyPolicy` | adaptive rules for speed/count/timeout |
| `safetyPolicy` | invalid input, blocked channels, emergency stop |
| `controlPolicy` | remote-only, local-only, hybrid fallback |
| `telemetryPolicy` | mandatory events and quality gates |

## Atom Catalog (Lowest Reusable Units)

Atoms are the smallest reusable units used to build actions and steps.  
Actions are compositions of atoms; games are compositions of actions.

### Interaction Atoms

| Atom ID | Meaning | Produced By |
| --- | --- | --- |
| `input_pointer_press` | Pointer click/trigger event | `PointerActionAdapter` |
| `input_tool_impact` | Tool collision event | `ToolImpactActionAdapter` |
| `input_hand_contact` | Hand collider touch event | `HandContactActionAdapter` |
| `input_grab_started` | Object grab started | `GrabPlaceActionAdapter` |
| `input_grab_released` | Object grab released | `GrabPlaceActionAdapter` |
| `input_zone_entered` | Object entered target zone | `GrabPlaceActionAdapter` |
| `input_gaze_tick` | Gaze sample on target | `GazeActionAdapter` |
| `input_breath_phase` | Inhale/hold/exhale sample | `BreathCycleAdapter` |
| `input_audio_choice` | Player selected sound source | `AudioSourceLocalizationAdapter` |
| `input_pose_sample` | Body/hand pose sample | `PosePathAdapter` |
| `input_timeline_tick` | Passive timeline progress sample | `TimelineWatchAdapter` |

### Condition Atoms

| Atom ID | Meaning |
| --- | --- |
| `condition_step_allows_action` | Action is allowed in active step |
| `condition_target_allowed` | Target belongs to allowed binding/group |
| `condition_tool_matches` | Required tool matches current tool |
| `condition_sequence_index_matches` | Player input order is correct |
| `condition_time_window_open` | Input happened in allowed time window |
| `condition_gaze_dwell_reached` | Required gaze dwell time reached |
| `condition_zone_accepts_object` | Zone accepts object/tag/type |
| `condition_dual_hand_sync_ok` | Left-right sync satisfied |
| `condition_pose_within_tolerance` | Pose/path error below threshold |
| `condition_prerequisite_done` | Previous required milestones completed |
| `condition_channel_enabled` | Channel is enabled by definition/policy |
| `condition_control_mode_allows_action` | Current control mode permits action |

### State Atoms

| Atom ID | Meaning |
| --- | --- |
| `state_counter_increment` | Increment progress/score/error counters |
| `state_timer_started` | Start timer for step/action |
| `state_timer_stopped` | Stop timer and record duration |
| `state_flag_set` | Set runtime flag (for branch/gate) |
| `state_binding_lock` | Reserve object/zone for current action |
| `state_binding_release` | Release object/zone lock |

### Decision Atoms

| Atom ID | Meaning |
| --- | --- |
| `decision_accept_action` | Action accepted by gate/validator |
| `decision_reject_action` | Action rejected with `reasonCode` |
| `decision_complete_step` | Current step finished |
| `decision_timeout_step` | Step timed out |
| `decision_branch_to_step` | Branch to a specific next step |
| `decision_fail_flow` | Flow marked as failed |
| `decision_complete_flow` | Flow marked as completed |

### Effect Atoms

| Atom ID | Meaning |
| --- | --- |
| `effect_show` | Show/enable object |
| `effect_hide` | Hide/disable object |
| `effect_spawn` | Spawn object/group |
| `effect_despawn` | Remove object/group |
| `effect_audio_play` | Start audio |
| `effect_audio_stop` | Stop audio |
| `effect_sfx_play` | One-shot SFX |
| `effect_vfx_play` | One-shot VFX |
| `effect_anim_trigger` | Trigger animation |
| `effect_timeline_play` | Play timeline |
| `effect_timeline_stop` | Stop timeline |
| `effect_hint_emit` | Show hint/narrator message |

### Telemetry Atoms

| Atom ID | Meaning |
| --- | --- |
| `telemetry_action_received` | Raw action captured |
| `telemetry_action_evaluated` | Action decision emitted |
| `telemetry_step_entered` | Step entered |
| `telemetry_step_completed` | Step completed |
| `telemetry_step_timed_out` | Step timeout |
| `telemetry_effect_executed` | Effect execution emitted |
| `telemetry_trace_ref` | Compact trace artifact linked |

### Action = Atom Composition Examples

| Action ID | Atom Composition |
| --- | --- |
| `touch_target_with_hand` | `input_hand_contact` + `condition_step_allows_action` + `condition_target_allowed` + `decision_accept_action/reject_action` + `telemetry_action_evaluated` |
| `touch_target_with_tool` | `input_tool_impact` + `condition_tool_matches` + `condition_target_allowed` + decision atoms + telemetry atoms |
| `collect_item_to_container` | `input_grab_started` + `input_zone_entered` + `condition_zone_accepts_object` + `state_counter_increment` + `decision_complete_step`(optional) |
| `hold_gaze_on_target` | `input_gaze_tick` + `condition_gaze_dwell_reached` + `state_timer_started/stopped` + decision atoms |
| `identify_sound_source` | `effect_audio_play` + `input_audio_choice` + `condition_target_allowed` + decision atoms |

## Component Catalog (Agreed Core Shape)

This is the explicit component set for scene workflow. Keep it per activity, not per game.

| Component | Responsibility | Input | Output | Status |
| --- | --- | --- | --- | --- |
| `SessionFlowRunner` | Runs flow steps in deterministic order | flow definition + bindings | active step, lifecycle hooks | To add |
| `ActionGate` | Decides if action is allowed in current step | normalized action event | `accepted/rejected + reasonCode` | To add |
| `ActionValidator` | Validates target/tool/sequence/time constraints | allowed action config + action event | validation result | To add |
| `TransitionEngine` | Chooses next step on success/fail/timeout/branch | step result + rules | next step id | To add |
| `EffectRunner` | Executes effects on step hooks | effect list + trigger context | scene/audio/vfx changes | To add |
| `FlowBindingRegistry` | Maps binding keys to scene objects/sources | scene references | lookup for actions/effects | To add |
| `TelemetryLedger` | Emits canonical flow/action events | runner/gate/effect signals | `flow_*`, `action_*`, `effect_*` events | To add |
| `MotionTraceRecorder` | Optional compact motion trace artifact | transform stream | encoded trace + `trace_ref` event | To add |
| `FlowConfigProvider` | Loads flow data (`ScriptableObject` or JSON) | game/session selection | resolved `FlowDefinition` | To add |
| `ActionAdapterRegistry` | Registers adapters for all input channels | adapter components | normalized action stream | To add |

### Adapter Components

| Adapter | Covers Action IDs | Status |
| --- | --- | --- |
| `PointerActionAdapter` | `point_and_select_target`, `confirm_choice`, `choose_reward` | Partial (via existing pointer telemetry path) |
| `ToolImpactActionAdapter` | `touch_target_with_tool`, `intercept_moving_target`, `avoid_hazard_contact` | Partial (via existing tool impact path) |
| `ToolGripActionAdapter` | `grab_object` (start), `release_object` | Partial (grip lifecycle exists, object semantics missing) |
| `HandContactActionAdapter` | `touch_target_with_hand` | Implemented in core plugin path |
| `GrabPlaceActionAdapter` | `grab_object`, `place_object_in_zone`, `remove_object_from_zone`, `collect_item_to_container` | Implemented in core plugin path |
| `GazeActionAdapter` | `hold_gaze_on_target`, `select_target_with_gaze_and_tool` | Implemented in core plugin path |
| `AudioSourceLocalizationAdapter` | `identify_sound_source` | Implemented in core plugin path |
| `BreathCycleAdapter` | `perform_breath_cycle` | Implemented in core plugin path |
| `DualHandSyncAdapter` | `mark_left_and_right_targets` | Implemented in core plugin path |
| `PosePathAdapter` | `hold_pose`, `follow_path` | Implemented in core plugin path |
| `SequenceReplayAdapter` | `repeat_visual_sequence`, `repeat_audio_sequence`, `select_sequence_in_order`, `match_pair` | Implemented in core plugin path |
| `TimelineWatchAdapter` | `watch_timeline_segment` | Implemented in core plugin path |

### Scene Composition Template

For each new scene, target this minimum component layout:

- `GameRuntimeService` (existing core runtime host)
- `FlowConfigProvider`
- `FlowBindingRegistry`
- `ActionAdapterRegistry`
- `SessionFlowRunner`
- `ActionGate`
- `ActionValidator`
- `TransitionEngine`
- `EffectRunner`
- `TelemetryLedger`
- `MotionTraceRecorder` (optional)

### Existing Core Building Blocks Already Reusable

- `InteractionEventBridge` for canonical interaction payload emission.
- `TargetValidationZone` for target validity rules.
- `ToolImpactProbe` for tool collision semantics.
- `ToolGripTracker` for grip lifecycle telemetry.
- `PointerTelemetryService` plus pointer interactor for ray-based action feed.
- `SequenceTaskEngine` and `TaskOutcomeAggregator` for sequence and outcome summaries.

## Data Contract

```json
{
  "flowId": "sample_flow_training",
  "commentVersion": "2026-02-24: added intercept_moving_target",
  "bindings": {
    "objectGroups": ["targets_blue", "targets_red", "reward_container"],
    "audio": ["narrator", "ambient", "sfx_correct", "sfx_wrong"],
    "timeline": ["story_main"]
  },
  "steps": [
    {
      "stepId": "step_01",
      "title": "Catch valid targets",
      "allowedActions": [
        {
          "actionId": "touch_target_with_tool",
          "targetGroup": "targets_blue",
          "constraints": {
            "requiredToolId": "wand_main",
            "timeWindowSec": 2.0
          }
        }
      ],
      "completion": {
        "mode": "count",
        "requiredCount": 10
      },
      "wrongActionPolicy": "warn",
      "timeoutSec": 120.0,
      "onEnter": [
        { "effectId": "spawn_object_group", "binding": "targets_blue" },
        { "effectId": "play_audio", "binding": "ambient" }
      ],
      "onAcceptedAction": [
        { "effectId": "play_sfx", "binding": "sfx_correct" },
        { "effectId": "play_vfx", "binding": "vfx_hit" }
      ],
      "onRejectedAction": [
        { "effectId": "play_sfx", "binding": "sfx_wrong" }
      ],
      "onExit": [
        { "effectId": "despawn_object_group", "binding": "targets_blue" }
      ],
      "nextOnSuccess": "step_02",
      "nextOnFail": "step_fail",
      "nextOnTimeout": "step_timeout"
    }
  ]
}
```

## Canonical Event Schema (Telemetry)

Canonical telemetry is append-only and transport-agnostic.

### Envelope (Required For Every Event)

| Field | Type | Required |
| --- | --- | --- |
| `schema` | string | yes |
| `schemaVersion` | string | yes |
| `eventId` | string (UUID) | yes |
| `sessionId` | string | yes |
| `taskRunId` | string | yes |
| `attemptId` | string | yes |
| `sequenceNumber` | integer | yes |
| `occurredAtUtc` | ISO-8601 string | yes |
| `monotonicSec` | number | yes |
| `gameId` | string | yes |
| `flowId` | string | yes |
| `stepId` | string | recommended |
| `nodeId` | string | recommended |
| `controlMode` | string | recommended |
| `sourceComponent` | string | yes |
| `payloadVersion` | integer | yes |

### Action Decision Payload (Required Fields)

| Field | Type | Required |
| --- | --- | --- |
| `eventType` | string (`action_received` or `action_evaluated`) | yes |
| `actionId` | string | yes |
| `channelId` | string | yes |
| `targetId` | string | recommended |
| `decision` | string (`accepted` or `rejected`) | for `action_evaluated` |
| `reasonCode` | string | for `action_evaluated` |
| `reactionSec` | number | recommended |
| `details` | object | recommended |

### Required Event Set Per Action Attempt

For each action attempt, emit:

1. `action_received`
2. `action_evaluated` (exactly once)

Optional compact trace linkage:

3. `trace_ref` (if motion trace exists)

## Compact Trace Artifact (Optional)

Use compact trace only as a supplementary artifact for dense motion streams.

- recommended for head/left/right transform streams,
- optional binary compression with checksum and optional gzip transport,
- always linked by `trace_ref`,
- never replaces canonical action/decision events used for AI training datasets.

Minimum `trace_ref` fields:

- `traceId`
- `traceType` (for example `motion_trace`)
- `encoding`
- `checksum`
- `frameCount`

## Hard Data Guarantees For AI

These rules are mandatory to avoid delayed/misassigned events.

1. Append-first: write event to local durable log before network send.
2. Strict sequence: `sequenceNumber` is monotonic per session and never reused.
3. Causality keys: every event includes `sessionId`, `taskRunId`, `attemptId`, `stepId`.
4. Exactly-one decision: each `action_received` must have one `action_evaluated`.
5. No mutation: never overwrite prior events; use compensating event if correction is needed.
6. Idempotent delivery: dedupe by `eventId` at ingest and export stages.
7. Outbox with ack: keep pending events until explicit ack; retry with backoff on disconnect.
8. Clock duality: store both `occurredAtUtc` and `monotonicSec` for robust ordering.
9. Session close gate: session cannot complete if unresolved action decisions remain.
10. Export integrity: generated datasets include checksum manifest and event count summary.

Quality gates:

- reject dataset export if action decision coverage < 100%,
- reject export if sequence gaps are detected,
- reject export if any event misses mandatory correlation keys.
- reject export if any `action_received` has no matching `action_evaluated`.
- reject export if `session_terminal` event is missing for closed sessions.

## Universal Action Catalog

These actions cover target therapeutic gameplay mechanics for this framework.

| Action ID | What It Means | Typical Inputs | Typical Validation |
| --- | --- | --- | --- |
| `touch_target_with_hand` | Hit/touch target using hand collider | hand trigger/collision | target id, allowed hand, timing |
| `touch_target_with_tool` | Hit/touch target using wand/stick/tool | tool collider, trigger | tool id, target validity, color/tag |
| `point_and_select_target` | Laser/pointer select | ray hit + click | interactive target, gate state |
| `grab_object` | Pick object | grab begin | grabbable state, ownership |
| `release_object` | Release object | grab end | held object present |
| `place_object_in_zone` | Put held object into zone | overlap + release | zone accepts object type/tag |
| `remove_object_from_zone` | Take object out of slot/zone | grab from zone | zone occupied, rules allow swap |
| `collect_item_to_container` | Deliver collected item to container | overlap/insert | required item class/color/count |
| `hold_gaze_on_target` | Keep head gaze on target for duration | gaze ray + dwell time | min dwell, max drift, visibility |
| `select_target_with_gaze_and_tool` | Combined gaze + tool confirmation | gaze + tool touch | both channels valid in same window |
| `identify_sound_source` | Indicate direction/source of sound | pointer/hand selection | selected source equals active source |
| `perform_breath_cycle` | Inhale/hold/exhale cycle control | mic amplitude or breath sensor | thresholds, phase order, cadence |
| `repeat_visual_sequence` | Replay shown light/object sequence | touches/selects | exact order and length |
| `repeat_audio_sequence` | Replay heard rhythm/morse sequence | button/tool taps | pattern order and timing tolerance |
| `select_sequence_in_order` | Activate targets in required order | any selection channel | strict order, no skipped nodes |
| `mark_left_and_right_targets` | Simultaneous dual-hand marks | both hands/controllers | side correctness, sync window |
| `follow_path` | Move tool/hand along defined path | continuous pose stream | path coverage, deviation threshold |
| `hold_pose` | Hold required body/hand pose | tracked transforms | pose tolerance for min duration |
| `match_pair` | Find matching cards/symbols | select/select | pair equality and memory rules |
| `assemble_puzzle_piece` | Place puzzle piece into slot | grab/place | slot match, orientation tolerance |
| `intercept_moving_target` | Touch moving valid objects | tool/hand impact | target class validity, timing |
| `avoid_hazard_contact` | Avoid forbidden collisions | collision stream | forbidden tag touched = penalty |
| `defend_zone` | Stop intruders before protected zone | touches/hits | intruder lifecycle and zone health |
| `watch_timeline_segment` | Stay through passive narrative segment | time + optional attention checks | min watched duration, branch rules |
| `confirm_choice` | Explicit acknowledge/continue action | button/select | allowed only in confirm steps |
| `choose_reward` | Select reward at session end | pointer/grab | reward availability policy |

## Universal Effect Catalog

| Effect ID | Purpose |
| --- | --- |
| `show_object` | Make bound object visible/enabled |
| `hide_object` | Hide/disable bound object |
| `spawn_object` | Spawn prefab/entity at binding point |
| `despawn_object` | Remove spawned entity |
| `spawn_object_group` | Spawn group of entities |
| `despawn_object_group` | Remove group of entities |
| `play_audio` | Start audio clip/loop |
| `stop_audio` | Stop audio clip/loop |
| `play_sfx` | One-shot sound feedback |
| `play_vfx` | One-shot visual feedback |
| `set_animator_trigger` | Trigger animation state |
| `play_timeline` | Start timeline segment |
| `stop_timeline` | Stop timeline segment |
| `enable_interaction` | Allow adapters/colliders/input |
| `disable_interaction` | Block adapters/colliders/input |
| `set_ui_text` | Update instruction/score UI text |
| `update_score` | Update runtime score/metric |
| `fade_screen` | Fade in/out transition |
| `teleport_actor` | Move helper/avatar/guide actor |
| `emit_hint` | Show hint or narrator message |

## Gate Decision Contract

Each incoming action must always produce a decision:

- `accepted`
- `rejected`

Each decision must contain:

- `reasonCode`
- `flowId`
- `stepId`
- `actionId`
- `targetId`
- `inputSource`
- `occurredAtUtc`

Recommended rejection `reasonCode` values:

- `ACTION_NOT_ALLOWED_IN_STEP`
- `TARGET_NOT_ALLOWED`
- `TOOL_ID_MISMATCH`
- `WRONG_SEQUENCE_ORDER`
- `TIME_WINDOW_EXPIRED`
- `PREREQUISITE_NOT_MET`
- `INPUT_CHANNEL_DISABLED`

## Telemetry Contract For ML

To answer "what, when, how, and why", log all of the events below.

| Event Name | Required Fields |
| --- | --- |
| `flow_started` | `flowId`, `sessionId`, `startedAtUtc` |
| `step_entered` | `flowId`, `stepId`, `enteredAtUtc` |
| `action_received` | `flowId`, `stepId`, `actionId`, `inputSource`, `targetId` |
| `action_evaluated` | `flowId`, `stepId`, `actionId`, `decision`, `reasonCode`, `reactionSec` |
| `action_rejected` | `flowId`, `stepId`, `actionId`, `reasonCode` |
| `step_completed` | `flowId`, `stepId`, `completionMode`, `elapsedSec` |
| `step_timed_out` | `flowId`, `stepId`, `timeoutSec` |
| `flow_completed` | `flowId`, `durationSec`, `resultSummary` |
| `flow_failed` | `flowId`, `failedStepId`, `reasonCode` |
| `session_terminal` | `sessionId`, `terminalState`, `reasonCode` |
| `effect_executed` | `flowId`, `stepId`, `effectId`, `trigger` |
| `trace_ref` | `flowId`, `traceType`, `traceId`, `encoding` |

Mandatory correlation keys in every event:

- `eventId`
- `sessionId`
- `taskRunId`
- `attemptId`
- `sequenceNumber`
- `gameId`

## Mechanic Pattern Coverage

| Pattern | Action IDs Needed |
| --- | --- |
| Session hub and progression selection | `confirm_choice`, `watch_timeline_segment` (optional), `choose_reward` |
| Breath pacing and focus control | `perform_breath_cycle`, `hold_gaze_on_target` |
| Moving target interception with hazard avoidance | `intercept_moving_target`, `avoid_hazard_contact`, `touch_target_with_tool` |
| Combined gaze + tool confirmation | `select_target_with_gaze_and_tool`, `touch_target_with_tool` |
| Ordered selective activation | `touch_target_with_tool`, `select_sequence_in_order` |
| Calibration-style movement tasks | `hold_pose`, `follow_path`, `repeat_visual_sequence` |
| Controller-driven scene/session orchestration | `confirm_choice` + `ControlRuntime` + `SceneRuntime` |
| Passive narrative segment playback | `watch_timeline_segment`, `confirm_choice` |
| Hit-then-collect loop | `touch_target_with_tool`, `collect_item_to_container`, `avoid_hazard_contact` |
| Audio localization decision | `identify_sound_source`, `point_and_select_target` |
| Sequence memory replay (visual/audio) | `repeat_visual_sequence`, `repeat_audio_sequence`, `touch_target_with_tool` |
| Puzzle assembly and placement | `grab_object`, `place_object_in_zone`, `assemble_puzzle_piece` |
| Dual-hand coordinated interaction | `mark_left_and_right_targets`, `touch_target_with_hand` |
| Zone defense from moving intruders | `defend_zone`, `intercept_moving_target`, `touch_target_with_tool` |
| Pair matching cognitive tasks | `match_pair`, `point_and_select_target` |
| Grab/insert/swap environment interaction | `grab_object`, `place_object_in_zone`, `remove_object_from_zone` |
| Item sorting and container assignment | `collect_item_to_container`, `point_and_select_target`, `confirm_choice` |

## What Is Already In Core vs To Add

Available now:

- pointer interaction telemetry path
- tool impact telemetry path
- grip lifecycle telemetry path
- sequence/outcome aggregation primitives

Needed for full coverage:

- hand contact adapter for `touch_target_with_hand`
- grab/place adapter for object manipulation actions
- gaze adapter for dwell and combined gaze-tool selection
- audio-source localization adapter
- breath input adapter
- dual-hand synchronization adapter
- pose/path tracking adapter
- timeline watcher adapter

## Definition Of Done For New Scene

- Scene is authored as flow data + object bindings + adapters.
- No game-specific core changes are required.
- Every allowed and rejected action is emitted with reason code.
- Step transitions are deterministic and replayable from telemetry.
- Optional compact trace artifacts are referenced by `trace_ref`, never replacing canonical events.
