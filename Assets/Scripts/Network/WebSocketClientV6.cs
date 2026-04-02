// WebSocketClientV6_refactored.cs
// REFACTORED VERSION - Key improvements:
// 1. Removed dead code (commented audio streaming, unused fields)
// 2. Fixed _lastControlMsgTime to update on all messages
// 3. Added encoding rate limiting to prevent frame buildup
// 4. Consolidated send methods
// 5. Improved error handling
// 6. Centralized constants

using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NativeWebSocket;
using System.IO;
using System.Threading;
using BitMiracle.LibJpeg.Classic;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class WebSocketClientV6 : MonoBehaviour
{
    // ============================================================================
    // CONSTANTS - Centralized configuration
    // ============================================================================

    private static class Constants
    {
        // Network ports
        public const int VR_DISCOVERY_PORT = 8767;
        public const int TEACHER_DISCOVERY_PORT = 8768;

        // Commands
        public const string CMD_START_VIDEO_STREAMING = "START_VIDEO_STREAMING";
        public const string CMD_STOP_VIDEO_STREAMING = "STOP_VIDEO_STREAMING";
        public const string CMD_START_AUDIO_STREAMING = "START_AUDIO_STREAMING";
        public const string CMD_STOP_AUDIO_STREAMING = "STOP_AUDIO_STREAMING";
        public const string CMD_SET_AUDIO_SOURCE_MIC = "SET_AUDIO_SOURCE_MIC";
        public const string CMD_SET_AUDIO_SOURCE_APP = "SET_AUDIO_SOURCE_APP";
        public const string CMD_BACK_TO_MAIN_SCENE = "BACK_TO_MAIN_SCENE";

        // Timeouts and intervals
        public const float CONTROL_IDLE_PAUSE_SECONDS = 5f;
        public const float STREAMING_HEALTH_CHECK_INTERVAL = 2.0f;
        public const int MAX_CONSECUTIVE_FRAME_FAILURES = 10;

        // Audio settings
        public const int AUDIO_BUFFER_SIZE = 4096;
        public const int AUDIO_CHUNK_SIZE = 1024;
        public const int AUDIO_PREFILL_SIZE = 8192;
        public const int AUDIO_SAMPLE_RATE = 44100;
    }

    // ============================================================================
    // SINGLETON
    // ============================================================================

    public static WebSocketClientV6 Instance;
    public CommandDispatcher commandDispatcher;

    // BACKWARD COMPATIBILITY: External code (CommandDispatcher.cs) depends on the typo
    public CommandDispatcher commandDispather
    {
        get => commandDispatcher;
        set => commandDispatcher = value;
    }

    // ============================================================================
    // WEBSOCKET CONNECTIONS
    // ============================================================================

    private WebSocket _controlWebSocket;
    private WebSocket _videoWebSocket;
    private WebSocket _audioWebSocket;

    // ============================================================================
    // CONFIGURATION
    // ============================================================================

    [Header("Debug Settings")]
    [SerializeField] private bool _debugInConsole = true;

    [Header("Video Streaming Settings")]
    [SerializeField] private Camera _streamingCamera;
    [SerializeField] private int _videoWidth = 1280;
    [SerializeField] private int _videoHeight = 720;
    [SerializeField] private int _videoFrameRate = 30;
    [SerializeField] private int _videoQuality = 75;

    [Header("Audio Streaming Settings")]
    [SerializeField] private int _audioSampleRate = 44100;
    [SerializeField] private int _audioBufferSize = 1024;
    [SerializeField] private string _microphoneDeviceName = null;
    public bool streamingFromMicrophone = false;

    [Header("Multi-Teacher Discovery")]
    [SerializeField] private float _discoveryBroadcastInterval = 2.0f;

    [Header("Command Handling")]
    [SerializeField] private List<NetworkCommand> _commandHandlers = new List<NetworkCommand>();

    [Header("Network Adapter")]
    [SerializeField] private NetworkAdapter _networkAdapter;

    // ============================================================================
    // EVENTS
    // ============================================================================

    public UnityEvent OnServerFound;
    public UnityEvent OnServerConnected;
    public UnityEvent OnServerDisconnected;
    public UnityEvent OnTeacherConnected;
    public UnityEvent OnTeacherDisconnected;

    // ============================================================================
    // STATE VARIABLES
    // ============================================================================

    // Connection state
    private bool _isConnected = false;
    private bool _isConnecting = false;
    private string _serverAddress = null;
    private DateTime _startTime = DateTime.UtcNow;

    // VR device identification
    private string _vrAppId;
    private string _deviceName;
    private string _selfIP = "0.0.0.0";
    public string selfIP => _selfIP;
    private bool _runtimeSessionBootstrapObserved = false;
    private ConnectionStatus _connectionStatus = ConnectionStatus.Available;
    private string _connectedTeacherId = null;
    private string _connectedTeacherIP = null;

    // Streaming state
    private bool _isVideoStreamingActive = false;
    private bool _isAudioStreamingActive = false;

    // Video streaming components
    private RenderTexture _renderTexture;
    private Texture2D _videoTexture;
    private int _currentVideoWidth;
    private int _currentVideoHeight;
    private int _adaptiveQuality;
    private float _adaptiveFrameRate;
    private readonly Queue<byte[]> _sendQueue = new Queue<byte[]>();

    // ADDED: Encoding rate limiter to prevent frame buildup
    private volatile bool _isEncoding = false;

    // Audio streaming components
    private AudioSource _microphoneSource;
    private AudioListener _mainAudioListener;
    private List<float> _audioBuffer = new List<float>();
    private bool _isAudioBufferInitialized = false;
    private int _audioUnderrunCount = 0;

    // Discovery sockets
    private UdpClient _discoveryBroadcastClient;
    private UdpClient _discoveryListenClient;
    private Coroutine _discoveryBroadcastCoroutine;
    private Coroutine _discoveryListenCoroutine;

    // Coroutine references
    private Coroutine _audioSendingCoroutine;
    private Coroutine _videoStreamingCoroutine;
    private Coroutine _streamingHealthCheckCoroutine;

    // Health monitoring
    private int _consecutiveFrameFailures = 0;
    private float _lastSuccessfulFrameTime = 0;
    private bool _streamingHealthCheckActive = false;

    // Lifecycle state
    private bool _isDestroying = false;
    private bool _isReinitializing = false;
    private bool _applicationHasFocus = true;
    private bool _vrSystemInterrupted = false;
    private float _lastFocusLostTime = 0;
    private bool _needsStreamingRecovery = false;
    private bool _wasVideoStreamingBeforePause = false;
    private bool _wasAudioStreamingBeforePause = false;

    // Control idle tracking
    private float _lastControlMsgTime = 0f;
    private bool _pausedDueToControlIdle = false;
    private bool _wasVideoStreamingBeforeIdle = false;
    private bool _wasAudioStreamingBeforeIdle = false;

    // Command handling
    private Dictionary<string, NetworkCommand> _commandHandlerMap = new Dictionary<string, NetworkCommand>();
    private Dictionary<string, NetworkCommand> _sceneCommandMap = new Dictionary<string, NetworkCommand>();

    // Frame statistics
    private int _framesSent = 0;

    // ============================================================================
    // ENUMS AND CLASSES
    // ============================================================================

    public enum ConnectionStatus
    {
        Available,
        Connected,
        Busy
    }

    [System.Serializable]
    public class ClientInfo
    {
        public string type = "CLIENT_INFO";
        public string ip;
        public string clientName;
        public string vrAppId;
    }

    [System.Serializable]
    public class VRStatusUpdate
    {
        public string type = "VR_STATUS_UPDATE";
        public string vrAppId;
        public string status;
        public string teacherId;
    }

    [System.Serializable]
    public class ConnectionRequest
    {
        public string teacherId;
        public string teacherIP;
        public int serverPort;
    }

    // ============================================================================
    // LIFECYCLE METHODS
    // ============================================================================

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;

            _vrAppId = $"VR_{SystemInfo.deviceName}_{DateTime.Now.Ticks}";
            _deviceName = SystemInfo.deviceName;
            _selfIP = GetLocalIPAddress();

            LogMessage($"VR App initialized with ID: {_vrAppId}, IP: {_selfIP}");
        }
        else
        {
            Destroy(this);
            return;
        }

        if (_networkAdapter == null)
        {
            _networkAdapter = GetComponent<NetworkAdapter>();
        }

        // Initialize command handlers
        foreach (var handler in _commandHandlers)
        {
            if (handler != null && !string.IsNullOrEmpty(handler.commandId))
            {
                if (!_commandHandlerMap.ContainsKey(handler.commandId))
                {
                    _commandHandlerMap.Add(handler.commandId, handler);
                }
            }
        }
    }

    private void Start()
    {
        _applicationHasFocus = true;
        _vrSystemInterrupted = false;
        _lastSuccessfulFrameTime = Time.time;
        _lastControlMsgTime = Time.time;

        if (_networkAdapter == null)
        {
            _networkAdapter = GetComponent<NetworkAdapter>();
        }

        InitializeCamera();
        StartMultiTeacherDiscovery();
        DiscoverSceneCommands();
        _networkAdapter?.InitializeIfNeeded();

        if (_debugInConsole)
        {
            LogRegisteredCommands();
        }

        LogMessage($"VR Device {_deviceName} ({_vrAppId}) ready for teacher connections");
    }

    private void Update()
    {
        if (_isDestroying) return;

        // Dispatch WebSocket messages
        _controlWebSocket?.DispatchMessageQueue();
        _videoWebSocket?.DispatchMessageQueue();
        _audioWebSocket?.DispatchMessageQueue();

        // Handle pause cleanup
        if (_vrSystemInterrupted && !_applicationHasFocus && _needsStreamingRecovery)
        {
            _needsStreamingRecovery = false;
            HandlePauseCleanupImmediate();
        }

        // Control idle fail-safe
        HandleControlIdleCheck();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnDestroy()
    {
        _isDestroying = true;
        _networkAdapter?.Shutdown();
        Cleanup();
    }

    private async void OnApplicationQuit()
    {
        _isDestroying = true;
        _networkAdapter?.Shutdown();
        Cleanup();
    }

    // ============================================================================
    // APPLICATION LIFECYCLE
    // ============================================================================

    private void OnApplicationPause(bool pauseStatus)
    {
        _networkAdapter?.OnApplicationPauseChanged(pauseStatus);

        if (pauseStatus)
        {
            // CRITICAL: Only set flags during pause to prevent ANR
            _lastFocusLostTime = Time.time;
            _applicationHasFocus = false;
            _vrSystemInterrupted = true;
            _wasVideoStreamingBeforePause = _isVideoStreamingActive;
            _wasAudioStreamingBeforePause = _isAudioStreamingActive;
            _needsStreamingRecovery = true;
        }
        else
        {
            _applicationHasFocus = true;
            _vrSystemInterrupted = false;

            if (!_isDestroying)
            {
                float pauseDuration = Time.time - _lastFocusLostTime;
                LogMessage($"Application resumed after {pauseDuration:F1}s pause");

                if (gameObject.activeInHierarchy)
                {
                    StartCoroutine(DelayedRecoveryAfterResume(pauseDuration));
                }
            }
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        _networkAdapter?.OnApplicationFocusChanged(hasFocus);
        _applicationHasFocus = hasFocus;

        if (!hasFocus)
        {
            _lastFocusLostTime = Time.time;
            _vrSystemInterrupted = true;
        }
        else
        {
            float focusLostDuration = Time.time - _lastFocusLostTime;
            LogMessage($"Application regained focus after {focusLostDuration:F1}s");

            _vrSystemInterrupted = false;

            if (focusLostDuration > 0.5f)
            {
                _ = ConnectVideoAndAudio();
                StartCoroutine(RecoverStreamingAfterPause(focusLostDuration));
            }
        }
    }

    private void HandleControlIdleCheck()
    {
        if ((_isVideoStreamingActive || _isAudioStreamingActive) && !_vrSystemInterrupted)
        {
            if (_lastControlMsgTime > 0f && (Time.time - _lastControlMsgTime) > Constants.CONTROL_IDLE_PAUSE_SECONDS)
            {
                if (!_pausedDueToControlIdle)
                {
                    _pausedDueToControlIdle = true;
                    _wasVideoStreamingBeforeIdle = _isVideoStreamingActive;
                    _wasAudioStreamingBeforeIdle = _isAudioStreamingActive;

                    LogMessage("Control channel idle; pausing streaming", LogType.Warning);
                    if (_isVideoStreamingActive) StopVideoStreaming();
                    if (_isAudioStreamingActive) StopAudioStreaming();
                }
            }
            else if (_pausedDueToControlIdle && _applicationHasFocus)
            {
                _pausedDueToControlIdle = false;
                if (_wasVideoStreamingBeforeIdle && !_isVideoStreamingActive)
                    StartVideoStreaming();
                if (_wasAudioStreamingBeforeIdle && !_isAudioStreamingActive)
                    StartAudioStreaming();
            }
        }
    }

    private void HandlePauseCleanupImmediate()
    {
        LogMessage("Starting immediate pause cleanup...");

        try
        {
            if (_isVideoStreamingActive)
            {
                _isVideoStreamingActive = false;
                if (_streamingCamera != null)
                    _streamingCamera.enabled = false;
            }

            if (_isAudioStreamingActive)
            {
                _isAudioStreamingActive = false;
                if (streamingFromMicrophone && Microphone.IsRecording(_microphoneDeviceName))
                {
                    Microphone.End(_microphoneDeviceName);
                }
            }
        }
        catch (Exception e)
        {
            LogMessage($"Error during pause cleanup: {e.Message}", LogType.Error);
        }
    }

    // ============================================================================
    // DISCOVERY SYSTEM
    // ============================================================================

    private void StartMultiTeacherDiscovery()
    {
        if (_discoveryBroadcastCoroutine != null || _discoveryListenCoroutine != null)
        {
            LogMessage("Discovery already running, skipping start");
            return;
        }

        _discoveryBroadcastCoroutine = StartCoroutine(BroadcastVRDeviceStatus());
        _discoveryListenCoroutine = StartCoroutine(ListenForTeacherRequests());
        LogMessage("Multi-teacher discovery system started");
    }

    private void StopMultiTeacherDiscovery()
    {
        if (_discoveryBroadcastCoroutine != null)
        {
            StopCoroutine(_discoveryBroadcastCoroutine);
            _discoveryBroadcastCoroutine = null;
        }

        if (_discoveryListenCoroutine != null)
        {
            StopCoroutine(_discoveryListenCoroutine);
            _discoveryListenCoroutine = null;
        }

        SafeCloseUdpClient(ref _discoveryBroadcastClient);
        SafeCloseUdpClient(ref _discoveryListenClient);

        LogMessage("Multi-teacher discovery system stopped");
    }

    private IEnumerator BroadcastVRDeviceStatus()
    {
        try
        {
            _discoveryBroadcastClient = new UdpClient();
            _discoveryBroadcastClient.EnableBroadcast = true;
            LogMessage($"Starting VR device broadcast on port {Constants.TEACHER_DISCOVERY_PORT}");
        }
        catch (Exception e)
        {
            LogMessage($"Failed to initialize broadcast client: {e.Message}", LogType.Error);
            yield break;
        }

        while (!_isDestroying)
        {
            SafeBroadcastVRStatus();
            yield return new WaitForSeconds(_discoveryBroadcastInterval);
        }

        SafeCloseUdpClient(ref _discoveryBroadcastClient);
    }

    private void SafeBroadcastVRStatus()
    {
        try
        {
            string statusMessage = CreateVRStatusMessage();
            byte[] data = Encoding.UTF8.GetBytes(statusMessage);

            IPEndPoint teacherEndpoint = new IPEndPoint(IPAddress.Broadcast, Constants.TEACHER_DISCOVERY_PORT);
            _discoveryBroadcastClient?.Send(data, data.Length, teacherEndpoint);

            // Also send to local subnet
            string localIP = GetLocalIPAddress();
            if (!string.IsNullOrEmpty(localIP) && localIP != "Unknown")
            {
                string[] parts = localIP.Split('.');
                if (parts.Length == 4)
                {
                    string subnetBroadcast = $"{parts[0]}.{parts[1]}.{parts[2]}.255";
                    try
                    {
                        IPEndPoint subnetEndPoint = new IPEndPoint(IPAddress.Parse(subnetBroadcast), Constants.TEACHER_DISCOVERY_PORT);
                        _discoveryBroadcastClient?.Send(data, data.Length, subnetEndPoint);
                    }
                    catch { }
                }
            }
        }
        catch (Exception e)
        {
            if (!_isDestroying)
            {
                LogMessage($"Error broadcasting VR status: {e.Message}", LogType.Error);
            }
        }
    }

    private string CreateVRStatusMessage()
    {
        string status;
        if (!_applicationHasFocus || _vrSystemInterrupted)
        {
            status = "PAUSED";
        }
        else if (_connectionStatus == ConnectionStatus.Connected)
        {
            status = $"CONNECTED_{_connectedTeacherId}";
        }
        else if (_connectionStatus == ConnectionStatus.Available)
        {
            status = "AVAILABLE";
        }
        else
        {
            status = "BUSY";
        }

        string localIP = GetLocalIPAddress();
        if (string.IsNullOrEmpty(localIP) || localIP == "Unknown")
        {
            localIP = "0.0.0.0";
        }

        string cleanDeviceName = _deviceName.Replace(":", "_").Replace(" ", "_");
        string cleanStatus = status.Replace(":", "_");

        return $"VR_DEVICE:{_vrAppId}:{localIP}:{cleanDeviceName}:{cleanStatus}";
    }

    private IEnumerator ListenForTeacherRequests()
    {
        try
        {
            _discoveryListenClient = new UdpClient(Constants.VR_DISCOVERY_PORT);
            _discoveryListenClient.Client.ReceiveTimeout = 1000;
            LogMessage($"Listening for teacher requests on port {Constants.VR_DISCOVERY_PORT}");
        }
        catch (Exception e)
        {
            LogMessage($"Failed to start teacher listener: {e.Message}", LogType.Error);
            yield break;
        }

        while (!_isDestroying)
        {
            SafeHandleTeacherMessage();
            yield return new WaitForSeconds(0.1f);
        }

        SafeCloseUdpClient(ref _discoveryListenClient);
    }

    private void SafeHandleTeacherMessage()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        try
        {
            if (_discoveryListenClient != null && _discoveryListenClient.Available > 0)
            {
                byte[] receivedBytes = _discoveryListenClient.Receive(ref remoteEndPoint);
                string receivedMessage = Encoding.UTF8.GetString(receivedBytes);
                LogMessage($"Received teacher message from {remoteEndPoint.Address}: {receivedMessage}");
                HandleTeacherMessage(receivedMessage, remoteEndPoint);
            }
        }
        catch (SocketException ex)
        {
            if (!_isDestroying && ex.SocketErrorCode != SocketError.TimedOut)
            {
                LogMessage($"Socket error in teacher listener: {ex.Message}", LogType.Warning);
            }
        }
        catch (Exception ex)
        {
            if (!_isDestroying)
            {
                LogMessage($"Error in teacher listener: {ex.Message}", LogType.Error);
            }
        }
    }

    private void HandleTeacherMessage(string message, IPEndPoint senderEndPoint)
    {
        try
        {
            if (message.StartsWith("TEACHER_REQUEST:"))
            {
                // Status request - respond with current status (handled by broadcast)
            }
            else if (message.StartsWith("CONNECT_REQUEST:"))
            {
                string jsonPart = message.Substring("CONNECT_REQUEST:".Length);
                var connectionRequest = JsonUtility.FromJson<ConnectionRequest>(jsonPart);
                HandleConnectionRequest(connectionRequest, senderEndPoint);
            }
        }
        catch (Exception e)
        {
            LogMessage($"Error handling teacher message: {e.Message}", LogType.Error);
        }
    }

    private void HandleConnectionRequest(ConnectionRequest request, IPEndPoint senderEndPoint)
    {
        LogMessage($"Connection request from teacher {request.teacherId} at {request.teacherIP}:{request.serverPort}");

        // Reject if already connected with active WebSocket
        if (_isConnected && _controlWebSocket != null && _controlWebSocket.State == WebSocketState.Open)
        {
            LogMessage("Ignoring connection request - already connected with active WebSocket");
            return;
        }

        bool isReconnection = (_connectedTeacherId == request.teacherId) &&
                             (_connectionStatus == ConnectionStatus.Connected);

        if (_connectionStatus != ConnectionStatus.Available && !isReconnection)
        {
            LogMessage($"Cannot accept connection - current status: {_connectionStatus}", LogType.Warning);
            return;
        }

        if (isReconnection)
        {
            LogMessage($"Reconnection request from teacher {request.teacherId}");
            CloseAllWebSocketsSync();
        }

        _connectionStatus = ConnectionStatus.Connected;
        _connectedTeacherId = request.teacherId;
        _connectedTeacherIP = request.teacherIP;
        _serverAddress = $"{request.teacherIP}:{request.serverPort}";
        _vrSystemInterrupted = false;

        LogMessage($"Accepting connection from teacher {request.teacherId} at {_serverAddress}");

        StartCoroutine(ConnectToTeacherServerCoroutine());

        if (!isReconnection)
        {
            OnTeacherConnected?.Invoke();
        }
    }

    // ============================================================================
    // WEBSOCKET CONNECTION
    // ============================================================================

    private IEnumerator ConnectToTeacherServerCoroutine()
    {
        yield return new WaitForSeconds(0.5f);

        LogMessage($"Attempting to connect to teacher server at {_serverAddress}");

        bool connected = false;
        int retryCount = 0;
        int maxRetries = 3;

        while (!connected && retryCount < maxRetries && !_isDestroying)
        {
            retryCount++;
            LogMessage($"Connection attempt {retryCount}/{maxRetries}");

            yield return StartCoroutine(ConnectToServerCoroutine());

            if (_isConnected)
            {
                connected = true;
                LogMessage("Successfully connected to teacher server");
                StopMultiTeacherDiscovery();
            }
            else
            {
                LogMessage($"Connection attempt {retryCount} failed", LogType.Warning);
                if (retryCount < maxRetries)
                {
                    yield return new WaitForSeconds(2.0f);
                }
            }
        }

        if (!connected)
        {
            LogMessage("Failed to connect to teacher server after all retries", LogType.Error);
            HandleDisconnection("connect_failed", "MAX_RETRIES_EXCEEDED");
        }
    }

    private IEnumerator ConnectToServerCoroutine()
    {
        bool connectionCompleted = false;
        bool connectionSucceeded = false;

        Task.Run(async () =>
        {
            try
            {
                await EstablishWebSocketConnections();
                connectionSucceeded = true;
            }
            catch (Exception e)
            {
                if (_isConnected)
                {
                    connectionSucceeded = true;
                }
            }
            connectionCompleted = true;
        });

        while (!connectionCompleted)
        {
            yield return new WaitForSeconds(0.1f);
        }

        if (!connectionSucceeded && _isConnected)
        {
            connectionSucceeded = true;
        }
    }

    private async Task EstablishWebSocketConnections()
    {
        if (string.IsNullOrEmpty(_serverAddress))
        {
            LogMessage("Cannot connect: Server address is null", LogType.Error);
            return;
        }

        LogMessage($"Establishing WebSocket connections to {_serverAddress}");

        _controlWebSocket = new WebSocket($"ws://{_serverAddress}/control");

        _controlWebSocket.OnOpen += () =>
        {
            LogMessage("Control WebSocket connected.");
            _isConnected = true;
            _lastControlMsgTime = (float)(DateTime.UtcNow - _startTime).TotalSeconds;

            // Send client info
            var clientInfo = new ClientInfo
            {
                ip = GetLocalIPAddress(),
                clientName = _deviceName,
                vrAppId = _vrAppId
            };
            _controlWebSocket.SendText(JsonUtility.ToJson(clientInfo));

            SendVRStatusUpdate("CONNECTED");
            _networkAdapter?.OnControlConnected();
            _ = ConnectVideoAndAudio();
            OnServerConnected?.Invoke();
        };

        _controlWebSocket.OnError += (e) =>
        {
            LogMessage($"Control WebSocket error: {e}", LogType.Error);
            _isConnected = false;
            HandleDisconnection("control_error", e);
        };

        _controlWebSocket.OnClose += (e) =>
        {
            _isConnected = false;
            LogMessage($"Control WebSocket closed: {e}", LogType.Warning);
            HandleDisconnection("control_close", e.ToString());
        };

        _controlWebSocket.OnMessage += (bytes) =>
        {
            string message = Encoding.UTF8.GetString(bytes);
            // FIXED: Update control message time on ALL messages
            _lastControlMsgTime = Time.time;
            LogMessage($"Received control message: {message}");
            if (_networkAdapter != null && _networkAdapter.TryHandleInbound(message))
            {
                return;
            }

            HandleCommand(message);
        };

        var connectTask = _controlWebSocket.Connect();
        if (await Task.WhenAny(connectTask, Task.Delay(5000)) != connectTask)
        {
            if (!_isConnected)
            {
                throw new TimeoutException("WebSocket connection timeout");
            }
        }
    }

    private async Task ConnectVideoAndAudio()
    {
        try
        {
            LogMessage("Connecting Video and Audio WebSockets...");

            _audioWebSocket = new WebSocket($"ws://{_serverAddress}/audio");
            _audioWebSocket.OnOpen += () => LogMessage("Audio WebSocket connected.");
            _audioWebSocket.OnError += (e) => LogMessage($"Audio WebSocket error: {e}", LogType.Error);
            _audioWebSocket.OnClose += (e) =>
            {
                LogMessage($"Audio WebSocket closed: {e}", LogType.Warning);
                if (_isAudioStreamingActive) StopAudioStreaming();
            };

            _videoWebSocket = new WebSocket($"ws://{_serverAddress}/video");
            _videoWebSocket.OnOpen += () => LogMessage("Video WebSocket connected.");
            _videoWebSocket.OnError += (e) => LogMessage($"Video WebSocket error: {e}", LogType.Error);
            _videoWebSocket.OnClose += (e) =>
            {
                LogMessage($"Video WebSocket closed: {e}", LogType.Warning);
                if (_isVideoStreamingActive) StopVideoStreaming();
            };

            await Task.WhenAll(_audioWebSocket.Connect(), _videoWebSocket.Connect());
            LogMessage("Audio and Video WebSockets connected.");
        }
        catch (Exception ex)
        {
            LogMessage($"Failed to connect WebSockets: {ex.Message}", LogType.Error);
        }
    }

    private void HandleDisconnection(string source = "control", string reason = "")
    {
        LogMessage($"Handling disconnection from teacher. Source: {source}, Reason: {reason}");
        _networkAdapter?.OnControlDisconnected(source, reason);

        if (_isConnected)
        {
            SendVRStatusUpdate("DISCONNECTED");
        }

        _connectionStatus = ConnectionStatus.Available;
        _connectedTeacherId = null;
        _connectedTeacherIP = null;
        _serverAddress = null;
        _isConnected = false;
        _vrSystemInterrupted = false;

        StopVideoStreaming();
        StopAudioStreaming();
        StartMultiTeacherDiscovery();

        OnTeacherDisconnected?.Invoke();
        OnServerDisconnected?.Invoke();
    }

    // ============================================================================
    // COMMAND HANDLING
    // ============================================================================

    private void HandleCommand(string rawMessage)
    {
        // FIXED: Already updated _lastControlMsgTime in OnMessage handler

        string commandId = rawMessage;

        // Try to parse as JSON
        try
        {
            if (rawMessage.StartsWith("{") && rawMessage.EndsWith("}"))
            {
                var jsonData = JsonUtility.FromJson<Dictionary<string, object>>(rawMessage);
                if (jsonData != null && jsonData.ContainsKey("type"))
                {
                    commandId = jsonData["type"].ToString();
                }
            }
        }
        catch { }

        // Built-in commands
        switch (commandId)
        {
            case Constants.CMD_START_VIDEO_STREAMING:
                StartVideoStreaming();
                return;
            case Constants.CMD_STOP_VIDEO_STREAMING:
                StopVideoStreaming();
                return;
            case Constants.CMD_START_AUDIO_STREAMING:
                StartAudioStreaming();
                return;
            case Constants.CMD_STOP_AUDIO_STREAMING:
                StopAudioStreaming();
                return;
            case Constants.CMD_SET_AUDIO_SOURCE_MIC:
                streamingFromMicrophone = true;
                if (_isAudioStreamingActive)
                {
                    StopAudioStreaming();
                    StartAudioStreaming();
                }
                return;
            case Constants.CMD_SET_AUDIO_SOURCE_APP:
                streamingFromMicrophone = false;
                if (_isAudioStreamingActive)
                {
                    StopAudioStreaming();
                    StartAudioStreaming();
                }
                return;
            case Constants.CMD_BACK_TO_MAIN_SCENE:
                SceneManager.LoadScene(0);
                return;
            case "PING":
                SendMessage("PONG");
                if (_pausedDueToControlIdle && _applicationHasFocus)
                {
                    _pausedDueToControlIdle = false;
                    if (_wasVideoStreamingBeforeIdle && !_isVideoStreamingActive)
                        StartVideoStreaming();
                    if (_wasAudioStreamingBeforeIdle && !_isAudioStreamingActive)
                        StartAudioStreaming();
                }
                return;
            case "APP_PAUSED":
                if (_isVideoStreamingActive) StopVideoStreaming();
                if (_isAudioStreamingActive) StopAudioStreaming();
                _vrSystemInterrupted = true;
                SendVRStatusUpdate("PAUSED");
                return;
            case "APP_RESUMED":
                _vrSystemInterrupted = false;
                _ = ConnectVideoAndAudio();
                StartCoroutine(RecoverStreamingAfterPause(0.5f));
                SendVRStatusUpdate("CONNECTED");
                return;
        }

        // Scene-based commands
        if (_sceneCommandMap.ContainsKey(commandId))
        {
            _sceneCommandMap[commandId].Raise(rawMessage);
            return;
        }

        // Legacy command handlers
        if (_commandHandlerMap.ContainsKey(commandId))
        {
            _commandHandlerMap[commandId].Raise(rawMessage);
            return;
        }

        // Fallback to CommandDispatcher
        if (commandDispatcher)
            commandDispatcher.OnServerMessage(rawMessage);
    }

    // ============================================================================
    // MESSAGING API - CONSOLIDATED
    // ============================================================================

    /// <summary>
    /// Send a simple string message to the server
    /// </summary>
    public bool SendMessage(string message)
    {
        if (_networkAdapter != null && _networkAdapter.TryHandleOutboundText(message))
        {
            return true;
        }

        return SendTextLegacyInternal(message);
    }

    /// <summary>
    /// Send a JSON object to the server
    /// </summary>
    public bool SendJsonMessage(object data)
    {
        if (_networkAdapter != null && _networkAdapter.TryHandleOutboundJson(data))
        {
            return true;
        }

        string jsonMessage = SerializeJsonPayloadLegacy(data);
        if (string.IsNullOrWhiteSpace(jsonMessage))
        {
            LogMessage("Cannot send JSON: Failed to serialize payload", LogType.Warning);
            return false;
        }

        return SendJsonStringLegacyInternal(jsonMessage);
    }

    /// <summary>
    /// BACKWARD COMPATIBILITY: Send a command message to the server
    /// Used by external scripts (UnityMessageExample.cs)
    /// </summary>
    public bool SendCommand(string commandType, object data = null)
    {
        if (_networkAdapter != null && _networkAdapter.TryHandleOutboundCommand(commandType, data))
        {
            return true;
        }

        return SendJsonStringLegacyInternal(BuildCommandJsonLegacy(commandType, data));
    }

    private void SendVRStatusUpdate(string status)
    {
        if (_controlWebSocket != null && _controlWebSocket.State == WebSocketState.Open)
        {
            var statusUpdate = new VRStatusUpdate
            {
                vrAppId = _vrAppId,
                status = status,
                teacherId = _connectedTeacherId
            };

            _controlWebSocket.SendText(JsonUtility.ToJson(statusUpdate));
            LogMessage($"Sent VR status update: {status}");
        }

        if (status == "CONNECTED")
        {
            _connectionStatus = ConnectionStatus.Connected;
        }
        else if (status == "DISCONNECTED")
        {
            _connectionStatus = ConnectionStatus.Available;
            _connectedTeacherId = null;
            _connectedTeacherIP = null;
        }
    }

    public bool IsReadyToSend()
    {
        return _isConnected &&
               _controlWebSocket != null &&
               _controlWebSocket.State == WebSocketState.Open;
    }

    internal string ConnectedTeacherId => _connectedTeacherId ?? string.Empty;
    internal string ConnectedTeacherIP => _connectedTeacherIP ?? string.Empty;
    internal string VrAppId => _vrAppId ?? string.Empty;

    internal void ObserveSessionBootstrapRaw(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return;
        }

        if (rawMessage.IndexOf("\"type\":\"SESSION_DATA\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rawMessage.IndexOf("\"type\": \"SESSION_DATA\"", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            _runtimeSessionBootstrapObserved = true;
            return;
        }

        bool isCommand =
            rawMessage.IndexOf("\"type\":\"COMMAND\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rawMessage.IndexOf("\"type\": \"COMMAND\"", StringComparison.OrdinalIgnoreCase) >= 0;
        bool isSelectedSession =
            rawMessage.IndexOf("\"name\":\"selectedSession\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rawMessage.IndexOf("\"name\": \"selectedSession\"", StringComparison.OrdinalIgnoreCase) >= 0;
        bool isEndSession =
            rawMessage.IndexOf("\"name\":\"EndSessionCommand\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rawMessage.IndexOf("\"name\": \"EndSessionCommand\"", StringComparison.OrdinalIgnoreCase) >= 0;

        if (isCommand && isSelectedSession)
        {
            _runtimeSessionBootstrapObserved = true;
        }
        else if (isCommand && isEndSession)
        {
            _runtimeSessionBootstrapObserved = false;
        }
    }

    internal bool ShouldReplayPersistedSessionBootstrap()
    {
        return !_runtimeSessionBootstrapObserved;
    }

    internal void MarkPersistedSessionBootstrapReplayed()
    {
        _runtimeSessionBootstrapObserved = true;
    }

    internal bool SendTextLegacyInternal(string message)
    {
        if (!IsReadyToSend())
        {
            LogMessage("Cannot send message: Not connected", LogType.Warning);
            return false;
        }

        try
        {
            _controlWebSocket.SendText(message);
            return true;
        }
        catch (Exception e)
        {
            LogMessage($"Failed to send message: {e.Message}", LogType.Error);
            return false;
        }
    }

    internal bool SendJsonStringLegacyInternal(string jsonMessage)
    {
        if (!IsReadyToSend())
        {
            LogMessage("Cannot send JSON: Not connected", LogType.Warning);
            return false;
        }

        try
        {
            _controlWebSocket.SendText(jsonMessage);
            return true;
        }
        catch (Exception e)
        {
            LogMessage($"Failed to send JSON: {e.Message}", LogType.Error);
            return false;
        }
    }

    internal string SerializeJsonPayloadLegacy(object data)
    {
        if (data == null)
        {
            return "null";
        }

        if (data is string stringPayload)
        {
            string trimmed = stringPayload.Trim();
            if ((trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal)) ||
                (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal)))
            {
                return trimmed;
            }

            return SimpleJsonSerializer.Serialize(stringPayload);
        }

        try
        {
            string serialized = SimpleJsonSerializer.Serialize(data);
            if (!string.IsNullOrWhiteSpace(serialized))
            {
                return serialized;
            }
        }
        catch (Exception e)
        {
            LogMessage($"Fallback serializer failed: {e.Message}", LogType.Warning);
        }

        try
        {
            return JsonUtility.ToJson(data);
        }
        catch (Exception e)
        {
            LogMessage($"Failed to serialize JSON payload: {e.Message}", LogType.Error);
            return null;
        }
    }

    internal string BuildCommandJsonLegacy(string commandType, object data = null)
    {
        string serializedData = data == null ? "null" : SerializeJsonPayloadLegacy(data);
        if (string.IsNullOrWhiteSpace(serializedData))
        {
            serializedData = "null";
        }

        return "{" +
               $"\"type\":\"{EscapeJsonString(commandType)}\"," +
               $"\"timestamp\":\"{DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")}\"," +
               $"\"vrAppId\":\"{EscapeJsonString(_vrAppId)}\"," +
               $"\"data\":{serializedData}" +
               "}";
    }

    internal void RouteInboundLegacyInternal(string rawMessage)
    {
        HandleCommand(rawMessage);
    }

    private static string EscapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\b", "\\b")
            .Replace("\f", "\\f")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    // ============================================================================
    // VIDEO STREAMING
    // ============================================================================

    private void StartVideoStreaming()
    {
        if (_isVideoStreamingActive)
        {
            LogMessage("Video streaming already active", LogType.Warning);
            return;
        }

        LogMessage("Starting video streaming");

        try
        {
            if (_streamingCamera == null || _renderTexture == null || !_renderTexture.IsCreated())
            {
                InitializeStreamingCamera();
            }

            _isVideoStreamingActive = true;
            _streamingCamera.enabled = true;
            _lastSuccessfulFrameTime = Time.time;
            _consecutiveFrameFailures = 0;

            _videoStreamingCoroutine = StartCoroutine(VideoStreamingCoroutine());
            StartStreamingHealthCheck();

            LogMessage("Video streaming started successfully");
        }
        catch (Exception e)
        {
            LogMessage($"Failed to start video streaming: {e.Message}", LogType.Error);
            _isVideoStreamingActive = false;
            if (_streamingCamera != null)
                _streamingCamera.enabled = false;
        }
    }

    private void StopVideoStreaming()
    {
        if (!_isVideoStreamingActive)
            return;

        LogMessage("Stopping video streaming");
        _isVideoStreamingActive = false;

        try
        {
            if (_streamingCamera != null)
                _streamingCamera.enabled = false;

            if (_videoStreamingCoroutine != null)
            {
                StopCoroutine(_videoStreamingCoroutine);
                _videoStreamingCoroutine = null;
            }

            lock (_sendQueue)
            {
                _sendQueue.Clear();
            }

            if (!_isAudioStreamingActive)
                StopStreamingHealthCheck();
        }
        catch (Exception e)
        {
            LogMessage($"Error stopping video streaming: {e.Message}", LogType.Error);
        }
    }

    private IEnumerator VideoStreamingCoroutine()
    {
        float frameInterval = 1.0f / _videoFrameRate;
        float nextFrameTime = Time.time;

        while (_isVideoStreamingActive && !_isDestroying)
        {
            // Skip during VR interruption
            if (_vrSystemInterrupted && !_applicationHasFocus)
            {
                while (_vrSystemInterrupted && !_applicationHasFocus && _isVideoStreamingActive && !_isDestroying)
                {
                    yield return new WaitForSeconds(0.1f);
                }

                if (!_isVideoStreamingActive || _isDestroying)
                    break;

                _lastSuccessfulFrameTime = Time.time;
                _consecutiveFrameFailures = 0;

                if (_streamingCamera == null || _renderTexture == null || !_renderTexture.IsCreated())
                {
                    yield return StartCoroutine(SafeInitializeStreamingCamera());
                }
            }

            // Check WebSocket connection
            if (_videoWebSocket == null || _videoWebSocket.State != WebSocketState.Open)
            {
                float waitStartTime = Time.time;
                while ((_videoWebSocket == null || _videoWebSocket.State != WebSocketState.Open) &&
                       _isVideoStreamingActive && !_isDestroying &&
                       (Time.time - waitStartTime) < 5.0f)
                {
                    yield return new WaitForSeconds(0.1f);
                }

                if (_videoWebSocket == null || _videoWebSocket.State != WebSocketState.Open)
                {
                    _isVideoStreamingActive = false;
                    break;
                }

                _lastSuccessfulFrameTime = Time.time;
                _consecutiveFrameFailures = 0;
            }

            // Render and send frame
            if (Time.time >= nextFrameTime)
            {
                nextFrameTime = Time.time + frameInterval;

                if (_streamingCamera != null && _renderTexture != null && _renderTexture.IsCreated())
                {
                    RenderAndEncodeFrame();
                }
            }

            // Send queued frames
            SendQueuedFrames();

            yield return null;
        }

        _isVideoStreamingActive = false;
    }

    private void RenderAndEncodeFrame()
    {
        try
        {
            _streamingCamera.clearFlags = CameraClearFlags.Skybox;
            _streamingCamera.cullingMask = -1;
            _streamingCamera.Render();

            RenderTexture.active = _renderTexture;
            if (_videoTexture == null)
                _videoTexture = new Texture2D(_currentVideoWidth, _currentVideoHeight, TextureFormat.RGB24, false);

            _videoTexture.ReadPixels(new Rect(0, 0, _currentVideoWidth, _currentVideoHeight), 0, 0);
            _videoTexture.Apply();

            byte[] pixels = _videoTexture.GetRawTextureData();
            ScheduleJpegEncode(pixels, _currentVideoWidth, _currentVideoHeight, _adaptiveQuality);
        }
        catch (Exception e)
        {
            LogMessage($"Error rendering frame: {e.Message}", LogType.Error);
        }
    }

    /// <summary>
    /// FIXED: Added rate limiting to prevent frame buildup
    /// </summary>
    private void ScheduleJpegEncode(byte[] raw, int width, int height, int quality)
    {
        // Skip if already encoding to prevent frame buildup
        if (_isEncoding) return;
        _isEncoding = true;

        Task.Run(() =>
        {
            try
            {
                var compressor = new jpeg_compress_struct(new jpeg_error_mgr());
                compressor.Image_width = width;
                compressor.Image_height = height;
                compressor.Input_components = 3;
                compressor.In_color_space = J_COLOR_SPACE.JCS_RGB;

                compressor.jpeg_set_defaults();
                compressor.jpeg_set_quality(quality, true);

                using (var ms = new MemoryStream())
                {
                    compressor.jpeg_stdio_dest(ms);
                    compressor.jpeg_start_compress(true);

                    int rowStride = width * 3;
                    byte[] rowBuffer = new byte[rowStride];

                    for (int i = height - 1; i >= 0; i--)
                    {
                        int sourceIndex = i * rowStride;
                        if (sourceIndex + rowStride <= raw.Length)
                        {
                            Buffer.BlockCopy(raw, sourceIndex, rowBuffer, 0, rowStride);
                            byte[][] scanlines = new byte[1][] { rowBuffer };
                            compressor.jpeg_write_scanlines(scanlines, 1);
                        }
                    }

                    compressor.jpeg_finish_compress();
                    byte[] jpegData = ms.ToArray();

                    UnityMainThreadDispatcher.Instance().Enqueue(() => EnqueueFrameForSend(jpegData));
                }
            }
            catch (Exception ex)
            {
                LogMessage($"JPEG encoding error: {ex.Message}", LogType.Error);
            }
            finally
            {
                _isEncoding = false;
            }
        });
    }

    private void EnqueueFrameForSend(byte[] jpgBytes)
    {
        lock (_sendQueue)
            _sendQueue.Enqueue(jpgBytes);
    }

    private void SendQueuedFrames()
    {
        lock (_sendQueue)
        {
            int framesToSend = Mathf.Min(_sendQueue.Count, 3);
            for (int i = 0; i < framesToSend; i++)
            {
                if (_sendQueue.Count > 0)
                {
                    try
                    {
                        byte[] frameData = _sendQueue.Dequeue();
                        _videoWebSocket?.Send(frameData);
                        _framesSent++;
                        _lastSuccessfulFrameTime = Time.time;
                        _consecutiveFrameFailures = 0;
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Error sending frame: {ex.Message}", LogType.Error);
                        _consecutiveFrameFailures++;
                        break;
                    }
                }
            }
        }
    }

    // ============================================================================
    // AUDIO STREAMING (Disabled - kept as stub)
    // ============================================================================

    private void StartAudioStreaming()
    {
        // Audio streaming is currently disabled
        // Keeping method as stub for API compatibility
        _isAudioStreamingActive = false;
    }

    private void StopAudioStreaming()
    {
        if (!_isAudioStreamingActive) return;

        _isAudioStreamingActive = false;

        if (streamingFromMicrophone && Microphone.IsRecording(_microphoneDeviceName))
        {
            Microphone.End(_microphoneDeviceName);
        }

        _audioBuffer.Clear();

        if (!_isVideoStreamingActive)
            StopStreamingHealthCheck();
    }

    // ============================================================================
    // CAMERA INITIALIZATION
    // ============================================================================

    private void InitializeCamera()
    {
        InitializeStreamingCamera();
    }

    private void InitializeStreamingCamera()
    {
        GameObject streamingCameraObj = new GameObject("StreamingCamera");
        _streamingCamera = streamingCameraObj.AddComponent<Camera>();

        _currentVideoWidth = _videoWidth;
        _currentVideoHeight = _videoHeight;

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            _streamingCamera.transform.SetParent(mainCamera.transform, false);
            _streamingCamera.CopyFrom(mainCamera);
            _streamingCamera.stereoTargetEye = StereoTargetEyeMask.None;
            _streamingCamera.depth = mainCamera.depth - 1;
            _streamingCamera.fieldOfView = 85;

            CreateRenderTexture();
            LogMessage("Streaming camera initialized");
        }
        else
        {
            LogMessage("Failed to find main camera", LogType.Error);
        }

        _streamingCamera.enabled = false;
        _mainAudioListener = FindObjectOfType<AudioListener>();

        _adaptiveQuality = _videoQuality;
        _adaptiveFrameRate = _videoFrameRate;
    }

    private void CreateRenderTexture()
    {
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }

        _currentVideoWidth = _currentVideoWidth - (_currentVideoWidth % 2);
        _currentVideoHeight = _currentVideoHeight - (_currentVideoHeight % 2);

        _renderTexture = new RenderTexture(_currentVideoWidth, _currentVideoHeight, 16);
        _renderTexture.antiAliasing = 1;
        _renderTexture.filterMode = FilterMode.Bilinear;
        _renderTexture.Create();

        _streamingCamera.targetTexture = _renderTexture;

        if (_videoTexture != null)
            Destroy(_videoTexture);

        _videoTexture = new Texture2D(_currentVideoWidth, _currentVideoHeight, TextureFormat.RGB24, false);
    }

    private IEnumerator SafeInitializeStreamingCamera()
    {
        try
        {
            InitializeStreamingCamera();
        }
        catch (Exception e)
        {
            LogMessage($"Error initializing camera: {e.Message}", LogType.Error);
        }
        yield return new WaitForSeconds(0.3f);
    }

    // ============================================================================
    // HEALTH MONITORING
    // ============================================================================

    private void StartStreamingHealthCheck()
    {
        if (_streamingHealthCheckActive) return;

        _streamingHealthCheckActive = true;
        _consecutiveFrameFailures = 0;
        _lastSuccessfulFrameTime = Time.time;

        if (_streamingHealthCheckCoroutine != null)
            StopCoroutine(_streamingHealthCheckCoroutine);

        _streamingHealthCheckCoroutine = StartCoroutine(StreamingHealthCheckLoop());
    }

    private void StopStreamingHealthCheck()
    {
        _streamingHealthCheckActive = false;

        if (_streamingHealthCheckCoroutine != null)
        {
            StopCoroutine(_streamingHealthCheckCoroutine);
            _streamingHealthCheckCoroutine = null;
        }
    }

    private IEnumerator StreamingHealthCheckLoop()
    {
        while (_streamingHealthCheckActive && !_isDestroying)
        {
            yield return new WaitForSeconds(Constants.STREAMING_HEALTH_CHECK_INTERVAL);

            if (!_isDestroying && !_isReinitializing && _applicationHasFocus && !_vrSystemInterrupted)
            {
                CheckStreamingHealth();
            }
        }
    }

    private void CheckStreamingHealth()
    {
        if (!_isVideoStreamingActive) return;

        float timeSinceLastFrame = Time.time - _lastSuccessfulFrameTime;
        bool videoStreamingStalled = timeSinceLastFrame > (Constants.STREAMING_HEALTH_CHECK_INTERVAL * 2);
        bool cameraInvalid = _streamingCamera == null;
        bool renderTextureInvalid = _renderTexture == null || !_renderTexture.IsCreated();
        bool webSocketDisconnected = _videoWebSocket == null || _videoWebSocket.State != WebSocketState.Open;

        if (videoStreamingStalled || cameraInvalid || renderTextureInvalid || webSocketDisconnected)
        {
            _consecutiveFrameFailures++;
            LogMessage($"Video streaming health issue (failure #{_consecutiveFrameFailures})", LogType.Warning);

            if (_consecutiveFrameFailures >= Constants.MAX_CONSECUTIVE_FRAME_FAILURES)
            {
                StartCoroutine(ForceVideoStreamingRecovery());
            }
        }
        else if (_consecutiveFrameFailures > 0)
        {
            _consecutiveFrameFailures = 0;
        }
    }

    // ============================================================================
    // RECOVERY
    // ============================================================================

    private IEnumerator DelayedRecoveryAfterResume(float pauseDuration)
    {
        yield return new WaitForSeconds(0.3f);

        if (!_isDestroying)
        {
            yield return StartCoroutine(RecoverStreamingAfterPause(pauseDuration));
        }
    }

    private IEnumerator RecoverStreamingAfterPause(float pauseDuration)
    {
        yield return new WaitForSeconds(0.5f);

        _vrSystemInterrupted = false;
        _needsStreamingRecovery = false;

        bool shouldRestartVideo = (_wasVideoStreamingBeforePause || _isConnected) && !_isVideoStreamingActive;
        bool shouldRestartAudio = _wasAudioStreamingBeforePause && !_isAudioStreamingActive;

        if (shouldRestartVideo)
        {
            if (_streamingCamera == null || _renderTexture == null || !_renderTexture.IsCreated())
            {
                yield return StartCoroutine(SafeInitializeStreamingCamera());
            }
            StartVideoStreaming();
        }

        if (shouldRestartAudio)
        {
            StartAudioStreaming();
        }

        if (!_streamingHealthCheckActive && (_isVideoStreamingActive || _isAudioStreamingActive))
        {
            StartStreamingHealthCheck();
        }
    }

    private IEnumerator ForceVideoStreamingRecovery()
    {
        LogMessage("Forcing video streaming recovery", LogType.Warning);

        _isReinitializing = true;
        StopStreamingHealthCheck();

        if (_isVideoStreamingActive)
        {
            StopVideoStreaming();
            yield return new WaitForSeconds(0.5f);
        }

        // Destroy existing components
        if (_streamingCamera != null)
        {
            Destroy(_streamingCamera.gameObject);
            _streamingCamera = null;
        }

        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
            _renderTexture = null;
        }

        yield return new WaitForSeconds(0.5f);

        // Reinitialize
        yield return StartCoroutine(SafeInitializeStreamingCamera());

        if (_videoWebSocket == null || _videoWebSocket.State != WebSocketState.Open)
        {
            _ = ConnectVideoAndAudio();
            yield return new WaitForSeconds(0.5f);
        }

        if (_wasVideoStreamingBeforePause || _isConnected)
        {
            StartVideoStreaming();
        }

        _consecutiveFrameFailures = 0;
        _lastSuccessfulFrameTime = Time.time;
        _isReinitializing = false;

        if (_isVideoStreamingActive || _isAudioStreamingActive)
        {
            StartStreamingHealthCheck();
        }
    }

    // ============================================================================
    // SCENE MANAGEMENT
    // ============================================================================

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        LogMessage($"Scene loaded: {scene.name}, reinitializing camera...");

        _isReinitializing = true;
        bool wasHealthCheckActive = _streamingHealthCheckActive;
        StopStreamingHealthCheck();

        _consecutiveFrameFailures = 0;
        _lastSuccessfulFrameTime = Time.time;

        InitializeCamera();
        DiscoverSceneCommands();

        _isReinitializing = false;

        if (wasHealthCheckActive && _isVideoStreamingActive)
        {
            StartStreamingHealthCheck();
        }
    }

    private void DiscoverSceneCommands()
    {
        _sceneCommandMap.Clear();

        NetworkCommandListener[] listeners = FindObjectsOfType<NetworkCommandListener>();

        foreach (var listener in listeners)
        {
            if (listener.commandSO != null && !string.IsNullOrEmpty(listener.commandSO.commandId))
            {
                string commandId = listener.commandSO.commandId;

                if (!_sceneCommandMap.ContainsKey(commandId))
                {
                    _sceneCommandMap.Add(commandId, listener.commandSO);
                    LogMessage($"Registered scene command: {commandId}");
                }
            }
        }

        LogMessage($"Scene command discovery complete. Found {_sceneCommandMap.Count} commands.");
    }

    public void RefreshSceneCommands()
    {
        DiscoverSceneCommands();
    }

    private void LogRegisteredCommands()
    {
        LogMessage("=== REGISTERED COMMANDS ===");
        LogMessage($"Legacy CommandHandlers: {_commandHandlerMap.Count}");
        LogMessage($"Scene NetworkCommands: {_sceneCommandMap.Count}");
        LogMessage("=== END REGISTERED COMMANDS ===");
    }

    // ============================================================================
    // UTILITY METHODS
    // ============================================================================

    private string GetLocalIPAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            string fallbackIP = null;

            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    string ipAddress = ip.ToString();

                    if (ipAddress.StartsWith("127.") || ipAddress.StartsWith("169.254."))
                        continue;

                    if (ipAddress.StartsWith("192.168."))
                        return ipAddress;

                    if (ipAddress.StartsWith("10.") ||
                        (ipAddress.StartsWith("172.") && int.TryParse(ipAddress.Split('.')[1], out int second) && second >= 16 && second <= 31))
                    {
                        if (fallbackIP == null)
                            fallbackIP = ipAddress;
                        continue;
                    }

                    if (ipAddress.StartsWith("100."))
                    {
                        if (fallbackIP == null)
                            fallbackIP = ipAddress;
                        continue;
                    }

                    if (fallbackIP == null)
                        fallbackIP = ipAddress;
                }
            }

            return fallbackIP ?? "Unknown";
        }
        catch (Exception e)
        {
            LogMessage($"Error getting local IP: {e.Message}", LogType.Error);
            return "Unknown";
        }
    }

    private void LogMessage(string message, LogType logType = LogType.Log)
    {
        if (_debugInConsole)
        {
            switch (logType)
            {
                case LogType.Log:
                    Debug.Log($"[WebSocketClient] {message}");
                    break;
                case LogType.Warning:
                    Debug.LogWarning($"[WebSocketClient] {message}");
                    break;
                case LogType.Error:
                    Debug.LogError($"[WebSocketClient] {message}");
                    break;
            }
        }
    }

    private void SafeCloseUdpClient(ref UdpClient client)
    {
        if (client != null)
        {
            try
            {
                client.Close();
                client.Dispose();
            }
            catch { }
            client = null;
        }
    }

    private void CloseAllWebSocketsSync()
    {
        try { _controlWebSocket?.Close(); } catch { }
        try { _videoWebSocket?.Close(); } catch { }
        try { _audioWebSocket?.Close(); } catch { }

        _controlWebSocket = null;
        _videoWebSocket = null;
        _audioWebSocket = null;
    }

    private void Cleanup()
    {
        _isDestroying = true;
        StopAllCoroutines();

        _isVideoStreamingActive = false;
        _isAudioStreamingActive = false;
        _isConnected = false;

        SafeCloseUdpClient(ref _discoveryBroadcastClient);
        SafeCloseUdpClient(ref _discoveryListenClient);

        if (_streamingCamera != null)
        {
            Destroy(_streamingCamera.gameObject);
            _streamingCamera = null;
        }

        if (_microphoneSource != null)
        {
            Destroy(_microphoneSource.gameObject);
            _microphoneSource = null;
        }

        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
            _renderTexture = null;
        }

        if (_videoTexture != null)
        {
            Destroy(_videoTexture);
            _videoTexture = null;
        }

        _audioBuffer.Clear();
        lock (_sendQueue)
        {
            _sendQueue.Clear();
        }

        _controlWebSocket = null;
        _videoWebSocket = null;
        _audioWebSocket = null;
    }
}






// using UnityEngine;
// using System;
// using System.Collections;
// using System.Collections.Generic;
// using System.Net;
// using System.Net.Sockets;
// using System.Text;
// using System.Threading.Tasks;
// using NativeWebSocket;
// using System.IO;
// using System.Threading;
// using BitMiracle.LibJpeg.Classic;
// using UnityEngine.Events;
// using UnityEngine.SceneManagement;

// public class WebSocketClientV6 : MonoBehaviour
// {
//     public static WebSocketClientV6 Instance;
//     public CommandDispatcher commandDispather;

//     // WebSocket configuration
//     private WebSocket _controlWebSocket;
//     private WebSocket _videoWebSocket;
//     private WebSocket _audioWebSocket;

//     // Configuration
//     public bool _debugInConsole = true;
//     public bool streamingFromMicrophone = false;
//     public string selfIP = "0.0.0.0";

//     // Streaming state
//     private bool _isVideoStreamingActive = false;
//     private bool _isAudioStreamingActive = false;

//     // Streaming components
//     [SerializeField] private Camera _streamingCamera;
//     private RenderTexture _renderTexture;
//     private AudioSource _microphoneSource;
//     private AudioListener _mainAudioListener;

//     // Video streaming settings
//     [Header("Video Streaming Settings")]
//     [SerializeField] private int _videoWidth = 1280;
//     [SerializeField] private int _videoHeight = 720;
//     [SerializeField] private int _videoFrameRate = 30;
//     [SerializeField] private int _videoQuality = 75;
//     private bool isStremingVideo = false;

//     // Audio streaming settings
//     [Header("Audio Streaming Settings")]
//     [SerializeField] private int _audioSampleRate = 44100;
//     [SerializeField] private int _audioBufferSize = 1024;
//     [SerializeField] private string _microphoneDeviceName = null;

//     // FIXED: Multi-teacher discovery system with correct ports
//     [Header("Multi-Teacher Discovery")]
//     [SerializeField] private int _vrDiscoveryPort = 8767; // VR devices listen here
//     [SerializeField] private int _teacherDiscoveryPort = 8768; // Teachers listen here
//     [SerializeField] private float _discoveryBroadcastInterval = 2.0f; // Reduced for faster reconnection

//     // VR device identification
//     private string _vrAppId;
//     private string _deviceName;
//     private ConnectionStatus _connectionStatus = ConnectionStatus.Available;
//     private string _connectedTeacherId = null;
//     private string _connectedTeacherIP = null;

//     // Discovery sockets
//     private UdpClient _discoveryBroadcastClient;
//     private UdpClient _discoveryListenClient;
//     private Coroutine _discoveryBroadcastCoroutine;
//     private Coroutine _discoveryListenCoroutine;

//     // Connection state
//     private string _serverAddress = null;
//     private bool _isConnected = false;
//     private DateTime _startTime = DateTime.UtcNow;

//     // ScriptableObject command handling
//     [Header("Command Handling")]
//     [SerializeField] private List<NetworkCommand> _commandHandlers = new List<NetworkCommand>();

//     public UnityEvent OnServerFound;
//     public UnityEvent OnServerConnected;
//     public UnityEvent OnServerDisconnected;
//     public UnityEvent OnTeacherConnected;
//     public UnityEvent OnTeacherDisconnected;

//     private Dictionary<string, NetworkCommand> _commandHandlerMap = new Dictionary<string, NetworkCommand>();

//     // ENHANCED: Scene-based command discovery
//     private Dictionary<string, NetworkCommand> _sceneCommandMap = new Dictionary<string, NetworkCommand>();

//     // Command constants
//     private const string CMD_START_VIDEO_STREAMING = "START_VIDEO_STREAMING";
//     private const string CMD_STOP_VIDEO_STREAMING = "STOP_VIDEO_STREAMING";
//     private const string CMD_START_AUDIO_STREAMING = "START_AUDIO_STREAMING";
//     private const string CMD_STOP_AUDIO_STREAMING = "STOP_AUDIO_STREAMING";
//     private const string CMD_SET_AUDIO_SOURCE_MIC = "SET_AUDIO_SOURCE_MIC";
//     private const string CMD_SET_AUDIO_SOURCE_APP = "SET_AUDIO_SOURCE_APP";
//     private const string CMD_BACK_TO_MAIN_SCENE = "BACK_TO_MAIN_SCENE";

//     // Byte buffer for video streaming
//     private byte[] _videoBuffer;
//     private Texture2D _videoTexture;

//     // Audio streaming
//     private float[] _audioSamples;
//     private ApplicationAudioCapture _appAudioCapture;
//     private List<float> _audioBuffer = new List<float>();
//     private bool _isAudioBufferInitialized = false;
//     private const int AUDIO_BUFFER_SIZE = 4096;
//     private const int AUDIO_CHUNK_SIZE = 1024;
//     private const int AUDIO_PREFILL_SIZE = 8192;
//     private int _audioUnderrunCount = 0;

//     private int _currentVideoWidth;
//     private int _currentVideoHeight;
//     private int _adaptiveQuality;
//     private Queue<long> _packetSizes = new Queue<long>();
//     private float[] _frameTimings = new float[30];
//     private float _lastFrameSentTime = 0;
//     private int _framesSent = 0;
//     private float _nextStatusLogTime = 0;
//     private float _adaptiveFrameRate;
//     private const float MIN_FRAME_INTERVAL = 0.016f;
//     private readonly Queue<byte[]> sendQueue = new Queue<byte[]>();

//     private bool _isDestroying = false;
//     private bool _isReinitializing = false;
//     private Coroutine _audioSendingCoroutine;
//     private Coroutine _videoStreamingCoroutine;
//     private bool _needsStreamingRecovery = false;


//     // Connection status enum
//     public enum ConnectionStatus
//     {
//         Available,
//         Connected,
//         Busy
//     }

//     [System.Serializable]
//     public class ClientInfo
//     {
//         public string type = "CLIENT_INFO";
//         public string ip;
//         public string clientName;
//         public string vrAppId;
//     }

//     // VR Status Update message
//     [System.Serializable]
//     public class VRStatusUpdate
//     {
//         public string type = "VR_STATUS_UPDATE";
//         public string vrAppId;
//         public string status; // "CONNECTED" or "DISCONNECTED"
//         public string teacherId;
//     }

//     // Connection request data structure
//     [System.Serializable]
//     public class ConnectionRequest
//     {
//         public string teacherId;
//         public string teacherIP;
//         public int serverPort;
//     }

//     // Enhanced video streaming health monitoring
//     [Header("Streaming Health Monitoring")]
//     [SerializeField] private float _streamingHealthCheckInterval = 2.0f;
//     [SerializeField] private int _maxConsecutiveFrameFailures = 10;
//     private int _consecutiveFrameFailures = 0;
//     private float _lastSuccessfulFrameTime = 0;
//     private bool _streamingHealthCheckActive = false;
//     private Coroutine _streamingHealthCheckCoroutine;

//     // VR System event handling
//     private bool _vrSystemInterrupted = false;
//     private bool _applicationHasFocus = true;
//     private bool _isInVRBoundary = true;
//     private float _lastFocusLostTime = 0;
//     private float _lastRenderTime = 0;

//     // Enhanced pause/resume handling
//     private bool _wasVideoStreamingBeforePause = false;
//     private bool _wasAudioStreamingBeforePause = false;
//     // private bool _needsStreamingRecovery = false;

//     // Heartbeat/control tracking
//     private float _lastControlMsgTime = 0f;
//     private const float CONTROL_IDLE_PAUSE_SECONDS = 5f;

//     // Control idle state
//     private bool _pausedDueToControlIdle = false;
//     private bool _wasVideoStreamingBeforeIdle = false;
//     private bool _wasAudioStreamingBeforeIdle = false;

//     private void Awake()
//     {
//         if (Instance == null)
//         {
//             Instance = this;

//             // Generate unique VR app ID
//             _vrAppId = $"VR_{SystemInfo.deviceName}_{DateTime.Now.Ticks}";
//             _deviceName = SystemInfo.deviceName;
//             selfIP = GetLocalIPAddress();

//             LogMessage($"VR App initialized with ID: {_vrAppId}, IP: {selfIP}", LogType.Log);
//         }
//         else
//         {
//             Destroy(this);
//             return;
//         }

//         foreach (var handler in _commandHandlers)
//         {
//             if (handler != null && !string.IsNullOrEmpty(handler.commandId))
//             {
//                 if (!_commandHandlerMap.ContainsKey(handler.commandId))
//                 {
//                     _commandHandlerMap.Add(handler.commandId, handler);
//                 }
//                 else
//                 {
//                     LogMessage($"Duplicate command handler found: {handler.commandId}", LogType.Warning);
//                 }
//             }
//         }

//         _audioSamples = new float[_audioBufferSize];
//     }

//     // FIXED: Start multi-teacher discovery system
//     private void StartMultiTeacherDiscovery()
//     {
//         if (_discoveryBroadcastCoroutine != null || _discoveryListenCoroutine != null)
//         {
//             LogMessage("Discovery already running, skipping start", LogType.Log);
//             return;
//         }

//         _discoveryBroadcastCoroutine = StartCoroutine(BroadcastVRDeviceStatus());
//         _discoveryListenCoroutine = StartCoroutine(ListenForTeacherRequests());
//         LogMessage("Multi-teacher discovery system started", LogType.Log);
//     }

//     // Stop multi-teacher discovery system
//     private void StopMultiTeacherDiscovery()
//     {
//         if (_discoveryBroadcastCoroutine != null)
//         {
//             StopCoroutine(_discoveryBroadcastCoroutine);
//             _discoveryBroadcastCoroutine = null;
//         }

//         if (_discoveryListenCoroutine != null)
//         {
//             StopCoroutine(_discoveryListenCoroutine);
//             _discoveryListenCoroutine = null;
//         }

//         _discoveryBroadcastClient?.Close();
//         _discoveryBroadcastClient?.Dispose();
//         _discoveryBroadcastClient = null;

//         _discoveryListenClient?.Close();
//         _discoveryListenClient?.Dispose();
//         _discoveryListenClient = null;

//         LogMessage("Multi-teacher discovery system stopped", LogType.Log);
//     }

//     // FIXED: Broadcast VR device status to all teachers
//     private IEnumerator BroadcastVRDeviceStatus()
//     {
//         _discoveryBroadcastClient = null;

//         // Initialize broadcast client
//         bool initSuccess = false;
//         try
//         {
//             _discoveryBroadcastClient = new UdpClient();
//             _discoveryBroadcastClient.EnableBroadcast = true;
//             initSuccess = true;
//             LogMessage($"Starting VR device broadcast on port {_teacherDiscoveryPort}", LogType.Log);
//         }
//         catch (Exception e)
//         {
//             LogMessage($"Failed to initialize broadcast client: {e.Message}", LogType.Error);
//         }

//         if (!initSuccess)
//         {
//             yield break;
//         }

//         while (!_isDestroying)
//         {
//             bool broadcastSuccess = SafeBroadcastVRStatus();

//             if (!broadcastSuccess && _debugInConsole)
//             {
//                 LogMessage("VR status broadcast failed", LogType.Warning);
//             }

//             // Use the configurable broadcast interval for flexibility
//             yield return new WaitForSeconds(_discoveryBroadcastInterval);
//         }

//         // Cleanup
//         SafeCloseBroadcastClient();
//     }

//     // Safe broadcast method without try-catch yield issues
//     private bool SafeBroadcastVRStatus()
//     {
//         try
//         {
//             string statusMessage = CreateVRStatusMessage();
//             byte[] data = Encoding.UTF8.GetBytes(statusMessage);

//             // Broadcast to teachers on the correct port
//             IPEndPoint teacherEndpoint = new IPEndPoint(IPAddress.Broadcast, _teacherDiscoveryPort);
//             _discoveryBroadcastClient.Send(data, data.Length, teacherEndpoint);

//             // Also send to local subnet
//             string localIP = GetLocalIPAddress();
//             if (!string.IsNullOrEmpty(localIP) && localIP != "Unknown")
//             {
//                 string[] parts = localIP.Split('.');
//                 if (parts.Length == 4)
//                 {
//                     string subnetBroadcast = $"{parts[0]}.{parts[1]}.{parts[2]}.255";
//                     try
//                     {
//                         IPEndPoint subnetEndPoint = new IPEndPoint(IPAddress.Parse(subnetBroadcast), _teacherDiscoveryPort);
//                         _discoveryBroadcastClient.Send(data, data.Length, subnetEndPoint);
//                     }
//                     catch (Exception e)
//                     {
//                         if (_debugInConsole)
//                             LogMessage($"Subnet broadcast failed: {e.Message}", LogType.Warning);
//                     }
//                 }
//             }

//             // CHANGED: More frequent logging to verify broadcasts are happening
//             if (_debugInConsole && Time.frameCount % 90 == 0) // Log every 3 seconds at 30fps
//             {
//                 LogMessage($"Broadcasting VR status: {statusMessage}", LogType.Log);
//             }

//             return true;
//         }
//         catch (Exception e)
//         {
//             if (!_isDestroying)
//             {
//                 LogMessage($"Error broadcasting VR status: {e.Message}", LogType.Error);
//             }
//             return false;
//         }
//     }

//     // Safe cleanup method
//     private void SafeCloseBroadcastClient()
//     {
//         if (_discoveryBroadcastClient != null)
//         {
//             try
//             {
//                 _discoveryBroadcastClient.Close();
//                 _discoveryBroadcastClient.Dispose();
//                 _discoveryBroadcastClient = null;
//             }
//             catch (Exception e)
//             {
//                 LogMessage($"Error closing broadcast client: {e.Message}", LogType.Error);
//             }
//         }
//     }

//     // FIXED: Listen for teacher connection requests
//     private IEnumerator ListenForTeacherRequests()
//     {
//         _discoveryListenClient = null;

//         // Initialize listen client
//         bool initSuccess = false;
//         try
//         {
//             _discoveryListenClient = new UdpClient(_vrDiscoveryPort);
//             _discoveryListenClient.Client.ReceiveTimeout = 1000;
//             initSuccess = true;
//             LogMessage($"Listening for teacher requests on port {_vrDiscoveryPort}", LogType.Log);
//         }
//         catch (Exception e)
//         {
//             LogMessage($"Failed to start teacher listener: {e.Message}", LogType.Error);
//         }

//         if (!initSuccess)
//         {
//             yield break;
//         }

//         while (!_isDestroying)
//         {
//             bool messageHandled = SafeHandleTeacherMessage();

//             // Small delay regardless of message handling success
//             yield return new WaitForSeconds(0.1f);
//         }

//         // Cleanup
//         SafeCloseDiscoveryListenClient();
//     }

//     // Safe message handling without try-catch yield issues
//     private bool SafeHandleTeacherMessage()
//     {
//         IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
//         bool messageReceived = false;
//         byte[] receivedBytes = null;

//         try
//         {
//             if (_discoveryListenClient != null && _discoveryListenClient.Available > 0)
//             {
//                 receivedBytes = _discoveryListenClient.Receive(ref remoteEndPoint);
//                 messageReceived = true;
//             }
//         }
//         catch (SocketException ex)
//         {
//             if (!_isDestroying && ex.SocketErrorCode != SocketError.TimedOut)
//             {
//                 LogMessage($"Socket error in teacher listener: {ex.Message}", LogType.Warning);
//             }
//         }
//         catch (Exception ex)
//         {
//             if (!_isDestroying)
//             {
//                 LogMessage($"Error in teacher listener: {ex.Message}", LogType.Error);
//             }
//         }

//         if (messageReceived && receivedBytes != null)
//         {
//             try
//             {
//                 string receivedMessage = Encoding.UTF8.GetString(receivedBytes);
//                 LogMessage($"Received teacher message from {remoteEndPoint.Address}: {receivedMessage}", LogType.Log);
//                 HandleTeacherMessage(receivedMessage, remoteEndPoint);
//                 return true;
//             }
//             catch (Exception e)
//             {
//                 LogMessage($"Error processing teacher message: {e.Message}", LogType.Error);
//             }
//         }

//         return false;
//     }

//     // Safe cleanup for discovery listen client
//     private void SafeCloseDiscoveryListenClient()
//     {
//         if (_discoveryListenClient != null)
//         {
//             try
//             {
//                 _discoveryListenClient.Close();
//                 _discoveryListenClient.Dispose();
//                 _discoveryListenClient = null;
//             }
//             catch (Exception e)
//             {
//                 LogMessage($"Error closing discovery listen client: {e.Message}", LogType.Error);
//             }
//         }
//     }

//     // FIXED: Create VR status message for broadcasting
//     private string CreateVRStatusMessage()
//     {
//         string status;
//         if (!_applicationHasFocus || _vrSystemInterrupted)
//         {
//             status = "PAUSED";
//         }
//         else if (_connectionStatus == ConnectionStatus.Connected)
//         {
//             status = $"CONNECTED_{_connectedTeacherId}";
//         }
//         else if (_connectionStatus == ConnectionStatus.Available)
//         {
//             status = "AVAILABLE";
//         }
//         else
//         {
//             status = "BUSY";
//         }

//         string localIP = GetLocalIPAddress();
//         if (string.IsNullOrEmpty(localIP) || localIP == "Unknown")
//         {
//             localIP = "0.0.0.0";
//         }

//         // FIXED: Ensure device name and status don't contain colons or special characters
//         string cleanDeviceName = _deviceName.Replace(":", "_").Replace(" ", "_");
//         string cleanStatus = status.Replace(":", "_");

//         string message = $"VR_DEVICE:{_vrAppId}:{localIP}:{cleanDeviceName}:{cleanStatus}";

//         // ADD: Debug logging to see what we're actually sending
//         if (_debugInConsole && Time.frameCount % 90 == 0)
//         {
//             LogMessage($"Creating VR status message: '{message}'", LogType.Log);
//             LogMessage($"Message parts: vrAppId='{_vrAppId}', ip='{localIP}', deviceName='{cleanDeviceName}', status='{cleanStatus}'", LogType.Log);
//         }

//         return message;
//     }

//     // FIXED: Handle messages from teachers
//     private void HandleTeacherMessage(string message, IPEndPoint senderEndPoint)
//     {
//         try
//         {
//             LogMessage($"Processing teacher message: {message}", LogType.Log);

//             if (message.StartsWith("TEACHER_REQUEST:"))
//             {
//                 // Teacher is requesting status update - respond immediately
//                 string[] parts = message.Substring("TEACHER_REQUEST:".Length).Split(':');
//                 if (parts.Length >= 2)
//                 {
//                     string teacherId = parts[0];
//                     string teacherIP = parts[1];
//                     LogMessage($"Status request from teacher {teacherId} at {teacherIP}", LogType.Log);
//                 }
//             }
//             else if (message.StartsWith("CONNECT_REQUEST:"))
//             {
//                 string jsonPart = message.Substring("CONNECT_REQUEST:".Length);
//                 LogMessage($"Parsing connection request: {jsonPart}", LogType.Log);

//                 try
//                 {
//                     var connectionRequest = JsonUtility.FromJson<ConnectionRequest>(jsonPart);
//                     HandleConnectionRequest(connectionRequest, senderEndPoint);
//                 }
//                 catch (Exception e)
//                 {
//                     LogMessage($"Failed to parse connection request JSON: {e.Message}", LogType.Error);
//                 }
//             }
//         }
//         catch (Exception e)
//         {
//             LogMessage($"Error handling teacher message: {e.Message}", LogType.Error);
//         }
//     }

//     // FIXED: Handle connection request from teacher with reconnection support
//     private void HandleConnectionRequest(ConnectionRequest request, IPEndPoint senderEndPoint)
//     {
//         LogMessage($"Connection request from teacher {request.teacherId} at {request.teacherIP}:{request.serverPort}", LogType.Log);

//         // KLUCZOWE: Jeśli już mamy aktywne połączenie (sockety otwarte), ignoruj wszystkie nowe requesty
//         if (_isConnected && _controlWebSocket != null && _controlWebSocket.State == WebSocketState.Open)
//         {
//             LogMessage($"Ignoring connection request - already connected with active WebSocket", LogType.Log);
//             return;
//         }

//         // Check if it's a reconnection from the same teacher
//         bool isReconnection = (_connectedTeacherId == request.teacherId) && 
//                              (_connectionStatus == ConnectionStatus.Connected);

//         if (_connectionStatus != ConnectionStatus.Available && !isReconnection)
//         {
//             LogMessage($"Cannot accept connection - current status: {_connectionStatus}", LogType.Warning);
//             return;
//         }

//         // If it's a reconnection, close existing connections first
//         if (isReconnection)
//         {
//             LogMessage($"Reconnection request from teacher {request.teacherId}, refreshing connection", LogType.Log);

//             // Quick cleanup of existing connections without full disconnection
//             if (_controlWebSocket != null)
//             {
//                 _controlWebSocket.Close();
//                 _controlWebSocket = null;
//             }
//             if (_videoWebSocket != null)
//             {
//                 _videoWebSocket.Close();
//                 _videoWebSocket = null;
//             }
//             if (_audioWebSocket != null)
//             {
//                 _audioWebSocket.Close();
//                 _audioWebSocket = null;
//             }
//         }

//         // Accept the connection
//         _connectionStatus = ConnectionStatus.Connected;
//         _connectedTeacherId = request.teacherId;
//         _connectedTeacherIP = request.teacherIP;
//         _serverAddress = $"{request.teacherIP}:{request.serverPort}";

//         // WAŻNE: Reset flagi VR interruption przy nowym połączeniu
//         // Jeśli VR przyjmuje połączenie, to znaczy że działa normalnie
//         _vrSystemInterrupted = false;
//         // Sprawdź aktualny stan focusu - jeśli możemy przyjąć połączenie, prawdopodobnie mamy focus
//         // Ale nie ustawiaj _applicationHasFocus = true bezpośrednio, bo to powinno być zarządzane przez eventy

//         LogMessage($"Accepting connection from teacher {request.teacherId} at {_serverAddress}", LogType.Log);

//         // Connect to teacher's WebSocket server
//         StartCoroutine(ConnectToTeacherServerCoroutine());

//         if (!isReconnection)
//         {
//             OnTeacherConnected?.Invoke();
//         }
//     }

//     // FIXED: Coroutine wrapper for connecting to teacher server
//     private IEnumerator ConnectToTeacherServerCoroutine()
//     {
//         yield return new WaitForSeconds(0.5f); // Small delay to ensure teacher server is ready

//         LogMessage($"Attempting to connect to teacher server at {_serverAddress}", LogType.Log);

//         // Use the existing connection method
//         bool connected = false;
//         int retryCount = 0;
//         int maxRetries = 3;

//         while (!connected && retryCount < maxRetries && !_isDestroying)
//         {
//             retryCount++;
//             LogMessage($"Connection attempt {retryCount}/{maxRetries}", LogType.Log);

//             yield return StartCoroutine(ConnectToServerCoroutine());

//             if (_isConnected)
//             {
//                 connected = true;
//                 LogMessage("Successfully connected to teacher server", LogType.Log);

//                 // WAŻNE: Zatrzymaj discovery po udanym połączeniu
//                 StopMultiTeacherDiscovery();
//             }
//             else
//             {
//                 LogMessage($"Connection attempt {retryCount} failed", LogType.Warning);
//                 if (retryCount < maxRetries)
//                 {
//                     yield return new WaitForSeconds(2.0f);
//                 }
//             }
//         }

//         if (!connected)
//         {
//             LogMessage("Failed to connect to teacher server after all retries", LogType.Error);
//             HandleDisconnection();
//         }
//     }

//     // FIXED: Coroutine wrapper for server connection
//     private IEnumerator ConnectToServerCoroutine()
//     {
//         bool connectionCompleted = false;
//         bool connectionSucceeded = false;
//         string connectionErrorMessage = null;

//         // Run the async connection in a task
//         Task.Run(async () =>
//         {
//             bool taskSuccess = false;
//             string taskError = null;

//             try
//             {
//                 await EstablishWebSocketConnections();
//                 taskSuccess = true;
//             }
//             catch (Exception e)
//             {
//                 // Jeśli _isConnected jest true mimo wyjątku (np. timeout), to połączenie się udało
//                 if (_isConnected)
//                 {
//                     taskSuccess = true;
//                 }
//                 else
//                 {
//                     taskError = e.Message;
//                 }
//             }

//             // Update results safely
//             connectionSucceeded = taskSuccess;
//             connectionErrorMessage = taskError;
//             connectionCompleted = true;
//         });

//         // Wait for connection to complete
//         while (!connectionCompleted)
//         {
//             yield return new WaitForSeconds(0.1f);
//         }

//         // Dodatkowy check: jeśli _isConnected jest true, to sukces (nawet jeśli task zgłosił błąd)
//         if (!connectionSucceeded && _isConnected)
//         {
//             connectionSucceeded = true;
//             LogMessage("Connection succeeded via OnOpen callback", LogType.Log);
//         }

//         if (!connectionSucceeded)
//         {
//             LogMessage($"Connection failed: {connectionErrorMessage}", LogType.Error);
//         }
//     }

//     // Send VR status update to connected teacher
//     private void SendVRStatusUpdate(string status)
//     {
//         if (_controlWebSocket != null && _controlWebSocket.State == WebSocketState.Open)
//         {
//             var statusUpdate = new VRStatusUpdate();
//             statusUpdate.vrAppId = _vrAppId;
//             statusUpdate.status = status;
//             statusUpdate.teacherId = _connectedTeacherId;

//             string jsonUpdate = JsonUtility.ToJson(statusUpdate);
//             _controlWebSocket.SendText(jsonUpdate);

//             LogMessage($"Sent VR status update: {jsonUpdate}", LogType.Log);
//         }

//         // Also update local connection status
//         if (status == "CONNECTED")
//         {
//             _connectionStatus = ConnectionStatus.Connected;
//         }
//         else if (status == "DISCONNECTED")
//         {
//             _connectionStatus = ConnectionStatus.Available;
//             _connectedTeacherId = null;
//             _connectedTeacherIP = null;
//         }
//     }

//     // FIXED: Handle disconnection from teacher
//     private void HandleDisconnection()
//     {
//         LogMessage("Handling disconnection from teacher", LogType.Log);

//         // Send disconnection status before clearing connection info
//         if (_isConnected)
//         {
//             SendVRStatusUpdate("DISCONNECTED");
//         }

//         _connectionStatus = ConnectionStatus.Available;
//         _connectedTeacherId = null;
//         _connectedTeacherIP = null;
//         _serverAddress = null;
//         _isConnected = false;

//         // WAŻNE: Reset flagi VR interruption przy rozłączeniu
//         // Następne połączenie powinno zacząć z czystym stanem
//         _vrSystemInterrupted = false;

//         // Stop any active streaming
//         StopVideoStreaming();
//         StopAudioStreaming();

//         // WAŻNE: Uruchom ponownie discovery aby móc się ponownie połączyć
//         StartMultiTeacherDiscovery();

//         OnTeacherDisconnected?.Invoke();
//         OnServerDisconnected?.Invoke();
//     }

//     // ... [Include all your existing audio/video streaming methods here - they remain the same] ...

//     private IEnumerator AudioSendingLoop()
//     {
//         _audioBuffer.Clear();
//         _isAudioBufferInitialized = false;
//         _audioUnderrunCount = 0;
//         float lastUnderrunLog = 0;

//         LogMessage("Waiting for audio buffer to fill...", LogType.Log);

//         while (_audioBuffer.Count < AUDIO_PREFILL_SIZE && _isAudioStreamingActive)
//         {
//             if (Time.time - lastUnderrunLog > 1.0f)
//             {
//                 LogMessage($"Prefilling audio buffer: {_audioBuffer.Count}/{AUDIO_PREFILL_SIZE}", LogType.Log);
//                 lastUnderrunLog = Time.time;
//             }

//             yield return new WaitForSeconds(0.05f);
//         }

//         if (!_isAudioStreamingActive) yield break;

//         _isAudioBufferInitialized = true;
//         LogMessage($"Audio buffer filled, starting streaming with {_audioBuffer.Count} samples", LogType.Log);

//         float initialChunkIntervalSeconds = AUDIO_CHUNK_SIZE / 44000f * 0.95f;
//         float normalChunkIntervalSeconds = AUDIO_CHUNK_SIZE / 44100f;
//         float currentChunkIntervalSeconds = initialChunkIntervalSeconds;
//         float adaptationRate = 0.01f;
//         int successiveUnderruns = 0;
//         int successiveOverruns = 0;
//         float lastAdaptationTime = Time.time;
//         float bufferHealth = 1.0f;

//         while (_isAudioStreamingActive && _audioWebSocket != null && _audioWebSocket.State == WebSocketState.Open)
//         {
//             bufferHealth = (float)_audioBuffer.Count / AUDIO_BUFFER_SIZE;

//             if (_audioBuffer.Count >= AUDIO_CHUNK_SIZE)
//             {
//                 float[] chunk = _audioBuffer.GetRange(0, AUDIO_CHUNK_SIZE).ToArray();
//                 _audioBuffer.RemoveRange(0, AUDIO_CHUNK_SIZE);

//                 byte[] audioBytes = ConvertAudioSamplesToBytes(chunk);
//                 _audioWebSocket.Send(audioBytes);

//                 successiveUnderruns = 0;

//                 if (bufferHealth > 1.5f)
//                 {
//                     successiveOverruns++;
//                     if (successiveOverruns > 10 && Time.time - lastAdaptationTime > 1.0f)
//                     {
//                         currentChunkIntervalSeconds *= 0.95f;
//                         LogMessage(
//                             $"Buffer overrun detected ({_audioBuffer.Count} samples), increasing send rate: {1f / currentChunkIntervalSeconds:F1} chunks/s",
//                             LogType.Log);
//                         lastAdaptationTime = Time.time;
//                         successiveOverruns = 0;
//                     }
//                 }
//                 else
//                 {
//                     successiveOverruns = 0;
//                 }
//             }
//             else
//             {
//                 _audioUnderrunCount++;
//                 successiveUnderruns++;

//                 if (successiveUnderruns >= 5)
//                 {
//                     LogMessage(
//                         $"Audio buffer underrun (attempt {_audioUnderrunCount}). Current buffer: {_audioBuffer.Count} samples",
//                         LogType.Warning);

//                     if (successiveUnderruns < 10)
//                     {
//                         byte[] silence = new byte[AUDIO_CHUNK_SIZE * 2];
//                         _audioWebSocket.Send(silence);
//                     }

//                     if (Time.time - lastAdaptationTime > 1.0f)
//                     {
//                         currentChunkIntervalSeconds *= 1.05f;
//                         LogMessage(
//                             $"Slowing down send rate to {1f / currentChunkIntervalSeconds:F1} chunks/s to rebuild buffer",
//                             LogType.Log);
//                         lastAdaptationTime = Time.time;
//                     }

//                     if (successiveUnderruns > 20)
//                     {
//                         LogMessage("Too many consecutive underruns, resetting audio buffer", LogType.Warning);
//                         _isAudioBufferInitialized = false;
//                         yield return StartCoroutine(ResetAudioBuffer());
//                         successiveUnderruns = 0;
//                     }
//                 }
//             }

//             if (bufferHealth >= 0.8f && bufferHealth <= 1.2f && Time.time - lastAdaptationTime > 3.0f)
//             {
//                 currentChunkIntervalSeconds =
//                     Mathf.Lerp(currentChunkIntervalSeconds, normalChunkIntervalSeconds, adaptationRate);
//                 lastAdaptationTime = Time.time;
//             }

//             currentChunkIntervalSeconds = Mathf.Clamp(currentChunkIntervalSeconds,
//                 normalChunkIntervalSeconds * 0.5f,
//                 normalChunkIntervalSeconds * 1.5f);

//             if (Time.frameCount % 300 == 0 && _debugInConsole)
//             {
//                 LogMessage($"Audio buffer status: {_audioBuffer.Count}/{AUDIO_BUFFER_SIZE} samples " +
//                            $"({bufferHealth:P0}), interval: {currentChunkIntervalSeconds * 1000:F1}ms", LogType.Log);
//             }

//             yield return new WaitForSeconds(currentChunkIntervalSeconds);
//         }
//     }

//     // FIXED: Cleanup method optimized to prevent blocking
//     private void Cleanup()
//     {
//         // Set flag first to stop all operations
//         _isDestroying = true;

//         // Stop all coroutines - this is non-blocking
//         StopAllCoroutines();

//         // Stop streaming flags immediately
//         _isVideoStreamingActive = false;
//         _isAudioStreamingActive = false;
//         _isConnected = false;

//         // Quick UDP cleanup - these are fast
//         try
//         {
//             _discoveryBroadcastClient?.Close();
//             _discoveryBroadcastClient = null;
//         }
//         catch { }

//         try
//         {
//             _discoveryListenClient?.Close();
//             _discoveryListenClient = null;
//         }
//         catch { }

//         // Clean Unity objects - fast operations only
//         CleanupUnityObjects();

//         // WebSockets will be cleaned up asynchronously
//         // We just null the references here
//         _controlWebSocket = null;
//         _videoWebSocket = null;
//         _audioWebSocket = null;
//     }

//     private void CleanupUnityObjects()
//     {
//         // These are safe to do synchronously as they're Unity object operations
//         if (_streamingCamera != null)
//         {
//             Destroy(_streamingCamera.gameObject);
//             _streamingCamera = null;
//         }

//         if (_microphoneSource != null)
//         {
//             Destroy(_microphoneSource.gameObject);
//             _microphoneSource = null;
//         }

//         if (_renderTexture != null)
//         {
//             _renderTexture.Release();
//             Destroy(_renderTexture);
//             _renderTexture = null;
//         }

//         if (_videoTexture != null)
//         {
//             Destroy(_videoTexture);
//             _videoTexture = null;
//         }

//         // Clear buffers
//         _audioBuffer.Clear();
//         lock (sendQueue)
//         {
//             sendQueue.Clear();
//         }
//     }

//     private IEnumerator CloseWebSocketsAsync()
//     {
//         // Close control WebSocket
//         if (_controlWebSocket != null)
//         {
//             bool shouldWait = false;
//             try
//             {
//                 if (_controlWebSocket.State == WebSocketState.Open || _controlWebSocket.State == WebSocketState.Connecting)
//                 {
//                     var closeTask = _controlWebSocket.Close();
//                     shouldWait = true;
//                     LogMessage("Control WebSocket close initiated", LogType.Log);
//                 }
//                 _controlWebSocket = null;
//             }
//             catch (Exception e)
//             {
//                 LogMessage($"Error closing control connection: {e.Message}", LogType.Error);
//             }

//             // Yield outside the try-catch block
//             if (shouldWait)
//             {
//                 yield return new WaitForSeconds(0.1f);
//             }
//         }

//         // Similar for video and audio websockets...
//         // [Include similar patterns for _videoWebSocket and _audioWebSocket]
//     }

//     private async Task CloseWebSockets()
//     {
//         // Close control WebSocket
//         try
//         {
//             if (_controlWebSocket != null)
//             {
//                 if (_controlWebSocket.State == WebSocketState.Open || _controlWebSocket.State == WebSocketState.Connecting)
//                 {
//                     await _controlWebSocket.Close();
//                     LogMessage("Control WebSocket closed", LogType.Log);
//                 }
//                 _controlWebSocket = null;
//             }
//         }
//         catch (Exception e)
//         {
//             LogMessage($"Error closing control connection: {e.Message}", LogType.Error);
//         }

//         // Close video WebSocket
//         try
//         {
//             if (_videoWebSocket != null)
//             {
//                 if (_videoWebSocket.State == WebSocketState.Open || _videoWebSocket.State == WebSocketState.Connecting)
//                 {
//                     await _videoWebSocket.Close();
//                     LogMessage("Video WebSocket closed", LogType.Log);
//                 }
//                 _videoWebSocket = null;
//             }
//         }
//         catch (Exception e)
//         {
//             LogMessage($"Error closing video connection: {e.Message}", LogType.Error);
//         }

//         // Close audio WebSocket
//         try
//         {
//             if (_audioWebSocket != null)
//             {
//                 if (_audioWebSocket.State == WebSocketState.Open || _audioWebSocket.State == WebSocketState.Connecting)
//                 {
//                     await _audioWebSocket.Close();
//                     LogMessage("Audio WebSocket closed", LogType.Log);
//                 }
//                 _audioWebSocket = null;
//             }
//         }
//         catch (Exception e)
//         {
//             LogMessage($"Error closing audio connection: {e.Message}", LogType.Error);
//         }
//     }

//     // FIXED: Enhanced connection establishment
//     private async Task EstablishWebSocketConnections()
//     {
//         if (string.IsNullOrEmpty(_serverAddress))
//         {
//             LogMessage("Cannot connect to server: Server address is null or empty", LogType.Error);
//             return;
//         }

//         LogMessage($"Establishing WebSocket connections to {_serverAddress}", LogType.Log);

//         try
//         {
//             _controlWebSocket = new WebSocket($"ws://{_serverAddress}/control");

//             _controlWebSocket.OnOpen += () =>
//             {
//                 LogMessage("Control WebSocket connected.", LogType.Log);
//                 _isConnected = true;        

//                 _lastControlMsgTime = (float)(DateTime.UtcNow - _startTime).TotalSeconds;

//                 // Send enhanced client info with VR app ID
//                 string clientIP = GetLocalIPAddress();
//                 var clientInfo = new ClientInfo();
//                 clientInfo.ip = clientIP;
//                 clientInfo.clientName = _deviceName;
//                 clientInfo.vrAppId = _vrAppId;

//                 string jsonInfo = JsonUtility.ToJson(clientInfo);
//                 LogMessage($"Sending client info: {jsonInfo}", LogType.Log);
//                 _controlWebSocket.SendText(jsonInfo);

//                 // Send VR status update
//                 SendVRStatusUpdate("CONNECTED");

//                 // Connect video and audio channels
//                 _ = ConnectVideoAndAudio();
//                 OnServerConnected?.Invoke();
//             };

//             _controlWebSocket.OnError += (e) =>
//             {
//                 LogMessage($"Control WebSocket error: {e}", LogType.Error);
//                 _isConnected = false;
//                 HandleDisconnection();
//             };

//             _controlWebSocket.OnClose += (e) =>
//             {
//                 _isConnected = false;
//                 LogMessage($"Control WebSocket closed: {e}", LogType.Warning);
//                 HandleDisconnection();
//             };

//             _controlWebSocket.OnMessage += (bytes) =>
//             {
//                 string message = Encoding.UTF8.GetString(bytes);
//                 _lastControlMsgTime = Time.time;
//                 LogMessage($"Received control message: {message}", LogType.Log);
//                 HandleCommand(message);
//             };

//             var connectTask = _controlWebSocket.Connect();
//             if (await Task.WhenAny(connectTask, Task.Delay(5000)) != connectTask)
//             {
//                 // Sprawdź czy OnOpen callback już ustawił _isConnected
//                 // Jeśli tak, połączenie się udało mimo że task jeszcze nie zakończył
//                 if (!_isConnected)
//                 {
//                     throw new TimeoutException("WebSocket connection timeout after 5 seconds");
//                 }
//                 // Jeśli _isConnected == true, ignorujemy timeout - połączenie działa
//                 LogMessage("Connect task timeout but connection is active - continuing", LogType.Log);
//             }
//         }
//         catch (Exception ex)
//         {
//             // Nie loguj błędu jeśli połączenie faktycznie działa
//             if (!_isConnected)
//             {
//                 LogMessage($"Failed to establish WebSocket connections: {ex.Message}", LogType.Error);
//                 throw;
//             }
//         }
//     }

//     private async Task ConnectVideoAndAudio()
//     {
//         using var timeoutCts = new CancellationTokenSource(10000);

//         try
//         {
//             // AUDIO
//             LogMessage("Connecting Audio WebSocket...", LogType.Log);

//             _audioWebSocket = new WebSocket($"ws://{_serverAddress}/audio");
//             _audioWebSocket.OnOpen += () => LogMessage("Audio WebSocket connected.", LogType.Log);
//             _audioWebSocket.OnError += (e) => LogMessage($"Audio WebSocket error: {e}", LogType.Error);
//             _audioWebSocket.OnClose += (e) =>
//             {
//                 LogMessage($"Audio WebSocket closed: {e}", LogType.Warning);
//                 if (_isAudioStreamingActive)
//                 {
//                     StopAudioStreaming();
//                 }
//             };

//             var audioConnectTask = _audioWebSocket.Connect();

//             // VIDEO
//             LogMessage("Connecting Video WebSocket...", LogType.Log);

//             _videoWebSocket = new WebSocket($"ws://{_serverAddress}/video");
//             _videoWebSocket.OnOpen += () => LogMessage("Video WebSocket connected.", LogType.Log);
//             _videoWebSocket.OnError += (e) => LogMessage($"Video WebSocket error: {e}", LogType.Error);
//             _videoWebSocket.OnClose += (e) =>
//             {
//                 LogMessage($"Video WebSocket closed: {e}", LogType.Warning);
//                 if (_isVideoStreamingActive)
//                 {
//                     StopVideoStreaming();
//                 }
//             };

//             var videoConnectTask = _videoWebSocket.Connect();

//             await Task.WhenAll(audioConnectTask, videoConnectTask);

//             LogMessage("Audio and Video WebSockets connected.", LogType.Log);
//         }
//         catch (Exception ex)
//         {
//             LogMessage($"Failed to connect WebSockets: {ex.Message}", LogType.Error);
//         }
//     }

//     private byte[] ConvertAudioSamplesToBytes(float[] samples)
//     {
//         byte[] bytes = new byte[samples.Length * 2];
//         int peakCount = 0;

//         for (int i = 0; i < samples.Length; i++)
//         {
//             float clampedSample = Mathf.Clamp(samples[i], -1.0f, 1.0f);

//             if (Mathf.Abs(clampedSample) > 0.8f)
//                 peakCount++;

//             short value = (short)(clampedSample * 32767);

//             bytes[i * 2] = (byte)(value & 0xFF);
//             bytes[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
//         }

//         if (_debugInConsole && peakCount > 0)
//         {
//             LogMessage($"Audio samples contain {peakCount} peak values out of {samples.Length}", LogType.Log);
//         }

//         return bytes;
//     }

//     private void CreateRenderTexture()
//     {
//         if (_renderTexture != null)
//         {
//             _renderTexture.Release();
//             Destroy(_renderTexture);
//         }

//         _currentVideoWidth = _videoWidth;
//         _currentVideoHeight = _videoHeight;

//         _currentVideoWidth = _currentVideoWidth - (_currentVideoWidth % 2);
//         _currentVideoHeight = _currentVideoHeight - (_currentVideoHeight % 2);

//         _renderTexture = new RenderTexture(_currentVideoWidth, _currentVideoHeight, 16);

//         _renderTexture.antiAliasing = 1;
//         _renderTexture.filterMode = FilterMode.Bilinear;
//         _renderTexture.anisoLevel = 0;
//         _renderTexture.useMipMap = false;
//         _renderTexture.autoGenerateMips = false;
//         _renderTexture.memorylessMode = RenderTextureMemoryless.None;
//         _renderTexture.vrUsage = VRTextureUsage.None;
//         _renderTexture.format = RenderTextureFormat.ARGB32;
//         _renderTexture.enableRandomWrite = false;

//         _renderTexture.Create();

//         _streamingCamera.targetTexture = _renderTexture;

//         if (_videoTexture != null)
//         {
//             Destroy(_videoTexture);
//         }

//         _videoTexture = new Texture2D(_currentVideoWidth, _currentVideoHeight, TextureFormat.RGB24, false);

//         LogMessage($"Created optimized render texture at {_currentVideoWidth}x{_currentVideoHeight}", LogType.Log);
//     }

//     private void EnqueueFrameForSend(byte[] jpgBytes)
//     {
//         lock (sendQueue)
//             sendQueue.Enqueue(jpgBytes);
//     }

//     private string GetLocalIPAddress()
//     {
//         try
//         {
//             var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());

//             // Priority list: prefer local network IPs over VPN/Tailscale
//             string fallbackIP = null;

//             foreach (var ip in host.AddressList)
//             {
//                 if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
//                 {
//                     string ipAddress = ip.ToString();

//                     // Skip loopback and link-local
//                     if (ipAddress.StartsWith("127.") || ipAddress.StartsWith("169.254."))
//                         continue;

//                     // Prefer 192.168.x.x (typical WiFi/LAN)
//                     if (ipAddress.StartsWith("192.168."))
//                     {
//                         return ipAddress;
//                     }

//                     // Prefer 10.0.x.x or 172.16-31.x.x (other private ranges)
//                     if (ipAddress.StartsWith("10.") || 
//                         (ipAddress.StartsWith("172.") && int.TryParse(ipAddress.Split('.')[1], out int second) && second >= 16 && second <= 31))
//                     {
//                         // Save as fallback, but keep looking for 192.168.x.x
//                         if (fallbackIP == null || !fallbackIP.StartsWith("10."))
//                             fallbackIP = ipAddress;
//                         continue;
//                     }

//                     // 100.x.x.x is likely Tailscale - lowest priority
//                     if (ipAddress.StartsWith("100."))
//                     {
//                         if (fallbackIP == null)
//                             fallbackIP = ipAddress;
//                         continue;
//                     }

//                     // Any other IP - use as fallback
//                     if (fallbackIP == null)
//                         fallbackIP = ipAddress;
//                 }
//             }

//             if (fallbackIP != null)
//             {
//                 return fallbackIP;
//             }

//             LogMessage("No suitable network adapter found", LogType.Warning);
//             return "Unknown";
//         }
//         catch (Exception e)
//         {
//             LogMessage($"Error getting local IP: {e.Message}", LogType.Error);
//             return "Unknown";
//         }
//     }

//     private void HandleCommand(string rawMessage)
//     {
//         LogMessage($"Received command: {rawMessage}", LogType.Log);

//         // ENHANCED: Try to parse as JSON first for structured commands
//         string commandId = rawMessage;
//         bool isJsonCommand = false;

//         try
//         {
//             if (rawMessage.StartsWith("{") && rawMessage.EndsWith("}"))
//             {
//                 var jsonData = JsonUtility.FromJson<System.Collections.Generic.Dictionary<string, object>>(rawMessage);
//                 if (jsonData != null && jsonData.ContainsKey("type"))
//                 {
//                     commandId = jsonData["type"].ToString();
//                     isJsonCommand = true;
//                 }
//             }
//         }
//         catch
//         {
//             // Not JSON, treat as simple string command
//         }

//         // Check for built-in commands first
//         switch (commandId)
//         {
//             case CMD_START_VIDEO_STREAMING:
//                 StartVideoStreaming();
//                 return;

//             case CMD_STOP_VIDEO_STREAMING:
//                 StopVideoStreaming();
//                 return;

//             case CMD_START_AUDIO_STREAMING:
//                 StartAudioStreaming();
//                 return;

//             case CMD_STOP_AUDIO_STREAMING:
//                 StopAudioStreaming();
//                 return;

//             case CMD_SET_AUDIO_SOURCE_MIC:
//                 streamingFromMicrophone = true;
//                 if (_isAudioStreamingActive)
//                 {
//                     StopAudioStreaming();
//                     StartAudioStreaming();
//                 }
//                 return;

//             case CMD_SET_AUDIO_SOURCE_APP:
//                 streamingFromMicrophone = false;
//                 if (_isAudioStreamingActive)
//                 {
//                     StopAudioStreaming();
//                     StartAudioStreaming();
//                 }
//                 return;

//             case CMD_BACK_TO_MAIN_SCENE:
//                 SceneManager.LoadScene(0);
//                 return;

//             // Heartbeat and lifecycle commands (string commands)
//             case "PING":
//                 SendMessage("PONG");
//                 _lastControlMsgTime = Time.time;
//                 // If we paused due to idle, resume previously active streams
//                 if (_pausedDueToControlIdle && _applicationHasFocus)
//                 {
//                     _pausedDueToControlIdle = false;
//                     if (_wasVideoStreamingBeforeIdle && !_isVideoStreamingActive)
//                         StartVideoStreaming();
//                     if (_wasAudioStreamingBeforeIdle && !_isAudioStreamingActive)
//                         StartAudioStreaming();
//                 }
//                 return;

//             case "APP_PAUSED":
//                 // Teacher indicates app is paused; stop streaming promptly and mark as busy/paused
//                 _lastControlMsgTime = Time.time;
//                 if (_isVideoStreamingActive) StopVideoStreaming();
//                 if (_isAudioStreamingActive) StopAudioStreaming();
//                 _vrSystemInterrupted = true;
//                 SendVRStatusUpdate("PAUSED");
//                 return;

//             case "APP_RESUMED":
//                 _lastControlMsgTime = Time.time;
//                 _vrSystemInterrupted = false;
//                 // Proactively refresh media sockets after app resume
//                 _ = ConnectVideoAndAudio();
//                 StartCoroutine(RecoverStreamingAfterPause(0.5f));
//                 SendVRStatusUpdate("CONNECTED");
//                 return;
//         }

//         // ENHANCED: Check scene-based NetworkCommand system (with raw message access)
//         if (_sceneCommandMap.ContainsKey(commandId))
//         {
//             LogMessage($"Executing scene command: {commandId} with raw message", LogType.Log);
//             _sceneCommandMap[commandId].Raise(rawMessage); // Pass full raw message!
//             return;
//         }

//         // Check legacy CommandHandler system (simple commands only)
//         if (_commandHandlerMap.ContainsKey(commandId))
//         {
//             LogMessage($"Executing legacy command handler: {commandId}", LogType.Log);
//             _commandHandlerMap[commandId].Raise(rawMessage);
//             return;
//         }

//         // Fallback to CommandDispatcher for backward compatibility
//         LogMessage($"Passing to CommandDispatcher: {rawMessage}", LogType.Log);
//         HandleTextMessage(rawMessage);
//     }

//     private void HandleTextMessage(string message)
//     {
//         LogMessage($"Text message from server: {message}", LogType.Log);

//         if (commandDispather)
//             commandDispather.OnServerMessage(message);
//     }

//     private void InitializeCamera()
//     {
//         InitializeStreamingCamera();

//         /*
//         _appAudioCapture = FindObjectOfType<ApplicationAudioCapture>();
//         if (_appAudioCapture == null && _mainAudioListener != null)
//         {
//             _appAudioCapture = _mainAudioListener.gameObject.AddComponent<ApplicationAudioCapture>();
//         }

//         if (_appAudioCapture != null)
//         {
//             _appAudioCapture.OnAudioDataCapture += OnApplicationAudioCaptured;
//             LogMessage("Application audio capture initialized", LogType.Log);
//         }
//         else
//         {
//             LogMessage("Failed to initialize application audio capture", LogType.Warning);
//         }
//         */
//     }

//     private void InitializeAdaptiveCompression()
//     {
//         _adaptiveQuality = _videoQuality;
//         _packetSizes.Clear();
//     }

//     private void InitializeAdaptiveStreaming()
//     {
//         _adaptiveFrameRate = _videoFrameRate;

//         for (int i = 0; i < _frameTimings.Length; i++)
//         {
//             _frameTimings[i] = 0;
//         }

//         _framesSent = 0;
//         _lastFrameSentTime = Time.time;
//         _nextStatusLogTime = Time.time + 5.0f;

//         LogMessage(
//             $"Initialized adaptive streaming: target {_adaptiveFrameRate} fps at {_currentVideoWidth}x{_currentVideoHeight}",
//             LogType.Log);
//     }

//     private void InitializeStreamingCamera()
//     {
//         GameObject streamingCameraObj = new GameObject("StreamingCamera");
//         _streamingCamera = streamingCameraObj.AddComponent<Camera>();

//         _currentVideoWidth = _videoWidth;
//         _currentVideoHeight = _videoHeight;

//         Camera mainCamera = Camera.main;
//         if (mainCamera != null)
//         {
//             _streamingCamera.transform.SetParent(mainCamera.transform, false);

//             _streamingCamera.CopyFrom(mainCamera);

//             _streamingCamera.clearFlags = mainCamera.clearFlags;
//             _streamingCamera.backgroundColor = mainCamera.backgroundColor;

//             _streamingCamera.cullingMask = mainCamera.cullingMask;

//             _streamingCamera.stereoTargetEye = StereoTargetEyeMask.None;
//             _streamingCamera.depth = mainCamera.depth - 1;
//             _streamingCamera.fieldOfView = 85;

//             _streamingCamera.allowHDR = mainCamera.allowHDR;
//             _streamingCamera.allowMSAA = mainCamera.allowMSAA;

//             CreateRenderTexture();

//             LogMessage("Streaming camera initialized with background rendering", LogType.Log);
//         }
//         else
//         {
//             LogMessage("Failed to find main camera for streaming setup", LogType.Error);
//         }

//         _streamingCamera.enabled = false;

//         _mainAudioListener = FindObjectOfType<AudioListener>();
//         if (_mainAudioListener == null)
//         {
//             LogMessage("No AudioListener found in the scene", LogType.Warning);
//         }

//         InitializeAdaptiveStreaming();
//         InitializeAdaptiveCompression();
//     }



//     private void LogMessage(string message, LogType logType)
//     {
//         if (_debugInConsole)
//         {
//             switch (logType)
//             {
//                 case LogType.Log:
//                     Debug.Log($"[WebSocketClient] {message}");
//                     break;
//                 case LogType.Warning:
//                     Debug.LogWarning($"[WebSocketClient] {message}");
//                     break;
//                 case LogType.Error:
//                     Debug.LogError($"[WebSocketClient] {message}");
//                     break;
//             }
//         }
//     }

//     private void OnApplicationAudioCaptured(float[] audioSamples, int channels)
//     {
//         if (_isAudioStreamingActive && !streamingFromMicrophone)
//         {
//             if (channels == 2)
//             {
//                 for (int i = 0; i < audioSamples.Length; i += 2)
//                 {
//                     float monoSample = (audioSamples[i] + audioSamples[i + 1]) * 0.5f;

//                     if (_audioBuffer.Count < AUDIO_BUFFER_SIZE * 2)
//                     {
//                         _audioBuffer.Add(monoSample);
//                     }
//                 }
//             }
//             else
//             {
//                 if (_audioBuffer.Count < AUDIO_BUFFER_SIZE * 2)
//                 {
//                     for (int i = 0; i < audioSamples.Length && _audioBuffer.Count < AUDIO_BUFFER_SIZE * 2; i++)
//                     {
//                         _audioBuffer.Add(audioSamples[i]);
//                     }
//                 }
//             }

//             if (_debugInConsole && Time.frameCount % 600 == 0)
//             {
//                 LogMessage($"Audio buffer size: {_audioBuffer.Count}, initialized: {_isAudioBufferInitialized}", LogType.Log);
//             }
//         }
//     }

//     // FIXED: OnApplicationPause without blocking operations to prevent ANR
//     private void OnApplicationPause(bool pauseStatus)
//     {
//         // CRITICAL: Do NOT log or do any operations during pause to prevent ANR
//         if (pauseStatus)
//         {
//             // Application is being paused - ONLY set flags, NO operations
//             _lastFocusLostTime = Time.time;
//             _applicationHasFocus = false;
//             _vrSystemInterrupted = true;
//             _wasVideoStreamingBeforePause = _isVideoStreamingActive;
//             _wasAudioStreamingBeforePause = _isAudioStreamingActive;
//             _needsStreamingRecovery = true;

//             // CRITICAL: Do NOT call any methods or log anything here
//             // All cleanup will be handled in Update() on the next frame
//         }
//         else
//         {
//             // Application is resuming - safe to do operations
//             _applicationHasFocus = true;

//             // WAŻNE: Natychmiast resetuj _vrSystemInterrupted gdy resume
//             _vrSystemInterrupted = false;

//             // Only log and start recovery after resume is complete
//             if (!_isDestroying)
//             {
//                 float pauseDuration = Time.time - _lastFocusLostTime;
//                 LogMessage($"Application resumed after {pauseDuration:F1}s pause", LogType.Log);

//                 // Delay recovery slightly to ensure system is ready
//                 if (!_isDestroying && gameObject.activeInHierarchy)
//                 {
//                     StartCoroutine(DelayedRecoveryAfterResume(pauseDuration));
//                 }
//             }
//         }
//     }

//     // NEW: Delayed recovery to ensure system stability after resume
//     private IEnumerator DelayedRecoveryAfterResume(float pauseDuration)
//     {
//         // Wait a moment for the system to fully resume
//         yield return new WaitForSeconds(0.3f);

//         if (!_isDestroying)
//         {
//             yield return StartCoroutine(RecoverStreamingAfterPause(pauseDuration));
//         }
//     }

//     // NEW: Async cleanup method to prevent ANR
//     private IEnumerator HandlePauseCleanupAsync()
//     {
//         LogMessage("Starting async pause cleanup...", LogType.Log);

//         // Yield immediately to prevent blocking the pause event
//         yield return null;

//         // Quick, non-blocking operations only
//         try
//         {
//             // Stop streaming immediately (non-blocking)
//             if (_isVideoStreamingActive)
//             {
//                 _isVideoStreamingActive = false;
//                 if (_streamingCamera != null)
//                     _streamingCamera.enabled = false;
//             }

//             if (_isAudioStreamingActive)
//             {
//                 _isAudioStreamingActive = false;
//                 if (streamingFromMicrophone && Microphone.IsRecording(_microphoneDeviceName))
//                 {
//                     Microphone.End(_microphoneDeviceName);
//                 }
//             }

//             // Send pause status quickly (non-blocking)
//             if (_isConnected && _controlWebSocket != null && _controlWebSocket.State == WebSocketState.Open)
//             {
//                 try
//                 {
//                     SendVRStatusUpdate("PAUSED");
//                 }
//                 catch (Exception e)
//                 {
//                     LogMessage($"Failed to send pause status: {e.Message}", LogType.Warning);
//                 }
//             }

//             LogMessage("Async pause cleanup completed", LogType.Log);
//         }
//         catch (Exception e)
//         {
//             LogMessage($"Error during pause cleanup: {e.Message}", LogType.Error);
//         }
//     }

//     // ENHANCED: Application focus handling
//     private void OnApplicationFocus(bool hasFocus)
//     {
//         LogMessage($"Application focus changed: {hasFocus}", LogType.Log);

//         _applicationHasFocus = hasFocus;

//         if (!hasFocus)
//         {
//             _lastFocusLostTime = Time.time;
//             _vrSystemInterrupted = true;
//         }
//         else
//         {
//             float focusLostDuration = Time.time - _lastFocusLostTime;
//             LogMessage($"Application regained focus after {focusLostDuration:F1}s", LogType.Log);

//             // WAŻNE: Natychmiast resetuj _vrSystemInterrupted gdy focus wraca
//             // Nie czekaj na RecoverStreamingAfterPause
//             _vrSystemInterrupted = false;

//             // Trigger recovery if we lost focus for more than a brief moment
//             if (focusLostDuration > 0.5f)
//             {
//                 // Also proactively refresh media sockets after focus regain
//                 _ = ConnectVideoAndAudio();
//                 StartCoroutine(RecoverStreamingAfterPause(focusLostDuration));
//             }
//         }
//     }

//     // ENHANCED: Streaming recovery after pause/focus loss
//     private IEnumerator RecoverStreamingAfterPause(float pauseDuration)
//     {
//         LogMessage("Starting streaming recovery process...", LogType.Log);

//         // Wait a moment for the system to stabilize
//         yield return new WaitForSeconds(0.5f);

//         _vrSystemInterrupted = false;
//         _needsStreamingRecovery = false;

//         // Check if we need to restart streaming
//         bool shouldRestartVideo = (_wasVideoStreamingBeforePause || _isConnected) && !_isVideoStreamingActive;
//         bool shouldRestartAudio = _wasAudioStreamingBeforePause && !_isAudioStreamingActive;

//         if (shouldRestartVideo || shouldRestartAudio)
//         {
//             LogMessage($"Recovering streaming - Video: {shouldRestartVideo}, Audio: {shouldRestartAudio}", LogType.Log);

//             // Reinitialize camera if needed
//             if (shouldRestartVideo)
//             {
//                 yield return StartCoroutine(RecoverVideoStreaming());
//             }

//             if (shouldRestartAudio)
//             {
//                 yield return StartCoroutine(RecoverAudioStreaming());
//             }
//         }

//         // Ensure health monitoring is active
//         if (!_streamingHealthCheckActive && (_isVideoStreamingActive || _isAudioStreamingActive))
//         {
//             StartStreamingHealthCheck();
//         }

//         LogMessage("Streaming recovery completed", LogType.Log);
//     }

//     // ENHANCED: Video streaming recovery - restructured to avoid try-catch with yield
//     private IEnumerator RecoverVideoStreaming()
//     {
//         LogMessage("Recovering video streaming...", LogType.Log);

//         // Stop any existing streaming first
//         if (_isVideoStreamingActive)
//         {
//             StopVideoStreaming();
//             yield return new WaitForSeconds(0.2f);
//         }

//         // Reinitialize camera components if needed
//         bool needsCameraInit = _streamingCamera == null || _renderTexture == null;
//         if (needsCameraInit)
//         {
//             LogMessage("Reinitializing streaming camera for recovery", LogType.Log);
//             yield return StartCoroutine(SafeInitializeStreamingCamera());
//         }

//         // Verify camera state and restart if valid
//         bool cameraValid = _streamingCamera != null && _renderTexture != null && _renderTexture.IsCreated();
//         if (cameraValid)
//         {
//             StartVideoStreaming();
//             LogMessage("Video streaming recovery successful", LogType.Log);
//         }
//         else
//         {
//             LogMessage("Video streaming recovery failed - camera or render texture invalid", LogType.Error);
//             // Try full reinitialization
//             yield return StartCoroutine(SafeFullInitializeCamera());
//             if (_wasVideoStreamingBeforePause)
//             {
//                 StartVideoStreaming();
//             }
//         }
//     }

//     // ENHANCED: Audio streaming recovery - restructured to avoid try-catch with yield  
//     private IEnumerator RecoverAudioStreaming()
//     {
//         LogMessage("Recovering audio streaming...", LogType.Log);

//         // Stop any existing streaming first
//         if (_isAudioStreamingActive)
//         {
//             StopAudioStreaming();
//             yield return new WaitForSeconds(0.2f);
//         }

//         // Restart audio streaming
//         StartAudioStreaming();
//         LogMessage("Audio streaming recovery successful", LogType.Log);
//     }

//     // Safe wrapper for camera initialization without try-catch yield issues
//     private IEnumerator SafeInitializeStreamingCamera()
//     {
//         bool success = false;

//         try
//         {
//             InitializeStreamingCamera();
//             success = true;
//         }
//         catch (Exception e)
//         {
//             LogMessage($"Error during camera initialization: {e.Message}", LogType.Error);
//         }

//         if (success)
//         {
//             yield return new WaitForSeconds(0.3f);
//         }
//         else
//         {
//             yield return new WaitForSeconds(0.1f);
//         }
//     }

//     // Safe wrapper for full camera initialization
//     private IEnumerator SafeFullInitializeCamera()
//     {
//         try
//         {
//             InitializeCamera();
//         }
//         catch (Exception e)
//         {
//             LogMessage($"Error during full camera initialization: {e.Message}", LogType.Error);
//         }

//         yield return new WaitForSeconds(0.5f);
//     }

//     private void OnDestroy()
//     {
//         _isDestroying = true;
//         Cleanup();
//     }

//     void OnDisable()
//     {
//         SceneManager.sceneLoaded -= OnSceneLoaded;
//     }

//     void OnEnable()
//     {
//         SceneManager.sceneLoaded += OnSceneLoaded;
//     }

//     private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
//     {
//         LogMessage($"Scene loaded: {scene.name}, reinitializing camera...", LogType.Log);

//         // Ustaw flagę reinicjalizacji - health check będzie ignorował failures
//         _isReinitializing = true;

//         // WAŻNE: Zatrzymaj health check podczas reinicjalizacji kamery
//         // żeby nie wykrył "failures" podczas gdy kamera jest w trakcie tworzenia
//         bool wasHealthCheckActive = _streamingHealthCheckActive;
//         StopStreamingHealthCheck();

//         // Reset failure counter
//         _consecutiveFrameFailures = 0;
//         _lastSuccessfulFrameTime = Time.time;

//         InitializeCamera();

//         // ENHANCED: Discover and register scene-based commands
//         DiscoverSceneCommands();

//         // Zakończ reinicjalizację
//         _isReinitializing = false;

//         // Wznów health check jeśli streaming jest aktywny
//         if (wasHealthCheckActive && _isVideoStreamingActive)
//         {
//             StartStreamingHealthCheck();
//         }
//     }

//     // ENHANCED: Discover all NetworkCommandListeners in the current scene
//     private void DiscoverSceneCommands()
//     {
//         LogMessage($"Discovering commands in scene: {SceneManager.GetActiveScene().name}", LogType.Log);

//         // Clear existing scene commands
//         _sceneCommandMap.Clear();

//         // Find all NetworkCommandListener components in the scene
//         NetworkCommandListener[] listeners = FindObjectsOfType<NetworkCommandListener>();

//         foreach (var listener in listeners)
//         {
//             if (listener.commandSO != null && !string.IsNullOrEmpty(listener.commandSO.commandId))
//             {
//                 string commandId = listener.commandSO.commandId;

//                 if (!_sceneCommandMap.ContainsKey(commandId))
//                 {
//                     _sceneCommandMap.Add(commandId, listener.commandSO);
//                     LogMessage($"Registered scene command: {commandId}", LogType.Log);
//                 }
//                 else
//                 {
//                     LogMessage($"Duplicate scene command found: {commandId}", LogType.Warning);
//                 }
//             }
//         }

//         LogMessage($"Scene command discovery complete. Found {_sceneCommandMap.Count} commands.", LogType.Log);
//     }

//     // ENHANCED: Public method to manually trigger command discovery
//     // Call this if you add NetworkCommandListeners dynamically during runtime
//     public void RefreshSceneCommands()
//     {
//         DiscoverSceneCommands();
//     }

//     // ENHANCED: Get information about registered commands (for debugging)
//     public void LogRegisteredCommands()
//     {
//         LogMessage("=== REGISTERED COMMANDS ===", LogType.Log);

//         LogMessage($"Legacy CommandHandlers: {_commandHandlerMap.Count}", LogType.Log);
//         foreach (var kvp in _commandHandlerMap)
//         {
//             LogMessage($"  - {kvp.Key} (Legacy)", LogType.Log);
//         }

//         LogMessage($"Scene NetworkCommands: {_sceneCommandMap.Count}", LogType.Log);
//         foreach (var kvp in _sceneCommandMap)
//         {
//             LogMessage($"  - {kvp.Key} (Scene-based with raw message)", LogType.Log);
//         }

//         LogMessage("=== END REGISTERED COMMANDS ===", LogType.Log);
//     }

//     // ENHANCED: Public messaging API for sending data back to server

//     /// <summary>
//     /// Send a simple string message to the server via control channel
//     /// </summary>
//     /// <param name="message">The message to send</param>
//     /// <returns>True if sent successfully, false otherwise</returns>
//     public bool SendMessage(string message)
//     {
//         if (!_isConnected || _controlWebSocket == null || _controlWebSocket.State != WebSocketState.Open)
//         {
//             LogMessage("Cannot send message: Not connected to server", LogType.Warning);
//             return false;
//         }

//         try
//         {
//             _controlWebSocket.SendText(message);
//             LogMessage($"Sent message to server: {message}", LogType.Log);
//             return true;
//         }
//         catch (Exception e)
//         {
//             LogMessage($"Failed to send message: {e.Message}", LogType.Error);
//             return false;
//         }
//     }

//     /// <summary>
//     /// Send a JSON object to the server via control channel
//     /// </summary>
//     /// <param name="data">The object to serialize and send</param>
//     /// <returns>True if sent successfully, false otherwise</returns>
//     public bool SendJsonMessage(object data)
//     {
//         if (!_isConnected || _controlWebSocket == null || _controlWebSocket.State != WebSocketState.Open)
//         {
//             LogMessage("Cannot send JSON message: Not connected to server", LogType.Warning);
//             return false;
//         }

//         try
//         {
//             string jsonMessage = JsonUtility.ToJson(data);
//             _controlWebSocket.SendText(jsonMessage);
//             LogMessage($"Sent JSON message to server: {jsonMessage}", LogType.Log);
//             return true;
//         }
//         catch (Exception e)
//         {
//             LogMessage($"Failed to send JSON message: {e.Message}", LogType.Error);
//             return false;
//         }
//     }

//     /// <summary>
//     /// Send a structured command message to the server
//     /// </summary>
//     /// <param name="commandType">The command type/name</param>
//     /// <param name="data">Optional data payload</param>
//     /// <returns>True if sent successfully, false otherwise</returns>
//     public bool SendCommand(string commandType, object data = null)
//     {
//         var commandMessage = new
//         {
//             type = commandType,
//             timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
//             vrAppId = _vrAppId,
//             data = data
//         };

//         return SendJsonMessage(commandMessage);
//     }

//     /// <summary>
//     /// Send a response to a specific command with correlation ID
//     /// </summary>
//     /// <param name="originalCommandType">The original command this is responding to</param>
//     /// <param name="responseData">The response data</param>
//     /// <param name="correlationId">Optional correlation ID for tracking</param>
//     /// <returns>True if sent successfully, false otherwise</returns>
//     public bool SendResponse(string originalCommandType, object responseData, string correlationId = null)
//     {
//         var responseMessage = new
//         {
//             type = "RESPONSE",
//             originalCommand = originalCommandType,
//             timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
//             vrAppId = _vrAppId,
//             correlationId = correlationId ?? Guid.NewGuid().ToString(),
//             data = responseData
//         };

//         return SendJsonMessage(responseMessage);
//     }

//     /// <summary>
//     /// Send VR session status update to the server
//     /// </summary>
//     /// <param name="status">Status message (e.g., "READY", "IN_PROGRESS", "COMPLETED")</param>
//     /// <param name="additionalData">Optional additional data</param>
//     /// <returns>True if sent successfully, false otherwise</returns>
//     public bool SendSessionStatus(string status, object additionalData = null)
//     {
//         var statusMessage = new
//         {
//             type = "SESSION_STATUS",
//             status = status,
//             timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
//             vrAppId = _vrAppId,
//             deviceName = _deviceName,
//             data = additionalData
//         };

//         return SendJsonMessage(statusMessage);
//     }

//     /// <summary>
//     /// Send game/session results back to the server
//     /// </summary>
//     /// <param name="results">The results data to send</param>
//     /// <returns>True if sent successfully, false otherwise</returns>
//     public bool SendGameResults(object results)
//     {
//         return SendCommand("GAME_RESULTS", results);
//     }

//     /// <summary>
//     /// Send error information to the server
//     /// </summary>
//     /// <param name="errorMessage">Error description</param>
//     /// <param name="errorCode">Optional error code</param>
//     /// <param name="additionalData">Optional additional error data</param>
//     /// <returns>True if sent successfully, false otherwise</returns>
//     public bool SendError(string errorMessage, string errorCode = null, object additionalData = null)
//     {
//         var errorData = new
//         {
//             type = "ERROR",
//             message = errorMessage,
//             code = errorCode,
//             timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
//             vrAppId = _vrAppId,
//             deviceName = _deviceName,
//             data = additionalData
//         };

//         return SendJsonMessage(errorData);
//     }

//     /// <summary>
//     /// Check if the client is connected and ready to send messages
//     /// </summary>
//     /// <returns>True if connected and ready</returns>
//     public bool IsReadyToSend()
//     {
//         return _isConnected &&
//                _controlWebSocket != null &&
//                _controlWebSocket.State == WebSocketState.Open;
//     }

//     /// <summary>
//     /// Get connection status information
//     /// </summary>
//     /// <returns>Connection status string</returns>
//     public string GetConnectionStatus()
//     {
//         if (!_isConnected)
//             return "Disconnected";

//         if (_controlWebSocket == null)
//             return "No Control Channel";

//         return $"Connected ({_controlWebSocket.State})";
//     }

//     private async void OnApplicationQuit()
//     {
//         _isDestroying = true;
//         Cleanup();
//     }

//     private IEnumerator ResetAudioBuffer()
//     {
//         _audioBuffer.Clear();

//         while (_audioBuffer.Count < AUDIO_PREFILL_SIZE && _isAudioStreamingActive)
//         {
//             yield return new WaitForSeconds(0.05f);
//         }

//         if (_isAudioStreamingActive)
//         {
//             _isAudioBufferInitialized = true;
//             LogMessage($"Audio buffer reset complete, continuing with {_audioBuffer.Count} samples", LogType.Log);
//         }
//     }

//     private void ScheduleJpegEncode(byte[] raw, int width, int height, int quality)
//     {
//         Task.Run(() =>
//         {
//             var compressor = new jpeg_compress_struct(new jpeg_error_mgr());
//             try
//             {
//                 compressor.Image_width = width;
//                 compressor.Image_height = height;
//                 compressor.Input_components = 3;
//                 compressor.In_color_space = J_COLOR_SPACE.JCS_RGB;

//                 compressor.jpeg_set_defaults();
//                 compressor.jpeg_set_quality(quality, true);

//                 using (var ms = new MemoryStream())
//                 {
//                     compressor.jpeg_stdio_dest(ms);
//                     compressor.jpeg_start_compress(true);

//                     int rowStride = width * 3;
//                     byte[] rowBuffer = new byte[rowStride];

//                     for (int i = height - 1; i >= 0; i--)
//                     {
//                         int sourceIndex = i * rowStride;
//                         if (sourceIndex + rowStride <= raw.Length)
//                         {
//                             Buffer.BlockCopy(
//                                 raw,
//                                 sourceIndex,
//                                 rowBuffer, 0,
//                                 rowStride
//                             );

//                             byte[][] scanlines = new byte[1][] { rowBuffer };
//                             compressor.jpeg_write_scanlines(scanlines, 1);
//                         }
//                         else
//                         {
//                             LogMessage($"JPEG encoding index out of range: {sourceIndex + rowStride} > {raw.Length}",
//                                 LogType.Warning);
//                             break;
//                         }
//                     }

//                     compressor.jpeg_finish_compress();
//                     byte[] jpegData = ms.ToArray();

//                     UnityMainThreadDispatcher.Instance().Enqueue(() => { EnqueueFrameForSend(jpegData); });
//                 }
//             }
//             catch (Exception ex)
//             {
//                 LogMessage($"JPEG encoding error: {ex.Message}", LogType.Error);
//             }
//         });
//     }

//     // Start method - only uses new multi-teacher discovery system
//     private void Start()
//     {
//         // Initialize application state tracking
//         _applicationHasFocus = true;
//         _vrSystemInterrupted = false;
//         _lastSuccessfulFrameTime = Time.time;

//         InitializeCamera();

//         // Start multi-teacher discovery system
//         StartMultiTeacherDiscovery();

//         // ENHANCED: Discover commands in the initial scene
//         DiscoverSceneCommands();

//         // ENHANCED: Log all registered commands for debugging
//         if (_debugInConsole)
//         {
//             LogRegisteredCommands();
//         }

//         // Initialize heartbeat timestamp
//         _lastControlMsgTime = Time.time;

//         LogMessage($"VR Device {_deviceName} ({_vrAppId}) ready for teacher connections with enhanced streaming robustness", LogType.Log);
//         LogMessage($"Health monitoring interval: {_streamingHealthCheckInterval}s, Max failures: {_maxConsecutiveFrameFailures}", LogType.Log);
//     }

//     private void StartAudioStreaming()
//     {/*
//         if (_isAudioStreamingActive)
//         {
//             LogMessage("Audio streaming already active", LogType.Warning);
//             return;
//         }

//         LogMessage($"Starting audio streaming (from {(streamingFromMicrophone ? "microphone" : "application")})", LogType.Log);

//         try
//         {
//             // Verify WebSocket connection
//             if (_audioWebSocket == null || _audioWebSocket.State != WebSocketState.Open)
//             {
//                 LogMessage("Audio WebSocket not ready, streaming may fail", LogType.Warning);
//             }

//             _isAudioStreamingActive = true;

//             if (streamingFromMicrophone)
//             {
//                 if (_microphoneSource == null)
//                 {
//                     GameObject micObj = new GameObject("MicrophoneSource");
//                     _microphoneSource = micObj.AddComponent<AudioSource>();
//                 }

//                 _microphoneSource.clip = Microphone.Start(_microphoneDeviceName, true, 1, _audioSampleRate);
//                 _microphoneSource.loop = true;
//                 _microphoneSource.Play();
//             }

//             _audioSendingCoroutine = StartCoroutine(AudioSendingLoop());

//             // Start health monitoring
//             StartStreamingHealthCheck();

//             LogMessage("Audio streaming started successfully", LogType.Log);
//         }
//         catch (Exception e)
//         {
//             LogMessage($"Failed to start audio streaming: {e.Message}", LogType.Error);
//             _isAudioStreamingActive = false;

//             if (streamingFromMicrophone && Microphone.IsRecording(_microphoneDeviceName))
//             {
//                 Microphone.End(_microphoneDeviceName);
//             }
//         }*/
//     }

//     private void StartVideoStreaming()
//     {
//         if (_isVideoStreamingActive)
//         {
//             LogMessage("Video streaming already active", LogType.Warning);
//             return;
//         }

//         LogMessage("Starting video streaming", LogType.Log);

//         try
//         {
//             // Ensure camera and render texture are properly initialized
//             if (_streamingCamera == null || _renderTexture == null || !_renderTexture.IsCreated())
//             {
//                 LogMessage("Initializing camera components for video streaming", LogType.Log);
//                 InitializeStreamingCamera();
//             }

//             // Verify WebSocket connection
//             if (_videoWebSocket == null || _videoWebSocket.State != WebSocketState.Open)
//             {
//                 LogMessage("Video WebSocket not ready, streaming may fail", LogType.Warning);
//             }

//             _isVideoStreamingActive = true;
//             _streamingCamera.enabled = true;
//             _lastSuccessfulFrameTime = Time.time;
//             _consecutiveFrameFailures = 0;

//             // Start the streaming coroutine
//             _videoStreamingCoroutine = StartCoroutine(VideoStreamingCoroutine());

//             // Start health monitoring
//             StartStreamingHealthCheck();

//             LogMessage("Video streaming started successfully", LogType.Log);
//         }
//         catch (Exception e)
//         {
//             LogMessage($"Failed to start video streaming: {e.Message}", LogType.Error);
//             _isVideoStreamingActive = false;
//             if (_streamingCamera != null)
//             {
//                 _streamingCamera.enabled = false;
//             }
//         }
//     }

//     private void StopAudioStreaming()
//     {
//         if (!_isAudioStreamingActive)
//         {
//             LogMessage("Audio streaming already stopped", LogType.Log);
//             return;
//         }

//         LogMessage("Stopping audio streaming", LogType.Log);

//         try
//         {
//             _isAudioStreamingActive = false;

//             if (streamingFromMicrophone && Microphone.IsRecording(_microphoneDeviceName))
//             {
//                 Microphone.End(_microphoneDeviceName);
//             }

//             // Stop the audio sending coroutine
//             if (_audioSendingCoroutine != null)
//             {
//                 StopCoroutine(_audioSendingCoroutine);
//                 _audioSendingCoroutine = null;
//             }

//             // Clear audio buffer
//             _audioBuffer.Clear();

//             // Stop health monitoring if no streams are active
//             if (!_isVideoStreamingActive)
//             {
//                 StopStreamingHealthCheck();
//             }

//             LogMessage("Audio streaming stopped successfully", LogType.Log);
//         }
//         catch (Exception e)
//         {
//             LogMessage($"Error stopping audio streaming: {e.Message}", LogType.Error);
//         }
//     }

//     private void StopVideoStreaming()
//     {
//         if (!_isVideoStreamingActive)
//         {
//             LogMessage("Video streaming already stopped", LogType.Log);
//             return;
//         }

//         LogMessage("Stopping video streaming", LogType.Log);

//         _isVideoStreamingActive = false;

//         try
//         {
//             if (_streamingCamera != null)
//             {
//                 _streamingCamera.enabled = false;
//             }

//             // Stop the streaming coroutine
//             if (_videoStreamingCoroutine != null)
//             {
//                 StopCoroutine(_videoStreamingCoroutine);
//                 _videoStreamingCoroutine = null;
//             }

//             // Clear the send queue
//             lock (sendQueue)
//             {
//                 sendQueue.Clear();
//             }

//             // Stop health monitoring if no streams are active
//             if (!_isAudioStreamingActive)
//             {
//                 StopStreamingHealthCheck();
//             }

//             LogMessage("Video streaming stopped successfully", LogType.Log);
//         }
//         catch (Exception e)
//         {
//             LogMessage($"Error stopping video streaming: {e.Message}", LogType.Error);
//         }
//     }

//     private void Update()
//     {
//         if (_isDestroying) return;

//         // Handle WebSocket message dispatching
//         if (_controlWebSocket != null)
//         {
//             _controlWebSocket.DispatchMessageQueue();
//         }

//         if (_videoWebSocket != null)
//         {
//             _videoWebSocket.DispatchMessageQueue();
//         }

//         if (_audioWebSocket != null)
//         {
//             _audioWebSocket.DispatchMessageQueue();
//         }

//         // CRITICAL: Handle pause cleanup IMMEDIATELY without coroutines
//         if (_vrSystemInterrupted && !_applicationHasFocus && _needsStreamingRecovery)
//         {
//             _needsStreamingRecovery = false; // Prevent multiple triggers

//             // Do immediate cleanup without coroutines
//             HandlePauseCleanupImmediate();
//         }

//         // Heartbeat idle fail-safe: if control is silent while streaming, pause streams to avoid compositor lock
//         if ((_isVideoStreamingActive || _isAudioStreamingActive) && !_vrSystemInterrupted)
//         {
//             if (_lastControlMsgTime > 0f && (Time.time - _lastControlMsgTime) > CONTROL_IDLE_PAUSE_SECONDS)
//             {
//                 if (!_pausedDueToControlIdle)
//                 {
//                     _pausedDueToControlIdle = true;
//                     _wasVideoStreamingBeforeIdle = _isVideoStreamingActive;
//                     _wasAudioStreamingBeforeIdle = _isAudioStreamingActive;

//                     LogMessage("Control channel idle; pausing streaming to prevent compositor lock", LogType.Warning);
//                     if (_isVideoStreamingActive) StopVideoStreaming();
//                     if (_isAudioStreamingActive) StopAudioStreaming();
//                 }
//             }
//             else if (_pausedDueToControlIdle && _applicationHasFocus)
//             {
//                 // Control traffic resumed; restore previous streaming
//                 _pausedDueToControlIdle = false;
//                 if (_wasVideoStreamingBeforeIdle && !_isVideoStreamingActive)
//                 {
//                     StartVideoStreaming();
//                 }
//                 if (_wasAudioStreamingBeforeIdle && !_isAudioStreamingActive)
//                 {
//                     StartAudioStreaming();
//                 }
//             }
//         }
//     }

//     private void HandlePauseCleanupImmediate()
//     {
//         LogMessage("Starting immediate pause cleanup...", LogType.Log);

//         try
//         {
//             // Stop streaming immediately (these are fast, non-blocking operations)
//             if (_isVideoStreamingActive)
//             {
//                 _isVideoStreamingActive = false;
//                 if (_streamingCamera != null)
//                     _streamingCamera.enabled = false;
//             }

//             if (_isAudioStreamingActive)
//             {
//                 _isAudioStreamingActive = false;
//                 if (streamingFromMicrophone && Microphone.IsRecording(_microphoneDeviceName))
//                 {
//                     Microphone.End(_microphoneDeviceName);
//                 }
//             }

//             // CRITICAL: DO NOT send network messages during pause cleanup
//             // This was causing the ANR - network operations block during pause

//             LogMessage("Immediate pause cleanup completed", LogType.Log);
//         }
//         catch (Exception e)
//         {
//             LogMessage($"Error during pause cleanup: {e.Message}", LogType.Error);
//         }
//     }

//     private IEnumerator VideoStreamingCoroutine()
//     {
//         float frameInterval = 1.0f / _videoFrameRate;
//         float nextFrameTime = Time.time;
//         int framesSentThisSession = 0;
//         float sessionStartTime = Time.time;

//         InitializeAdaptiveStreaming();
//         InitializeAdaptiveCompression();

//         LogMessage($"Video streaming coroutine started - Target: {_videoFrameRate}fps, Resolution: {_currentVideoWidth}x{_currentVideoHeight}", LogType.Log);
//         LogMessage($"Streaming flags: vrInterrupted={_vrSystemInterrupted}, hasFocus={_applicationHasFocus}", LogType.Log);

//         while (_isVideoStreamingActive && !_isDestroying)
//         {
//             // Check if we should pause streaming due to VR system interruption
//             if (_vrSystemInterrupted && !_applicationHasFocus)
//             {
//                 LogMessage("Pausing video streaming due to VR system interruption", LogType.Log);

//                 // Wait for the application to regain focus
//                 while (_vrSystemInterrupted && !_applicationHasFocus && _isVideoStreamingActive && !_isDestroying)
//                 {
//                     yield return new WaitForSeconds(0.1f);
//                 }

//                 if (!_isVideoStreamingActive || _isDestroying)
//                     break;

//                 LogMessage("Resuming video streaming after VR system interruption", LogType.Log);

//                 // WAŻNE: Reset timer żeby health check nie wykrył "stalled" od razu po powrocie
//                 _lastSuccessfulFrameTime = Time.time;
//                 _consecutiveFrameFailures = 0;

//                 // Reinitialize components if needed after interruption
//                 if (_streamingCamera == null || _renderTexture == null || !_renderTexture.IsCreated())
//                 {
//                     LogMessage("Reinitializing camera after VR interruption", LogType.Log);
//                     yield return StartCoroutine(SafeInitializeStreamingCamera());
//                 }
//             }

//             // Verify WebSocket connection
//             if (_videoWebSocket == null || _videoWebSocket.State != WebSocketState.Open)
//             {
//                 LogMessage("Video WebSocket disconnected, waiting for reconnection...", LogType.Warning);

//                 // Wait for reconnection
//                 float waitStartTime = Time.time;
//                 while ((_videoWebSocket == null || _videoWebSocket.State != WebSocketState.Open) &&
//                        _isVideoStreamingActive && !_isDestroying &&
//                        (Time.time - waitStartTime) < 5.0f)
//                 {
//                     yield return new WaitForSeconds(0.1f);
//                 }

//                 if (_videoWebSocket == null || _videoWebSocket.State != WebSocketState.Open)
//                 {
//                     LogMessage("Video WebSocket reconnection timeout, stopping streaming", LogType.Error);
//                     // Mark inactive so recovery logic can restart correctly
//                     _isVideoStreamingActive = false;
//                     break;
//                 }

//                 LogMessage("Video WebSocket reconnected, resuming streaming", LogType.Log);

//                 // WAŻNE: Reset timer po reconnect
//                 _lastSuccessfulFrameTime = Time.time;
//                 _consecutiveFrameFailures = 0;
//             }

//             // Check if it's time to send a frame
//             if (Time.time >= nextFrameTime)
//             {
//                 nextFrameTime = Time.time + frameInterval;

//                 // Verify camera and render texture are still valid
//                 if (_streamingCamera == null || _renderTexture == null || !_renderTexture.IsCreated())
//                 {
//                     LogMessage("Camera or render texture invalid during streaming, attempting recovery", LogType.Warning);

//                     yield return StartCoroutine(SafeRecoverCameraDuringStreaming());
//                     continue;
//                 }

//                 // Render frame
//                 bool frameRenderSuccess = SafeRenderFrame();
//                 if (frameRenderSuccess)
//                 {
//                     _lastRenderTime = Time.time;
//                     framesSentThisSession++;
//                 }
//                 else
//                 {
//                     yield return new WaitForSeconds(0.1f);
//                     continue;
//                 }
//             }

//             // Send queued frames
//             SafeSendQueuedFrames();

//             // Log streaming statistics periodically
//             if (Time.time - sessionStartTime > 10.0f && framesSentThisSession > 0)
//             {
//                 float avgFps = framesSentThisSession / (Time.time - sessionStartTime);
//                 LogMessage($"Video streaming stats: {framesSentThisSession} frames sent, avg {avgFps:F1} fps, queue: {sendQueue.Count}", LogType.Log);
//                 framesSentThisSession = 0;
//                 sessionStartTime = Time.time;
//             }

//             yield return null;
//         }

//         // Ensure flag reflects the actual state when coroutine ends
//         _isVideoStreamingActive = false;
//         LogMessage("Video streaming coroutine ended", LogType.Log);
//     }

//     // Safe camera recovery during streaming without yield in try-catch
//     private IEnumerator SafeRecoverCameraDuringStreaming()
//     {
//         bool recoverySuccess = false;

//         try
//         {
//             InitializeStreamingCamera();
//             recoverySuccess = true;
//         }
//         catch (Exception e)
//         {
//             LogMessage($"Failed to recover camera during streaming: {e.Message}", LogType.Error);
//         }

//         if (recoverySuccess)
//         {
//             yield return new WaitForSeconds(0.1f);
//         }
//         else
//         {
//             yield return new WaitForSeconds(1.0f);
//         }
//     }

//     // Safe frame rendering without try-catch yield issues
//     private bool SafeRenderFrame()
//     {
//         try
//         {
//             _streamingCamera.clearFlags = CameraClearFlags.Skybox;
//             _streamingCamera.cullingMask = -1;
//             _streamingCamera.Render();

//             RenderTexture.active = _renderTexture;
//             if (_videoTexture == null)
//                 _videoTexture = new Texture2D(_currentVideoWidth, _currentVideoHeight, TextureFormat.RGB24, false);

//             _videoTexture.ReadPixels(new Rect(0, 0, _currentVideoWidth, _currentVideoHeight), 0, 0);
//             _videoTexture.Apply();

//             byte[] pixels = _videoTexture.GetRawTextureData();
//             ScheduleJpegEncode(pixels, _currentVideoWidth, _currentVideoHeight, _adaptiveQuality);

//             return true;
//         }
//         catch (Exception e)
//         {
//             LogMessage($"Error rendering frame: {e.Message}", LogType.Error);
//             return false;
//         }
//     }

//     // Safe frame sending without try-catch yield issues
//     private void SafeSendQueuedFrames()
//     {
//         lock (sendQueue)
//         {
//             int framesToSend = Mathf.Min(sendQueue.Count, 3); // Limit frames per update to prevent blocking
//             for (int i = 0; i < framesToSend; i++)
//             {
//                 if (sendQueue.Count > 0)
//                 {
//                     try
//                     {
//                         byte[] frameData = sendQueue.Dequeue();
//                         _videoWebSocket.Send(frameData);
//                         _framesSent++;

//                         // Update successful frame tracking for health monitoring
//                         _lastSuccessfulFrameTime = Time.time;
//                         _consecutiveFrameFailures = 0;
//                     }
//                     catch (Exception ex)
//                     {
//                         LogMessage($"Error sending frame: {ex.Message}", LogType.Error);
//                         _consecutiveFrameFailures++;
//                         break;
//                     }
//                 }
//             }
//         }
//     }

//     // ENHANCED: Streaming health monitoring system
//     private void StartStreamingHealthCheck()
//     {
//         if (_streamingHealthCheckActive) return;

//         _streamingHealthCheckActive = true;
//         _consecutiveFrameFailures = 0;
//         _lastSuccessfulFrameTime = Time.time;

//         if (_streamingHealthCheckCoroutine != null)
//         {
//             StopCoroutine(_streamingHealthCheckCoroutine);
//         }

//         _streamingHealthCheckCoroutine = StartCoroutine(StreamingHealthCheckLoop());
//         LogMessage("Streaming health monitoring started", LogType.Log);
//     }

//     private void StopStreamingHealthCheck()
//     {
//         _streamingHealthCheckActive = false;

//         if (_streamingHealthCheckCoroutine != null)
//         {
//             StopCoroutine(_streamingHealthCheckCoroutine);
//             _streamingHealthCheckCoroutine = null;
//         }

//         LogMessage("Streaming health monitoring stopped", LogType.Log);
//     }

//     private IEnumerator StreamingHealthCheckLoop()
//     {
//         while (_streamingHealthCheckActive && !_isDestroying)
//         {
//             yield return new WaitForSeconds(_streamingHealthCheckInterval);

//             if (!_isDestroying)
//             {
//                 CheckStreamingHealth();
//             }
//         }
//     }

//     private void CheckStreamingHealth()
//     {
//         // Ignoruj health check podczas reinicjalizacji (np. zmiana sceny)
//         if (_isReinitializing)
//         {
//             return;
//         }

//         // WAŻNE: Ignoruj health check gdy aplikacja nie ma focusu lub jest VR interruption
//         // W tym stanie streaming jest celowo wstrzymany - to nie jest failure!
//         if (!_applicationHasFocus || _vrSystemInterrupted)
//         {
//             // Reset timer żeby nie wykryć "stalled" po powrocie
//             _lastSuccessfulFrameTime = Time.time;
//             return;
//         }

//         float currentTime = Time.time;
//         float timeSinceLastFrame = currentTime - _lastSuccessfulFrameTime;

//         // Check video streaming health
//         if (_isVideoStreamingActive)
//         {
//             bool videoStreamingStalled = timeSinceLastFrame > (_streamingHealthCheckInterval * 2);

//             // NAPRAWIONE: Kamera jest invalid tylko jeśli jest NULL
//             // Nie sprawdzaj .enabled bo kamera jest celowo wyłączona podczas renderowania w tle!
//             bool cameraInvalid = _streamingCamera == null;

//             bool renderTextureInvalid = _renderTexture == null || !_renderTexture.IsCreated();
//             bool webSocketDisconnected = _videoWebSocket == null || _videoWebSocket.State != WebSocketState.Open;

//             if (videoStreamingStalled || cameraInvalid || renderTextureInvalid || webSocketDisconnected)
//             {
//                 _consecutiveFrameFailures++;
//                 LogMessage($"Video streaming health issue detected (failure #{_consecutiveFrameFailures}): " +
//                           $"Stalled: {videoStreamingStalled}, Camera: {cameraInvalid}, RenderTexture: {renderTextureInvalid}, WebSocket: {webSocketDisconnected}",
//                           LogType.Warning);

//                 if (_consecutiveFrameFailures >= _maxConsecutiveFrameFailures)
//                 {
//                     LogMessage("Too many consecutive failures, attempting video streaming recovery", LogType.Error);
//                     StartCoroutine(ForceVideoStreamingRecovery());
//                 }
//             }
//             else
//             {
//                 // Reset failure count on successful health check
//                 if (_consecutiveFrameFailures > 0)
//                 {
//                     LogMessage($"Video streaming health recovered after {_consecutiveFrameFailures} failures", LogType.Log);
//                     _consecutiveFrameFailures = 0;
//                 }
//             }
//         }

//         // Check for VR system interruption recovery
//         if (_vrSystemInterrupted && _applicationHasFocus && (currentTime - _lastFocusLostTime) > 1.0f)
//         {
//             LogMessage("VR system interruption detected, checking for needed recovery", LogType.Log);

//             bool needsVideoRecovery = _wasVideoStreamingBeforePause && !_isVideoStreamingActive;
//             bool needsAudioRecovery = _wasAudioStreamingBeforePause && !_isAudioStreamingActive;

//             if (needsVideoRecovery || needsAudioRecovery)
//             {
//                 _needsStreamingRecovery = true;
//                 StartCoroutine(RecoverStreamingAfterPause(currentTime - _lastFocusLostTime));
//             }
//         }
//     }

//     private IEnumerator ForceVideoStreamingRecovery()
//     {
//         LogMessage("Forcing video streaming recovery due to health check failures", LogType.Warning);

//         // Ustaw flagę reinicjalizacji
//         _isReinitializing = true;

//         // Stop health monitoring temporarily during recovery
//         bool wasHealthCheckActive = _streamingHealthCheckActive;
//         StopStreamingHealthCheck();

//         // Force stop current streaming
//         if (_isVideoStreamingActive)
//         {
//             StopVideoStreaming();
//             yield return new WaitForSeconds(0.5f);
//         }

//         // Destroy existing camera components
//         yield return StartCoroutine(SafeDestroyStreamingComponents());

//         yield return new WaitForSeconds(0.5f);

//         // Reinitialize camera only
//         yield return StartCoroutine(SafeInitializeStreamingCamera());

//         // NAPRAWIONE: Nie twórz nowych websocketów jeśli stare działają!
//         // Tylko sprawdź czy video websocket wymaga ponownego połączenia
//         bool needsVideoReconnect = _videoWebSocket == null || _videoWebSocket.State != WebSocketState.Open;
//         if (needsVideoReconnect)
//         {
//             LogMessage("Video WebSocket needs reconnection during recovery", LogType.Warning);
//             // Tylko jeśli naprawdę potrzebujemy - połącz ponownie
//             _ = ConnectVideoAndAudio();
//             yield return new WaitForSeconds(0.5f);
//         }

//         // Restart streaming if we should be streaming
//         if (_wasVideoStreamingBeforePause || _isConnected)
//         {
//             StartVideoStreaming();
//             LogMessage("Forced video streaming recovery completed", LogType.Log);
//         }

//         // Reset failure tracking
//         _consecutiveFrameFailures = 0;
//         _lastSuccessfulFrameTime = Time.time;

//         // Zakończ reinicjalizację
//         _isReinitializing = false;

//         // Restart health monitoring if it was active
//         if (wasHealthCheckActive && (_isVideoStreamingActive || _isAudioStreamingActive))
//         {
//             StartStreamingHealthCheck();
//         }
//     }

//     // Safe wrapper for destroying streaming components
//     private IEnumerator SafeDestroyStreamingComponents()
//     {
//         try
//         {
//             // Full camera reinitialization
//             if (_streamingCamera != null)
//             {
//                 Destroy(_streamingCamera.gameObject);
//                 _streamingCamera = null;
//             }

//             if (_renderTexture != null)
//             {
//                 _renderTexture.Release();
//                 Destroy(_renderTexture);
//                 _renderTexture = null;
//             }

//             LogMessage("Streaming components destroyed for recovery", LogType.Log);
//         }
//         catch (Exception e)
//         {
//             LogMessage($"Error destroying streaming components: {e.Message}", LogType.Error);
//         }

//         yield return null;
//     }
// }
