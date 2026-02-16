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
    /// On client connect: start WebRTC, create offer, send via TCP.
    /// Handles WEBRTC_ANSWER and WEBRTC_ICE_CANDIDATE from Flutter; sends ICE candidates to Flutter.
    /// </summary>
    public class WebRTCServerSignaling : MonoBehaviour
    {
        [Header("Dependencies")]
        [FormerlySerializedAs("_videoStreamService")]
        [SerializeField] private MediaStreamService _mediaStreamService;
        [SerializeField] private TCPServerService _tcpServer;

        [Header("Debug")]
        [SerializeField] private bool _logSignaling = true;

        private bool _isNegotiating;

        private void Awake()
        {
            if (_mediaStreamService == null) _mediaStreamService = GetComponent<MediaStreamService>();
            if (_tcpServer == null) _tcpServer = GetComponent<TCPServerService>();
            if (_tcpServer == null) _tcpServer = FindFirstObjectByType<TCPServerService>();
            if (_tcpServer == null)
            {
                Debug.LogError("[WebRTCServerSignaling] TCPServerService not found!");
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (_tcpServer != null)
            {
                _tcpServer.OnClientConnected += HandleClientConnected;
                _tcpServer.OnMessageReceived += HandleTCPMessage;
                _tcpServer.OnClientDisconnected += HandleClientDisconnected;
            }
            if (_mediaStreamService != null)
                _mediaStreamService.OnIceCandidateGenerated += SendICECandidate;
        }

        private void OnDisable()
        {
            if (_tcpServer != null)
            {
                _tcpServer.OnClientConnected -= HandleClientConnected;
                _tcpServer.OnMessageReceived -= HandleTCPMessage;
                _tcpServer.OnClientDisconnected -= HandleClientDisconnected;
            }
            if (_mediaStreamService != null)
                _mediaStreamService.OnIceCandidateGenerated -= SendICECandidate;
        }

        private void HandleClientConnected(string clientIP)
        {
            if (_mediaStreamService == null || _tcpServer == null) return;
            if (_mediaStreamService.IsStreaming)
            {
                if (_logSignaling) Debug.Log("[WebRTCServerSignaling] Already streaming, skipping offer");
                return;
            }
            _mediaStreamService.StartStreaming();
            StartCoroutine(CreateOfferAndSend());
        }

        private void HandleClientDisconnected()
        {
            _isNegotiating = false;
            if (_mediaStreamService != null) _mediaStreamService.StopStreaming();
            if (_logSignaling) Debug.Log("[WebRTCServerSignaling] Client disconnected, stopped stream");
        }

        private IEnumerator CreateOfferAndSend()
        {
            if (_isNegotiating) yield break;
            _isNegotiating = true;
            if (_logSignaling) Debug.Log("[WebRTCServerSignaling] Creating WebRTC offer...");

            yield return _mediaStreamService.CreateOffer(offer =>
            {
                var offerData = new WebRTCOfferMessage { type = "offer", sdp = offer.sdp };
                _ = _tcpServer.SendCommandAsync("WEBRTC_OFFER", offerData);
                if (_logSignaling) Debug.Log($"[WebRTCServerSignaling] Sent WEBRTC_OFFER to client ({offer.sdp.Length} chars)");
            });
            _isNegotiating = false;
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
            }
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
}
