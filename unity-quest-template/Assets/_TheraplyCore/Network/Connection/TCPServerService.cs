using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.Serialization;

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
        
        [Header("Dependencies")]
        [Tooltip("UDP Discovery Service to pause/resume broadcast on connect/disconnect")]
        [SerializeField] private TheraplyCore.Network.Discovery.UDPDiscoveryService _discoveryService;
        
        [Tooltip("media stream Service to auto-start/stop streaming on connect/disconnect")]
        [FormerlySerializedAs("_videoStreamService")]
        [SerializeField] private TheraplyCore.Streaming.MediaStreamService _mediaStreamService;
        
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
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        
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
            StartCoroutine(DelayedStartServer());
        }
        
        private System.Collections.IEnumerator DelayedStartServer()
        {
            yield return new WaitForSeconds(1f);
            if (_discoveryService == null)
                _discoveryService = GetComponent<TheraplyCore.Network.Discovery.UDPDiscoveryService>();
            if (_mediaStreamService == null)
                _mediaStreamService = FindFirstObjectByType<TheraplyCore.Streaming.MediaStreamService>();
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
            // Server keeps running during pause/resume
            // No action needed - server and connections are persistent
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
            
            // Stop media streaming when client disconnects
            if (_mediaStreamService != null && _mediaStreamService.IsStreaming)
            {
                _mediaStreamService.StopStreaming();
                Debug.Log("[TCPServer] Stopped media stream (client disconnected)");
            }
            
            // Resume UDP broadcast when client disconnects (allow reconnection)
            if (_discoveryService != null)
            {
                _discoveryService.ResumeBroadcast();
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
                await _sendLock.WaitAsync();
                if (!HasClient || _stream == null)
                {
                    Debug.LogWarning("[TCPServer] Cannot send - client disconnected before write");
                    return false;
                }

                // JsonUtility does not serialize byte[] - use wrapper with Base64 payload
                string jsonString = SerializeMessageForWire(message);
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
            finally
            {
                if (_sendLock.CurrentCount == 0)
                {
                    _sendLock.Release();
                }
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
        
        /// <summary>
        /// Serialize NetworkMessage to JSON with payload as Base64 (JsonUtility does not serialize byte[]).
        /// </summary>
        private static string SerializeMessageForWire(NetworkMessage message)
        {
            var wrapper = new NetworkMessageJson
            {
                messageId = message.messageId,
                timestamp = message.timestamp,
                commandId = message.commandId,
                payload = message.payload != null ? Convert.ToBase64String(message.payload) : null
            };
            return JsonUtility.ToJson(wrapper);
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
                    
                    // Check if still running before accepting (prevents disposed exception)
                    if (!_isRunning)
                    {
                        break;
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
                    
                    // Pause UDP broadcast when client connects (save bandwidth)
                    if (_discoveryService != null)
                    {
                        _discoveryService.PauseBroadcast();
                    }
                    
                    EnqueueMainThreadAction(() =>
                    {
                        OnClientConnected?.Invoke(clientIP);
                        
                        // WebRTC streaming + offer sent by WebRTCServerSignaling on OnClientConnected
                    });
                    
                    // Start receive loop for this client
                    _ = ReceiveLoopAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation during shutdown
            }
            catch (ObjectDisposedException)
            {
                // Listener was closed during shutdown - this is expected
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.Interrupted)
            {
                // Accept was interrupted during shutdown - this is expected
            }
            catch (Exception e)
            {
                // Only log unexpected errors
                if (_isRunning)
                {
                    Debug.LogError($"[TCPServer] Accept error: {e.Message}");
                    EnqueueMainThreadAction(() => OnError?.Invoke(e));
                }
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
                        if (bytesRead == 0)
                            Debug.Log("[TCPServer] Client closed connection");
                        else
                            Debug.LogWarning($"[TCPServer] Incomplete length prefix (got {bytesRead} bytes) - client disconnected");
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
                    NetworkMessage message = NetworkMessageWireAdapter.DeserializeMessageFromWire(jsonString);
                    
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
    
    /// <summary>
    /// JSON-serializable message with payload as Base64 (Unity JsonUtility does not serialize byte[]).
    /// </summary>
    [Serializable]
    public class NetworkMessageJson
    {
        public string messageId;
        public long timestamp;
        public string commandId;
        public string payload; // Base64-encoded when sent from server
    }

    internal static class NetworkMessageWireAdapter
    {
        public static NetworkMessage DeserializeMessageFromWire(string jsonString)
        {
            var wrapper = JsonUtility.FromJson<NetworkMessageJson>(jsonString);
            return new NetworkMessage
            {
                messageId = wrapper.messageId,
                timestamp = wrapper.timestamp,
                commandId = wrapper.commandId,
                payload = string.IsNullOrEmpty(wrapper.payload) ? null : Convert.FromBase64String(wrapper.payload)
            };
        }
    }
}
