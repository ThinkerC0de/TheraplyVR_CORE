# Creating Games (Framework Contract v1)

This guide explains how to build mini-games so core code does not need changes for each new game.

## Goal

Each mini-game should:
- implement a stable contract,
- use only services from game context,
- emit standard telemetry,
- produce a standard game result.

## Required Contracts

Use interfaces from:
- `Assets/_TheraplyCore/Games/Contracts/GameContracts.cs`
- `Assets/_TheraplyCore/Games/Contracts/GameCommands.cs`
- `Assets/_TheraplyCore/Games/Contracts/GameRegistry.cs`
- runtime implementations from `Assets/_TheraplyCore/Games/Runtime/`

Required implementation points:
1. `IMiniGameModule`
2. `IMiniGameConfig`
3. `IMiniGameResult`

Recommended base class:
- `MiniGameModuleBase` (path: `Assets/_TheraplyCore/Games/Runtime/MiniGameModuleBase.cs`)

## Standard Lifecycle

Required lifecycle sequence:
1. `Initialize(config, context)`
2. `StartGame()`
3. `PauseGame()` / `ResumeGame()` (zero or more times)
4. `StopGame(reason)`
5. `BuildResult()`

State transitions should use `MiniGameState`.

## Typed Commands

Do not parse raw command strings in game code.
Use typed commands:
- `StartGameCommand`
- `PauseGameCommand`
- `ResumeGameCommand`
- `StopGameCommand`
- `UpdateConfigCommand<TConfig>`

Wire-level IDs are defined in `MiniGameCommandIds`.

## Telemetry Standard

Track all critical events using `ITelemetryService.Track(...)`.

Mandatory events:
- `game_initialized`
- `game_started`
- `game_paused`
- `game_resumed`
- `game_stopped`
- `game_completed`
- `error_raised`

Each event payload should include:
- `gameId`
- `sessionId`
- `timestampUtc`
- event-specific metrics

## Configuration Rules

Config must be versioned from day one:
- `GameId` must be constant and unique,
- `Version` must increase on breaking schema changes.

Example config fields:
- difficulty
- time limit
- target score
- accessibility toggles

## Scene/Prefab Configuration Checklist

1. Add game manager object with mini-game component.
2. Add runtime components from `docs/01-Runtime-Setup.md`.
3. Inject/register required services for `IMiniGameContext`.
4. Register game in registry (`gameId -> module`).
5. Verify telemetry events in logs.

## Runtime Components (Required)

At least one scene object must provide:
- `MiniGameSessionContext`
- `MiniGameCommandBus`
- `MiniGameTelemetryService`
- `MiniGameClockService`
- `MiniGameFeedbackService`
- `MiniGameContextService`
- `MiniGameRegistryService`
- `MiniGameRuntimeService`

## Definition of Done

A mini-game is ready only if:
- it runs full lifecycle,
- typed commands work,
- required telemetry events are emitted,
- result object is produced,
- no core code change was required.
