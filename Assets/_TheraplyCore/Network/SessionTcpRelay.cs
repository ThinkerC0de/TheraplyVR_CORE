using System;
using System.Collections.Generic;
using System.Globalization;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Interactions;
using TheraplyCore.Network.Connection;
using UnityEngine;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Network
{
    /// <summary>
    /// Forwards game lifecycle events and interaction batches to the mobile controller
    /// via the TCP channel, replacing the Cloud Function HTTP outbox.
    ///
    /// Called from GameRuntimeService at game_start, game_end, session_start, session_stop.
    /// Buffers interaction events during a game run and sends them as a single batch at game_end.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionTcpRelay : MonoBehaviour
    {
        [SerializeField] private TCPServerService _tcpServer;

        private string _activeSessionId = string.Empty;
        private string _activeGameRunId;
        private string _activeGameId;
        private DateTime _gameStartedAt;

        private readonly List<QuestInteractionEventItem> _interactionBuffer =
            new List<QuestInteractionEventItem>();

        private int _hitCount;
        private int _missCount;

        private InteractionEventBridge _bridge;
        private float _nextBridgeResolveAtRealtime;
        private bool _interactionBridgeMissingLogged;

        // ─── Unity lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            if (_tcpServer == null)
                _tcpServer = FindFirstObjectByType<TCPServerService>();
        }

        private void Start()
        {
            EnsureInteractionBridgeSubscription(forceRefresh: true, reason: "start");
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextBridgeResolveAtRealtime)
            {
                return;
            }

            if (_bridge != null && string.IsNullOrWhiteSpace(_activeGameRunId))
            {
                return;
            }

            var reason = string.IsNullOrWhiteSpace(_activeGameRunId)
                ? "idle_poll"
                : "active_game_poll";
            EnsureInteractionBridgeSubscription(reason: reason);
        }

        private void OnDestroy()
        {
            if (_bridge != null)
                _bridge.EventPublished -= HandleInteractionEvent;
        }

        // ─── Public API (called by GameRuntimeService) ───────────────────────

        public void SetSessionId(string sessionId)
        {
            _activeSessionId = sessionId?.Trim() ?? string.Empty;
        }

        public void OnGameStart(string gameId, string sessionId)
        {
            EnsureInteractionBridgeSubscription(forceRefresh: true, reason: "game_start");
            _activeSessionId = sessionId?.Trim() ?? _activeSessionId;
            _activeGameId = gameId ?? string.Empty;
            _activeGameRunId = Guid.NewGuid().ToString("D");
            _gameStartedAt = DateTime.UtcNow;
            _interactionBuffer.Clear();
            _hitCount = 0;
            _missCount = 0;

            Logger.Info($"[SessionTcpRelay] game_start: gameId={_activeGameId} gameRunId={_activeGameRunId}");
            SendGameEvent("game_start", _activeGameRunId, _activeGameId,
                string.Empty, 0, 0, 0, 0);
        }

        public void OnGameEnd(GameStopReason stopReason, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(_activeGameRunId)) return;

            _activeSessionId = sessionId?.Trim() ?? _activeSessionId;

            var durationSec = (int)(DateTime.UtcNow - _gameStartedAt).TotalSeconds;
            var finalState = MapStopReasonToFinalState(stopReason);
            var interactionCount = _interactionBuffer.Count;

            Logger.Info($"[SessionTcpRelay] game_end: gameId={_activeGameId} " +
                $"gameRunId={_activeGameRunId} finalState={finalState} " +
                $"interactions={interactionCount} hits={_hitCount} misses={_missCount} " +
                $"durationSec={durationSec}");

            if (_interactionBuffer.Count > 0)
                SendInteractionBatch();

            SendGameEvent("game_end", _activeGameRunId, _activeGameId,
                finalState, durationSec, _hitCount, _missCount, interactionCount);

            _activeGameRunId = null;
            _activeGameId = null;
            _interactionBuffer.Clear();
        }

        public void OnSessionStart(string sessionId)
        {
            _activeSessionId = sessionId?.Trim() ?? _activeSessionId;
            Logger.Info($"[SessionTcpRelay] session_start: sessionId={_activeSessionId}");
            SendGameEvent("session_start", string.Empty, string.Empty,
                string.Empty, 0, 0, 0, 0);
        }

        public void OnSessionStop(string sessionId, string reason)
        {
            _activeSessionId = sessionId?.Trim() ?? _activeSessionId;
            Logger.Info($"[SessionTcpRelay] session_stop: sessionId={_activeSessionId} reason={reason}");
            SendGameEvent("session_stop", string.Empty, string.Empty,
                reason ?? string.Empty, 0, 0, 0, 0);
        }

        // ─── Interaction buffering ────────────────────────────────────────────

        private void HandleInteractionEvent(IReadOnlyDictionary<string, object> payload)
        {
            if (string.IsNullOrWhiteSpace(_activeGameRunId) || payload == null) return;

            // trace_ref events carry the motion trace binary payload — route separately.
            // Note: InteractionEventBridge sets eventType = moduleEventType ("trace_ref"),
            //       while interactionType is always "GAMEPLAY" for RecordGameplayEvent calls.
            var eventType = GetStr(payload, "eventType");
            if (string.Equals(eventType, "TRACE_REF", StringComparison.OrdinalIgnoreCase))
            {
                SendMotionTrace(payload);
                return;
            }

            var actionOutcome = GetStr(payload, "actionOutcome");
            if (string.Equals(actionOutcome, "CORRECT", StringComparison.Ordinal))
                _hitCount++;
            else if (string.Equals(actionOutcome, "INCORRECT", StringComparison.Ordinal))
                _missCount++;

            var details = payload.TryGetValue("details", out var detailsObj)
                ? detailsObj as IReadOnlyDictionary<string, object>
                : null;
            var rawId = GetStr(payload, "eventId");
            var targetAppearedAtElapsedSec = GetFloat(details, "targetAppearedAtElapsedSec");
            if (targetAppearedAtElapsedSec <= 0f)
            {
                targetAppearedAtElapsedSec = GetFloat(payload, "targetAppearedAtElapsedSec");
            }

            var responseSec = GetFloat(details, "responseSec");
            if (responseSec <= 0f)
            {
                responseSec = GetFloat(payload, "responseSec");
            }

            var interactionEvent = new QuestInteractionEventItem
            {
                eventId = string.IsNullOrWhiteSpace(rawId)
                    ? Guid.NewGuid().ToString("D")
                    : rawId,
                eventType = eventType ?? string.Empty,
                gameId = GetStr(payload, "gameId") ?? _activeGameId ?? string.Empty,
                occurredAtUtc = GetStr(payload, "occurredAtUtc")
                    ?? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                sequenceNo = _interactionBuffer.Count + 1,
                interactionType = GetStr(payload, "interactionType") ?? string.Empty,
                actionOutcome = actionOutcome ?? string.Empty,
                targetId = GetStr(payload, "targetId") ?? string.Empty,
                targetName = GetStr(payload, "targetName") ?? string.Empty,
                targetInstanceId = GetStr(details, "targetInstanceId")
                    ?? GetStr(payload, "targetInstanceId")
                    ?? string.Empty,
                targetCategory = GetStr(details, "targetCategory")
                    ?? GetStr(payload, "targetCategory")
                    ?? string.Empty,
                targetAppearedAtUtc = GetStr(details, "targetAppearedAtUtc")
                    ?? GetStr(payload, "targetAppearedAtUtc")
                    ?? string.Empty,
                targetAppearedAtElapsedSec = targetAppearedAtElapsedSec,
                responseSec = responseSec,
                inputHand = GetStr(payload, "inputHand") ?? string.Empty,
                inputSource = GetStr(payload, "inputSource") ?? string.Empty,
                inputValue = GetFloat(payload, "inputValue"),
                sourceComponent = GetStr(payload, "sourceComponent") ?? string.Empty,
                reasonCode = GetStr(payload, "reasonCode") ?? string.Empty,
            };

            _interactionBuffer.Add(interactionEvent);
            SendInteractionPreview(interactionEvent);
        }

        private void EnsureInteractionBridgeSubscription(bool forceRefresh = false, string reason = "")
        {
            _nextBridgeResolveAtRealtime = Time.unscaledTime + 1f;

            if (!forceRefresh && _bridge != null)
            {
                return;
            }

            var resolvedBridge = InteractionEventBridge.Instance
                ?? FindFirstObjectByType<InteractionEventBridge>();

            if (resolvedBridge == null)
            {
                if (!_interactionBridgeMissingLogged)
                {
                    Logger.Warning(
                        "[SessionTcpRelay] InteractionEventBridge not found (" +
                        reason + ") — interactions will not be forwarded.");
                    _interactionBridgeMissingLogged = true;
                }

                _bridge = null;
                return;
            }

            if (_bridge != null && !ReferenceEquals(_bridge, resolvedBridge))
            {
                _bridge.EventPublished -= HandleInteractionEvent;
            }

            _bridge = resolvedBridge;
            _bridge.EventPublished -= HandleInteractionEvent;
            _bridge.EventPublished += HandleInteractionEvent;

            if (_interactionBridgeMissingLogged || forceRefresh)
            {
                Logger.Info("[SessionTcpRelay] InteractionEventBridge subscribed (" + reason + ").");
            }

            _interactionBridgeMissingLogged = false;
        }

        // ─── TCP send helpers ─────────────────────────────────────────────────

        private void SendInteractionBatch()
        {
            if (!CanSend()) return;

            var batch = new QuestInteractionBatchPayload
            {
                sessionId = _activeSessionId,
                gameRunId = _activeGameRunId,
                events = new List<QuestInteractionEventItem>(_interactionBuffer),
            };
            _ = _tcpServer.SendCommandAsync(GameCommandIds.QuestInteractionBatch, batch);
        }

        private void SendInteractionPreview(QuestInteractionEventItem interactionEvent)
        {
            if (!CanSend() || interactionEvent == null) return;

            var preview = new QuestInteractionPreviewPayload
            {
                sessionId = _activeSessionId,
                gameRunId = _activeGameRunId,
                @event = interactionEvent,
            };
            _ = _tcpServer.SendCommandAsync(GameCommandIds.QuestInteractionPreview, preview);
        }

        private void SendGameEvent(string eventType, string gameRunId, string gameId,
            string finalState, int durationSec, int hitCount, int missCount, int interactionCount)
        {
            if (!CanSend()) return;

            var evt = new QuestGameEventPayload
            {
                eventType = eventType,
                sessionId = _activeSessionId,
                gameRunId = gameRunId,
                gameId = gameId,
                occurredAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                finalState = finalState,
                durationSec = durationSec,
                hitCount = hitCount,
                missCount = missCount,
                interactionCount = interactionCount,
            };
            _ = _tcpServer.SendCommandAsync(GameCommandIds.QuestGameEvent, evt);
        }

        private void SendMotionTrace(IReadOnlyDictionary<string, object> payload)
        {
            if (!CanSend()) return;

            // InteractionEventBridge wraps the original payload fields inside a nested "details" dict.
            var details = payload.TryGetValue("details", out var detailsObj)
                ? detailsObj as IReadOnlyDictionary<string, object>
                : null;
            var src = (IReadOnlyDictionary<string, object>)details ?? payload;

            var inlineStatus = GetStr(src, "inlinePayloadStatus") ?? string.Empty;
            var tracePayloadBase64 = GetStr(src, "tracePayloadBase64") ?? string.Empty;

            var trace = new QuestMotionTracePayload
            {
                sessionId = _activeSessionId,
                gameRunId = _activeGameRunId,
                gameId = _activeGameId ?? string.Empty,
                traceId = GetStr(src, "traceId") ?? Guid.NewGuid().ToString("N"),
                encoding = GetStr(src, "encoding") ?? "none",
                format = GetStr(src, "format") ?? "vrl",
                checksum = GetStr(src, "checksum") ?? string.Empty,
                frameCount = GetInt(src, "frameCount"),
                payloadBytes = GetInt(src, "inlinePayloadBytes"),
                tracePayloadBase64 = tracePayloadBase64,
                inlinePayloadStatus = inlineStatus,
            };

            Logger.Info($"[SessionTcpRelay] motion_trace: traceId={trace.traceId} " +
                $"frames={trace.frameCount} status={inlineStatus} bytes={trace.payloadBytes}");

            _ = _tcpServer.SendCommandAsync(GameCommandIds.QuestMotionTrace, trace);
        }

        private bool CanSend() => _tcpServer != null && _tcpServer.HasClient;

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static string MapStopReasonToFinalState(GameStopReason reason)
        {
            switch (reason)
            {
                case GameStopReason.Completed: return "completed";
                case GameStopReason.TherapistStop: return "interrupted";
                case GameStopReason.UserExit: return "interrupted";
                default: return "failed";
            }
        }

        private static string GetStr(IReadOnlyDictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out var v) ? v?.ToString() : null;

        private static int GetInt(IReadOnlyDictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return 0;
            if (v is int i) return i;
            if (v is long l) return (int)l;
            if (v is double db) return (int)db;
            return int.TryParse(v.ToString(), out var r) ? r : 0;
        }

        private static float GetFloat(IReadOnlyDictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return 0f;
            if (v is float f) return f;
            if (v is double db) return (float)db;
            if (v is int i) return i;
            if (v is long l) return l;
            return float.TryParse(v.ToString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var r) ? r : 0f;
        }
    }
}
