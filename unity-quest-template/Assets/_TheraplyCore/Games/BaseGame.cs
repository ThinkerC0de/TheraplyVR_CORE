using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;

namespace TheraplyCore.Games
{
    /// <summary>
    /// Base helper class for implementing IGameModule
    /// Provides common functionality and reduces boilerplate
    /// 
    /// USAGE:
    /// public class MyGame : BaseGame {
    ///     public override string GameId => "my_game";
    ///     public override string DisplayName => "My Game";
    ///     
    ///     public override void StartGame() {
    ///         // Your logic here
    ///         CollectDataPoint("game_started", new { difficulty = 3 });
    ///     }
    /// }
    /// </summary>
    public abstract class BaseGame : MonoBehaviour, IGameModule
    {
        // ============================================
        // ABSTRACT PROPERTIES (must override)
        // ============================================
        
        public abstract string GameId { get; }
        public abstract string DisplayName { get; }
        
        // ============================================
        // VIRTUAL PROPERTIES (can override)
        // ============================================
        
        public virtual Sprite Icon => null;
        public virtual string Description => "";
        
        // ============================================
        // STATE
        // ============================================
        
        protected GameState _currentState = GameState.NotInitialized;
        public GameState CurrentState => _currentState;
        
        protected GameConfig _currentConfig;
        protected DateTime _sessionStartTime;
        protected List<GameDataPoint> _dataBuffer = new List<GameDataPoint>();
        
        // ============================================
        // EVENTS
        // ============================================
        
        public event Action<GameDataPoint> OnDataPoint;
        public event Action<GameResult> OnGameComplete;
        
        // ============================================
        // INJECTED SERVICES (VContainer)
        // ============================================
        
        // These are injected by VContainer at runtime
        // Games can access them directly
        protected ISessionService SessionService { get; private set; }
        protected IConnectionService ConnectionService { get; private set; }
        
        [Inject]
        public void Construct(
            ISessionService sessionService,
            IConnectionService connectionService)
        {
            SessionService = sessionService;
            ConnectionService = connectionService;
        }
        
        // ============================================
        // LIFECYCLE (virtual - can override)
        // ============================================
        
        public virtual void Initialize(GameConfig config)
        {
            _currentConfig = config;
            _currentState = GameState.Initialized;
            
            Debug.Log($"[{GameId}] Initialized with config: {JsonUtility.ToJson(config)}");
        }
        
        public virtual void StartGame()
        {
            if (_currentState != GameState.Initialized && _currentState != GameState.Paused)
            {
                Debug.LogWarning($"[{GameId}] Cannot start game from state: {_currentState}");
                return;
            }
            
            _currentState = GameState.Playing;
            _sessionStartTime = DateTime.UtcNow;
            
            Debug.Log($"[{GameId}] Game started");
            
            CollectDataPoint("game_started", new Dictionary<string, object>
            {
                { "config", JsonUtility.ToJson(_currentConfig) }
            });
        }
        
        public virtual void PauseGame()
        {
            if (_currentState != GameState.Playing)
            {
                Debug.LogWarning($"[{GameId}] Cannot pause game from state: {_currentState}");
                return;
            }
            
            _currentState = GameState.Paused;
            
            Debug.Log($"[{GameId}] Game paused");
            
            CollectDataPoint("game_paused", new Dictionary<string, object>
            {
                { "duration", (DateTime.UtcNow - _sessionStartTime).TotalSeconds }
            });
        }
        
        public virtual void ResumeGame()
        {
            if (_currentState != GameState.Paused)
            {
                Debug.LogWarning($"[{GameId}] Cannot resume game from state: {_currentState}");
                return;
            }
            
            _currentState = GameState.Playing;
            
            Debug.Log($"[{GameId}] Game resumed");
            
            CollectDataPoint("game_resumed", null);
        }
        
        public virtual void RestartGame()
        {
            Debug.Log($"[{GameId}] Game restarting");
            
            // Clean up current session
            _dataBuffer.Clear();
            
            // Re-initialize with same config
            Initialize(_currentConfig);
            StartGame();
        }
        
        public virtual void EndGame()
        {
            if (_currentState != GameState.Playing && _currentState != GameState.Paused)
            {
                Debug.LogWarning($"[{GameId}] Cannot end game from state: {_currentState}");
                return;
            }
            
            float duration = (float)(DateTime.UtcNow - _sessionStartTime).TotalSeconds;
            
            Debug.Log($"[{GameId}] Game ended after {duration}s");
            
            // Flush remaining data points
            FlushDataBuffer();
            
            // Subclasses should call this to report results
            _currentState = GameState.Completed;
        }
        
        // ============================================
        // CONFIGURATION
        // ============================================
        
        public virtual void UpdateConfig(GameConfig config)
        {
            _currentConfig = config;
            
            Debug.Log($"[{GameId}] Config updated: {JsonUtility.ToJson(config)}");
            
            CollectDataPoint("config_updated", new Dictionary<string, object>
            {
                { "newConfig", JsonUtility.ToJson(config) }
            });
        }
        
        public virtual GameConfig GetCurrentConfig()
        {
            return _currentConfig;
        }
        
        // ============================================
        // DATA COLLECTION HELPERS
        // ============================================
        
        /// <summary>
        /// Collect a data point for ML analysis
        /// Automatically batched and uploaded to Firebase
        /// 
        /// USAGE:
        /// CollectDataPoint("hit_target", new Dictionary<string, object> {
        ///     { "reactionTime", 0.523f },
        ///     { "accuracy", 0.92f },
        ///     { "targetId", 5 }
        /// });
        /// </summary>
        protected void CollectDataPoint(string dataType, Dictionary<string, object> payload)
        {
            var dataPoint = new GameDataPoint
            {
                timestamp = DateTime.UtcNow,
                dataType = dataType,
                payload = payload ?? new Dictionary<string, object>()
            };
            
            // Add to buffer
            _dataBuffer.Add(dataPoint);
            
            // Fire event (framework listens and batches uploads)
            OnDataPoint?.Invoke(dataPoint);
            
            // Auto-flush every 10 points
            if (_dataBuffer.Count >= 10)
            {
                FlushDataBuffer();
            }
        }
        
        /// <summary>
        /// Report final game results
        /// Call this from EndGame() override in your game
        /// </summary>
        protected void ReportResults(int score, bool completed, Dictionary<string, object> metrics = null)
        {
            var result = new GameResult
            {
                gameId = GameId,
                score = score,
                completed = completed,
                duration = (float)(DateTime.UtcNow - _sessionStartTime).TotalSeconds,
                metrics = metrics ?? new Dictionary<string, object>()
            };
            
            OnGameComplete?.Invoke(result);
            
            Debug.Log($"[{GameId}] Results reported: Score={score}, Completed={completed}");
        }
        
        /// <summary>
        /// Flush data buffer to Firebase
        /// Called automatically every 10 points or on EndGame()
        /// </summary>
        protected void FlushDataBuffer()
        {
            if (_dataBuffer.Count == 0) return;
            
            // SessionService handles actual Firebase upload
            SessionService?.BatchUploadData(_dataBuffer);
            
            Debug.Log($"[{GameId}] Flushed {_dataBuffer.Count} data points");
            
            _dataBuffer.Clear();
        }
        
        // ============================================
        // UNITY LIFECYCLE
        // ============================================
        
        protected virtual void OnDestroy()
        {
            // Clean up on destroy
            if (_currentState == GameState.Playing)
            {
                FlushDataBuffer();
            }
        }
        
        protected virtual void OnApplicationPause(bool pause)
        {
            if (pause && _currentState == GameState.Playing)
            {
                // Flush data before app pauses
                FlushDataBuffer();
            }
        }
    }
    
    // ============================================
    // SERVICE INTERFACES (defined elsewhere)
    // ============================================
    
    // Placeholder interfaces - actual implementations in separate files
    
    public interface ISessionService
    {
        void BatchUploadData(List<GameDataPoint> dataPoints);
    }
    
    public interface IConnectionService
    {
        void SendMessage(byte[] data);
        bool IsConnected { get; }
    }
}
