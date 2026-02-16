using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TheraplyCore.Games.Contracts
{
    /// <summary>
    /// Stable contract for all future mini-games.
    /// Core code should depend on this interface only.
    /// </summary>
    public interface IMiniGameModule
    {
        string GameId { get; }
        MiniGameState State { get; }

        void Initialize(IMiniGameConfig config, IMiniGameContext context);
        void StartGame();
        void PauseGame();
        void ResumeGame();
        void StopGame(MiniGameStopReason reason);
        void UpdateConfig(IMiniGameConfig newConfig);

        IMiniGameResult BuildResult();
    }

    public interface IMiniGameConfig
    {
        string GameId { get; }
        int Version { get; }
    }

    public interface IMiniGameResult
    {
        string GameId { get; }
        bool Completed { get; }
        float DurationSec { get; }
        IReadOnlyDictionary<string, object> Metrics { get; }
    }

    public interface IMiniGameContext
    {
        ISessionContext Session { get; }
        ICommandBus CommandBus { get; }
        ITelemetryService Telemetry { get; }
        IGameClock Clock { get; }
        IGameFeedback Feedback { get; }
    }

    public interface ISessionContext
    {
        string SessionId { get; }
        string PatientId { get; }
        string TherapistId { get; }
        DateTime StartedAtUtc { get; }
    }

    public interface ICommandBus
    {
        void Subscribe<TCommand>(Action<TCommand> handler) where TCommand : IGameCommand;
        void Unsubscribe<TCommand>(Action<TCommand> handler) where TCommand : IGameCommand;
        Task PublishAsync<TCommand>(TCommand command) where TCommand : IGameCommand;
    }

    public interface ITelemetryService
    {
        void Track(string eventName, IReadOnlyDictionary<string, object> payload, int payloadVersion = 1);
    }

    public interface IGameClock
    {
        float ElapsedSeconds { get; }
    }

    public interface IGameFeedback
    {
        void PlaySfx(string id);
        void HapticPulse(float amplitude, float durationSec);
        void ShowHint(string messageKey);
    }

    public enum MiniGameState
    {
        NotInitialized,
        Initialized,
        Playing,
        Paused,
        Completed,
        Failed,
    }

    public enum MiniGameStopReason
    {
        Completed,
        TherapistStop,
        Timeout,
        UserExit,
        NetworkLoss,
        Error,
    }
}