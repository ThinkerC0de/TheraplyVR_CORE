using System;
using System.Collections;
using UnityEngine;
using Unity.WebRTC;
using UnityEngine.Serialization;
using TheraplyCore.Network.Connection;

namespace TheraplyCore.Streaming
{
    /// <summary>
    /// WebRTC signaling when Unity is the TCP server (Quest).
    /// On client connect: keep TCP/session alive and wait for explicit preview resume.
    /// Handles WEBRTC_ANSWER and WEBRTC_ICE_CANDIDATE from Flutter; sends ICE candidates to Flutter.
    /// </summary>
    public class WebRTCServerSignaling : MonoBehaviour
    {
        private const string PreviewPauseCommandId = "WEBRTC_PREVIEW_PAUSE";
        private const string PreviewResumeCommandId = "WEBRTC_PREVIEW_RESUME";
        private const string SetVideoBitrateCommandId = "WEBRTC_SET_VIDEO_BITRATE";
        private const string VideoBitrateStatusCommandId = "WEBRTC_VIDEO_BITRATE_STATUS";

        [Header("Dependencies")]
        [FormerlySerializedAs("_videoStreamService")]
        [SerializeField] private MediaStreamService _mediaStreamService;
        [SerializeField] private TCPServerService _tcpServer;

        [Header("Debug")]
        [SerializeField] private bool _logSignaling = true;

        private bool _isNegotiating;
        private bool _tcpCallbacksRegistered;
        private bool _mediaCallbacksRegistered;
        private Coroutine _deferredNegotiationCoroutine;

        private void Awake()
        {
            ResolveDependencies();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            RegisterCallbacks();
        }

        private void OnDisable()
        {
            UnregisterCallbacks();
        }

        public void Configure(MediaStreamService mediaStreamService, TCPServerService tcpServer)
        {
            var wasActive = isActiveAndEnabled;
            if (wasActive)
            {
                UnregisterCallbacks();
            }

            _mediaStreamService = mediaStreamService;
            _tcpServer = tcpServer;

            ResolveDependencies();

            if (wasActive)
            {
                RegisterCallbacks();
            }
        }

        private void ResolveDependencies()
        {
            if (_mediaStreamService == null) _mediaStreamService = GetComponent<MediaStreamService>();
            if (_mediaStreamService == null) _mediaStreamService = FindFirstObjectByType<MediaStreamService>();

            if (_tcpServer == null) _tcpServer = GetComponent<TCPServerService>();
            if (_tcpServer == null) _tcpServer = FindFirstObjectByType<TCPServerService>();

            if (_tcpServer == null && _logSignaling)
            {
                Debug.LogWarning("[WebRTCServerSignaling] TCPServerService not found yet.");
            }
        }

        private void RegisterCallbacks()
        {
            if (_tcpServer != null && !_tcpCallbacksRegistered)
            {
                _tcpServer.OnClientConnected += HandleClientConnected;
                _tcpServer.OnMessageReceived += HandleTCPMessage;
                _tcpServer.OnClientDisconnected += HandleClientDisconnected;
                _tcpCallbacksRegistered = true;
            }

            if (_mediaStreamService != null && !_mediaCallbacksRegistered)
            {
                _mediaStreamService.OnIceCandidateGenerated += SendICECandidate;
                _mediaStreamService.OnStunFallbackRequested += HandleStunFallbackRequested;
                _mediaCallbacksRegistered = true;
            }
        }

        private void UnregisterCallbacks()
        {
            if (_tcpServer != null && _tcpCallbacksRegistered)
            {
                _tcpServer.OnClientConnected -= HandleClientConnected;
                _tcpServer.OnMessageReceived -= HandleTCPMessage;
                _tcpServer.OnClientDisconnected -= HandleClientDisconnected;
                _tcpCallbacksRegistered = false;
            }

            if (_mediaStreamService != null && _mediaCallbacksRegistered)
            {
                _mediaStreamService.OnIceCandidateGenerated -= SendICECandidate;
                _mediaStreamService.OnStunFallbackRequested -= HandleStunFallbackRequested;
                _mediaCallbacksRegistered = false;
            }
        }

        private void HandleClientConnected(string clientIP)
        {
            ResolveDependencies();
            RegisterCallbacks();
            if (_mediaStreamService == null || _tcpServer == null) return;

            if (_deferredNegotiationCoroutine != null)
            {
                StopCoroutine(_deferredNegotiationCoroutine);
                _deferredNegotiationCoroutine = null;
            }

            _isNegotiating = false;

            if (_logSignaling)
            {
                Debug.Log("[WebRTCServerSignaling] Client connected - waiting for explicit preview resume request.");
            }
        }

        private void HandleClientDisconnected()
        {
            _isNegotiating = false;
            if (_deferredNegotiationCoroutine != null)
            {
                StopCoroutine(_deferredNegotiationCoroutine);
                _deferredNegotiationCoroutine = null;
            }
            if (_mediaStreamService != null) _mediaStreamService.StopStreaming();
            if (_logSignaling) Debug.Log("[WebRTCServerSignaling] Client disconnected, stopped stream");
        }

        private IEnumerator CreateOfferAndSendInternal(string reasonCode)
        {
            if (_logSignaling) Debug.Log($"[WebRTCServerSignaling] Creating WebRTC offer ({reasonCode})...");

            yield return _mediaStreamService.CreateOffer(offer =>
            {
                var offerData = new WebRTCOfferMessage
                {
                    type = "offer",
                    sdp = offer.sdp,
                    iceMode = _mediaStreamService != null && !_mediaStreamService.IsLanOnlyMode ? "STUN" : "LAN",
                    reasonCode = reasonCode
                };
                _ = _tcpServer.SendCommandAsync("WEBRTC_OFFER", offerData);
                if (_logSignaling) Debug.Log($"[WebRTCServerSignaling] Sent WEBRTC_OFFER to client ({offer.sdp.Length} chars)");
            });
        }

        private void HandleStunFallbackRequested(string reasonCode)
        {
            if (_mediaStreamService == null || _tcpServer == null)
            {
                return;
            }

            if (!_tcpServer.HasClient)
            {
                if (_logSignaling)
                {
                    Debug.LogWarning($"[WebRTCServerSignaling] Ignoring STUN fallback ({reasonCode}) - no active client.");
                }
                return;
            }

            StartCoroutine(RestartStreamingWithStunFallback(reasonCode));
        }

        private IEnumerator RestartStreamingWithStunFallback(string reasonCode)
        {
            if (_isNegotiating)
            {
                if (_logSignaling)
                {
                    Debug.LogWarning($"[WebRTCServerSignaling] STUN fallback requested during negotiation ({reasonCode}) - skipping.");
                }
                yield break;
            }

            _isNegotiating = true;
            if (_logSignaling)
            {
                Debug.LogWarning($"[WebRTCServerSignaling] Restarting WebRTC in STUN mode ({reasonCode}).");
            }

            _mediaStreamService.StopStreaming();
            yield return null;
            yield return StartStreamingAndNegotiateWhenReady(forceStun: true, reasonCode: "STUN_FALLBACK");
        }

        private IEnumerator StartStreamingAndNegotiateWhenReady(bool forceStun, string reasonCode)
        {
            const float timeoutSeconds = 5f;
            float waitedSeconds = 0f;
            bool loggedWait = false;
            _isNegotiating = true;

            while (_mediaStreamService != null &&
                   _tcpServer != null &&
                   _tcpServer.HasClient &&
                   !_mediaStreamService.EnsureCaptureReady() &&
                   waitedSeconds < timeoutSeconds)
            {
                if (!loggedWait && _logSignaling)
                {
                    Debug.Log("[WebRTCServerSignaling] Waiting for MediaStreamService capture to become ready...");
                    loggedWait = true;
                }

                waitedSeconds += Time.unscaledDeltaTime;
                yield return null;
            }

            if (_mediaStreamService == null || _tcpServer == null || !_tcpServer.HasClient)
            {
                _isNegotiating = false;
                _deferredNegotiationCoroutine = null;
                yield break;
            }

            if (!_mediaStreamService.HasCaptureReady)
            {
                Debug.LogError("[WebRTCServerSignaling] Cannot negotiate preview - MediaStreamService capture is still not ready.");
                _isNegotiating = false;
                _deferredNegotiationCoroutine = null;
                yield break;
            }

            if (!_mediaStreamService.IsStreaming)
            {
                _mediaStreamService.StartStreaming(forceStun);
            }

            if (!_mediaStreamService.IsStreaming)
            {
                Debug.LogError("[WebRTCServerSignaling] Cannot negotiate preview - WebRTC stream failed to start.");
                _isNegotiating = false;
                _deferredNegotiationCoroutine = null;
                yield break;
            }

            yield return CreateOfferAndSendInternal(reasonCode);
            _isNegotiating = false;
            _deferredNegotiationCoroutine = null;
        }

        private void HandleTCPMessage(NetworkMessage message)
        {
            switch (message.commandId)
            {
                case "WEBRTC_ANSWER":
                    HandleWebRTCAnswer(message.payloadString);
                    break;
                case "WEBRTC_ICE_CANDIDATE":
                    HandleICECandidate(message.payloadString);
                    break;
                case "WEBRTC_STUN_FALLBACK_REQUEST":
                    HandleClientStunFallbackRequest(message.payloadString);
                    break;
                case PreviewPauseCommandId:
                    HandlePreviewPause(message.payloadString);
                    break;
                case PreviewResumeCommandId:
                    HandlePreviewResume(message.payloadString);
                    break;
                case SetVideoBitrateCommandId:
                    HandleSetVideoBitrate(message.payloadString);
                    break;
            }
        }

        private void HandlePreviewResume(string payloadJson)
        {
            if (_mediaStreamService == null || _tcpServer == null)
            {
                return;
            }

            if (!_tcpServer.HasClient)
            {
                if (_logSignaling)
                {
                    Debug.LogWarning("[WebRTCServerSignaling] Ignoring preview resume - no active TCP client.");
                }
                return;
            }

            if (_isNegotiating)
            {
                if (_logSignaling)
                {
                    Debug.Log("[WebRTCServerSignaling] Ignoring preview resume - negotiation already in progress.");
                }
                return;
            }

            if (_mediaStreamService.IsStreaming)
            {
                if (_logSignaling)
                {
                    Debug.Log("[WebRTCServerSignaling] Ignoring preview resume - preview stream already active.");
                }
                return;
            }

            var reasonCode = ParsePreviewReasonCode(payloadJson, "PREVIEW_RESUME");
            _deferredNegotiationCoroutine = StartCoroutine(StartStreamingAndNegotiateWhenReady(
                forceStun: false,
                reasonCode: reasonCode));
        }

        private void HandlePreviewPause(string payloadJson)
        {
            if (_mediaStreamService == null)
            {
                return;
            }

            if (_deferredNegotiationCoroutine != null)
            {
                StopCoroutine(_deferredNegotiationCoroutine);
                _deferredNegotiationCoroutine = null;
            }

            _isNegotiating = false;

            if (_mediaStreamService.IsStreaming)
            {
                _mediaStreamService.StopStreaming();
            }

            if (_logSignaling)
            {
                var reasonCode = ParsePreviewReasonCode(payloadJson, "PREVIEW_PAUSE");
                Debug.Log($"[WebRTCServerSignaling] Preview paused ({reasonCode}) while TCP session stays connected.");
            }
        }

        private void HandleSetVideoBitrate(string payloadJson)
        {
            if (_mediaStreamService == null || _tcpServer == null)
            {
                return;
            }

            var request = new WebRTCSetVideoBitrateMessage();
            if (!string.IsNullOrWhiteSpace(payloadJson))
            {
                try
                {
                    request = JsonUtility.FromJson<WebRTCSetVideoBitrateMessage>(payloadJson) ??
                              new WebRTCSetVideoBitrateMessage();
                }
                catch (Exception ex)
                {
                    if (_logSignaling)
                    {
                        Debug.LogWarning($"[WebRTCServerSignaling] Failed to parse {SetVideoBitrateCommandId}: {ex.Message}");
                    }
                    SendVideoBitrateStatus(
                        success: false,
                        requestedBitrateBps: 0,
                        appliedBitrateBps: 0,
                        reasonCode: "VIDEO_BITRATE_INVALID_PAYLOAD");
                    return;
                }
            }

            var requestedBitrateBps = request.bitrateBps;
            if (requestedBitrateBps <= 0 && request.bitrateKbps > 0)
            {
                requestedBitrateBps = request.bitrateKbps * 1000;
            }

            if (requestedBitrateBps <= 0)
            {
                SendVideoBitrateStatus(
                    success: false,
                    requestedBitrateBps: 0,
                    appliedBitrateBps: 0,
                    reasonCode: "VIDEO_BITRATE_INVALID_VALUE");
                return;
            }

            var success = _mediaStreamService.TrySetTargetBitrateBps(
                requestedBitrateBps,
                out var appliedBitrateBps,
                out var reasonCode);

            if (_logSignaling)
            {
                Debug.Log(
                    $"[WebRTCServerSignaling] {SetVideoBitrateCommandId}: " +
                    $"requested={requestedBitrateBps}bps applied={appliedBitrateBps}bps success={success} reason={reasonCode}");
            }

            SendVideoBitrateStatus(
                success: success,
                requestedBitrateBps: requestedBitrateBps,
                appliedBitrateBps: appliedBitrateBps,
                reasonCode: reasonCode);
        }

        private void SendVideoBitrateStatus(
            bool success,
            int requestedBitrateBps,
            int appliedBitrateBps,
            string reasonCode)
        {
            if (_tcpServer == null || !_tcpServer.HasClient)
            {
                return;
            }

            var payload = new WebRTCVideoBitrateStatusMessage
            {
                success = success,
                requestedBitrateBps = requestedBitrateBps,
                appliedBitrateBps = appliedBitrateBps,
                requestedBitrateKbps = requestedBitrateBps > 0 ? requestedBitrateBps / 1000 : 0,
                appliedBitrateKbps = appliedBitrateBps > 0 ? appliedBitrateBps / 1000 : 0,
                reasonCode = string.IsNullOrWhiteSpace(reasonCode) ? (success ? "VIDEO_BITRATE_APPLIED" : "VIDEO_BITRATE_FAILED") : reasonCode
            };

            _ = _tcpServer.SendCommandAsync(VideoBitrateStatusCommandId, payload);
        }

        private void HandleClientStunFallbackRequest(string payloadJson)
        {
            var reasonCode = "CLIENT_REQUEST";
            if (!string.IsNullOrEmpty(payloadJson))
            {
                try
                {
                    var request = JsonUtility.FromJson<WebRTCStunFallbackRequestMessage>(payloadJson);
                    if (request != null && !string.IsNullOrWhiteSpace(request.reasonCode))
                    {
                        reasonCode = $"CLIENT_REQUEST_{request.reasonCode}";
                    }
                }
                catch
                {
                    // Keep default reason code.
                }
            }

            if (_mediaStreamService != null &&
                _mediaStreamService.IsStreaming &&
                !_mediaStreamService.IsLanOnlyMode)
            {
                if (_logSignaling)
                {
                    Debug.Log($"[WebRTCServerSignaling] Ignoring {reasonCode} - already in STUN mode.");
                }
                return;
            }

            StartCoroutine(RestartStreamingWithStunFallback(reasonCode));
        }

        private string ParsePreviewReasonCode(string payloadJson, string fallbackReasonCode)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return fallbackReasonCode;
            }

            try
            {
                var request = JsonUtility.FromJson<WebRTCPreviewLifecycleMessage>(payloadJson);
                if (request != null && !string.IsNullOrWhiteSpace(request.reasonCode))
                {
                    return request.reasonCode;
                }
            }
            catch (Exception ex)
            {
                if (_logSignaling)
                {
                    Debug.LogWarning($"[WebRTCServerSignaling] Failed to parse preview lifecycle payload: {ex.Message}");
                }
            }

            return fallbackReasonCode;
        }

        private void HandleWebRTCAnswer(string answerJson)
        {
            if (string.IsNullOrEmpty(answerJson) || _mediaStreamService == null) return;
            try
            {
                var answerData = JsonUtility.FromJson<WebRTCAnswerMessage>(answerJson);
                var answer = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = answerData.sdp };
                StartCoroutine(_mediaStreamService.SetRemoteAnswer(answer));
                if (_logSignaling) Debug.Log("[WebRTCServerSignaling] Set remote answer from client");
            }
            catch (Exception e)
            {
                Debug.LogError($"[WebRTCServerSignaling] Handle answer failed: {e.Message}");
            }
        }

        private void HandleICECandidate(string candidateJson)
        {
            if (string.IsNullOrEmpty(candidateJson) || _mediaStreamService == null) return;
            try
            {
                var data = JsonUtility.FromJson<WebRTCIceCandidateMessage>(candidateJson);
                var candidate = new RTCIceCandidate(new RTCIceCandidateInit
                {
                    candidate = data.candidate,
                    sdpMid = data.sdpMid,
                    sdpMLineIndex = data.sdpMLineIndex
                });
                _mediaStreamService.AddIceCandidate(candidate);
                if (_logSignaling) Debug.Log("[WebRTCServerSignaling] Added ICE candidate from client");
            }
            catch (Exception e)
            {
                Debug.LogError($"[WebRTCServerSignaling] Handle ICE candidate failed: {e.Message}");
            }
        }

        private void SendICECandidate(RTCIceCandidate candidate)
        {
            if (_tcpServer == null || !_tcpServer.HasClient) return;
            try
            {
                var data = new WebRTCIceCandidateMessage
                {
                    candidate = candidate.Candidate,
                    sdpMid = candidate.SdpMid,
                    sdpMLineIndex = candidate.SdpMLineIndex ?? 0
                };
                _ = _tcpServer.SendCommandAsync("WEBRTC_ICE_CANDIDATE", data);
                if (_logSignaling) Debug.Log("[WebRTCServerSignaling] Sent ICE candidate to client");
            }
            catch (Exception e)
            {
                Debug.LogError($"[WebRTCServerSignaling] Send ICE candidate failed: {e.Message}");
            }
        }
    }
    
    // ============================================
    // DATA STRUCTURES
    // ============================================
    
    [System.Serializable]
    public class WebRTCOfferMessage
    {
        public string type;  // "offer"
        public string sdp;   // SDP string
        public string iceMode; // "LAN" or "STUN"
        public string reasonCode; // signaling reason metadata
    }
    
    [System.Serializable]
    public class WebRTCAnswerMessage
    {
        public string type;  // "answer"
        public string sdp;   // SDP string
    }
    
    [System.Serializable]
    public class WebRTCIceCandidateMessage
    {
        public string candidate;     // ICE candidate string
        public string sdpMid;        // Media stream ID
        public int sdpMLineIndex;    // Media line index
    }

    [System.Serializable]
    public class WebRTCStunFallbackRequestMessage
    {
        public string reasonCode;
    }

    [System.Serializable]
    public class WebRTCPreviewLifecycleMessage
    {
        public string reasonCode;
    }

    [System.Serializable]
    public class WebRTCSetVideoBitrateMessage
    {
        public int bitrateKbps;
        public int bitrateBps;
        public string reasonCode;
        public string origin;
    }

    [System.Serializable]
    public class WebRTCVideoBitrateStatusMessage
    {
        public bool success;
        public int requestedBitrateKbps;
        public int requestedBitrateBps;
        public int appliedBitrateKbps;
        public int appliedBitrateBps;
        public string reasonCode;
    }
}
