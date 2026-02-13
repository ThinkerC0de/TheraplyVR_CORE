using System;
using System.Collections.Generic;
using UnityEngine;
using TheraplyCore.Games;
using TheraplyCore.Network;
using VContainer;

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
    /// 2. Player clicks cube with controller
    /// 3. Score increases, cube moves
    /// 4. Game ends when target score reached or time limit expires
    /// 
    /// DEMONSTRATES:
    /// - Implementing IGameModule via BaseGame
    /// - Network command handling
    /// - Configuration with JSON
    /// - Data collection for ML
    /// - Result reporting
    /// </summary>
    public class SimpleCubeGame : BaseGame
    {
        // ============================================
        // GAME METADATA
        // ============================================
        
        public override string GameId => "example_cube_clicker";
        public override string DisplayName => "Cube Clicker (Example)";
        public override string Description => "Click cubes to score points - Example game demonstrating framework API";
        
        // ============================================
        // CONFIGURATION
        // ============================================
        
        [Header("Network Commands")]
        [SerializeField] private NetworkCommand _onSessionStart;
        [SerializeField] private NetworkCommand _onSessionPause;
        [SerializeField] private NetworkCommand _onSessionResume;
        [SerializeField] private NetworkCommand _onConfigUpdate;
        
        [Header("Game Objects")]
        [SerializeField] private GameObject _cubePrefab;
        [SerializeField] private Transform _spawnArea;
        [SerializeField] private float _spawnRadius = 3f;
        
        [Header("Audio (Optional)")]
        [SerializeField] private AudioClip _clickSound;
        [SerializeField] private AudioClip _successSound;
        
        // ============================================
        // STATE
        // ============================================
        
        private GameObject _currentCube;
        private SimpleCubeConfig _config;
        private int _score = 0;
        private int _totalClicks = 0;
        private new float _sessionStartTime;
        private List<float> _reactionTimes = new List<float>();
        private float _lastSpawnTime;
        
        // ============================================
        // UNITY LIFECYCLE
        // ============================================
        
        void Awake()
        {
            // Subscribe to network commands
            _onSessionStart.OnReceived += HandleSessionStart;
            _onSessionPause.OnReceived += HandleSessionPause;
            _onSessionResume.OnReceived += HandleSessionResume;
            _onConfigUpdate.OnReceived += HandleConfigUpdate;
            
            Debug.Log("[SimpleCubeGame] Initialized");
        }

        
        void Update()
        {
            if (CurrentState != GameState.Playing) return;
            
            // Check time limit
            if (_config != null && _config.timeLimit > 0)
            {
                float elapsed = Time.time - _sessionStartTime;
                if (elapsed >= _config.timeLimit)
                {
                    EndGame();
                }
            }
        }
        
        // ============================================
        // COMMAND HANDLERS
        // ============================================
        
        void HandleSessionStart(string payload)
        {
            try
            {
                // Deserialize configuration from JSON
                SimpleCubeConfig config = JsonUtility.FromJson<SimpleCubeConfig>(
                    System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
                
                Debug.Log($"[SimpleCubeGame] Received config: difficulty={config.difficulty}, targetScore={config.targetScore}");
                
                // Initialize and start
                Initialize(config);
                StartGame();
            }
            catch (Exception e)
            {
                Debug.LogError($"[SimpleCubeGame] Failed to start: {e.Message}");
            }
        }
        
        void HandleSessionPause(string payload)
        {
            PauseGame();
        }
        
        void HandleSessionResume(string payload)
        {
            ResumeGame();
        }
        
        void HandleConfigUpdate(string payload)
        {
            try
            {
                SimpleCubeConfig newConfig = JsonUtility.FromJson<SimpleCubeConfig>(
                    System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
                
                UpdateConfig(newConfig);
                
                // Apply changes immediately
                ApplyConfiguration();
            }
            catch (Exception e)
            {
                Debug.LogError($"[SimpleCubeGame] Failed to update config: {e.Message}");
            }
        }
        
        // ============================================
        // GAME LIFECYCLE (Override BaseGame)
        // ============================================
        
        public override void Initialize(GameConfig config)
        {
            base.Initialize(config);
            
            _config = config as SimpleCubeConfig;
            
            // Reset state
            _score = 0;
            _totalClicks = 0;
            _reactionTimes.Clear();
            
            // Apply configuration
            ApplyConfiguration();
            
            Debug.Log($"[SimpleCubeGame] Initialized with difficulty={_config.difficulty}");
        }
        
        public override void StartGame()
        {
            base.StartGame();
            
            _sessionStartTime = Time.time;
            
            // Spawn first cube
            SpawnCube();
            
            Debug.Log("[SimpleCubeGame] Game started!");
        }
        
        public override void PauseGame()
        {
            base.PauseGame();
            
            // Hide cube while paused
            if (_currentCube != null)
            {
                _currentCube.SetActive(false);
            }
        }
        
        public override void ResumeGame()
        {
            base.ResumeGame();
            
            // Show cube again
            if (_currentCube != null)
            {
                _currentCube.SetActive(true);
            }
        }
        
        public override void EndGame()
        {
            base.EndGame();
            
            // Destroy cube
            if (_currentCube != null)
            {
                Destroy(_currentCube);
            }
            
            // Calculate metrics
            float avgReactionTime = _reactionTimes.Count > 0 
                ? CalculateAverage(_reactionTimes) 
                : 0f;
            
            float accuracy = _totalClicks > 0 
                ? (float)_score / _totalClicks 
                : 0f;
            
            float duration = Time.time - _sessionStartTime;
            
            bool completed = _score >= _config.targetScore;
            
            // Report results to framework
            ReportResults(_score, completed, new Dictionary<string, object>
            {
                { "totalClicks", _totalClicks },
                { "avgReactionTime", avgReactionTime },
                { "accuracy", accuracy },
                { "duration", duration },
                { "difficulty", _config.difficulty }
            });
            
            Debug.Log($"[SimpleCubeGame] Game ended! Score: {_score}/{_config.targetScore}, Completed: {completed}");
        }
        
        // ============================================
        // GAME LOGIC
        // ============================================
        
        void SpawnCube()
        {
            if (_currentCube != null)
            {
                Destroy(_currentCube);
            }
            
            // Random position within spawn area
            Vector3 randomPos = _spawnArea.position + UnityEngine.Random.insideUnitSphere * _spawnRadius;
            
            // Spawn cube
            _currentCube = Instantiate(_cubePrefab, randomPos, Quaternion.identity);
            
            // Scale based on difficulty (harder = smaller)
            float scale = Mathf.Lerp(1.0f, 0.3f, (_config.difficulty - 1) / 4f);
            _currentCube.transform.localScale = Vector3.one * scale;
            
            // Add click handler
            var clickable = _currentCube.AddComponent<CubeClickHandler>();
            clickable.OnClick += HandleCubeClick;
            
            _lastSpawnTime = Time.time;
            
            // Collect spawn event
            CollectDataPoint("cube_spawned", new Dictionary<string, object>
            {
                { "position", randomPos },
                { "scale", scale },
                { "difficulty", _config.difficulty }
            });
        }
        
        void HandleCubeClick()
        {
            if (CurrentState != GameState.Playing) return;
            
            _totalClicks++;
            _score++;
            
            // Calculate reaction time
            float reactionTime = Time.time - _lastSpawnTime;
            _reactionTimes.Add(reactionTime);
            
            // Collect data point (automatically batched & uploaded by framework)
            CollectDataPoint("cube_clicked", new Dictionary<string, object>
            {
                { "score", _score },
                { "reactionTime", reactionTime },
                { "totalClicks", _totalClicks },
                { "cubeScale", _currentCube.transform.localScale.x },
                { "cubePosition", _currentCube.transform.position }
            });
            
            // Play sound
            if (_clickSound != null)
            {
                AudioSource.PlayClipAtPoint(_clickSound, _currentCube.transform.position);
            }
            
            Debug.Log($"[SimpleCubeGame] Cube clicked! Score: {_score}, Reaction: {reactionTime:F3}s");
            
            // Check win condition
            if (_score >= _config.targetScore)
            {
                // Play success sound
                if (_successSound != null)
                {
                    AudioSource.PlayClipAtPoint(_successSound, Camera.main.transform.position);
                }
                
                EndGame();
            }
            else
            {
                // Spawn next cube
                SpawnCube();
            }
        }
        
        void ApplyConfiguration()
        {
            if (_config == null) return;
            
            // Apply difficulty settings
            // (In this example, difficulty affects cube size in SpawnCube())
            
            Debug.Log($"[SimpleCubeGame] Applied config: difficulty={_config.difficulty}, target={_config.targetScore}");
        }
        
        // ============================================
        // HELPERS
        // ============================================
        
        float CalculateAverage(List<float> values)
        {
            float sum = 0f;
            foreach (float v in values)
            {
                sum += v;
            }
            return sum / values.Count;
        }
        
        // ============================================
        // DEBUG (Editor only)
        // ============================================
        
#if UNITY_EDITOR
        [ContextMenu("Test Start Game")]
        void TestStartGame()
        {
            var config = new SimpleCubeConfig
            {
                difficulty = 2,
                targetScore = 5,
                timeLimit = 60f,
                cubeSize = 1f
            };
            
            Initialize(config);
            StartGame();
        }
        
        [ContextMenu("Test End Game")]
        void TestEndGame()
        {
            EndGame();
        }
        
        void OnDrawGizmos()
        {
            if (_spawnArea != null)
            {
                // Draw spawn area
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(_spawnArea.position, _spawnRadius);
            }
        }
#endif
    }
    
    // ============================================
    // CONFIGURATION CLASS
    // ============================================
    
    /// <summary>
    /// Configuration for SimpleCubeGame
    /// MUST be marked with [Serializable] for JSON serialization
    /// </summary>
    [Serializable]
    public class SimpleCubeConfig : GameConfig
    {
        public int targetScore = 10;      // Score to win
        public new float timeLimit = 0f;      // Time limit (0 = unlimited)
        public float cubeSize = 1.0f;     // Cube size multiplier
        
        // Constructor
        public SimpleCubeConfig()
        {
            gameId = "example_cube_clicker";
            difficulty = 1;
        }
    }
    
    // ============================================
    // CLICK HANDLER COMPONENT
    // ============================================
    
    /// <summary>
    /// Simple click detection for VR controllers
    /// Attach this to clickable objects
    /// </summary>
    public class CubeClickHandler : MonoBehaviour
    {
        public event Action OnClick;
        
        // Called by VR controller raycast or collision
        void OnMouseDown()
        {
            OnClick?.Invoke();
        }
        
        // For VR controller trigger
        void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Controller"))
            {
                OnClick?.Invoke();
            }
        }
    }
}