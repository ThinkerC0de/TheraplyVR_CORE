# Game Contracts API

## Namespace

`TheraplyCore.Games.Contracts`

## Interfaces

### IGameModule

Core contract implemented by each game.

Methods:
- `Initialize(IGameConfig config, IGameContext context)`
- `StartGame()`
- `PauseGame()`
- `ResumeGame()`
- `StopGame(GameStopReason reason)`
- `UpdateConfig(IGameConfig newConfig)`
- `BuildResult()`

Properties:
- `string GameId`
- `GameState State`

### IGameConfig

Versioned config contract for each game.

Properties:
- `string GameId`
- `int Version`

### IGameResult

Unified game result contract.

Properties:
- `string GameId`
- `bool Completed`
- `float DurationSec`
- `IReadOnlyDictionary<string, object> Metrics`

### IGameContext

Dependency boundary for games.

Properties:
- `ISessionContext Session`
- `ICommandBus CommandBus`
- `ITelemetryService Telemetry`
- `IGameClock Clock`
- `IGameFeedback Feedback`

### ICommandBus

Typed command pub/sub.

Methods:
- `Subscribe<TCommand>(Action<TCommand> handler)`
- `Unsubscribe<TCommand>(Action<TCommand> handler)`
- `PublishAsync<TCommand>(TCommand command)`

### ITelemetryService

Unified telemetry emission.

Methods:
- `Track(string eventName, IReadOnlyDictionary<string, object> payload, int payloadVersion = 1)`

### IGameRegistry

Game lookup abstraction to avoid core branching.

Methods:
- `bool TryResolve(string gameId, out IGameModule module)`

## Command Models

- `StartGameCommand`
- `PauseGameCommand`
- `ResumeGameCommand`
- `StopGameCommand`
- `UpdateConfigCommand<TConfig>`

All commands implement `IGameCommand` and include `CorrelationId`.
Standard IDs are exposed in `GameCommandIds`.

## Enums

- `GameState`
- `GameStopReason`

## Runtime Implementations

Namespace:
- `TheraplyCore.Games.Runtime`

Main runtime services:
- `GameSessionContext`
- `GameClockService`
- `GameFeedbackService`
- `GameTelemetryService`
- `GameCommandBus`
- `GameContextService`
- `GameRegistryService`
- `GameRuntimeService`
- `GameModuleBase` (base class for new games)
