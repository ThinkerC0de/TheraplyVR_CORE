using System;
using System.Threading.Tasks;
using UnityEngine;
using TheraplyCore.Logging;
using TheraplyCore.Network.Discovery;
using TheraplyCore.Network.Connection;

namespace TheraplyCore.Connection
{
    /// <summary>
    /// Manages connection lifecycle with automatic reconnection
    /// 
    /// FEATURES:
    /// - State machine (Disconnected → Discovering → Connecting → Connected → Reconnecting)
    /// - Exponential backoff (1s, 2s, 4s, 8s, 15s, 30s...)
    /// - Heartbeat monitoring (10s timeout)
    /// - Command queuing during disconnection
    /// - State sync after reconnect
    /// 
    /// USAGE:
    /// connectionManager.OnConnected += HandleConnected;
    /// connectionManager.OnReconnecting += HandleReconnecting;
    /// connectionManager.StartConnection();
    /// </summary>
    public class ConnectionStateManager : MonoBehaviour
    {
        // ============================================
        // CONFIGURATION
        // ============================================
        
        [Header("Reconnection Settings")]
        [SerializeField] private int _maxReconnectAttempts = 10;
        [SerializeField] private float _heartbeatTimeout = 10f;
        [SerializeField] private float _heartbeatInterval = 2f;
        
        [Header("Dependencies")]
        [SerializeField] private UDPDiscoveryService _discoveryService;
        [SerializeField] private TCPConnectionService _tcpService;
        
        [Header("Debug")]
        [SerializeField] private bool _logStateChanges = true;
        
        // ============================================
        // STATE
        // ============================================
        
        private ConnectionState _state = ConnectionState.Disconnected;
        private int _reconnectAttempts = 0;
        
        // Exponential backoff delays (seconds)
        private readonly float[] _retryDelays = { 1, 2, 4, 8, 15, 30, 60, 120, 180, 300 };
        
        // Heartbeat tracking
        private DateTime _lastHeartbeatReceived;
        private DateTime _lastHeartbeatSent;
        private bool _heartbeatRunning = false;
        
        // Current connection info
        private DeviceInfo _currentDevice;
        private string _sessionId;
        
        // ============================================
        // PROPERTIES
        // ============================================
        
        public ConnectionState CurrentState => _state;
        public bool IsConnected => _state == ConnectionState.Connected;
        public DeviceInfo CurrentDevice => _currentDevice;
        
        // ============================================
        // EVENTS
        // ============================================
        
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action OnReconnecting;
        public event Action OnReconnected;
        public event Action OnReconnectionFailed;
        public event Action<ConnectionState, ConnectionState> OnStateChanged;
        
        // ============================================
        // UNITY LIFECYCLE
        // ============================================
        
        void Awake()
        {
            if (_discoveryService == null)
            {
                _discoveryService = GetComponent<UDPDiscoveryService>();
            }
            
            if (_tcpService == null)
            {
                _tcpService = GetComponent<TCPConnectionService>();
            }
        }
        
        void Start()
        {
            // Subscribe to TCP events
            _tcpService.OnDisconnected += HandleTcpDisconnected;
            
            Logger.Info("[ConnectionManager] Initialized");
        }
        
        void Update()
        {
            // Monitor heartbeat timeout
            if (_state == ConnectionState.Connected && _heartbeatRunning)
            {
                float timeSinceLastHeartbeat = (float)(DateTime.UtcNow - _lastHeartbeatReceived).TotalSeconds;
                
                if (timeSinceLastHeartbeat > _heartbeatTimeout)
                {
                    Logger.Warning($"[ConnectionManager] Heartbeat timeout ({timeSinceLastHeartbeat:F1}s)");
                    StartReconnection();
                }
            }
        }
        
        void OnDestroy()
        {
            StopHeartbeat();
            
            if (_tcpService != null)
            {
                _tcpService.OnDisconnected -= HandleTcpDisconnected;
            }
        }
        
        // ============================================
        // PUBLIC API
        // ============================================
        
        public async void StartConnection()
        {
            if (_state != ConnectionState.Disconnected)
            {
                Logger.Warning("[ConnectionManager] Already connecting/connected");
                return;
            }
            
            Logger.Info("[ConnectionManager] Starting connection process");
            
            ChangeState(ConnectionState.Discovering);
            _discoveryService.StartDiscovery();
            
            _discoveryService.OnDeviceDiscovered += HandleDeviceDiscovered;
        }
        
        public void Disconnect()
        {
            Logger.Info("[ConnectionManager] Manual disconnect");
            
            StopHeartbeat();
            _tcpService.Disconnect();
            
            ChangeState(ConnectionState.Disconnected);
            OnDisconnected?.Invoke();
        }
        
        public void OnHeartbeatReceived()
        {
            _lastHeartbeatReceived = DateTime.UtcNow;
            Logger.Debug("[ConnectionManager] Heartbeat received");
        }
        
        // ============================================
        // CONNECTION FLOW
        // ============================================
        
        private async void HandleDeviceDiscovered(DeviceInfo device)
        {
            _discoveryService.OnDeviceDiscovered -= HandleDeviceDiscovered;
            _discoveryService.StopDiscovery();
            
            _currentDevice = device;
            
            Logger.Info($"[ConnectionManager] Device discovered: {device.deviceName} @ {device.ip}");
            
            await ConnectToDevice(device);
        }
        
        private async Task ConnectToDevice(DeviceInfo device)
        {
            ChangeState(ConnectionState.Connecting);
            
            Logger.Info($"[ConnectionManager] Connecting to {device.ip}:{device.controlPort}");
            
            bool success = await _tcpService.ConnectAsync(device.ip, device.controlPort);
            
            if (success)
            {
                Logger.Info("[ConnectionManager] TCP connection established");
                
                StartHeartbeat();
                
                _sessionId = Guid.NewGuid().ToString();
                
                ChangeState(ConnectionState.Connected);
                OnConnected?.Invoke();
                
                _reconnectAttempts = 0;
            }
            else
            {
                Logger.Error("[ConnectionManager] TCP connection failed");
                ChangeState(ConnectionState.Disconnected);
                OnDisconnected?.Invoke();
            }
        }
        
        // ============================================
        // RECONNECTION LOGIC
        // ============================================
        
        private void HandleTcpDisconnected()
        {
            if (_state == ConnectionState.Disconnected)
            {
                return;
            }
            
            Logger.Warning("[ConnectionManager] TCP connection lost");
            StartReconnection();
        }
        
        public async void StartReconnection()
        {
            if (_state == ConnectionState.Reconnecting)
            {
                return;
            }
            
            Logger.Warning("[ConnectionManager] Starting reconnection process");
            
            StopHeartbeat();
            ChangeState(ConnectionState.Reconnecting);
            OnReconnecting?.Invoke();
            
            while (_reconnectAttempts < _maxReconnectAttempts)
            {
                Logger.Info($"[ConnectionManager] Reconnect attempt {_reconnectAttempts + 1}/{_maxReconnectAttempts}");
                
                bool success = await AttemptReconnect();
                
                if (success)
                {
                    Logger.Info("[ConnectionManager] Reconnection successful");
                    _reconnectAttempts = 0;
                    
                    StartHeartbeat();
                    ChangeState(ConnectionState.Connected);
                    OnReconnected?.Invoke();
                    
                    await SyncStateAfterReconnect();
                    return;
                }
                
                float delay = _retryDelays[Mathf.Min(_reconnectAttempts, _retryDelays.Length - 1)];
                Logger.Debug($"[ConnectionManager] Waiting {delay}s before next attempt");
                
                await Task.Delay(TimeSpan.FromSeconds(delay));
                
                _reconnectAttempts++;
            }
            
            Logger.Error("[ConnectionManager] Reconnection failed - max attempts exceeded");
            ChangeState(ConnectionState.Failed);
            OnReconnectionFailed?.Invoke();
        }
        
        private async Task<bool> AttemptReconnect()
        {
            try
            {
                if (_currentDevice.ip != null)
                {
                    Logger.Debug($"[ConnectionManager] Attempting TCP reconnect to {_currentDevice.ip}");
                    
                    bool tcpOk = await _tcpService.ConnectAsync(_currentDevice.ip, _currentDevice.controlPort);
                    
                    if (tcpOk)
                    {
                        Logger.Info("[ConnectionManager] TCP reconnected");
                        await SendReconnectCommand();
                        return true;
                    }
                }
                
                Logger.Debug("[ConnectionManager] TCP failed, attempting rediscovery");
                
                var device = await DiscoverDevice(timeout: 5f);
                
                if (device.HasValue)
                {
                    _currentDevice = device.Value;
                    
                    bool tcpOk = await _tcpService.ConnectAsync(device.Value.ip, device.Value.controlPort);
                    
                    if (tcpOk)
                    {
                        Logger.Info("[ConnectionManager] Reconnected via rediscovery");
                        await SendReconnectCommand();
                        return true;
                    }
                }
                
                return false;
            }
            catch (Exception e)
            {
                Logger.Error($"[ConnectionManager] Reconnect attempt failed: {e.Message}", e);
                return false;
            }
        }
        
        private async Task<DeviceInfo?> DiscoverDevice(float timeout)
        {
            DeviceInfo? discoveredDevice = null;
            
            void OnDeviceFound(DeviceInfo device)
            {
                discoveredDevice = device;
            }
            
            _discoveryService.OnDeviceDiscovered += OnDeviceFound;
            _discoveryService.StartDiscovery();
            
            float elapsed = 0f;
            while (elapsed < timeout && !discoveredDevice.HasValue)
            {
                await Task.Delay(100);
                elapsed += 0.1f;
            }
            
            _discoveryService.OnDeviceDiscovered -= OnDeviceFound;
            _discoveryService.StopDiscovery();
            
            return discoveredDevice;
        }
        
        private async Task SendReconnectCommand()
        {
            await _tcpService.SendCommandAsync("RECONNECT", new
            {
                sessionId = _sessionId,
                timestamp = DateTime.UtcNow.Ticks
            });
            
            Logger.Debug($"[ConnectionManager] Sent RECONNECT command (sessionId: {_sessionId})");
        }
        
        private async Task SyncStateAfterReconnect()
        {
            await _tcpService.SendCommandAsync("REQUEST_STATE_SYNC", null);
            
            Logger.Debug("[ConnectionManager] Requested state sync");
        }
        
        // ============================================
        // HEARTBEAT
        // ============================================
        
        private async void StartHeartbeat()
        {
            if (_heartbeatRunning) return;
            
            _heartbeatRunning = true;
            _lastHeartbeatReceived = DateTime.UtcNow;
            
            Logger.Debug("[ConnectionManager] Heartbeat started");
            
            while (_heartbeatRunning && _state == ConnectionState.Connected)
            {
                try
                {
                    await _tcpService.SendCommandAsync("HEARTBEAT", new
                    {
                        timestamp = DateTime.UtcNow.Ticks
                    });
                    
                    _lastHeartbeatSent = DateTime.UtcNow;
                    Logger.Debug("[ConnectionManager] Heartbeat sent");
                }
                catch (Exception e)
                {
                    Logger.Warning($"[ConnectionManager] Heartbeat send failed: {e.Message}");
                }
                
                await Task.Delay(TimeSpan.FromSeconds(_heartbeatInterval));
            }
        }
        
        private void StopHeartbeat()
        {
            _heartbeatRunning = false;
            Logger.Debug("[ConnectionManager] Heartbeat stopped");
        }
        
        // ============================================
        // STATE MANAGEMENT
        // ============================================
        
        private void ChangeState(ConnectionState newState)
        {
            if (_state == newState) return;
            
            var oldState = _state;
            _state = newState;
            
            if (_logStateChanges)
            {
                Logger.Info($"[ConnectionManager] State: {oldState} → {newState}");
            }
            
            OnStateChanged?.Invoke(oldState, newState);
        }
        
        // ============================================
        // DEBUG
        // ============================================
        
#if UNITY_EDITOR
        [ContextMenu("Force Reconnect")]
        private void ForceReconnect()
        {
            StartReconnection();
        }
        
        [ContextMenu("Log Status")]
        private void LogStatus()
        {
            Logger.Info(
                $"[ConnectionManager] Status:\n" +
                $"State: {_state}\n" +
                $"Device: {_currentDevice.deviceName} @ {_currentDevice.ip}\n" +
                $"Session ID: {_sessionId}\n" +
                $"Reconnect attempts: {_reconnectAttempts}/{_maxReconnectAttempts}\n" +
                $"Last heartbeat: {(DateTime.UtcNow - _lastHeartbeatReceived).TotalSeconds:F1}s ago"
            );
        }
#endif
    }
    
    // ============================================
    // ENUMS
    // ============================================
    
    public enum ConnectionState
    {
        Disconnected,
        Discovering,
        Connecting,
        Connected,
        Reconnecting,
        Failed
    }
}
