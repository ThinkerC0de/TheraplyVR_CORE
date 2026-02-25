# Session Flow Implementation Backlog

Date: 2026-02-24  
Source of truth: `docs/33-Session-Flow-Action-Card.md`  
Goal: implement the new framework direction end-to-end so no per-game core rewrites are needed.

## Rules

1. Keep naming activity-oriented and framework-neutral.
2. Do not add project/game-specific references.
3. Keep contracts stable; version changes only via schema fields/comments.
4. Treat canonical event telemetry as source of truth.
5. Keep compact traces optional and linked by `trace_ref`.

## Status Legend

- `TODO`
- `IN_PROGRESS`
- `DONE`
- `BLOCKED`

## Phase A - Contracts and Definitions

| ID | Task | Status | Acceptance Criteria | Depends On |
| --- | --- | --- | --- | --- |
| SF-A-001 | Define `GameDefinition` runtime model classes | DONE | `config`, `taskGraph`, `bindings`, `policies`, `channels`, `controlMode` represented in typed contracts | None |
| SF-A-002 | Define `TaskGraph` node contracts | DONE | Node types `Action/Condition/Branch/Timer/Complete/Fail` and route fields compiled | SF-A-001 |
| SF-A-003 | Define policy contracts | DONE | `wrongActionPolicy/retryPolicy/timeoutPolicy/branchPolicy/scoringPolicy/difficultyPolicy/safetyPolicy/controlPolicy/telemetryPolicy` available | SF-A-001 |
| SF-A-004 | Define action runtime contracts | DONE | `ActionIntent`, `AllowedActionDefinition`, `ActionValidationResult`, `ActionApplyResult`, `ActionContext` compiled | SF-A-001 |
| SF-A-005 | Define channel contracts | DONE | Channel IDs and enable/disable settings validated at load | SF-A-001 |

## Phase B - Core Runtimes

| ID | Task | Status | Acceptance Criteria | Depends On |
| --- | --- | --- | --- | --- |
| SF-B-001 | Implement `FlowConfigProvider` | DONE | Loads and validates definition from asset/json, returns typed `GameDefinition` | SF-A-001 |
| SF-B-002 | Implement `FlowBindingRegistry` | DONE | Stable key lookup for objects/zones/audio/timeline | SF-A-001 |
| SF-B-003 | Implement `SessionRuntime` bridge | DONE | FSM path enforced (`CREATED -> IN_PROGRESS -> ...`) and terminal states guarded | SF-A-001 |
| SF-B-004 | Implement `SceneRuntimeController` | DONE | Supports `LoadScene/UnloadScene/ResetScene/Spawn/Despawn/TeleportAnchor` | SF-A-001 |
| SF-B-005 | Implement `TaskGraphRunner` | DONE | Single active node, deterministic transition logic, timeout handling | SF-A-002 |
| SF-B-006 | Implement `ActionGate` | DONE | Every action receives `accepted/rejected` decision with reason code | SF-A-004 |
| SF-B-007 | Implement `ActionValidator` | DONE | Validates target/tool/sequence/time/channel/control-mode constraints | SF-A-004 |
| SF-B-008 | Implement `TransitionEngine` | DONE | Success/fail/timeout/branch transitions deterministic and logged | SF-A-002 |
| SF-B-009 | Implement `SessionFlowRunner` host | DONE | Binds definition + session bridge + adapters + task graph and emits flow/action telemetry | SF-B-005 |

## Phase C - Action Plugin System

| ID | Task | Status | Acceptance Criteria | Depends On |
| --- | --- | --- | --- | --- |
| SF-C-001 | Implement `IActionPlugin` interface and registry | DONE | Plugins can register/resolve by `actionId` at runtime | SF-A-004 |
| SF-C-002 | Implement adapter registry | DONE | Channel adapters publish normalized intents into gate/graph flow | SF-A-005 |
| SF-C-003 | Add pointer plugin path | DONE | `point_and_select_target`, `confirm_choice`, `choose_reward` resolve through plugin flow | SF-C-001 |
| SF-C-004 | Add tool impact plugin path | DONE | `touch_target_with_tool`, `intercept_moving_target`, `avoid_hazard_contact` resolve through plugin flow | SF-C-001 |
| SF-C-005 | Add hand contact adapter/plugin | DONE | `touch_target_with_hand` supported end-to-end | SF-C-001 |
| SF-C-006 | Add grab/place adapter/plugin | DONE | `grab_object`, `release_object`, `place_object_in_zone`, `remove_object_from_zone`, `collect_item_to_container` supported | SF-C-001 |
| SF-C-007 | Add gaze adapter/plugin | DONE | `hold_gaze_on_target`, `select_target_with_gaze_and_tool` supported | SF-C-001 |
| SF-C-008 | Add breath adapter/plugin | DONE | `perform_breath_cycle` supported with configurable thresholds/phases | SF-C-001 |
| SF-C-009 | Add audio source adapter/plugin | DONE | `identify_sound_source` supported with active cue + selected source validation | SF-C-001 |
| SF-C-010 | Add dual-hand adapter/plugin | DONE | `mark_left_and_right_targets` supported with sync window constraints | SF-C-001 |
| SF-C-011 | Add pose/path adapter/plugin | DONE | `hold_pose` and `follow_path` with tolerance checks supported | SF-C-001 |
| SF-C-012 | Add timeline watch adapter/plugin | DONE | `watch_timeline_segment` progress/interrupt decisions supported | SF-C-001 |
| SF-C-013 | Add sequence replay plugin | DONE | `repeat_visual_sequence`, `repeat_audio_sequence`, `select_sequence_in_order`, `match_pair` supported | SF-C-001 |

## Phase D - Effects and Scoring

| ID | Task | Status | Acceptance Criteria | Depends On |
| --- | --- | --- | --- | --- |
| SF-D-001 | Implement `EffectRunner` | DONE | Executes enter/exit/accepted/rejected/timeout effect hooks | SF-B-005 |
| SF-D-002 | Implement effect plugin interface | DONE | Effect handlers resolve by `effectId` | SF-D-001 |
| SF-D-003 | Implement base effect plugins | DONE | `show/hide/spawn/despawn/play_audio/stop_audio/play_sfx/play_vfx/set_animator_trigger/play_timeline/stop_timeline/enable_interaction/disable_interaction/set_ui_text/update_score/fade_screen/teleport_actor/emit_hint` available | SF-D-002 |
| SF-D-004 | Implement `ScoringRuntime` | DONE | Outputs `scoreTotal/correctCount/wrongCount/livesRemaining/successThresholdReached/adaptiveDifficultyState` | SF-A-003 |
| SF-D-005 | Integrate adaptive difficulty policy | DONE | Policy can adjust speed/count/timeout based on performance | SF-D-004 |

## Phase E - Telemetry and Data Correctness

| ID | Task | Status | Acceptance Criteria | Depends On |
| --- | --- | --- | --- | --- |
| SF-E-001 | Implement canonical event mapper | DONE | Required envelope fields emitted for all runtime events | SF-B-005 |
| SF-E-002 | Emit mandatory action event pair | DONE | Every `action_received` has exactly one `action_evaluated` | SF-E-001 |
| SF-E-003 | Add `session_terminal` emission | DONE | Closed sessions always emit terminal event with state/reason | SF-B-003 |
| SF-E-004 | Enforce append-first durability | DONE | Events persisted locally before network send path | SF-E-001 |
| SF-E-005 | Wire outbox ack/retry/dedupe for flow events | DONE | Reliable sync with idempotent `eventId` handling | SF-E-004 |
| SF-E-006 | Add compact trace recorder | DONE | Optional motion trace persisted and linked through `trace_ref` | SF-E-001 |
| SF-E-007 | Add export quality gates | DONE | Export blocked on missing decision pairs, sequence gaps, or missing mandatory keys | SF-E-002 |

## Phase F - Control Modes

| ID | Task | Status | Acceptance Criteria | Depends On |
| --- | --- | --- | --- | --- |
| SF-F-001 | Implement `ControlRuntimeGateway` | DONE | Supports `remote_only`, `local_only`, `hybrid` control modes | SF-A-003 |
| SF-F-002 | Integrate remote command path | DONE | Runtime start/pause/resume/stop works with controller transport | SF-F-001 |
| SF-F-003 | Integrate local fallback path | DONE | Session can run fully without remote controller when policy allows | SF-F-001 |
| SF-F-004 | Validate control-mode safety constraints | DONE | Disallowed control source is rejected with explicit reason code | SF-B-006 |

## Phase G - Validation and Hardening

| ID | Task | Status | Acceptance Criteria | Depends On |
| --- | --- | --- | --- | --- |
| SF-G-001 | Unit tests for contracts and validators | DONE | Contract parsing, policy validation, and reason-code coverage pass | SF-A-005 |
| SF-G-002 | Runtime integration tests for graph flow | DONE | Success/fail/timeout/branch scenarios deterministic and reproducible | SF-B-008 |
| SF-G-003 | Adapter integration tests | DONE | Each channel adapter emits normalized intent and final decision telemetry | SF-C-013 |
| SF-G-004 | Telemetry consistency tests | DONE | No missing mandatory fields, no unresolved action decisions | SF-E-007 |
| SF-G-005 | Reconnect/offline resilience tests | DONE | Outbox replay and dedupe behavior validated under disconnect/reconnect | SF-E-005 |
| SF-G-006 | Smoke scene template | DONE | One generic flow scene can run end-to-end using definition only | SF-D-005 |

## Phase H - Adoption Playbook

| ID | Task | Status | Acceptance Criteria | Depends On |
| --- | --- | --- | --- | --- |
| SF-H-001 | Create scene authoring checklist | DONE | Designer checklist for bindings/actions/effects/policies finalized | SF-G-006 |
| SF-H-002 | Create flow debugging runbook | DONE | Operator/dev guide for reason codes, traces, and failure triage finalized | SF-G-004 |
| SF-H-003 | Create migration template from mechanic pattern | DONE | New scene can be planned via action/effect/policy mapping without core edits | SF-H-001 |

## Phase I - Core Gap Closure (Current Direction)

Execution details: `docs/39-Core-Gap-Closure-Execution-Plan.md`

| ID | Task | Status | Acceptance Criteria | Depends On |
| --- | --- | --- | --- | --- |
| SF-I-001 | Implement deterministic `Condition` and `Branch` evaluation path | DONE | `TaskGraphRunner` + `TransitionEngine` evaluate declared conditions and branch policy, no implicit fallback routing | SF-B-008 |
| SF-I-002 | Implement universal narrator and localization runtime | DONE | Narration and locale switching are data-driven through effects, with fallback and telemetry | SF-D-003 |
| SF-I-003 | Implement calendar runtime and date-driven event rules | DONE | Date windows/profile-date rules can activate variants and sequences without scene scripts, with deterministic conflict policy and calendar telemetry | SF-A-003 |
| SF-I-004 | Expose scene lifecycle operations as flow effects | DONE | `load_scene`, `unload_scene`, `reset_scene` are available through effect plugins with reason-coded failures | SF-B-004, SF-D-003 |
| SF-I-005 | Add locale sync from mobile settings to Unity runtime | TODO | Language selected in mobile settings is applied in Unity at runtime through `ControlRuntimeGateway` + `LocalizationRuntime`, with explicit telemetry and reason-coded rejection | SF-I-002, SF-F-002 |

## Global Definition Of Done

1. New scene is authored using `GameDefinition` only.
2. No per-game branches added to core runtime.
3. Every action attempt has complete decision telemetry.
4. Export quality gates pass with 100% required coverage.
5. Session runs in both remote and local modes as configured.
6. Phase `SF-I-001` to `SF-I-005` are delivered and marked `DONE`.

## Historical Note

The old "Sprint 1 Plan" section was removed because phases `A-H` are now the active source of truth and are fully marked `DONE`.
