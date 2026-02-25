# Core Gap Closure Execution Plan

Date: 2026-02-25  
Scope: close the five critical core gaps before scaling scene authoring.

## Goal

Make the framework truly "author new scene from definition only" with no per-game runtime edits by closing:

1. deterministic `Condition` and `Branch` execution in `TaskGraphRuntime`,
2. universal narrator + localization runtime,
3. calendar/date-driven runtime events,
4. scene load/unload/reset actions exposed through flow effects,
5. locale switch in Unity driven by language selection in mobile settings.

## Checkpoint Protocol (Per Point)

Each point is one delivery checkpoint.

1. Implement code + tests + docs for the point.
2. Run required validation commands for that point.
3. Commit and push immediately.
4. Publish short status:
   `what we changed` + `what value it gives`.

Suggested commit naming:

- `SF-I1: deterministic condition and branch evaluation`
- `SF-I2: narrator and localization runtime`
- `SF-I3: calendar runtime and date events`
- `SF-I4: scene lifecycle effects for flow`
- `SF-I5: mobile settings locale sync`

## Point 1 - Deterministic Condition and Branch Evaluation

### Target

`TaskGraphRuntime` must evaluate real conditions, not route by fallback only.

### Deliverables

1. Add condition evaluation contract and runtime:
   - `IConditionEvaluator`,
   - `ConditionEvaluatorRegistry`,
   - `ConditionEvaluationResult`.
2. Extend `TransitionEngine` and `TaskGraphRunner`:
   - evaluate `node.conditions` for `Condition` and `Branch` nodes,
   - respect `branchPolicy.precedence`,
   - produce explicit failure reason codes when no route matches.
3. Add built-in condition evaluators:
   - score threshold,
   - state flag equals,
   - control mode equals,
   - channel enabled,
   - elapsed time window.
4. Add telemetry events:
   - `condition_evaluated`,
   - `branch_routed`.
5. Add tests:
   - branch true/false routing,
   - deterministic precedence,
   - no-match failure path,
   - telemetry emission coverage.

### Acceptance Criteria

1. `Condition`/`Branch` routes are deterministic from declared conditions.
2. No implicit success fallback when condition set does not match.
3. Validation suite passes for branch/condition scenarios.

### Short status after commit

- What we changed: "Implemented real condition/branch evaluator path in graph runtime."
- What it gives: "Scene author controls routing by data, not hardcoded fallback behavior."

### Implementation Update (2026-02-25)

Status: `DONE`

Delivered:

1. Added condition runtime contracts and registry:
   - `IConditionEvaluator`,
   - `ConditionEvaluatorRegistry`,
   - `ConditionEvaluationResult`,
   - `ConditionEvaluationContext`,
   - branch/condition trace models.
2. Added built-in condition evaluators:
   - `score_threshold`,
   - `state_flag_equals`,
   - `control_mode_equals`,
   - `channel_enabled`,
   - `elapsed_time_window`.
3. Updated `TaskGraphRunner`:
   - deterministic `Condition`/`Branch` routing from declared `conditions`,
   - precedence support via `branchPolicy.precedence` (`first_match`/`last_match`),
   - explicit no-match handling (`nextOnFail` route or deterministic fail),
   - no implicit branch fallback routing.
4. Updated `SessionFlowRunner`:
   - condition runtime state wiring (scoring/channels/state flags/precedence),
   - telemetry emission for `condition_evaluated` and `branch_routed`.
5. Updated `TransitionEngine`:
   - `Branch` trigger now requires explicit condition evaluation path.
6. Updated validation coverage:
   - true/false branch routing,
   - precedence determinism,
   - no-match failure path,
   - condition/branch telemetry event coverage.

## Point 2 - Universal Narrator and Localization Runtime

### Target

Provide one actor runtime API for speech/animation/attachments, fully locale-aware.

### Deliverables

1. Add contracts:
   - `INarratorService`,
   - `ILocalizationService`,
   - locale fallback policy contract.
2. Add runtimes:
   - `NarratorRuntime` (queue, priority, interrupt policy),
   - `LocalizationRuntime` (key resolution + fallback chain).
3. Add new flow effects:
   - `narrator_speak`,
   - `narrator_play_sequence`,
   - `narrator_play_animation`,
   - `narrator_set_attachment`,
   - `set_locale`.
4. Add locale-aware payload support:
   - keys instead of raw text where required,
   - fallback sequence (`primary -> language fallback -> default`).
5. Add telemetry events:
   - `narrator_line_started`,
   - `narrator_line_completed`,
   - `narrator_interrupted`,
   - `localization_fallback_used`,
   - `localization_key_missing`.
6. Add tests:
   - queue/interrupt behavior,
   - fallback locale behavior,
   - missing-key reason codes.

### Acceptance Criteria

1. Narration is triggered from flow effects only.
2. Locale switch works at runtime without scene-specific code.
3. Missing keys never fail silently; telemetry reports reason.

### Short status after commit

- What we changed: "Added universal narrator + localization runtime and effect hooks."
- What it gives: "Any scene can reuse one actor flow with multilingual speech and clean fallback."

### Implementation Update (2026-02-25)

Status: `DONE`

Delivered:

1. Added contracts:
   - `INarratorService`,
   - `ILocalizationService`,
   - `NarratorLineRequest`,
   - `LocalizationPolicy` in session flow policies.
2. Added runtimes:
   - `NarratorRuntime` with queue, priority, and interrupt behavior,
   - `LocalizationRuntime` with deterministic fallback chain.
3. Added new flow effects:
   - `narrator_speak`,
   - `narrator_play_sequence`,
   - `narrator_play_animation`,
   - `narrator_set_attachment`,
   - `set_locale`.
4. Added locale-aware resolution path:
   - `textKey` resolution in `set_ui_text`,
   - localized value resolution for `emit_hint`,
   - narrator line and audio-binding key resolution via localization keys.
5. Added telemetry events:
   - `narrator_line_started`,
   - `narrator_line_completed`,
   - `narrator_interrupted`,
   - `localization_fallback_used`,
   - `localization_key_missing`.
6. Added validation coverage:
   - narrator queue/interrupt deterministic behavior,
   - locale fallback behavior,
   - missing-key reason codes.

## Point 3 - Calendar Runtime and Date Events

### Target

Support date-driven variants and event windows as pure framework behavior.

### Deliverables

1. Add contracts:
   - `CalendarRuleDefinition`,
   - `CalendarEventDefinition`,
   - `CalendarPolicy`.
2. Add `CalendarRuntime` with pluggable time source:
   - UTC normalization,
   - timezone policy support,
   - override mode for QA/testing.
3. Add built-in date conditions:
   - `is_event_active`,
   - `is_within_date_window`,
   - `is_profile_birthday`.
4. Add event-driven effects:
   - activate/deactivate binding variants,
   - trigger event sequence.
5. Add telemetry events:
   - `calendar_rule_evaluated`,
   - `calendar_event_activated`,
   - `calendar_event_expired`.
6. Add tests:
   - yearly and cross-year windows,
   - timezone edge cases,
   - event priority and conflict policy.

### Acceptance Criteria

1. Date-driven scene variants run by definition/policy only.
2. No direct `DateTime.Now` logic in game scene scripts.
3. Event activation is testable and auditable in telemetry.

### Short status after commit

- What we changed: "Added calendar runtime and date-event rules to flow."
- What it gives: "Seasonal and profile-date behaviors are reusable and deterministic."

### Implementation Update (2026-02-25)

Status: `DONE`

Delivered:

1. Added calendar contracts and policy model:
   - `CalendarRuleDefinition`,
   - `CalendarEventDefinition`,
   - `CalendarPolicy`,
   - conflict and rule-type normalization contracts.
2. Added `CalendarRuntime` with:
   - pluggable `ICalendarTimeSource`,
   - timezone-aware rule evaluation,
   - QA override mode via runtime UTC override,
   - deterministic variant conflict resolution by policy.
3. Added built-in date conditions in deterministic condition path:
   - `is_event_active`,
   - `is_within_date_window`,
   - `is_profile_birthday`.
4. Added calendar-driven effects:
   - `set_binding_active_by_calendar_event`,
   - `trigger_sequence_by_calendar_event`.
5. Added telemetry events:
   - `calendar_rule_evaluated`,
   - `calendar_event_activated`,
   - `calendar_event_expired`.
6. Added validation coverage:
   - recurring yearly and cross-year windows,
   - timezone window edge behavior,
   - event priority/conflict policy behavior,
   - calendar condition routing integration in task graph runtime.

## Point 4 - Scene Lifecycle Effects in Flow

### Target

Expose scene lifecycle operations as first-class effects.

### Deliverables

1. Add effect plugins:
   - `load_scene`,
   - `unload_scene`,
   - `reset_scene`.
2. Wire these effects to `SceneRuntimeController` with strict reason codes.
3. Add safety checks:
   - prevent invalid scene operations in disallowed session states,
   - enforce mode constraints (`single`/`additive`).
4. Add telemetry events:
   - `scene_load_requested`,
   - `scene_unload_requested`,
   - `scene_reset_requested`,
   - success/failure result codes.
5. Add tests:
   - effect-to-runtime wiring,
   - invalid operation rejection,
   - repeated operation idempotency behavior.

### Acceptance Criteria

1. Scene lifecycle changes can be fully authored from `GameDefinition` effects.
2. No custom scene scripts needed for load/unload/reset orchestration.
3. Failure reasons are explicit and observable.

### Short status after commit

- What we changed: "Added scene lifecycle effect plugins backed by SceneRuntime."
- What it gives: "Flow can orchestrate scene swaps/resets without framework rewrites."

### Implementation Update (2026-02-25)

Status: `DONE`

Delivered:

1. Added built-in effect plugins:
   - `load_scene`,
   - `unload_scene`,
   - `reset_scene`.
2. Extended `SceneRuntimeController` with strict scene operation guards:
   - session-state safety gate (`SCENE_OPERATION_NOT_ALLOWED_IN_SESSION_STATE`),
   - explicit load mode validation (`single` or `additive`, otherwise `SCENE_LOAD_MODE_INVALID`),
   - idempotent repeated-operation behavior (`SCENE_ALREADY_LOADED`, `SCENE_ALREADY_ACTIVE`, `SCENE_ALREADY_UNLOADED`).
3. Added canonical scene request telemetry emissions:
   - `scene_load_requested`,
   - `scene_unload_requested`,
   - `scene_reset_requested`,
   with acceptance and reason codes.
4. Wired runtime context propagation (`gameId`, `flowId`, `sessionId`) from `EffectRunner` to `SceneRuntimeController`.
5. Added dedicated validation:
   - `SceneLifecycleEffectsValidation` for effect wiring, invalid-state rejection, and idempotency behavior.
6. Added validation pack step integration:
   - `scripts/unity_session_flow_validation_pack.ps1` now runs `SceneLifecycleEffectsValidation`.

## Point 5 - Mobile Settings Locale Sync

### Target

Allow mobile settings ("gears") language choice to change active Unity locale at runtime.

### Deliverables

1. Add control command contract for locale change:
   - `set_locale_request` with locale code payload.
2. Extend `ControlRuntimeGateway` command mapping:
   - accept locale command in `remote_only` and `hybrid` modes,
   - reject disallowed source with explicit reason code.
3. Wire command to `ILocalizationService`:
   - call locale switch in runtime immediately,
   - keep deterministic fallback behavior from `localizationPolicy`.
4. Add telemetry events:
   - `locale_change_requested`,
   - `locale_change_applied`,
   - `locale_change_rejected`.
5. Add tests:
   - runtime locale switch from remote command,
   - invalid/unsupported locale rejection,
   - mode restriction behavior (`local_only` rejects remote command).

### Acceptance Criteria

1. Changing language in mobile settings updates Unity locale without scene-specific scripts.
2. Locale switch path is reason-coded and fully observable in canonical telemetry.
3. Flow can continue safely after locale change during active session.

### Short status after commit

- What we changed: "Connected mobile settings language command to runtime locale switch."
- What it gives: "Operator can change session language live from controller settings."

## Validation Gate Per Checkpoint

Run the smallest set required to verify each point before commit.

1. Contract validation.
2. Task graph integration validation.
3. Adapter/effect integration validation relevant to the checkpoint.
4. Telemetry quality gate validation.

## Global Done Criteria for This Plan

1. All 5 points are merged with separate commits and pushes.
2. Scene author can express branching, narration/localization, date events, and scene lifecycle changes in definition/policies/effects only.
3. Canonical telemetry captures causal chain for all added runtime behaviors.
4. No per-game runtime branch is introduced.
