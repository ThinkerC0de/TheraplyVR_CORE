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
        [SerializeField] private float _broadcastInterval = 0.5f;
        
        [Tooltip("Device info timeout (seconds) - mark device as offline")]
        [SerializeField] private float _deviceTimeout = 10.0f;
        
        [Header("Device Info")]
        [Tooltip("Student ID from Firebase (cached locally)")]
        [SerializeField] private string _studentId = "";
        
        [Tooltip("Custom device name (default: SystemInfo.deviceName)")]
        [SerializeField] private string _customDeviceName = "";
        
        [Header("Debug")]
        [SerializeField] private bool _logBroadcasts = true;  // Enable for timing diagnosis
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
            // Broadcasting continues during pause/resume
            // Coroutine is already running, no action needed
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
                
                // Set running flag BEFORE starting coroutine (critical!)
                _isRunning = true;
                
                // Start broadcasting
                // Note: Broadcasts on all platforms for testing (Quest + Editor)
                _broadcastCoroutine = StartCoroutine(BroadcastLoop());
                
                // Start listening (always)
                StartListening();
                
                Debug.Log($"[UDPDiscovery] Started on port {_discoveryPort} at {System.DateTime.Now:HH:mm:ss.fff}");
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
            // Calculate proper broadcast address for our network
            string localIP = _myDeviceInfo.ip;
            IPAddress broadcastAddress = CalculateBroadcastAddress(localIP);
            IPEndPoint broadcastEndpoint = new IPEndPoint(broadcastAddress, _discoveryPort);
            
            Debug.Log($"[UDPDiscovery] Broadcasting to {broadcastAddress}:{_discoveryPort}");
            
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
                        Debug.Log($"[UDPDiscovery] Broadcast: {_myDeviceInfo.deviceName} @ {_myDeviceInfo.ip} at {System.DateTime.Now:HH:mm:ss.fff}");
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
                IPAddress bestIP = null;
                
                Debug.Log("[UDPDiscovery] Available network interfaces:");
                
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ipStr = ip.ToString();
                        Debug.Log($"  - {ipStr}");
                        
                        // Skip loopback (127.x.x.x)
                        if (ipStr.StartsWith("127."))
                        {
                            Debug.Log($"    -> Skipped (loopback)");
                            continue;
                        }
                        
                        // Skip APIPA/link-local (169.254.x.x)
                        if (ipStr.StartsWith("169.254."))
                        {
                            Debug.Log($"    -> Skipped (APIPA/link-local)");
                            continue;
                        }
                        
                        // Skip common virtual adapters (100.x.x.x range often used by VPN/virtual adapters)
                        if (ipStr.StartsWith("100."))
                        {
                            Debug.Log($"    -> Skipped (likely virtual adapter)");
                            continue;
                        }
                        
                        // Prefer typical private network ranges:
                        // 192.168.x.x (most common home/office)
                        // 10.x.x.x (corporate)
                        // 172.16-31.x.x (less common)
                        if (ipStr.StartsWith("192.168."))
                        {
                            Debug.Log($"    -> SELECTED (192.168.x.x - typical home/office network)");
                            return ipStr;
                        }
                        
                        if (ipStr.StartsWith("10."))
                        {
                            if (bestIP == null)
                            {
                                bestIP = ip;
                                Debug.Log($"    -> Candidate (10.x.x.x - corporate network)");
                            }
                        }
                        else if (ipStr.StartsWith("172."))
                        {
                            // Check if it's in 172.16.0.0 - 172.31.255.255 range
                            string[] parts = ipStr.Split('.');
                            if (parts.Length >= 2)
                            {
                                int secondOctet = int.Parse(parts[1]);
                                if (secondOctet >= 16 && secondOctet <= 31)
                                {
                                    if (bestIP == null)
                                    {
                                        bestIP = ip;
                                        Debug.Log($"    -> Candidate (172.16-31.x.x - private network)");
                                    }
                                }
                            }
                        }
                        else if (bestIP == null)
                        {
                            // Fallback to any other IPv4 if nothing better found
                            bestIP = ip;
                            Debug.Log($"    -> Fallback candidate");
                        }
                    }
                }
                
                if (bestIP != null)
                {
                    Debug.Log($"[UDPDiscovery] Selected IP: {bestIP}");
                    return bestIP.ToString();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[UDPDiscovery] Failed to get IP: {e.Message}");
            }
            
            Debug.LogError("[UDPDiscovery] No suitable IP address found!");
            return "0.0.0.0";
        }
        
        /// <summary>
        /// Calculate broadcast address for the given local IP
        /// For typical /24 networks (255.255.255.0), this converts:
        /// 192.168.1.100 -> 192.168.1.255
        /// </summary>
        private IPAddress CalculateBroadcastAddress(string localIP)
        {
            try
            {
                string[] parts = localIP.Split('.');
                if (parts.Length == 4)
                {
                    // Assume /24 network (most common for home/office)
                    // Keep first 3 octets, set last to 255
                    string broadcast = $"{parts[0]}.{parts[1]}.{parts[2]}.255";
                    Debug.Log($"[UDPDiscovery] Calculated broadcast: {localIP} -> {broadcast}");
                    return IPAddress.Parse(broadcast);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UDPDiscovery] Failed to calculate broadcast address: {e.Message}");
            }
            
            // Fallback to global broadcast
            Debug.Log("[UDPDiscovery] Using global broadcast (255.255.255.255)");
            return IPAddress.Broadcast;
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