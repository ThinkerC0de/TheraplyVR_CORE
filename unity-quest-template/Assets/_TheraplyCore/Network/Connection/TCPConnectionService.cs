using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using UnityEngine;

namespace TheraplyCore.Network.Connection
{
    /// <summary>
    /// TCP-based connection service for reliable control messages
    /// Uses JSON for message serialization
    /// 
    /// FEATURES:
    /// - Persistent TCP connection (NoDelay enabled)
    /// - Async send/receive
    /// - Automatic message length prefix (4 bytes)
    /// - Command dispatch to NetworkCommand ScriptableObjects
    /// - Thread-safe queue for Unity main thread
    /// </summary>
    public class TCPConnectionService : MonoBehaviour
    {
        // ============================================
        // CONFIGURATION
        // ============================================
        
        [Header("Connection Settings")]
        [Tooltip("TCP port for control messages")]
        [SerializeField] private int _controlPort = 8080;
        
        [Tooltip("Connection timeout (seconds)")]
        [SerializeField] private float _connectionTimeout = 10f;
        
        [Tooltip("Send buffer size (bytes)")]
        [SerializeField] private int _sendBufferSize = 8192;
        
        [Tooltip("Receive buffer size (bytes)")]
        [SerializeField] private int _receiveBufferSize = 8192;
        
        [Header("Debug")]
        [SerializeField] private bool _logConnections = true;
        [SerializeField] private bool _logMessages = false;
        
        // ============================================
        // STATE
        // ============================================
        
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isConnected = false;
        
        // Thread-safe queue for main thread execution
        private ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        
        // Statistics
        private int _messagesSent = 0;
        private int _messagesReceived = 0;
        private long _bytesSent = 0;
        private long _bytesReceived = 0;
        
        // ============================================
        // PROPERTIES
        // ============================================
        
        public bool IsConnected => _isConnected && _tcpClient?.Connected == true;
        public int MessagesSent => _messagesSent;
        public int MessagesReceived => _messagesReceived;
        public long BytesSent => _bytesSent;
        public long BytesReceived => _bytesReceived;
        
        // ============================================
        // EVENTS
        // ============================================
        
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<NetworkMessage> OnMessageReceived;
        public event Action<Exception> OnError;
        
        // ============================================
        // UNITY LIFECYCLE
        // ============================================
        
        private void Update()
        {
            // Process queued actions on main thread
            while (_mainThreadQueue.TryDequeue(out Action action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[TCPConnection] Error executing main thread action: {e.Message}");
                }
            }
        }
        
        private void OnDestroy()
        {
            Disconnect();
        }
        
        private void OnApplicationPause(bool pause)
        {
            if (pause)
            {
                // Keep connection alive but pause sending
            }
            else
            {
                // Resume - reconnect if needed
                if (!IsConnected)
                {
                    Debug.Log("[TCPConnection] Reconnecting after resume...");
                }
            }
        }
        
        // ============================================
        // PUBLIC API
        // ============================================
        
        /// <summary>
        /// Connect to therapist controller
        /// </summary>
        public async Task<bool> ConnectAsync(string ip, int port = -1)
        {
            if (IsConnected)
            {
                Debug.LogWarning("[TCPConnection] Already connected");
                return true;
            }
            
            if (port == -1) port = _controlPort;
            
            try
            {
                if (_logConnections)
                {
                    Debug.Log($"[TCPConnection] Connecting to {ip}:{port}...");
                }
                
                // Create TCP client
                _tcpClient = new TcpClient
                {
                    SendBufferSize = _sendBufferSize,
                    ReceiveBufferSize = _receiveBufferSize,
                    NoDelay = true // Disable Nagle's algorithm for low latency
                };
                
                // Connect with timeout
                _cancellationTokenSource = new CancellationTokenSource();
                var connectTask = _tcpClient.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(_connectionTimeout));
                
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    throw new TimeoutException($"Connection timeout after {_connectionTimeout}s");
                }
                
                // Get stream
                _stream = _tcpClient.GetStream();
                _isConnected = true;
                
                if (_logConnections)
                {
                    Debug.Log($"[TCPConnection] Connected to {ip}:{port}");
                }
                
                // Start receive loop
                _ = ReceiveLoopAsync(_cancellationTokenSource.Token);
                
                // Fire event on main thread
                EnqueueMainThreadAction(() => OnConnected?.Invoke());
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[TCPConnection] Connection failed: {e.Message}");
                
                Disconnect();
                
                EnqueueMainThreadAction(() => OnError?.Invoke(e));
                
                return false;
            }
        }
        
        /// <summary>
        /// Disconnect from controller
        /// </summary>
        public void Disconnect()
        {
            if (!_isConnected) return;
            
            _isConnected = false;
            
            // Cancel async operations
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            
            // Close stream and client
            try
            {
                _stream?.Close();
                _tcpClient?.Close();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TCPConnection] Error during disconnect: {e.Message}");
            }
            
            _stream = null;
            _tcpClient = null;
            
            if (_logConnections)
            {
                Debug.Log("[TCPConnection] Disconnected");
            }
            
            EnqueueMainThreadAction(() => OnDisconnected?.Invoke());
        }
        
        /// <summary>
        /// Send a network message
        /// </summary>
        public async Task<bool> SendMessageAsync(NetworkMessage message)
        {
            if (!IsConnected)
            {
                Debug.LogWarning("[TCPConnection] Cannot send - not connected");
                return false;
            }
            
            try
            {
                // Serialize with JSON
                string jsonString = JsonUtility.ToJson(message);
                byte[] messageData = System.Text.Encoding.UTF8.GetBytes(jsonString);
                
                // Prepend length (4 bytes, big-endian)
                byte[] lengthPrefix = BitConverter.GetBytes(messageData.Length);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(lengthPrefix);
                }
                
                // Send length + message
                await _stream.WriteAsync(lengthPrefix, 0, 4);
                await _stream.WriteAsync(messageData, 0, messageData.Length);
                await _stream.FlushAsync();
                
                // Update stats
                _messagesSent++;
                _bytesSent += messageData.Length + 4;
                
                if (_logMessages)
                {
                    Debug.Log($"[TCPConnection] Sent: {message.commandId} ({messageData.Length} bytes)");
                }
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[TCPConnection] Send error: {e.Message}");
                
                // Connection probably lost
                Disconnect();
                
                EnqueueMainThreadAction(() => OnError?.Invoke(e));
                
                return false;
            }
        }
        
        /// <summary>
        /// Send a command with payload
        /// </summary>
        public async Task<bool> SendCommandAsync(string commandId, object payload = null)
        {
            var message = new NetworkMessage
            {
                messageId = Guid.NewGuid().ToString(),
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                commandId = commandId,
                payload = payload != null ? System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload)) : null
            };
            
            return await SendMessageAsync(message);
        }
        
        // ============================================
        // RECEIVE LOOP
        // ============================================
        
        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            byte[] lengthBuffer = new byte[4];
            
            try
            {
                while (!cancellationToken.IsCancellationRequested && IsConnected)
                {
                    // Read length prefix (4 bytes)
                    int bytesRead = await ReadExactAsync(_stream, lengthBuffer, 0, 4, cancellationToken);
                    
                    if (bytesRead != 4)
                    {
                        Debug.LogWarning("[TCPConnection] Incomplete length prefix - connection closed");
                        break;
                    }
                    
                    // Parse length (big-endian)
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(lengthBuffer);
                    }
                    int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                    
                    // Validate length
                    if (messageLength <= 0 || messageLength > 10 * 1024 * 1024) // Max 10MB
                    {
                        Debug.LogError($"[TCPConnection] Invalid message length: {messageLength}");
                        break;
                    }
                    
                    // Read message body
                    byte[] messageBuffer = new byte[messageLength];
                    bytesRead = await ReadExactAsync(_stream, messageBuffer, 0, messageLength, cancellationToken);
                    
                    if (bytesRead != messageLength)
                    {
                        Debug.LogWarning("[TCPConnection] Incomplete message - connection closed");
                        break;
                    }
                    
                    // Deserialize from JSON
                    string jsonString = System.Text.Encoding.UTF8.GetString(messageBuffer);
                    NetworkMessage message = JsonUtility.FromJson<NetworkMessage>(jsonString);
                    
                    // Update stats
                    _messagesReceived++;
                    _bytesReceived += messageLength + 4;
                    
                    if (_logMessages)
                    {
                        Debug.Log($"[TCPConnection] Received: {message.commandId} ({messageLength} bytes)");
                    }
                    
                    // Dispatch on main thread
                    EnqueueMainThreadAction(() =>
                    {
                        try
                        {
                            OnMessageReceived?.Invoke(message);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[TCPConnection] Error processing message: {e.Message}");
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception e)
            {
                Debug.LogError($"[TCPConnection] Receive error: {e.Message}");
                
                EnqueueMainThreadAction(() => OnError?.Invoke(e));
            }
            finally
            {
                Disconnect();
            }
        }
        
        /// <summary>
        /// Read exact number of bytes (helper)
        /// </summary>
        private async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int totalRead = 0;
            
            while (totalRead < count)
            {
                int bytesRead = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, cancellationToken);
                
                if (bytesRead == 0)
                {
                    // Connection closed
                    return totalRead;
                }
                
                totalRead += bytesRead;
            }
            
            return totalRead;
        }
        
        // ============================================
        // HELPERS
        // ============================================
        
        private void EnqueueMainThreadAction(Action action)
        {
            _mainThreadQueue.Enqueue(action);
        }
        
        /// <summary>
        /// Get connection statistics
        /// </summary>
        public ConnectionStats GetStats()
        {
            return new ConnectionStats
            {
                isConnected = IsConnected,
                messagesSent = _messagesSent,
                messagesReceived = _messagesReceived,
                bytesSent = _bytesSent,
                bytesReceived = _bytesReceived
            };
        }
        
        /// <summary>
        /// Reset statistics
        /// </summary>
        public void ResetStats()
        {
            _messagesSent = 0;
            _messagesReceived = 0;
            _bytesSent = 0;
            _bytesReceived = 0;
        }
    }
    
    // ============================================
    // DATA STRUCTURES
    // ============================================
    
    /// <summary>
    /// Network message structure (JSON serialized)
    /// </summary>
    [Serializable]
    public struct NetworkMessage
    {
        public string messageId;      // Unique message ID
        public long timestamp;        // Unix timestamp
        public string commandId;      // Command identifier (e.g., "SESSION_START")
        public byte[] payload;        // Nested JSON data as bytes
    }
    
    /// <summary>
    /// Connection statistics
    /// </summary>
    public struct ConnectionStats
    {
        public bool isConnected;
        public int messagesSent;
        public int messagesReceived;
        public long bytesSent;
        public long bytesReceived;
    }
}