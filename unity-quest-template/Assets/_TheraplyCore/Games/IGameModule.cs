using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheraplyCore.Games
{
    /// <summary>
    /// Core interface that all game modules must implement.
    /// Defines the contract between the framework and individual games.
    /// </summary>
    public interface IGameModule
    {
        // ============================================
        // METADATA
        // ============================================
        
        /// <summary>
        /// Unique identifier for this game (e.g., "piniata", "puzzle_3x3")
        /// Used for routing messages and Firebase storage
        /// </summary>
        string GameId { get; }
        
        /// <summary>
        /// Display name shown in controller app (e.g., "Piñata Game")
        /// </summary>
        string DisplayName { get; }
        
        /// <summary>
        /// Game icon for UI (optional, can be null)
        /// </summary>
        Sprite Icon { get; }
        
        /// <summary>
        /// Short description of the game
        /// </summary>
        string Description { get; }
        
        // ============================================
        // LIFECYCLE METHODS
        // ============================================
        
        /// <summary>
        /// Called once when game is loaded/initialized with configuration
        /// Parse and apply game-specific settings here
        /// </summary>
        /// <param name="config">Game configuration (MessagePack deserialized)</param>
        void Initialize(GameConfig config);
        
        /// <summary>
        /// Start the game session
        /// Called after Initialize and when therapist presses "Start"
        /// </summary>
        void StartGame();
        
        /// <summary>
        /// Pause the game (e.g., therapist pauses or HMD removed)
        /// Save current state, freeze gameplay
        /// </summary>
        void PauseGame();
        
        /// <summary>
        /// Resume from pause
        /// Restore state, continue gameplay
        /// </summary>
        void ResumeGame();
        
        /// <summary>
        /// Restart the game from beginning with same configuration
        /// </summary>
        void RestartGame();
        
        /// <summary>
        /// End the game session and clean up
        /// Report final results via OnGameComplete event
        /// </summary>
        void EndGame();
        
        // ============================================
        // CONFIGURATION
        // ============================================
        
        /// <summary>
        /// Update game configuration during gameplay
        /// Called when therapist changes settings mid-session
        /// </summary>
        /// <param name="config">New configuration</param>
        void UpdateConfig(GameConfig config);
        
        /// <summary>
        /// Get current game configuration
        /// </summary>
        GameConfig GetCurrentConfig();
        
        // ============================================
        // DATA COLLECTION (ML)
        // ============================================
        
        /// <summary>
        /// Fired whenever a data point is collected
        /// Framework automatically batches and uploads to Firebase
        /// 
        /// Example: Player hits target, reaction time recorded
        /// </summary>
        event Action<GameDataPoint> OnDataPoint;
        
        /// <summary>
        /// Fired when game completes (success or failure)
        /// Contains aggregated metrics and final score
        /// </summary>
        event Action<GameResult> OnGameComplete;
        
        // ============================================
        // STATE
        // ============================================
        
        /// <summary>
        /// Current game state
        /// </summary>
        GameState CurrentState { get; }
    }
    
    // ============================================
    // SUPPORTING CLASSES
    // ============================================
    
    /// <summary>
    /// Base class for game-specific configuration
    /// Inherit and add your own fields, mark with [MessagePackObject]
    /// </summary>
    [Serializable]
    public abstract class GameConfig
    {
        public string gameId;
        public int difficulty = 1; // 1-5 scale
        public float timeLimit = 0f; // 0 = unlimited
    }
    
    /// <summary>
    /// Single data point collected during gameplay
    /// Used for ML training and analysis
    /// </summary>
    [Serializable]
    public class GameDataPoint
    {
        public DateTime timestamp;
        public string dataType; // "hit", "miss", "completion", etc.
        public Dictionary<string, object> payload; // Flexible game-specific data
    }
    
    /// <summary>
    /// Final game results with aggregated metrics
    /// </summary>
    [Serializable]
    public class GameResult
    {
        public string gameId;
        public int score;
        public bool completed;
        public float duration; // seconds
        public Dictionary<string, object> metrics; // Aggregated stats
    }
    
    /// <summary>
    /// Current state of the game
    /// </summary>
    public enum GameState
    {
        NotInitialized,
        Initialized,
        Playing,
        Paused,
        Completed,
        Failed
    }
}
