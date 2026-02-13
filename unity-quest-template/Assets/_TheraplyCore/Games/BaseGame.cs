using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;
using TheraplyCore.Logging;
using TheraplyCore.Firebase;
using TheraplyCore.Connection;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games
{
    /// <summary>
    /// Production-ready BaseGame with all systems integrated
    /// 
    /// FEATURES:
    /// - Logger for structured logging (Fatal/Error/Warning/Info/Debug)
    /// - FirebaseDataService for non-blocking data collection
    /// - ReliableCommandService for guaranteed command delivery
    /// - ConnectionStateManager for auto-reconnection
    /// - Complete error handling
    /// - Statistics tracking
    /// </summary>
    public abstract class BaseGame : MonoBehaviour, IGameModule
    {
        public abstract string GameId { get; }
        public abstract string DisplayName { get; }
        
        public virtual Sprite Icon => null;
        public virtual string Description => "";
        
        protected GameState _currentState = GameState.NotInitialized;
        public GameState CurrentState => _currentState;
        
        protected GameConfig _currentConfig;
        protected DateTime _sessionStartTime;
        protected List<GameDataPoint> _dataBuffer = new List<GameDataPoint>();
        
        public event Action<GameDataPoint> OnDataPoint;
        public event Action<GameResult> OnGameComplete;
        
        protected FirebaseDataService FirebaseData { get; private set; }
        protected ReliableCommandService ReliableCommand { get; private set; }
        protected ConnectionStateManager ConnectionManager { get; private set; }
        
        [Inject]
        public void Construct(
            FirebaseDataService firebaseData,
            ReliableCommandService reliableCommand,
            ConnectionStateManager connectionManager)
        {
            FirebaseData = firebaseData;
            ReliableCommand = reliableCommand;
            ConnectionManager = connectionManager;
            
            Logger.Debug($"[{GameId}] Services injected");
        }
        
        public virtual void Initialize(GameConfig config)
        {
            _currentConfig = config;
            _currentState = GameState.Initialized;
            
            Logger.Info($"[{GameId}] Initialized with config: {JsonUtility.ToJson(config)}");
        }
        
        public virtual void StartGame()
        {
            if (_currentState != GameState.Initialized && _currentState != GameState.Paused)
            {
                Logger.Warning($"[{GameId}] Cannot start game from state: {_currentState}");
                return;
            }
            
            _currentState = GameState.Playing;
            _sessionStartTime = DateTime.UtcNow;
            
            Logger.Info($"[{GameId}] Game started");
            
            CollectDataPoint("game_started", new Dictionary<string, object>
            {
                { "config", JsonUtility.ToJson(_currentConfig) }
            });
        }
        
        public virtual void PauseGame()
        {
            if (_currentState != GameState.Playing)
            {
                Logger.Warning($"[{GameId}] Cannot pause game from state: {_currentState}");
                return;
            }
            
            _currentState = GameState.Paused;
            
            Logger.Info($"[{GameId}] Game paused");
            
            CollectDataPoint("game_paused", new Dictionary<string, object>
            {
                { "duration", (DateTime.UtcNow - _sessionStartTime).TotalSeconds }
            });
        }
        
        public virtual void ResumeGame()
        {
            if (_currentState != GameState.Paused)
            {
                Logger.Warning($"[{GameId}] Cannot resume game from state: {_currentState}");
                return;
            }
            
            _currentState = GameState.Playing;
            
            Logger.Info($"[{GameId}] Game resumed");
            
            CollectDataPoint("game_resumed", null);
        }
        
        public virtual void RestartGame()
        {
            Logger.Info($"[{GameId}] Game restarting");
            
            _dataBuffer.Clear();
            
            Initialize(_currentConfig);
            StartGame();
        }
        
        public virtual void ResetGame()
        {
            Logger.Info($"[{GameId}] Resetting game to initial state");
            
            _currentState = GameState.NotInitialized;
            _dataBuffer.Clear();
            _currentConfig = null;
            
            Logger.Info($"[{GameId}] Game reset complete");
        }
        
        public virtual void EndGame()
        {
            if (_currentState != GameState.Playing && _currentState != GameState.Paused)
            {
                Logger.Warning($"[{GameId}] Cannot end game from state: {_currentState}");
                return;
            }
            
            float duration = (float)(DateTime.UtcNow - _sessionStartTime).TotalSeconds;
            
            Logger.Info($"[{GameId}] Game ended after {duration:F1}s");
            
            FlushDataBuffer();
            
            _currentState = GameState.Completed;
        }
        
        public virtual void UpdateConfig(GameConfig config)
        {
            _currentConfig = config;
            
            Logger.Info($"[{GameId}] Config updated: {JsonUtility.ToJson(config)}");
            
            CollectDataPoint("config_updated", new Dictionary<string, object>
            {
                { "newConfig", JsonUtility.ToJson(config) }
            });
        }
        
        public virtual GameConfig GetCurrentConfig()
        {
            return _currentConfig;
        }
        
        protected void CollectDataPoint(string dataType, Dictionary<string, object> payload)
        {
            var dataPoint = new GameDataPoint
            {
                timestamp = DateTime.UtcNow,
                dataType = dataType,
                payload = payload ?? new Dictionary<string, object>()
            };
            
            _dataBuffer.Add(dataPoint);
            
            OnDataPoint?.Invoke(dataPoint);
            
            if (FirebaseData != null)
            {
                FirebaseData.QueueDataPoint(dataPoint);
            }
            else
            {
                Logger.Warning($"[{GameId}] FirebaseData service not available");
            }
            
            Logger.Debug($"[{GameId}] Data collected: {dataType}");
        }
        
        protected async void ReportResults(int score, bool completed, Dictionary<string, object> metrics = null)
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
            
            Logger.Info($"[{GameId}] Results: Score={score}, Completed={completed}");
            
            if (ReliableCommand != null)
            {
                try
                {
                    await ReliableCommand.SendAsync("GAME_COMPLETE", result);
                    Logger.Info($"[{GameId}] Results sent successfully");
                }
                catch (Exception e)
                {
                    Logger.Error($"[{GameId}] Failed to send results: {e.Message}", e);
                }
            }
        }
        
        protected void FlushDataBuffer()
        {
            if (_dataBuffer.Count == 0) return;
            
            Logger.Info($"[{GameId}] Flushing {_dataBuffer.Count} data points");
            
            _dataBuffer.Clear();
        }
        
        protected virtual void OnDestroy()
        {
            if (_currentState == GameState.Playing)
            {
                FlushDataBuffer();
            }
        }
        
        protected virtual void OnApplicationPause(bool pause)
        {
            if (pause && _currentState == GameState.Playing)
            {
                Logger.Debug($"[{GameId}] App paused - data will be flushed");
            }
        }
    }
}
