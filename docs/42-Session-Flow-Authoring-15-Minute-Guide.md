# Session Flow Authoring 15-Minute Guide

Date: 2026-02-25

Goal: create and run a new scene setup without adding per-game logic in core runtime.

## Prerequisites

- Unity project opens (`unity-quest-template`).
- Flutter controller builds (`flutter_controller`).
- Catalog source can be updated from `contracts/game_catalog_seed.json`.

## 15-Minute Path

1. Minute 0-2: Prepare a base definition asset in Unity.
   - Open `Theraply/Session Flow/Flow Graph Editor`.
   - Load `DemoCubeFlowDefinition.asset` from `Assets/_YourGames/Samples/SessionFlow`.
   - Use `Save Asset` to create a new asset in your game folder (if no asset is selected, editor asks for target path).

2. Minute 2-6: Author flow nodes and transitions.
   - Edit node list with `Action/Condition/Branch/Timer/Complete/Fail`.
   - Configure `allowedActions`, `conditions`, `onEnter/onExit` effects, and transition targets.
   - Set policies and channels in the same window.

3. Minute 6-7: Validate the definition in editor.
   - Run `Validate`.
   - Resolve reason-coded errors from `SessionFlowDefinitionValidator`.
   - Click `Export Contracts` to save asset and export contracts from Unity to `contracts/`.

4. Minute 7-10: Author mobile controls schema.
   - Start from `contracts/mobile_control_schema_demo_cube_clicker.json`.
   - Update `gameId`, section labels, controls, bindings, and validation rules.
   - Keep payload target as `game_config` for dynamic config path.

5. Minute 10-12: Register schema in catalog.
   - Add or update `mobileControlSchema` for your game entry in `contracts/game_catalog_seed.json`.
   - Keep control IDs and binding paths aligned with runtime config fields.

6. Minute 12-14: Run mobile setup and verify dynamic rendering.
   - Open game setup on Flutter controller.
   - Confirm controls are rendered from schema.
   - Confirm fallback hardcoded UI appears only when schema is missing.

7. Minute 14-15: Run runtime command path check.
   - Start session (`START_GAME`) and verify config payload is schema-built.
   - Trigger runtime update (`UPDATE_CONFIG`) from schema button control if present.
   - Verify no core runtime changes were required.

## Graph Editor Quick Controls

- Canvas:
  - `MMB drag` = pan.
  - `Mouse wheel` = zoom in/out.
  - `RMB` on empty canvas = context menu with categorized node creation.
- Node:
  - `RMB` on node = `Select`, `Rename`, `Set As Entry (Start)`, `Delete`, `Disconnect Outgoing`, `Disconnect Incoming`, `Disconnect All`.
  - `RMB` on node = `Effects -> Add On Enter -> Spawn Prefab Wave` adds ready-to-edit spawn preset.
  - `Rename` opens rename section in right inspector; apply a stable id like `start_action`.
- Start node:
  - Set explicit start in left panel: `Task Graph -> Entry Node`.
  - Renaming node id to `start` auto-assigns it as `Entry Node`.
  - Recommended convention: rename first node to `start`.

## Spawn Prefab Wave Parameters

Use effect id: `spawn_prefab_wave`

- `prefabKey`: prefab registry key from `SceneRuntimeController`.
- `spawnPointKey`: optional transform key used as spawn center.
- `bindingKeyPrefix`: prefix assigned to spawned instances (`prefix_1`, `prefix_2`, ...).
- `count`: number of spawned prefabs.
- `durationSec`: total spawn duration (`0` = instant batch).
- `areaSizeX`, `areaSizeY`, `areaSizeZ`: spawn box size around `spawnPointKey`.
- `randomYaw`: random Y rotation per spawned instance.

## Validation Commands

Run after authoring updates:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/unity_export_authoring_contracts.ps1
```

`unity_export_authoring_contracts.ps1` also synchronizes `mobile_control_schema_*.json` into `contracts/game_catalog_seed.json`.
It also synchronizes admin console seed asset: `admin_console_web/assets/contracts/game_catalog_seed.json`.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/authoring_contract_gate.ps1
```

```powershell
powershell -ExecutionPolicy Bypass -File scripts/unity_session_flow_validation_pack.ps1 -SkipCompile
```

```powershell
cd flutter_controller
flutter analyze
flutter test test/widget_test.dart test/game_catalog_service_test.dart test/mobile_control_schema_test.dart
```

## Done Checklist

- New `GameDefinitionAsset` is valid in Unity editor.
- Mobile schema passes Unity and Flutter contract parsing.
- Flutter setup renders controls dynamically from schema.
- `START_GAME` and optional `UPDATE_CONFIG` payloads are schema-driven.
- Core runtime stays generic (no per-game branch logic).
