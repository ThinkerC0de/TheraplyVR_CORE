using System;
using System.Collections.Generic;
using UnityEngine;
using TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Default implementation for IMiniGameModule lifecycle with telemetry hooks.
    /// </summary>
    public abstract class MiniGameModuleBase : MonoBehaviour, IMiniGameModule
    {
        private float _startedAtRealtime = -1f;
        private float _pauseStartedAtRealtime = -1f;
        private float _accumulatedPauseSeconds = 0f;

        protected IMiniGameContext Context { get; private set; }
        protected IMiniGameConfig CurrentConfig { get; private set; }

        public abstract string GameId { get; }

        public MiniGameState State { get; protected set; } = MiniGameState.NotInitialized;

        public virtual void Initialize(IMiniGameConfig config, IMiniGameContext context)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (context == null) throw new ArgumentNullException(nameof(context));

            Context = context;
            CurrentConfig = config;
            State = MiniGameState.Initialized;

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
            if (State != MiniGameState.Initialized && State != MiniGameState.Paused)
            {
                Logger.Warning($"[{GameId}] Cannot start from state: {State}");
                return;
            }

            if (_startedAtRealtime < 0f)
            {
                _startedAtRealtime = Time.realtimeSinceStartup;
            }

            if (State == MiniGameState.Paused && _pauseStartedAtRealtime >= 0f)
            {
                _accumulatedPauseSeconds += Time.realtimeSinceStartup - _pauseStartedAtRealtime;
                _pauseStartedAtRealtime = -1f;
            }

            var eventName = State == MiniGameState.Paused ? "game_resumed" : "game_started";
            State = MiniGameState.Playing;
            TrackEvent(eventName);
        }

        public virtual void PauseGame()
        {
            if (State != MiniGameState.Playing)
            {
                Logger.Warning($"[{GameId}] Cannot pause from state: {State}");
                return;
            }

            _pauseStartedAtRealtime = Time.realtimeSinceStartup;
            State = MiniGameState.Paused;
            TrackEvent("game_paused");
        }

        public virtual void ResumeGame()
        {
            if (State != MiniGameState.Paused)
            {
                Logger.Warning($"[{GameId}] Cannot resume from state: {State}");
                return;
            }

            StartGame();
        }

        public virtual void StopGame(MiniGameStopReason reason)
        {
            if (State == MiniGameState.NotInitialized)
            {
                Logger.Warning($"[{GameId}] StopGame ignored. State is NotInitialized.");
                return;
            }

            if (State == MiniGameState.Paused && _pauseStartedAtRealtime >= 0f)
            {
                _accumulatedPauseSeconds += Time.realtimeSinceStartup - _pauseStartedAtRealtime;
                _pauseStartedAtRealtime = -1f;
            }

            State = reason == MiniGameStopReason.Completed ? MiniGameState.Completed : MiniGameState.Failed;

            TrackEvent("game_stopped", new Dictionary<string, object>
            {
                { "reason", reason.ToString() },
                { "durationSec", GetDurationSeconds() },
                { "finalState", State.ToString() },
            });
        }

        public virtual void UpdateConfig(IMiniGameConfig newConfig)
        {
            if (newConfig == null) throw new ArgumentNullException(nameof(newConfig));

            CurrentConfig = newConfig;
            TrackEvent("config_updated", new Dictionary<string, object>
            {
                { "configVersion", newConfig.Version },
                { "configGameId", newConfig.GameId },
            });
        }

        public virtual IMiniGameResult BuildResult()
        {
            var metrics = new Dictionary<string, object>
            {
                { "state", State.ToString() },
                { "durationSec", GetDurationSeconds() },
            };

            return new MiniGameResult(
                GameId,
                State == MiniGameState.Completed,
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

            if (State == MiniGameState.Paused && _pauseStartedAtRealtime >= 0f)
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
        }

        private void ResetTiming()
        {
            _startedAtRealtime = -1f;
            _pauseStartedAtRealtime = -1f;
            _accumulatedPauseSeconds = 0f;
        }
    }

    /// <summary>
    /// Default result implementation for contract-based mini-games.
    /// </summary>
    public sealed class MiniGameResult : IMiniGameResult
    {
        private readonly IReadOnlyDictionary<string, object> _metrics;

        public MiniGameResult(string gameId, bool completed, float durationSec, IReadOnlyDictionary<string, object> metrics)
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
