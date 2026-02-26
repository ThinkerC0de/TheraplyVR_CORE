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

## Mobile layout authoring contract

- `MobileControlLayoutAsset` (Unity):
  - control `type`, `label`, `bindingKey`, `position`, `scale`, optional `description`
  - value constraints: `min/max/step/default`, options list
- Export to contract consumed by Flutter dynamic renderer.

## Acceptance criteria

1. New game can be created by scene script + layout asset without core runtime edits.
2. Mobile setup renders configured controls and updates runtime config.
3. Canonical events include action/effect/flow decisions for QA and ML export.
