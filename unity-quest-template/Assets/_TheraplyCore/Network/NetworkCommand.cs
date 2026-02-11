using System;
using UnityEngine;

namespace TheraplyCore.Network
{
    /// <summary>
    /// ScriptableObject-based network command system
    /// Allows designers to create commands in the Unity Editor
    /// Games subscribe to OnReceived event
    /// 
    /// BENEFITS:
    /// - Designer-friendly (create in Editor, no coding)
    /// - Loose coupling (scenes don't reference each other)
    /// - Visual debugging (see flow in Inspector)
    /// - Scalable (add new commands without code changes)
    /// 
    /// USAGE IN EDITOR:
    /// 1. Right-click in Project → Create → Theraply → Network Command
    /// 2. Name it (e.g., "CMD_StartGame")
    /// 3. Fill in commandId (e.g., "SESSION_START")
    /// 
    /// USAGE IN CODE:
    /// [SerializeField] private NetworkCommand _onStartGame;
    /// 
    /// void Awake() {
    ///     _onStartGame.OnReceived += HandleStartGame;
    /// }
    /// 
    /// void HandleStartGame(string payload) {
    ///     // Deserialize payload, start game
    /// }
    /// </summary>
    [CreateAssetMenu(
        fileName = "CMD_NewCommand",
        menuName = "Theraply/Network Command",
        order = 1)]
    public class NetworkCommand : ScriptableObject
    {
        // ============================================
        // CONFIGURATION (set in Inspector)
        // ============================================
        
        [Header("Command Configuration")]
        [Tooltip("Unique command ID (e.g., 'SESSION_START', 'GAME_CONFIG')")]
        public string commandId;
        
        [Tooltip("Human-readable description of what this command does")]
        [TextArea(3, 10)]
        public string description;
        
        [Header("Debug")]
        [Tooltip("Log when this command is received")]
        public bool logOnReceive = true;
        
        [Tooltip("Show payload content in logs (disable for sensitive data)")]
        public bool logPayload = false;
        
        // ============================================
        // EVENTS
        // ============================================
        
        /// <summary>
        /// Fired when this command is received from network
        /// Payload is the raw message data (usually MessagePack base64)
        /// </summary>
        public event Action<string> OnReceived;
        
        /// <summary>
        /// Fired when this command is sent to network
        /// Useful for debugging/logging
        /// </summary>
        public event Action<string> OnSent;
        
        // ============================================
        // STATISTICS (runtime)
        // ============================================
        
        private int _receiveCount = 0;
        private int _sendCount = 0;
        private DateTime _lastReceived;
        private DateTime _lastSent;
        
        public int ReceiveCount => _receiveCount;
        public int SendCount => _sendCount;
        public DateTime LastReceived => _lastReceived;
        public DateTime LastSent => _lastSent;
        
        // ============================================
        // PUBLIC API
        // ============================================
        
        /// <summary>
        /// Trigger this command with a payload
        /// Called by network layer when message arrives
        /// </summary>
        public void Raise(string payload)
        {
            _receiveCount++;
            _lastReceived = DateTime.UtcNow;
            
            if (logOnReceive)
            {
                string logMessage = $"[NetworkCommand] {name} ({commandId}) received";
                if (logPayload && !string.IsNullOrEmpty(payload))
                {
                    logMessage += $"\nPayload: {payload.Substring(0, Mathf.Min(100, payload.Length))}...";
                }
                Debug.Log(logMessage);
            }
            
            try
            {
                OnReceived?.Invoke(payload);
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkCommand] Error handling {commandId}: {e.Message}\n{e.StackTrace}");
            }
        }
        
        /// <summary>
        /// Send this command with a payload
        /// Called by game logic to send message to controller
        /// </summary>
        public void Send(string payload)
        {
            _sendCount++;
            _lastSent = DateTime.UtcNow;
            
            if (logOnReceive)
            {
                string logMessage = $"[NetworkCommand] {name} ({commandId}) sent";
                if (logPayload && !string.IsNullOrEmpty(payload))
                {
                    logMessage += $"\nPayload: {payload.Substring(0, Mathf.Min(100, payload.Length))}...";
                }
                Debug.Log(logMessage);
            }
            
            try
            {
                OnSent?.Invoke(payload);
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkCommand] Error sending {commandId}: {e.Message}\n{e.StackTrace}");
            }
        }
        
        /// <summary>
        /// Reset statistics (useful for testing)
        /// </summary>
        public void ResetStats()
        {
            _receiveCount = 0;
            _sendCount = 0;
            _lastReceived = default;
            _lastSent = default;
        }
        
        // ============================================
        // VALIDATION
        // ============================================
        
        private void OnValidate()
        {
            // Ensure commandId is set
            if (string.IsNullOrEmpty(commandId))
            {
                Debug.LogWarning($"[NetworkCommand] {name} has empty commandId!");
            }
            
            // Warn if commandId has spaces
            if (commandId.Contains(" "))
            {
                Debug.LogWarning($"[NetworkCommand] {name} commandId contains spaces - consider using underscores");
            }
            
            // Suggest uppercase convention
            if (commandId != commandId.ToUpper())
            {
                // Don't enforce, just suggest in description
            }
        }
        
        // ============================================
        // UNITY LIFECYCLE
        // ============================================
        
        private void OnEnable()
        {
            // Reset stats when ScriptableObject loads
            // Prevents stats from persisting between play sessions
            ResetStats();
        }
        
        // ============================================
        // EDITOR HELPERS
        // ============================================
        
#if UNITY_EDITOR
        [ContextMenu("Test Raise (Empty Payload)")]
        private void TestRaiseEmpty()
        {
            Raise("");
            Debug.Log($"[NetworkCommand] Test raised {commandId} with empty payload");
        }
        
        [ContextMenu("Test Raise (Sample Payload)")]
        private void TestRaiseSample()
        {
            string samplePayload = "{\"test\": true, \"value\": 123}";
            Raise(samplePayload);
            Debug.Log($"[NetworkCommand] Test raised {commandId} with sample payload");
        }
        
        [ContextMenu("Log Statistics")]
        private void LogStatistics()
        {
            Debug.Log($"[NetworkCommand] {name} ({commandId}) Statistics:\n" +
                     $"Received: {_receiveCount} times (last: {_lastReceived})\n" +
                     $"Sent: {_sendCount} times (last: {_lastSent})");
        }
#endif
    }
}
