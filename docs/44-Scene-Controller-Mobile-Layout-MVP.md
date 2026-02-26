# Scene Controller + Mobile Layout MVP

Date: 2026-02-26

## Goal

Ship playable demos quickly using a simple authoring path:

1. One scene script per game (`SceneGameController`) owns gameplay logic and object wiring.
2. Unity editor defines mobile card controls/layout and exported config contract.
3. Mobile app renders controls dynamically and sends `START_GAME` / `UPDATE_CONFIG`.

## Scope

- Keep existing SessionFlow core, telemetry, and dataset/ML pipeline.
- Do not delete Node Graph attempt; keep it as `Legacy/WIP`.
- Focus implementation on speed and predictability for demo delivery.

## Runtime contract

- `SceneGameController`:
  - `Initialize(...)`
  - `ApplyConfig(json/config object)`
  - `StartGame()`
  - `PauseGame()`
  - `ResumeGame()`
  - `StopGame()`
- Controller must emit canonical gameplay events through `InteractionEventBridge`.

Implementation status (2026-02-26):

- Base runtime contract is implemented in
  - `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/SceneGameController.cs`
- Core bridge behavior:
  - `START_GAME` and `UPDATE_CONFIG` are resolved through `SceneGameConfig` using existing `IStartCommandConfigProvider` path in `GameRuntimeService`.
  - no per-game branching is added to core runtime handlers.
- Authoring expectation:
  - each new game script inherits `SceneGameController` and overrides scene hooks (`OnStarted`, `OnPaused`, `OnResumed`, `OnStopped`, `ApplyConfig`).

## Mobile layout authoring contract

- `MobileControlLayoutAsset` (Unity):
  - control `type`, `label`, `bindingKey`, `position`, `scale`, optional `description`
  - value constraints: `min/max/step/default`, options list
- Export to contract consumed by Flutter dynamic renderer.

Implementation status (2026-02-26):

- Unity contract/asset:
  - `unity-quest-template/Assets/_TheraplyCore/Games/Contracts/MobileControlLayoutContracts.cs`
- Runtime compatibility bridge:
  - `MobileControlLayoutConverter` maps layout contract to existing `MobileControlSchema`.
- Flutter contract parser (ready for editor/runtime wiring in next step):
  - `flutter_controller/lib/models/mobile_control_layout_contract.dart`
- Unity visual editor:
  - `unity-quest-template/Assets/_TheraplyCore/Editor/Authoring/MobileControlLayoutEditorWindow.cs`
  - menu: `Theraply > Scene Controller > Mobile Layout Editor`
  - supports visual control arrangement in preview canvas (drag), contract validation, and export (`layout`/`schema` JSON).

## Acceptance criteria

1. New game can be created by scene script + layout asset without core runtime edits.
2. Mobile setup renders configured controls and updates runtime config.
3. Canonical events include action/effect/flow decisions for QA and ML export.
