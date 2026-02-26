# Node Graph Authoring - Archived WIP

Date: 2026-02-26

This directory keeps the Unity Node Graph authoring attempt as a preserved prototype.
Nothing was deleted from runtime/editor code; it is marked as `Legacy/WIP` and parked for future completion.

## What is parked

- Unity node graph editor window (`Flow Graph Editor`).
- Node-template workflow around TaskGraph authoring.
- Related "15-minute guide" for node authoring:
  - `docs/archive/node-graph-wip/42-Session-Flow-Authoring-15-Minute-Guide.md`

## Why parked

- Delivery pressure for near-term demos requires faster authoring with lower cognitive load.
- Current node workflow is functional but still too complex for rapid self-service use.

## Future completion backlog (when resumed)

1. Simplify node inspector into block-style UX (Scratch/Blockly-like).
2. Add type-safe control/constraint pickers (avoid raw string fields).
3. Add guided game templates with one-click setup and validation hints.
4. Add per-node contextual docs and reason-code troubleshooting panel.
5. Add graph-level simulation preview before runtime launch.

## Active direction now

- One scene script per game (`SceneGameController`) as source of gameplay logic.
- Dedicated Unity editor for mobile control layout authoring (type/position/scale/label/bindings).
- Keep canonical telemetry and ML dataset pipeline unchanged.
