using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TheraplyCore.Games.Contracts
{
    /// <summary>
    /// Stable contract for all future games.
    /// Core code should depend on this interface only.
    /// </summary>
    public interface IGameModule
    {
        string GameId { get; }
        GameState State { get; }

        void Initialize(IGameConfig config, IGameContext context);
        void StartGame();
        void PauseGame();
        void ResumeGame();
        void StopGame(GameStopReason reason);
        void UpdateConfig(IGameConfig newConfig);

        IGameResult BuildResult();
    }

    public interface IGameConfig
    {
        string GameId { get; }
        int Version { get; }
    }

    /// <summary>
    /// Optional contract for modules that can supply a runtime-safe default config.
    /// Used by smoke tests and minimal start flows when no explicit UPDATE_CONFIG exists yet.
    /// </summary>
    public interface IDefaultGameConfigProvider
    {
        IGameConfig CreateDefaultConfig();
    }

    /// <summary>
    /// Optional contract for modules that can translate START_GAME payload fields into runtime config.
    /// Allows mobile-selected parameters to be applied without a separate UPDATE_CONFIG command.
    /// </summary>
    public interface IStartCommandConfigProvider
    {
        bool TryCreateConfigFromStartCommand(
            StartGameCommand command,
            IGameConfig previousConfig,
            out IGameConfig resolvedConfig,
            out string reasonCode);
    }

    public interface IGameResult
    {
        string GameId { get; }
        bool Completed { get; }
        float DurationSec { get; }
        IReadOnlyDictionary<string, object> Metrics { get; }
    }

    public interface IGameContext
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

    public sealed class NarratorLineRequest
    {
        public string lineKey = string.Empty;
        public string textKey = string.Empty;
        public string audioBindingKey = string.Empty;
        public string fallbackText = string.Empty;
        public int priority;
        public bool interruptIfBusy;
        public float simulatedDurationSec;
    }

    public interface INarratorService
    {
        bool IsSpeaking { get; }
        string ActiveLocale { get; }

        bool TrySpeak(NarratorLineRequest request, out string reasonCode);
        bool TrySpeakSequence(IReadOnlyList<NarratorLineRequest> requests, out string reasonCode);
        bool TryPlayAnimation(string actorBindingKey, string trigger, out string reasonCode);
        bool TrySetAttachment(string attachmentBindingKey, bool visible, out string reasonCode);
        bool TryInterrupt(string reasonCode = "NARRATOR_INTERRUPTED");
    }

    public interface ILocalizationService
    {
        string CurrentLocale { get; }
        string DefaultLocale { get; }

        bool TrySetLocale(string locale, out string reasonCode);
        bool TryResolve(string key, out string value, out string resolvedLocale, out string reasonCode);
        IReadOnlyList<string> BuildFallbackChain(string locale);
    }

    public interface ICalendarTimeSource
    {
        DateTime UtcNow { get; }
    }

    public interface ICalendarService
    {
        string ActiveTimezoneId { get; }
        DateTime CurrentUtc { get; }

        void SetRuntimeContext(string gameId, string flowId, string sessionId);
        void SetTimeSource(ICalendarTimeSource timeSource);
        bool TryApplyPolicy(
            CalendarPolicy policy,
            IReadOnlyDictionary<string, string> profileDatesByKey,
            out string reasonCode);
        bool TrySetOverrideUtc(string utcIso, out string reasonCode);
        void ClearOverrideUtc();
        void TickRuntime();

        bool TryEvaluateEventActive(string eventId, out bool isActive, out string reasonCode);
        bool TryEvaluateDateWindow(
            string start,
            string end,
            bool yearlyRecurring,
            string timezoneId,
            out bool matched,
            out string reasonCode);
        bool TryEvaluateProfileDate(
            string profileDateKey,
            int daysBefore,
            int daysAfter,
            string timezoneId,
            out bool matched,
            out string reasonCode);
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

    public enum GameState
    {
        NotInitialized,
        Initialized,
        Playing,
        Paused,
        Completed,
        Failed,
    }

    public enum GameStopReason
    {
        Completed,
        TherapistStop,
        Timeout,
        UserExit,
        NetworkLoss,
        Error,
    }
}
