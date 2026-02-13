using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using UnityEngine;

namespace TheraplyCore.Network.Connection
{
    /// <summary>
    /// TCP Server for Quest - accepts connections from Flutter controllers
    /// Uses JSON for message serialization
    /// 
    /// ARCHITECTURE:
    /// - Quest runs TCP server on port 8080
    /// - Flutter controller connects as client
    /// - Persistent connection with NoDelay enabled
    /// </summary>
    public class TCPServerService : MonoBehaviour
    {
        // ============================================
        // CONFIGURATION
        // ============================================
        
        [Header("Server Settings")]
        [Tooltip("TCP port for control messages")]
        [SerializeField] private int _serverPort = 8080;
        
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
        
        private TcpListener _tcpListener;
        private TcpClient _connectedClient;
        private NetworkStream _stream;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isRunning = false;
        private bool _hasClient = false;
        
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
        
        public bool IsRunning => _isRunning;
        public bool HasClient => _hasClient && _connectedClient?.Connected == true;
        public int MessagesSent => _messagesSent;
        public int MessagesReceived => _messagesReceived;
        public long BytesSent => _bytesSent;
        public long BytesReceived => _bytesReceived;
        
        // ============================================
        // EVENTS
        // ============================================
        
        public event Action OnServerStarted;
        public event Action OnServerStopped;
        public event Action<string> OnClientConnected;  // IP address
        public event Action OnClientDisconnected;
        public event Action<NetworkMessage> OnMessageReceived;
        public event Action<Exception> OnError;
        
        // ============================================
        // UNITY LIFECYCLE
        // ============================================
        
        private void Start()
        {
            // Auto-start server on Quest and Editor (for testing)
            StartServer();
        }
        
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
                    Debug.LogError($"[TCPServer] Error executing main thread action: {e.Message}");
                }
            }
        }
        
        private void OnDestroy()
        {
            StopServer();
        }
        
        private void OnApplicationPause(bool pause)
        {
            if (pause)
            {
                // Keep server running but connection might drop
            }
            else
            {
                // Resume - restart server if needed
                if (!_isRunning)
                {
                    Debug.Log("[TCPServer] Restarting server after resume...");
                    StartServer();
                }
            }
        }
        
        // ============================================
        // PUBLIC API
        // ============================================
        
        /// <summary>
        /// Start TCP server and accept connections
        /// </summary>
        public void StartServer()
        {
            if (_isRunning)
            {
                Debug.LogWarning("[TCPServer] Server already running");
                return;
            }
            
            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, _serverPort);
                _tcpListener.Start();
                _isRunning = true;
                
                _cancellationTokenSource = new CancellationTokenSource();
                
                if (_logConnections)
                {
                    Debug.Log($"[TCPServer] Server started on port {_serverPort}");
                }
                
                // Start accept loop
                _ = AcceptClientsAsync(_cancellationTokenSource.Token);
                
                EnqueueMainThreadAction(() => OnServerStarted?.Invoke());
            }
            catch (Exception e)
            {
                Debug.LogError($"[TCPServer] Failed to start server: {e.Message}");
                EnqueueMainThreadAction(() => OnError?.Invoke(e));
            }
        }
        
        /// <summary>
        /// Stop TCP server
        /// </summary>
        public void StopServer()
        {
            if (!_isRunning) return;
            
            _isRunning = false;
            
            // Cancel async operations
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            
            // Disconnect client
            DisconnectClient();
            
            // Stop listener
            try
            {
                _tcpListener?.Stop();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TCPServer] Error stopping listener: {e.Message}");
            }
            
            _tcpListener = null;
            
            if (_logConnections)
            {
                Debug.Log("[TCPServer] Server stopped");
            }
            
            EnqueueMainThreadAction(() => OnServerStopped?.Invoke());
        }
        
        /// <summary>
        /// Disconnect current client
        /// </summary>
        public void DisconnectClient()
        {
            if (!_hasClient) return;
            
            _hasClient = false;
            
            try
            {
                _stream?.Close();
                _connectedClient?.Close();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TCPServer] Error disconnecting client: {e.Message}");
            }
            
            _stream = null;
            _connectedClient = null;
            
            if (_logConnections)
            {
                Debug.Log("[TCPServer] Client disconnected");
            }
            
            EnqueueMainThreadAction(() => OnClientDisconnected?.Invoke());
        }
        
        /// <summary>
        /// Send a network message to connected client
        /// </summary>
        public async Task<bool> SendMessageAsync(NetworkMessage message)
        {
            if (!HasClient)
            {
                Debug.LogWarning("[TCPServer] Cannot send - no client connected");
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
                    Debug.Log($"[TCPServer] Sent: {message.commandId} ({messageData.Length} bytes)");
                }
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[TCPServer] Send error: {e.Message}");
                
                // Connection probably lost
                DisconnectClient();
                
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
        // ACCEPT CLIENTS LOOP
        // ============================================
        
        private async Task AcceptClientsAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _isRunning)
                {
                    // Wait for client connection
                    if (_logConnections)
                    {
                        Debug.Log("[TCPServer] Waiting for client connection...");
                    }
                    
                    TcpClient client = await _tcpListener.AcceptTcpClientAsync();
                    
                    // Disconnect existing client if any
                    if (_hasClient)
                    {
                        Debug.LogWarning("[TCPServer] New client connecting - disconnecting old client");
                        DisconnectClient();
                    }
                    
                    // Configure new client
                    client.SendBufferSize = _sendBufferSize;
                    client.ReceiveBufferSize = _receiveBufferSize;
                    client.NoDelay = true; // Disable Nagle's algorithm for low latency
                    
                    _connectedClient = client;
                    _stream = client.GetStream();
                    _hasClient = true;
                    
                    string clientIP = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                    
                    if (_logConnections)
                    {
                        Debug.Log($"[TCPServer] Client connected: {clientIP}");
                    }
                    
                    EnqueueMainThreadAction(() => OnClientConnected?.Invoke(clientIP));
                    
                    // Start receive loop for this client
                    _ = ReceiveLoopAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception e)
            {
                Debug.LogError($"[TCPServer] Accept error: {e.Message}");
                EnqueueMainThreadAction(() => OnError?.Invoke(e));
            }
        }
        
        // ============================================
        // RECEIVE LOOP
        // ============================================
        
        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            byte[] lengthBuffer = new byte[4];
            
            try
            {
                while (!cancellationToken.IsCancellationRequested && HasClient)
                {
                    // Read length prefix (4 bytes)
                    int bytesRead = await ReadExactAsync(_stream, lengthBuffer, 0, 4, cancellationToken);
                    
                    if (bytesRead != 4)
                    {
                        Debug.LogWarning("[TCPServer] Incomplete length prefix - client disconnected");
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
                        Debug.LogError($"[TCPServer] Invalid message length: {messageLength}");
                        break;
                    }
                    
                    // Read message body
                    byte[] messageBuffer = new byte[messageLength];
                    bytesRead = await ReadExactAsync(_stream, messageBuffer, 0, messageLength, cancellationToken);
                    
                    if (bytesRead != messageLength)
                    {
                        Debug.LogWarning("[TCPServer] Incomplete message - client disconnected");
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
                        Debug.Log($"[TCPServer] Received: {message.commandId} ({messageLength} bytes)");
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
                            Debug.LogError($"[TCPServer] Error processing message: {e.Message}");
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
                Debug.LogError($"[TCPServer] Receive error: {e.Message}");
                EnqueueMainThreadAction(() => OnError?.Invoke(e));
            }
            finally
            {
                DisconnectClient();
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
        /// Get server statistics
        /// </summary>
        public ServerStats GetStats()
        {
            return new ServerStats
            {
                isRunning = IsRunning,
                hasClient = HasClient,
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
    /// Server statistics
    /// </summary>
    public struct ServerStats
    {
        public bool isRunning;
        public bool hasClient;
        public int messagesSent;
        public int messagesReceived;
        public long bytesSent;
        public long bytesReceived;
    }
}
