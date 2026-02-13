using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections;
using UnityEngine;

namespace TheraplyCore.Network.Discovery
{
    /// <summary>
    /// UDP-based device discovery service
    /// Quest broadcasts presence, therapist controller listens
    /// 
    /// ARCHITECTURE:
    /// - Quest: Broadcasts DeviceInfo every 2 seconds on port 8767
    /// - Controller: Listens on port 8767, discovers available devices
    /// 
    /// MESSAGE FORMAT: JSON (DeviceInfo struct)
    /// </summary>
    public class UDPDiscoveryService : MonoBehaviour
    {
        // ============================================
        // CONFIGURATION
        // ============================================
        
        [Header("Discovery Settings")]
        [Tooltip("Port for UDP broadcast/listen")]
        [SerializeField] private int _discoveryPort = 8767;
        
        [Tooltip("Interval between broadcasts (seconds)")]
        [SerializeField] private float _broadcastInterval = 2.0f;
        
        [Tooltip("Device info timeout (seconds) - mark device as offline")]
        [SerializeField] private float _deviceTimeout = 10.0f;
        
        [Header("Device Info")]
        [Tooltip("Student ID from Firebase (cached locally)")]
        [SerializeField] private string _studentId = "";
        
        [Tooltip("Custom device name (default: SystemInfo.deviceName)")]
        [SerializeField] private string _customDeviceName = "";
        
        [Header("Debug")]
        [SerializeField] private bool _logBroadcasts = false;
        [SerializeField] private bool _logReceives = true;
        
        // ============================================
        // STATE
        // ============================================
        
        private UdpClient _udpClient;
        private bool _isRunning = false;
        private DeviceInfo _myDeviceInfo;
        private Coroutine _broadcastCoroutine;
        
        // ============================================
        // EVENTS
        // ============================================
        
        /// <summary>
        /// Fired when a device is discovered or updates its info
        /// </summary>
        public event Action<DeviceInfo> OnDeviceDiscovered;
        
        /// <summary>
        /// Fired when discovery service starts
        /// </summary>
        public event Action OnDiscoveryStarted;
        
        /// <summary>
        /// Fired when discovery service stops
        /// </summary>
        public event Action OnDiscoveryStopped;
        
        // ============================================
        // UNITY LIFECYCLE
        // ============================================
        
        private void Start()
        {
            // Auto-start discovery on Quest
            StartDiscovery();
        }
        
        private void OnDestroy()
        {
            StopDiscovery();
        }
        
        private void OnApplicationPause(bool pause)
        {
            if (pause)
            {
                // Pause broadcasting but keep listening
                if (_broadcastCoroutine != null)
                {
                    StopCoroutine(_broadcastCoroutine);
                    _broadcastCoroutine = null;
                }
            }
            else
            {
                // Resume broadcasting
                if (_isRunning && _broadcastCoroutine == null)
                {
                    _broadcastCoroutine = StartCoroutine(BroadcastLoop());
                }
            }
        }
        
        // ============================================
        // PUBLIC API
        // ============================================
        
        /// <summary>
        /// Start discovery service
        /// Begins broadcasting (if Quest) and listening for devices
        /// </summary>
        public void StartDiscovery()
        {
            if (_isRunning)
            {
                Debug.LogWarning("[UDPDiscovery] Already running");
                return;
            }
            
            try
            {
                // Initialize device info
                _myDeviceInfo = CreateDeviceInfo();
                
                // Create UDP client
                _udpClient = new UdpClient(_discoveryPort);
                _udpClient.EnableBroadcast = true;
                
                // Start broadcasting (Quest only)
#if UNITY_ANDROID && !UNITY_EDITOR
                _broadcastCoroutine = StartCoroutine(BroadcastLoop());
#endif
                
                // Start listening (always)
                StartListening();
                
                _isRunning = true;
                
                Debug.Log($"[UDPDiscovery] Started on port {_discoveryPort}");
                OnDiscoveryStarted?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[UDPDiscovery] Failed to start: {e.Message}");
            }
        }
        
        /// <summary>
        /// Stop discovery service
        /// </summary>
        public void StopDiscovery()
        {
            if (!_isRunning) return;
            
            _isRunning = false;
            
            // Stop broadcasting
            if (_broadcastCoroutine != null)
            {
                StopCoroutine(_broadcastCoroutine);
                _broadcastCoroutine = null;
            }
            
            // Close UDP client
            if (_udpClient != null)
            {
                _udpClient.Close();
                _udpClient = null;
            }
            
            Debug.Log("[UDPDiscovery] Stopped");
            OnDiscoveryStopped?.Invoke();
        }
        
        /// <summary>
        /// Update student ID (e.g., after Firebase login)
        /// </summary>
        public void SetStudentId(string studentId)
        {
            _studentId = studentId;
            
            if (_isRunning)
            {
                // Refresh device info
                _myDeviceInfo = CreateDeviceInfo();
            }
        }
        
        /// <summary>
        /// Get current device info
        /// </summary>
        public DeviceInfo GetMyDeviceInfo()
        {
            return _myDeviceInfo;
        }
        
        // ============================================
        // BROADCASTING (Quest)
        // ============================================
        
        private IEnumerator BroadcastLoop()
        {
            IPEndPoint broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, _discoveryPort);
            
            while (_isRunning)
            {
                try
                {
                    // Update timestamp
                    _myDeviceInfo.timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    
                    // Serialize with JSON
                    string jsonString = JsonUtility.ToJson(_myDeviceInfo);
                    byte[] data = System.Text.Encoding.UTF8.GetBytes(jsonString);
                    
                    // Broadcast
                    _udpClient.Send(data, data.Length, broadcastEndpoint);
                    
                    if (_logBroadcasts)
                    {
                        Debug.Log($"[UDPDiscovery] Broadcast: {_myDeviceInfo.deviceName} @ {_myDeviceInfo.ip}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[UDPDiscovery] Broadcast error: {e.Message}");
                }
                
                yield return new WaitForSeconds(_broadcastInterval);
            }
        }
        
        // ============================================
        // LISTENING (Controller)
        // ============================================
        
        private async void StartListening()
        {
            try
            {
                while (_isRunning && _udpClient != null)
                {
                    // Receive data asynchronously
                    UdpReceiveResult result = await _udpClient.ReceiveAsync();
                    
                    // Parse device info
                    try
                    {
                        string jsonString = System.Text.Encoding.UTF8.GetString(result.Buffer);
                        DeviceInfo deviceInfo = JsonUtility.FromJson<DeviceInfo>(jsonString);
                        
                        // Ignore messages from self (check device ID)
                        if (deviceInfo.deviceId == _myDeviceInfo.deviceId)
                        {
                            continue;
                        }
                        
                        if (_logReceives)
                        {
                            Debug.Log($"[UDPDiscovery] Discovered: {deviceInfo.deviceName} @ {deviceInfo.ip}");
                        }
                        
                        // Fire event
                        OnDeviceDiscovered?.Invoke(deviceInfo);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[UDPDiscovery] Failed to parse message: {e.Message}");
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // UDP client was closed, normal shutdown
            }
            catch (Exception e)
            {
                Debug.LogError($"[UDPDiscovery] Listen error: {e.Message}");
            }
        }
        
        // ============================================
        // HELPERS
        // ============================================
        
        private DeviceInfo CreateDeviceInfo()
        {
            return new DeviceInfo
            {
                deviceId = SystemInfo.deviceUniqueIdentifier,
                deviceName = string.IsNullOrEmpty(_customDeviceName) 
                    ? SystemInfo.deviceName 
                    : _customDeviceName,
                ip = GetLocalIPAddress(),
                controlPort = 8080,
                videoPort = 8081,
                audioPort = 8082,
                studentId = _studentId,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
        }
        
        private string GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UDPDiscovery] Failed to get IP: {e.Message}");
            }
            
            return "0.0.0.0";
        }
    }
    
    // ============================================
    // DATA STRUCTURES
    // ============================================
    
    /// <summary>
    /// Device information broadcasted via UDP
    /// Serialized with JSON for simplicity
    /// </summary>
    [Serializable]
    public struct DeviceInfo
    {
        public string deviceId;       // Unique device identifier
        public string deviceName;     // Human-readable name
        public string ip;             // Local IP address
        public int controlPort;       // TCP control port (8080)
        public int videoPort;         // UDP video port (8081)
        public int audioPort;         // UDP audio port (8082)
        public string studentId;      // Firebase student ID (if logged in)
        public long timestamp;        // Unix timestamp
    }
}