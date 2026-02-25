# Session Flow Smoke Template

Date: 2026-02-25  
Scope: generic scene flow validation without per-game core code.

## Goal

Provide one reusable smoke template that verifies end-to-end execution driven only by `GameDefinition`:

1. load definition,
2. start session flow,
3. accept valid action,
4. reach `Complete` node,
5. repeat run without runtime reconfiguration.

## Runtime Stack

The smoke template uses framework runtimes only:

1. `FlowConfigProvider`
2. `ActionAdapterRegistry`
3. `SessionFlowRunner`

No game-specific module is required.

## Definition Contract Used

Validation fixture builds a `GameDefinition` with:

1. `gameId = "session_flow_smoke_template"`
2. `controlMode = "hybrid"`
3. channels from `SessionFlowDefaults.CreateDefaultChannels()`
4. task graph:
   `n_action (Action: confirm_choice, requiredChannelId=pointer, requiredTargetId=target_primary) -> n_complete (Complete)`

## Automation Entry Point

Run:

`TheraplyCore.Editor.Automation.SessionFlowSmokeTemplateValidation.RunSessionFlowSmokeTemplateValidation`

Result file:

`Temp/CliValidation/session_flow_smoke_template_validation_result.txt`

## Pass Criteria

Validation passes only when:

1. flow starts successfully from definition,
2. valid action transitions graph to `n_complete`,
3. graph state is `Completed`,
4. second run also completes (template is reusable).
