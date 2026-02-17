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
        SessionLifecycleState SessionState { get; }
        bool CanTransitionTo(SessionLifecycleState nextState);
        bool TryTransitionTo(SessionLifecycleState nextState, string reasonCode = null);
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

    /// <summary>
    /// Canonical session lifecycle states shared by runtime and signaling layers.
    /// Use these exact wire-safe values.
    /// </summary>
    public enum SessionLifecycleState
    {
        CREATED,
        IN_PROGRESS,
        PAUSED,
        INTERRUPTED,
        COMPLETED,
        ABORTED_BY_THERAPIST,
        FAILED_TECHNICAL,
    }

    /// <summary>
    /// FSM transition rules for SessionLifecycleState.
    /// </summary>
    public static class SessionFsmContract
    {
        private static readonly Dictionary<SessionLifecycleState, HashSet<SessionLifecycleState>> AllowedTransitions =
            new Dictionary<SessionLifecycleState, HashSet<SessionLifecycleState>>
            {
                {
                    SessionLifecycleState.CREATED,
                    new HashSet<SessionLifecycleState>
                    {
                        SessionLifecycleState.IN_PROGRESS,
                        SessionLifecycleState.ABORTED_BY_THERAPIST,
                        SessionLifecycleState.FAILED_TECHNICAL,
                    }
                },
                {
                    SessionLifecycleState.IN_PROGRESS,
                    new HashSet<SessionLifecycleState>
                    {
                        SessionLifecycleState.PAUSED,
                        SessionLifecycleState.INTERRUPTED,
                        SessionLifecycleState.COMPLETED,
                        SessionLifecycleState.ABORTED_BY_THERAPIST,
                        SessionLifecycleState.FAILED_TECHNICAL,
                    }
                },
                {
                    SessionLifecycleState.PAUSED,
                    new HashSet<SessionLifecycleState>
                    {
                        SessionLifecycleState.IN_PROGRESS,
                        SessionLifecycleState.INTERRUPTED,
                        SessionLifecycleState.ABORTED_BY_THERAPIST,
                        SessionLifecycleState.FAILED_TECHNICAL,
                    }
                },
                {
                    SessionLifecycleState.INTERRUPTED,
                    new HashSet<SessionLifecycleState>
                    {
                        SessionLifecycleState.IN_PROGRESS,
                        SessionLifecycleState.ABORTED_BY_THERAPIST,
                        SessionLifecycleState.FAILED_TECHNICAL,
                    }
                },
                { SessionLifecycleState.COMPLETED, new HashSet<SessionLifecycleState>() },
                { SessionLifecycleState.ABORTED_BY_THERAPIST, new HashSet<SessionLifecycleState>() },
                { SessionLifecycleState.FAILED_TECHNICAL, new HashSet<SessionLifecycleState>() },
            };

        public static bool CanTransition(SessionLifecycleState fromState, SessionLifecycleState toState)
        {
            if (fromState == toState)
            {
                return true;
            }

            return AllowedTransitions.TryGetValue(fromState, out var nextStates) && nextStates.Contains(toState);
        }

        public static string ToWireState(SessionLifecycleState state)
        {
            return state.ToString();
        }

        public static bool TryParseWireState(string wireState, out SessionLifecycleState parsedState)
        {
            return Enum.TryParse(wireState, ignoreCase: false, out parsedState);
        }
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
