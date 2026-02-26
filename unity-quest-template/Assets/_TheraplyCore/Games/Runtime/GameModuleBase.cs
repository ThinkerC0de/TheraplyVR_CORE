using System;
using System.Collections.Generic;
using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Interactions;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Default implementation for GameContracts.IGameModule lifecycle with telemetry hooks.
    /// </summary>
    public abstract class GameModuleBase : MonoBehaviour, GameContracts.IGameModule
    {
        private float _startedAtRealtime = -1f;
        private float _pauseStartedAtRealtime = -1f;
        private float _accumulatedPauseSeconds = 0f;

        protected GameContracts.IGameContext Context { get; private set; }
        protected GameContracts.IGameConfig CurrentConfig { get; private set; }

        public abstract string GameId { get; }

        public GameContracts.GameState State { get; protected set; } = GameContracts.GameState.NotInitialized;

        public virtual void Initialize(GameContracts.IGameConfig config, GameContracts.IGameContext context)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (context == null) throw new ArgumentNullException(nameof(context));

            Context = context;
            CurrentConfig = config;
            State = GameContracts.GameState.Initialized;

            ResetTiming();

            TrackEvent("game_initialized", new Dictionary<string, object>
            {
                { "gameId", GameId },
                { "configVersion", config.Version },
                { "configGameId", config.GameId },
            });
        }

        public virtual void StartGame()
        {
            if (State != GameContracts.GameState.Initialized && State != GameContracts.GameState.Paused)
            {
                Logger.Warning($"[{GameId}] Cannot start from state: {State}");
                return;
            }

            if (_startedAtRealtime < 0f)
            {
                _startedAtRealtime = Time.realtimeSinceStartup;
            }

            if (State == GameContracts.GameState.Paused && _pauseStartedAtRealtime >= 0f)
            {
                _accumulatedPauseSeconds += Time.realtimeSinceStartup - _pauseStartedAtRealtime;
                _pauseStartedAtRealtime = -1f;
            }

            var eventName = State == GameContracts.GameState.Paused ? "game_resumed" : "game_started";
            State = GameContracts.GameState.Playing;
            TrackEvent(eventName);
        }

        public virtual void PauseGame()
        {
            if (State != GameContracts.GameState.Playing)
            {
                Logger.Warning($"[{GameId}] Cannot pause from state: {State}");
                return;
            }

            _pauseStartedAtRealtime = Time.realtimeSinceStartup;
            State = GameContracts.GameState.Paused;
            TrackEvent("game_paused");
        }

        public virtual void ResumeGame()
        {
            if (State != GameContracts.GameState.Paused)
            {
                Logger.Warning($"[{GameId}] Cannot resume from state: {State}");
                return;
            }

            StartGame();
        }

        public virtual void StopGame(GameContracts.GameStopReason reason)
        {
            if (State == GameContracts.GameState.NotInitialized)
            {
                Logger.Warning($"[{GameId}] StopGame ignored. State is NotInitialized.");
                return;
            }

            if (State == GameContracts.GameState.Paused && _pauseStartedAtRealtime >= 0f)
            {
                _accumulatedPauseSeconds += Time.realtimeSinceStartup - _pauseStartedAtRealtime;
                _pauseStartedAtRealtime = -1f;
            }

            State = reason == GameContracts.GameStopReason.Completed ? GameContracts.GameState.Completed : GameContracts.GameState.Failed;
            var durationSec = GetDurationSeconds();
            var finalState = State.ToString();
            var reasonName = reason.ToString();
            var reasonCode = ResolveTerminalReasonCode(reason);

            TrackEvent("game_stopped", new Dictionary<string, object>
            {
                { "reason", reasonName },
                { "reasonCode", reasonCode },
                { "durationSec", durationSec },
                { "finalState", finalState },
            });

            TrackEvent("session_terminal", new Dictionary<string, object>
            {
                { "reason", reasonName },
                { "reasonCode", reasonCode },
                { "durationSec", durationSec },
                { "finalState", finalState },
                { "sessionState", finalState },
            });
        }

        public virtual void UpdateConfig(GameContracts.IGameConfig newConfig)
        {
            if (newConfig == null) throw new ArgumentNullException(nameof(newConfig));

            CurrentConfig = newConfig;
            TrackEvent("config_updated", new Dictionary<string, object>
            {
                { "configVersion", newConfig.Version },
                { "configGameId", newConfig.GameId },
            });
        }

        public virtual GameContracts.IGameResult BuildResult()
        {
            var metrics = new Dictionary<string, object>
            {
                { "state", State.ToString() },
                { "durationSec", GetDurationSeconds() },
            };

            return new GameResult(
                GameId,
                State == GameContracts.GameState.Completed,
                GetDurationSeconds(),
                metrics);
        }

        protected float GetDurationSeconds()
        {
            if (_startedAtRealtime < 0f)
            {
                return 0f;
            }

            var now = Time.realtimeSinceStartup;
            var paused = _accumulatedPauseSeconds;

            if (State == GameContracts.GameState.Paused && _pauseStartedAtRealtime >= 0f)
            {
                paused += now - _pauseStartedAtRealtime;
            }

            return Mathf.Max(0f, now - _startedAtRealtime - paused);
        }

        protected void TrackEvent(string eventName, IReadOnlyDictionary<string, object> payload = null, int payloadVersion = 1)
        {
            if (Context?.Telemetry == null)
            {
                return;
            }

            var mergedPayload = payload != null
                ? new Dictionary<string, object>(payload)
                : new Dictionary<string, object>();

            if (!mergedPayload.ContainsKey("gameId")) mergedPayload["gameId"] = GameId;
            if (!mergedPayload.ContainsKey("state")) mergedPayload["state"] = State.ToString();

            Context.Telemetry.Track(eventName, mergedPayload, payloadVersion);

            var interactionBridge = InteractionEventBridge.Instance;
            if (interactionBridge == null)
            {
                return;
            }

            try
            {
                interactionBridge.RecordGameplayEvent(
                    GameId,
                    eventName,
                    State.ToString(),
                    mergedPayload,
                    GetType().Name);
            }
            catch (Exception e)
            {
                Logger.Warning(
                    $"[{GameId}] Interaction bridge publish failed for event '{eventName}': {e.Message}");
            }
        }

        private void ResetTiming()
        {
            _startedAtRealtime = -1f;
            _pauseStartedAtRealtime = -1f;
            _accumulatedPauseSeconds = 0f;
        }

        private static string ResolveTerminalReasonCode(GameContracts.GameStopReason reason)
        {
            switch (reason)
            {
                case GameContracts.GameStopReason.Completed:
                    return "GAME_COMPLETED";
                case GameContracts.GameStopReason.TherapistStop:
                    return "GAME_STOPPED_BY_THERAPIST";
                case GameContracts.GameStopReason.Timeout:
                    return "GAME_TIMEOUT";
                case GameContracts.GameStopReason.UserExit:
                    return "GAME_STOPPED_BY_USER";
                case GameContracts.GameStopReason.NetworkLoss:
                    return "GAME_NETWORK_LOSS";
                case GameContracts.GameStopReason.Error:
                default:
                    return "GAME_RUNTIME_ERROR";
            }
        }
    }

    /// <summary>
    /// Default result implementation for contract-based games.
    /// </summary>
    public sealed class GameResult : GameContracts.IGameResult
    {
        private readonly IReadOnlyDictionary<string, object> _metrics;

        public GameResult(string gameId, bool completed, float durationSec, IReadOnlyDictionary<string, object> metrics)
        {
            GameId = gameId;
            Completed = completed;
            DurationSec = durationSec;
            _metrics = metrics ?? new Dictionary<string, object>();
        }

        public string GameId { get; }
        public bool Completed { get; }
        public float DurationSec { get; }
        public IReadOnlyDictionary<string, object> Metrics => _metrics;
    }
}
