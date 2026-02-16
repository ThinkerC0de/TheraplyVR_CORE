# Mini-Game Contracts API

## Namespace

`TheraplyCore.Games.Contracts`

## Interfaces

### IMiniGameModule

Core contract implemented by each mini-game.

Methods:
- `Initialize(IMiniGameConfig config, IMiniGameContext context)`
- `StartGame()`
- `PauseGame()`
- `ResumeGame()`
- `StopGame(MiniGameStopReason reason)`
- `UpdateConfig(IMiniGameConfig newConfig)`
- `BuildResult()`

Properties:
- `string GameId`
- `MiniGameState State`

### IMiniGameConfig

Versioned config contract for each game.

Properties:
- `string GameId`
- `int Version`

### IMiniGameResult

Unified game result contract.

Properties:
- `string GameId`
- `bool Completed`
- `float DurationSec`
- `IReadOnlyDictionary<string, object> Metrics`

### IMiniGameContext

Dependency boundary for mini-games.

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
- `bool TryResolve(string gameId, out IMiniGameModule module)`

## Command Models

- `StartGameCommand`
- `PauseGameCommand`
- `ResumeGameCommand`
- `StopGameCommand`
- `UpdateConfigCommand<TConfig>`

All commands implement `IGameCommand` and include `CorrelationId`.
Standard IDs are exposed in `MiniGameCommandIds`.

## Enums

- `MiniGameState`
- `MiniGameStopReason`

## Runtime Implementations

Namespace:
- `TheraplyCore.Games.Runtime`

Main runtime services:
- `MiniGameSessionContext`
- `MiniGameClockService`
- `MiniGameFeedbackService`
- `MiniGameTelemetryService`
- `MiniGameCommandBus`
- `MiniGameContextService`
- `MiniGameRegistryService`
- `MiniGameRuntimeService`
- `MiniGameModuleBase` (base class for new mini-games)
