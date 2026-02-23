using System;
using System.Collections;
using UnityEngine;
using Unity.WebRTC;

namespace TheraplyCore.Streaming
{
    /// <summary>
    /// Media (video + audio) streaming service using Unity WebRTC
    /// Hardware-accelerated H.264 encoding with peer-to-peer streaming
    /// 
    /// REQUIRES: com.unity.webrtc package (install via Package Manager)
    /// 
    /// FEATURES:
    /// - Hardware H.264/VP8/VP9 encoding
    /// - Real-time P2P streaming (sub-50ms latency)
    /// - Automatic codec selection
    /// - NAT traversal with STUN/TURN support
    /// - Built-in error recovery
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class MediaStreamService : MonoBehaviour
    {
        // ============================================
        // CONFIGURATION
        // ============================================
        
        [Header("Camera Settings")]
        [SerializeField] private Camera _sourceCamera;
        [Tooltip("Auto-detect if not assigned")]
        [SerializeField] private bool _autoDetectCamera = false;

        [Header("Audio Settings")]
        [SerializeField] private bool _sendQuestAudio = false;
        [SerializeField] private AudioListener _sourceAudioListener;
        [SerializeField] private bool _receiveTherapistVoice = true;
        
        [Header("Streaming Settings")]
        [SerializeField] private int _streamWidth = 1280;
        [SerializeField] private int _streamHeight = 720;
        [SerializeField] private int _targetFps = 30;
        [SerializeField] private int _targetBitrate = 2500000; // 2.5 Mbps
        
        [Header("WebRTC Settings")]
        [SerializeField] private string[] _stunServers = new string[] 
        { 
            "stun:stun.l.google.com:19302",
            "stun:stun1.l.google.com:19302"
        };
        [SerializeField] private bool _preferLanFirst = true;
        [SerializeField] private float _lanProbeTimeoutSeconds = 4f;
        
        [Header("Debug")]
        [SerializeField] private bool _logStats = true;
        [SerializeField] private bool _logVerbose = false;
        
        // ============================================
        // STATE
        // ============================================
        
        private RTCPeerConnection _peerConnection;
        private MediaStream _mediaStream;
        private VideoStreamTrack _videoTrack;
        private AudioStreamTrack _audioTrack;
        private RenderTexture _renderTexture;
        private Camera _captureCamera;
        private GameObject _captureCameraGo;
        private AudioStreamTrack _remoteAudioTrack;
        private AudioSource _remoteVoiceAudioSource;
        private GameObject _remoteVoiceAudioGo;
        
        private bool _isStreaming = false;
        private bool _isLanOnlyModeActive = false;
        private bool _stunFallbackRequestedForCurrentPeer = false;
        private Coroutine _lanFallbackProbeCoroutine;

        private Coroutine _webrtcUpdateCoroutine;
        private int _framesSent = 0;
        
        /// <summary>Fired when an ICE candidate is generated; signaling layer sends it to the client via TCP.</summary>
        public event Action<RTCIceCandidate> OnIceCandidateGenerated;
        /// <summary>Raised when LAN-only ICE path failed and signaling should renegotiate with STUN.</summary>
        public event Action<string> OnStunFallbackRequested;
        
        // ============================================
        // LIFECYCLE
        // ============================================
        
        private void Awake()
        {
            _webrtcUpdateCoroutine = StartCoroutine(WebRTC.Update());
        }
        
        private void Start()
        {
            StartCoroutine(DelayedInitAndStartStreaming());
        }
        
        private IEnumerator DelayedInitAndStartStreaming()
        {
            yield return new WaitForSeconds(1f);
            if (_sourceCamera == null && _autoDetectCamera)
                _sourceCamera = FindVRCamera();
            if (_sourceCamera == null)
            {
                Debug.LogError("[MediaStreamService] No camera found! Video streaming disabled.");
                enabled = false;
                yield break;
            }
            Debug.Log($"[MediaStreamService] Camera assigned: {_sourceCamera.name}");
            InitializeCapture();
            // WebRTC streaming starts when client connects (StartStreaming + signaling)
        }
        
        private void OnEnable()
        {
            // StartStreaming is called from DelayedInitAndStartStreaming to avoid blocking main thread on Play
        }
        
        private void OnDisable()
        {
            StopStreaming();
        }
        
        private void OnDestroy()
        {
            if (_webrtcUpdateCoroutine != null) StopCoroutine(_webrtcUpdateCoroutine);
            Cleanup();
        }
        
        private void Update()
        {
            // Process WebRTC events
            if (_isStreaming && _peerConnection != null)
            {
                // Update peer connection state
            }
        }

        private void LateUpdate()
        {
            // Keep dedicated capture camera synced with XR head camera pose.
            if (_captureCamera != null && _sourceCamera != null)
            {
                Transform src = _sourceCamera.transform;
                Transform dst = _captureCamera.transform;
                dst.position = src.position;
                dst.rotation = src.rotation;
                _captureCamera.fieldOfView = _sourceCamera.fieldOfView;
            }
        }
        
        // ============================================
        // CAMERA DETECTION
        // ============================================
        
        private Camera FindVRCamera()
        {
            // 1. Try Main Camera tag
            GameObject mainCameraGO = GameObject.FindGameObjectWithTag("MainCamera");
            if (mainCameraGO != null)
            {
                Camera cam = mainCameraGO.GetComponent<Camera>();
                if (cam != null && cam.enabled)
                {
                    Debug.Log("[MediaStreamService] Found Main Camera");
                    return cam;
                }
            }
            
            // 2. Try OVRCameraRig (Quest)
            Camera ovrCamera = FindOVRCamera();
            if (ovrCamera != null)
            {
                Debug.Log("[MediaStreamService] Found OVRCameraRig camera");
                return ovrCamera;
            }
            
            // 3. Fallback: Any active camera
            Camera[] allCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (Camera cam in allCameras)
            {
                if (cam.enabled && cam.gameObject.activeInHierarchy)
                {
                    Debug.LogWarning($"[MediaStreamService] Using fallback camera: {cam.name}");
                    return cam;
                }
            }
            
            return null;
        }
        
        private Camera FindOVRCamera()
        {
            // Look for OVRCameraRig/TrackingSpace/CenterEyeAnchor
            GameObject centerEye = GameObject.Find("OVRCameraRig/TrackingSpace/CenterEyeAnchor");
            if (centerEye != null)
            {
                Camera cam = centerEye.GetComponent<Camera>();
                if (cam != null && cam.enabled)
                {
                    return cam;
                }
            }
            
            // Alternative: Search by name
            GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (GameObject obj in allObjects)
            {
                if (obj.name == "CenterEyeAnchor")
                {
                    Camera cam = obj.GetComponent<Camera>();
                    if (cam != null && cam.enabled)
                    {
                        return cam;
                    }
                }
            }
            
            return null;
        }
        
        // ============================================
        // CAPTURE INITIALIZATION
        // ============================================
        
        private void InitializeCapture()
        {
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }

            // Create RenderTexture for camera output
            _renderTexture = new RenderTexture(_streamWidth, _streamHeight, 24, RenderTextureFormat.BGRA32);
            _renderTexture.name = "VideoStreamRenderTexture";
            _renderTexture.Create();

            // XR cameras often produce black frames when targetTexture is assigned directly.
            // Use a dedicated monoscopic capture camera for streaming.
            bool sourceIsStereo = _sourceCamera.stereoTargetEye != StereoTargetEyeMask.None;
            if (sourceIsStereo)
            {
                _captureCameraGo = new GameObject("TheraplyCaptureCamera");
                _captureCameraGo.transform.SetParent(_sourceCamera.transform.parent, worldPositionStays: false);
                _captureCamera = _captureCameraGo.AddComponent<Camera>();
                _captureCamera.CopyFrom(_sourceCamera);
                _captureCamera.stereoTargetEye = StereoTargetEyeMask.None;
                _captureCamera.targetTexture = _renderTexture;
                _captureCamera.enabled = true;

                Debug.Log($"[MediaStreamService] Using dedicated capture camera for XR source '{_sourceCamera.name}'");
            }
            else
            {
                _sourceCamera.targetTexture = _renderTexture;
            }
            
            Debug.Log($"[MediaStreamService] Initialized capture: {_streamWidth}x{_streamHeight} @ {_targetFps}fps");
            Debug.Log($"[MediaStreamService] RenderTexture source camera: {(_captureCamera != null ? _captureCamera.name : _sourceCamera.name)}");
        }
        
        // ============================================
        // WEBRTC STREAMING
        // ============================================
        
        /// <summary>Start WebRTC streaming (creates peer connection). Signaling sends offer when client is connected.</summary>
        public void StartStreaming(bool forceStun = false)
        {
            if (_isStreaming) return;
            if (_sourceCamera == null || _renderTexture == null)
            {
                Debug.LogError("[MediaStreamService] Cannot start streaming - no camera or render texture!");
                return;
            }
            try
            {
                _videoTrack = new VideoStreamTrack(_renderTexture);
                _mediaStream = new MediaStream();
                _mediaStream.AddTrack(_videoTrack);

                if (_sendQuestAudio)
                {
                    if (_sourceAudioListener == null)
                    {
                        _sourceAudioListener = _sourceCamera != null
                            ? _sourceCamera.GetComponent<AudioListener>()
                            : null;
                    }
                    if (_sourceAudioListener == null)
                    {
                        _sourceAudioListener = FindFirstObjectByType<AudioListener>();
                    }

                    if (_sourceAudioListener != null)
                    {
                        _audioTrack = new AudioStreamTrack(_sourceAudioListener);
                        _audioTrack.Loopback = false;
                        _mediaStream.AddTrack(_audioTrack);
                        Debug.Log($"[MediaStreamService] Audio send enabled from listener: {_sourceAudioListener.name}");
                    }
                    else
                    {
                        Debug.LogWarning("[MediaStreamService] Audio send enabled but no AudioListener found.");
                    }
                }

                _stunFallbackRequestedForCurrentPeer = false;
                _isLanOnlyModeActive = _preferLanFirst && !forceStun;
                var config = BuildIceConfiguration(_isLanOnlyModeActive);
                _peerConnection = new RTCPeerConnection(ref config);
                foreach (var track in _mediaStream.GetTracks())
                    _peerConnection.AddTrack(track, _mediaStream);
                _peerConnection.OnIceCandidate = OnIceCandidate;
                _peerConnection.OnIceConnectionChange = OnIceConnectionChange;
                _peerConnection.OnConnectionStateChange = OnConnectionStateChange;
                _peerConnection.OnTrack = OnTrack;
                _isStreaming = true;
                _framesSent = 0;
                if (_isLanOnlyModeActive)
                {
                    Debug.Log("[MediaStreamService] LAN-first mode active (host ICE only, STUN fallback armed).");
                    StartLanFallbackProbe();
                }
                else
                {
                    Debug.Log("[MediaStreamService] STUN mode active (direct + server reflexive ICE candidates).");
                }
                Debug.Log("[MediaStreamService] ✅ WebRTC streaming started (offer will be sent by signaling)");
                if (_logStats) StartCoroutine(LogStatistics());
            }
            catch (Exception e)
            {
                Debug.LogError($"[MediaStreamService] Failed to start streaming: {e.Message}");
            }
        }
        
        public void StopStreaming()
        {
            if (!_isStreaming) return;
            StopLanFallbackProbe();
            _isStreaming = false;
            _isLanOnlyModeActive = false;
            _stunFallbackRequestedForCurrentPeer = false;
            if (_peerConnection != null)
            {
                _peerConnection.Close();
                _peerConnection.Dispose();
                _peerConnection = null;
            }
            if (_videoTrack != null)
            {
                _videoTrack.Dispose();
                _videoTrack = null;
            }
            if (_audioTrack != null)
            {
                _audioTrack.Dispose();
                _audioTrack = null;
            }
            _remoteAudioTrack = null;
            if (_mediaStream != null)
            {
                _mediaStream.Dispose();
                _mediaStream = null;
            }
            if (_remoteVoiceAudioSource != null)
            {
                _remoteVoiceAudioSource.Stop();
                _remoteVoiceAudioSource.clip = null;
                _remoteVoiceAudioSource = null;
            }
            if (_remoteVoiceAudioGo != null)
            {
                Destroy(_remoteVoiceAudioGo);
                _remoteVoiceAudioGo = null;
            }
            Debug.Log("[MediaStreamService] ⏸️ Streaming stopped");
        }
        
        // ============================================
        // WEBRTC SIGNALING
        // ============================================
        
        /// <summary>
        /// Create offer for peer connection
        /// Send this offer to Flutter client via TCP
        /// </summary>
        public IEnumerator CreateOffer(Action<RTCSessionDescription> onOfferCreated)
        {
            if (_peerConnection == null)
            {
                Debug.LogError("[MediaStreamService] Cannot create offer - no peer connection!");
                yield break;
            }
            
            var offerOptions = new RTCOfferAnswerOptions
            {
                iceRestart = false
            };
            
            var op = _peerConnection.CreateOffer(ref offerOptions);
            yield return op;
            
            if (op.IsError)
            {
                Debug.LogError($"[MediaStreamService] Failed to create offer: {op.Error.message}");
                yield break;
            }
            
            var offer = op.Desc;
            var setLocalOp = _peerConnection.SetLocalDescription(ref offer);
            yield return setLocalOp;
            
            if (setLocalOp.IsError)
            {
                Debug.LogError($"[MediaStreamService] Failed to set local description: {setLocalOp.Error.message}");
                yield break;
            }
            
            if (_logVerbose)
            {
                Debug.Log($"[MediaStreamService] Created offer: {offer.sdp}");
            }
            else
            {
                Debug.Log("[MediaStreamService] ✅ Created WebRTC offer");
            }
            
            onOfferCreated?.Invoke(offer);
        }
        
        /// <summary>
        /// Set remote answer from Flutter client
        /// Call this after receiving answer via TCP
        /// </summary>
        public IEnumerator SetRemoteAnswer(RTCSessionDescription answer)
        {
            if (_peerConnection == null)
            {
                Debug.LogError("[MediaStreamService] Cannot set answer - no peer connection!");
                yield break;
            }
            
            var op = _peerConnection.SetRemoteDescription(ref answer);
            yield return op;
            
            if (op.IsError)
            {
                Debug.LogError($"[MediaStreamService] Failed to set remote description: {op.Error.message}");
                yield break;
            }
            
            Debug.Log("[MediaStreamService] ✅ Remote answer set - connection establishing...");
        }
        
        /// <summary>
        /// Add ICE candidate from Flutter client
        /// </summary>
        public void AddIceCandidate(RTCIceCandidate candidate)
        {
            if (_peerConnection == null)
            {
                Debug.LogWarning("[MediaStreamService] Cannot add ICE candidate - no peer connection!");
                return;
            }
            
            _peerConnection.AddIceCandidate(candidate);
            
            if (_logVerbose)
            {
                Debug.Log($"[MediaStreamService] Added ICE candidate: {candidate.Candidate}");
            }
        }
        
        // ============================================
        // WEBRTC EVENT HANDLERS
        // ============================================
        
        private void OnIceCandidate(RTCIceCandidate candidate)
        {
            if (_logVerbose) Debug.Log($"[MediaStreamService] ICE Candidate: {candidate.Candidate}");
            OnIceCandidateGenerated?.Invoke(candidate);
        }

        private RTCConfiguration BuildIceConfiguration(bool lanOnlyMode)
        {
            if (lanOnlyMode || _stunServers == null || _stunServers.Length == 0)
            {
                return new RTCConfiguration();
            }

            return new RTCConfiguration
            {
                iceServers = new RTCIceServer[] { new RTCIceServer { urls = _stunServers } }
            };
        }

        private void StartLanFallbackProbe()
        {
            StopLanFallbackProbe();

            if (!_isLanOnlyModeActive || _lanProbeTimeoutSeconds <= 0f)
            {
                return;
            }

            _lanFallbackProbeCoroutine = StartCoroutine(LanFallbackProbeTimeout());
        }

        private void StopLanFallbackProbe()
        {
            if (_lanFallbackProbeCoroutine == null)
            {
                return;
            }

            StopCoroutine(_lanFallbackProbeCoroutine);
            _lanFallbackProbeCoroutine = null;
        }

        private IEnumerator LanFallbackProbeTimeout()
        {
            yield return new WaitForSeconds(_lanProbeTimeoutSeconds);
            _lanFallbackProbeCoroutine = null;

            if (!_isStreaming || !_isLanOnlyModeActive)
            {
                yield break;
            }

            if (IsConnectionEstablished())
            {
                yield break;
            }

            RequestStunFallback("LAN_PROBE_TIMEOUT");
        }

        private bool IsConnectionEstablished()
        {
            if (_peerConnection == null)
            {
                return false;
            }

            return _peerConnection.ConnectionState == RTCPeerConnectionState.Connected ||
                   _peerConnection.IceConnectionState == RTCIceConnectionState.Connected ||
                   _peerConnection.IceConnectionState == RTCIceConnectionState.Completed;
        }

        private void RequestStunFallback(string reasonCode)
        {
            if (!_isStreaming || !_isLanOnlyModeActive || _stunFallbackRequestedForCurrentPeer)
            {
                return;
            }

            if (_stunServers == null || _stunServers.Length == 0)
            {
                Debug.LogWarning($"[MediaStreamService] STUN fallback requested ({reasonCode}), but no STUN servers configured.");
                return;
            }

            _stunFallbackRequestedForCurrentPeer = true;
            StopLanFallbackProbe();
            Debug.LogWarning($"[MediaStreamService] LAN-first attempt failed ({reasonCode}). Requesting STUN fallback renegotiation.");
            OnStunFallbackRequested?.Invoke(reasonCode);
        }
        
        private void OnIceConnectionChange(RTCIceConnectionState state)
        {
            Debug.Log($"[MediaStreamService] ICE Connection State: {state}");
            
            switch (state)
            {
                case RTCIceConnectionState.Connected:
                case RTCIceConnectionState.Completed:
                    StopLanFallbackProbe();
                    Debug.Log("[MediaStreamService] ✅ ICE Connected - streaming active!");
                    break;
                case RTCIceConnectionState.Disconnected:
                    Debug.LogWarning("[MediaStreamService] ⚠️ ICE Disconnected");
                    if (_isLanOnlyModeActive && !_stunFallbackRequestedForCurrentPeer)
                    {
                        StartLanFallbackProbe();
                    }
                    break;
                case RTCIceConnectionState.Failed:
                    Debug.LogError("[MediaStreamService] ❌ ICE Failed - connection lost");
                    RequestStunFallback("ICE_FAILED");
                    break;
            }
        }
        
        private void OnConnectionStateChange(RTCPeerConnectionState state)
        {
            Debug.Log($"[MediaStreamService] Peer Connection State: {state}");
            
            switch (state)
            {
                case RTCPeerConnectionState.Connected:
                    StopLanFallbackProbe();
                    Debug.Log("[MediaStreamService] 🎥 Video streaming LIVE!");
                    break;
                case RTCPeerConnectionState.Failed:
                    Debug.LogError("[MediaStreamService] ❌ Peer connection failed");
                    RequestStunFallback("PEER_FAILED");
                    break;
                case RTCPeerConnectionState.Closed:
                    Debug.Log("[MediaStreamService] Connection closed");
                    break;
            }
        }

        private void OnTrack(RTCTrackEvent e)
        {
            if (!_receiveTherapistVoice || e.Track == null)
            {
                return;
            }

            if (e.Track is AudioStreamTrack incomingAudioTrack)
            {
                _remoteAudioTrack = incomingAudioTrack;

                if (_remoteVoiceAudioSource == null)
                {
                    if (_remoteVoiceAudioGo == null)
                    {
                        _remoteVoiceAudioGo = new GameObject("TheraplyRemoteVoiceAudio");
                        _remoteVoiceAudioGo.transform.SetParent(transform, worldPositionStays: false);
                        _remoteVoiceAudioGo.transform.localPosition = Vector3.zero;
                    }

                    _remoteVoiceAudioSource = _remoteVoiceAudioGo.GetComponent<AudioSource>();
                    if (_remoteVoiceAudioSource == null)
                    {
                        _remoteVoiceAudioSource = _remoteVoiceAudioGo.AddComponent<AudioSource>();
                    }
                    _remoteVoiceAudioSource.playOnAwake = false;
                    _remoteVoiceAudioSource.loop = true;
                    _remoteVoiceAudioSource.spatialBlend = 0f;
                    _remoteVoiceAudioSource.volume = 1f;
                }

                _remoteVoiceAudioSource.SetTrack(incomingAudioTrack);
                Debug.Log("[MediaStreamService] 🎤 Therapist voice track connected.");
            }
        }
        
        // ============================================
        // STATISTICS
        // ============================================
        
        private IEnumerator LogStatistics()
        {
            while (_isStreaming)
            {
                yield return new WaitForSeconds(5f);
                
                if (_peerConnection != null)
                {
                    var stats = _peerConnection.GetStats();
                    
                    // Log basic stats
                    Debug.Log($"[MediaStreamService] Stats: Connection={_peerConnection.ConnectionState}, ICE={_peerConnection.IceConnectionState}");
                    
                    // TODO: Parse detailed stats (bitrate, packet loss, etc.)
                }
            }
        }
        
        // ============================================
        // CLEANUP
        // ============================================
        
        private void Cleanup()
        {
            StopStreaming();
            
            if (_sourceCamera != null)
            {
                _sourceCamera.targetTexture = null;
            }

            if (_captureCamera != null)
            {
                _captureCamera.targetTexture = null;
                _captureCamera = null;
            }

            if (_captureCameraGo != null)
            {
                Destroy(_captureCameraGo);
                _captureCameraGo = null;
            }
            
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }
            Debug.Log("[MediaStreamService] Cleanup complete");
        }
        
        // ============================================
        // PUBLIC API
        // ============================================
        
        public bool IsStreaming => _isStreaming;
        public bool IsLanOnlyMode => _isLanOnlyModeActive;
        public Camera SourceCamera => _sourceCamera;
        public RTCPeerConnection PeerConnection => _peerConnection;
        public int FramesSent => _framesSent;
        
        /// <summary>
        /// Manually assign source camera (for testing)
        /// </summary>
        public void SetSourceCamera(Camera camera)
        {
            if (_sourceCamera != null)
            {
                _sourceCamera.targetTexture = null;
            }

            if (_captureCamera != null)
            {
                _captureCamera.targetTexture = null;
                _captureCamera = null;
            }

            if (_captureCameraGo != null)
            {
                Destroy(_captureCameraGo);
                _captureCameraGo = null;
            }
            
            _sourceCamera = camera;
            
            if (_renderTexture != null && _sourceCamera != null)
            {
                // Rebuild capture route when source changes.
                InitializeCapture();
                Debug.Log($"[MediaStreamService] Camera changed to: {_sourceCamera.name}");
            }
        }
    }
}
