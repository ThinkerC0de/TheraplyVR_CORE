using System;
using System.Collections.Generic;
using UnityEngine;
using TheraplyCore.Games.Runtime;
using TheraplyCore.Games.Contracts;

namespace TheraplyExamples
{
    /// <summary>
    /// SIMPLE CUBE GAME - Example Implementation
    ///
    /// PURPOSE: Demonstrate the framework API
    /// This is NOT a production game - it's a learning tool
    ///
    /// GAMEPLAY:
    /// 1. Cube spawns at random position
    /// 2. Player clicks cube with controller (or mouse in Editor)
    /// 3. Score increases, cube moves
    /// 4. Game ends when target score reached or time limit expires
    ///
    /// DEMONSTRATES:
    /// - Implementing IGameModule via GameModuleBase
    /// - Telemetry via TrackEvent()
    /// - Result reporting via BuildResult()
    /// - IDefaultGameConfigProvider for zero-config start
    /// </summary>
    public class SimpleCubeGame : GameModuleBase, IDefaultGameConfigProvider
    {
        public override string GameId => "example_cube_clicker";

        [Header("Game Objects")]
        [SerializeField] private GameObject _cubePrefab;
        [SerializeField] private Transform _spawnArea;
        [SerializeField] private float _spawnRadius = 3f;

        [Header("Audio (Optional)")]
        [SerializeField] private AudioClip _clickSound;
        [SerializeField] private AudioClip _successSound;

        private GameObject _currentCube;
        private SimpleCubeConfig _config;
        private int _score;
        private int _totalClicks;
        private readonly List<float> _reactionTimes = new List<float>();
        private float _lastSpawnTime;

        // ============================================
        // IDefaultGameConfigProvider
        // ============================================

        public IGameConfig CreateDefaultConfig() => new SimpleCubeConfig();

        // ============================================
        // LIFECYCLE (override GameModuleBase)
        // ============================================

        public override void Initialize(IGameConfig config, IGameContext context)
        {
            base.Initialize(config, context);

            _config = config as SimpleCubeConfig ?? new SimpleCubeConfig();
            _score = 0;
            _totalClicks = 0;
            _reactionTimes.Clear();
        }

        public override void StartGame()
        {
            var isResume = State == GameState.Paused;
            base.StartGame();

            if (isResume)
            {
                if (_currentCube != null) _currentCube.SetActive(true);
            }
            else
            {
                SpawnCube();
            }
        }

        public override void PauseGame()
        {
            base.PauseGame();
            if (_currentCube != null) _currentCube.SetActive(false);
        }

        public override void StopGame(GameStopReason reason)
        {
            if (_currentCube != null)
            {
                Destroy(_currentCube);
                _currentCube = null;
            }
            base.StopGame(reason);
        }

        public override IGameResult BuildResult()
        {
            var avgReaction = _reactionTimes.Count > 0 ? CalculateAverage(_reactionTimes) : 0f;
            var accuracy = _totalClicks > 0 ? (float)_score / _totalClicks : 0f;

            var metrics = new Dictionary<string, object>
            {
                { "score", _score },
                { "totalClicks", _totalClicks },
                { "avgReactionTime", avgReaction },
                { "accuracy", accuracy },
                { "completed", _score >= _config.targetScore },
                { "durationSec", GetDurationSeconds() },
            };

            return new GameResult(GameId, _score >= _config.targetScore, GetDurationSeconds(), metrics);
        }

        // ============================================
        // UNITY LIFECYCLE
        // ============================================

        private void Update()
        {
            if (State != GameState.Playing) return;

            if (_config.timeLimit > 0f && GetDurationSeconds() >= _config.timeLimit)
            {
                StopGame(GameStopReason.Timeout);
            }
        }

        // ============================================
        // GAME LOGIC
        // ============================================

        private void SpawnCube()
        {
            if (_currentCube != null) Destroy(_currentCube);

            var center = _spawnArea != null ? _spawnArea.position : Vector3.zero;
            var pos = center + UnityEngine.Random.insideUnitSphere * _spawnRadius;

            _currentCube = Instantiate(_cubePrefab, pos, Quaternion.identity);

            var scale = Mathf.Lerp(1.0f, 0.3f, (_config.difficulty - 1) / 4f);
            _currentCube.transform.localScale = Vector3.one * scale;

            var handler = _currentCube.AddComponent<CubeClickHandler>();
            handler.OnClick += HandleCubeClick;

            _lastSpawnTime = Time.time;

            TrackEvent("cube_spawned", new Dictionary<string, object>
            {
                { "position", pos.ToString() },
                { "scale", scale },
                { "difficulty", _config.difficulty },
            });
        }

        private void HandleCubeClick()
        {
            if (State != GameState.Playing) return;

            _totalClicks++;
            _score++;

            var reactionTime = Time.time - _lastSpawnTime;
            _reactionTimes.Add(reactionTime);

            var cubePos = _currentCube != null ? _currentCube.transform.position : Vector3.zero;
            var cubeScale = _currentCube != null ? _currentCube.transform.localScale.x : 0f;

            TrackEvent("cube_clicked", new Dictionary<string, object>
            {
                { "score", _score },
                { "reactionTime", reactionTime },
                { "totalClicks", _totalClicks },
                { "cubeScale", cubeScale },
            });

            if (_clickSound != null)
                AudioSource.PlayClipAtPoint(_clickSound, cubePos);

            if (_score >= _config.targetScore)
            {
                if (_successSound != null && Camera.main != null)
                    AudioSource.PlayClipAtPoint(_successSound, Camera.main.transform.position);
                StopGame(GameStopReason.Completed);
            }
            else
            {
                SpawnCube();
            }
        }

        private static float CalculateAverage(List<float> values)
        {
            var sum = 0f;
            foreach (var v in values) sum += v;
            return sum / values.Count;
        }

#if UNITY_EDITOR
        [ContextMenu("Test: Start with default config")]
        private void EditorTestStart()
        {
            if (Context == null)
            {
                Debug.LogWarning("[SimpleCubeGame] Cannot test start: no IGameContext. Use GameRuntimeService to initialize.");
                return;
            }
            Initialize(CreateDefaultConfig(), Context);
            StartGame();
        }

        [ContextMenu("Test: Stop game")]
        private void EditorTestStop() => StopGame(GameStopReason.UserExit);
#endif
    }

    // ============================================
    // CONFIGURATION
    // ============================================

    [Serializable]
    public class SimpleCubeConfig : IGameConfig
    {
        public string GameId => "example_cube_clicker";
        public int Version => 1;

        public int difficulty = 1;
        public float timeLimit = 0f;
        public int targetScore = 10;
        public float cubeSize = 1.0f;
    }

    // ============================================
    // CLICK HANDLER COMPONENT
    // ============================================

    /// <summary>
    /// Click detection for Editor (OnMouseDown) and VR controllers (OnTriggerEnter).
    /// </summary>
    public class CubeClickHandler : MonoBehaviour
    {
        public event Action OnClick;

#if UNITY_EDITOR
        private void OnMouseDown() => OnClick?.Invoke();
#endif

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Controller")) OnClick?.Invoke();
        }
    }
}
