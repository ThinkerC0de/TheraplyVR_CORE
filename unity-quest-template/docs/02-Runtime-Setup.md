# Runtime Setup (Contracts v1)

This guide configures the runtime required by `TheraplyCore.Games.Contracts`.

## 1. Add Runtime Root Object

In your scene create one GameObject, for example `MiniGameRuntimeRoot`.

Add components:
1. `MiniGameSessionContext`
2. `MiniGameClockService`
3. `MiniGameFeedbackService`
4. `MiniGameTelemetryService`
5. `MiniGameCommandBus`
6. `MiniGameContextService`
7. `MiniGameRegistryService`
8. `MiniGameRuntimeService`

Paths:
- `Assets/_TheraplyCore/Games/Runtime/`

## 2. Wire Network Dependencies

`MiniGameCommandBus` supports both runtime modes:
- host mode: `TCPServerService`
- client mode: `TCPConnectionService`

Assign whichever mode your scene uses.
If both exist, host route (`TCPServerService`) is preferred when a client is connected.

## 3. Wire Telemetry Dependencies

On `MiniGameTelemetryService`:
1. Assign `FirebaseDataService`.
2. Assign `MiniGameSessionContext`.
3. Enable payload enrichment (recommended).

## 4. Register Games

On `MiniGameRegistryService` add one entry per game:
- `gameId`: unique ID, same as game module.
- `moduleBehaviour`: component implementing `IMiniGameModule`.

## 5. Runtime Controller

On `MiniGameRuntimeService`:
1. Assign registry/context/command bus.
2. Set optional default game ID.
3. Keep `Subscribe To Standard Commands` enabled for automatic command handling.

Standard command IDs:
- `START_GAME`
- `PAUSE_GAME`
- `RESUME_GAME`
- `STOP_GAME`
- `UPDATE_CONFIG`

## 6. Smoke Test

Minimal smoke sequence:
1. Start scene.
2. Confirm registry logs loaded mappings.
3. Initialize one game through runtime API.
4. Send `START_GAME`, then `PAUSE_GAME`, then `RESUME_GAME`, then `STOP_GAME`.
5. Confirm telemetry events are queued.

Expected events:
- `game_initialized`
- `game_started`
- `game_paused`
- `game_resumed`
- `game_stopped`

## 7. Common Pitfalls

1. Game not found:
- missing or duplicate `gameId` in registry.

2. Commands ignored:
- wrong command ID casing.
- no TCP route connected.

3. Telemetry not appearing:
- missing `FirebaseDataService` reference.
- event payload contains unsupported data types.
