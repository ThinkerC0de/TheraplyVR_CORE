# Session Flow End-to-End Demo

Date: 2026-02-25

## Demo assets

- Sample flow asset:
  - `unity-quest-template/Assets/_YourGames/Samples/SessionFlow/DemoCubeFlowDefinition.asset`
- Sample mobile control schema:
  - `contracts/mobile_control_schema_demo_cube_clicker.json`
- Sample catalog seed (contains embedded schema for `demo_cube_clicker`):
  - `contracts/game_catalog_seed.json`

## End-to-end path

1. Unity author opens `Theraply/Session Flow/Flow Graph Editor (Legacy WIP)` and loads `DemoCubeFlowDefinition.asset`.
2. Author edits graph/policies/channels in one place and validates with `SessionFlowDefinitionValidator` from the editor window.
3. Catalog source (`game_catalog`) is seeded from `contracts/game_catalog_seed.json`.
4. Flutter controller opens `demo_cube_clicker` setup:
   - if schema exists in catalog entry, mobile renders controls dynamically,
   - if schema is absent, legacy hardcoded setup is used.
5. Therapist starts game:
   - mobile builds `START_GAME` payload from schema bindings (`gameConfigType/gameConfigVersion/gameConfigJson`).
6. Therapist can press schema button `Apply Runtime Config`:
   - mobile sends `UPDATE_CONFIG` payload built from the same schema bindings,
   - Unity runtime applies config through contract-based `IStartCommandConfigProvider` path,
   - no per-game branching is added in core runtime.

## Evidence checklist

- `START_GAME` still launches runtime with schema-built config.
- `UPDATE_CONFIG` is accepted by runtime command bus.
- Dynamic controls are rendered from schema (`slider/toggle/select/number/text/button`).
- Fallback path remains active only when schema is missing.
