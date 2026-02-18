using System;
using System.Collections.Generic;
using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;

namespace TheraplyExamples
{
    /// <summary>
    /// Minimal deterministic game used for mobile/editor smoke validation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SmokeTestGameModule : GameModuleBase, GameContracts.IDefaultGameConfigProvider
    {
        [Header("Smoke Config")]
        [SerializeField] private float _defaultAutoCompleteAfterSeconds = 0f;

        private bool _metricEmitted;
        private int _startCount;
        private int _pauseCount;
        private int _resumeCount;
        private int _stopCount;
        private GameContracts.GameStopReason _lastStopReason = GameContracts.GameStopReason.TherapistStop;
        private SmokeTestGameConfig _activeConfig;

        public override string GameId => SmokeTestGameConfig.DefaultGameId;

        public GameContracts.IGameConfig CreateDefaultConfig()
        {
            return SmokeTestGameConfig.CreateDefault(_defaultAutoCompleteAfterSeconds);
        }

        public override void Initialize(GameContracts.IGameConfig config, GameContracts.IGameContext context)
        {
            _activeConfig = config as SmokeTestGameConfig ??
                            SmokeTestGameConfig.CreateDefault(_defaultAutoCompleteAfterSeconds);

            _metricEmitted = false;
            _startCount = 0;
            _pauseCount = 0;
            _resumeCount = 0;
            _stopCount = 0;
            _lastStopReason = GameContracts.GameStopReason.TherapistStop;

            base.Initialize(_activeConfig, context);
        }

        public override void StartGame()
        {
            var previousState = State;
            base.StartGame();

            if (State != GameContracts.GameState.Playing)
            {
                return;
            }

            if (previousState != GameContracts.GameState.Paused)
            {
                _startCount++;
            }

            if (_metricEmitted)
            {
                return;
            }

            _metricEmitted = true;
            TrackEvent("smoke_metric", new Dictionary<string, object>
            {
                { "metric", "start_signal" },
                { "value", 1 },
                { "gameId", GameId },
            });
        }

        public override void PauseGame()
        {
            var wasPlaying = State == GameContracts.GameState.Playing;
            base.PauseGame();

            if (wasPlaying && State == GameContracts.GameState.Paused)
            {
                _pauseCount++;
            }
        }

        public override void ResumeGame()
        {
            var wasPaused = State == GameContracts.GameState.Paused;
            base.ResumeGame();

            if (wasPaused && State == GameContracts.GameState.Playing)
            {
                _resumeCount++;
            }
        }

        public override void StopGame(GameContracts.GameStopReason reason)
        {
            if (_stopCount > 0 &&
                (State == GameContracts.GameState.Completed || State == GameContracts.GameState.Failed))
            {
                return;
            }

            base.StopGame(reason);
            _stopCount++;
            _lastStopReason = reason;

            TrackEvent("smoke_result", new Dictionary<string, object>
            {
                { "completed", reason == GameContracts.GameStopReason.Completed },
                { "stopReason", reason.ToString() },
                { "startCount", _startCount },
                { "pauseCount", _pauseCount },
                { "resumeCount", _resumeCount },
                { "stopCount", _stopCount },
            });
        }

        public override GameContracts.IGameResult BuildResult()
        {
            var metrics = new Dictionary<string, object>
            {
                { "startCount", _startCount },
                { "pauseCount", _pauseCount },
                { "resumeCount", _resumeCount },
                { "stopCount", _stopCount },
                { "stopReason", _lastStopReason.ToString() },
                { "metricEmitted", _metricEmitted },
            };

            return new GameResult(
                GameId,
                _lastStopReason == GameContracts.GameStopReason.Completed,
                GetDurationSeconds(),
                metrics);
        }

        private void Update()
        {
            if (State != GameContracts.GameState.Playing || _activeConfig == null)
            {
                return;
            }

            if (_activeConfig.AutoCompleteAfterSec <= 0f)
            {
                return;
            }

            if (GetDurationSeconds() >= _activeConfig.AutoCompleteAfterSec)
            {
                StopGame(GameContracts.GameStopReason.Completed);
            }
        }
    }

    [Serializable]
    public sealed class SmokeTestGameConfig : GameContracts.IGameConfig
    {
        public const string DefaultGameId = "smoke_test_game";

        [SerializeField] private string _gameId = DefaultGameId;
        [SerializeField] private int _version = 1;
        [SerializeField] private float _autoCompleteAfterSec = 0f;

        public string GameId => string.IsNullOrWhiteSpace(_gameId) ? DefaultGameId : _gameId;
        public int Version => _version <= 0 ? 1 : _version;
        public float AutoCompleteAfterSec => Mathf.Max(0f, _autoCompleteAfterSec);

        public static SmokeTestGameConfig CreateDefault(float autoCompleteAfterSec = 0f)
        {
            return new SmokeTestGameConfig
            {
                _gameId = DefaultGameId,
                _version = 1,
                _autoCompleteAfterSec = Mathf.Max(0f, autoCompleteAfterSec),
            };
        }
    }
}
