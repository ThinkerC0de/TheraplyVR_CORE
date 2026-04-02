using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(WebSocketClientV6))]
public class NetworkAdapter : MonoBehaviour
{
    [SerializeField] private WebSocketClientV6 _client;
    [SerializeField] private NetworkAdapterConfig _config;
    [SerializeField] private bool _logVerbose;

    private bool _initialized;
    private CommunicationMode _mode = CommunicationMode.Legacy;
    private SessionContextStore _sessionContext;
    private ReliableCommandJournal _journal;
    private RecoveryStateMachine _recovery;
    private OutboundCriticalQueue _criticalQueue;
    private InboundMessageGate _inboundGate;
    private ConnectionHealthMonitor _healthMonitor;
    private CriticalCommandRegistry _criticalRegistry;

    private void Reset()
    {
        _client = GetComponent<WebSocketClientV6>();
    }

    private void Awake()
    {
        if (_client == null)
        {
            _client = GetComponent<WebSocketClientV6>();
        }
    }

    private void Start()
    {
        InitializeIfNeeded();
    }

    private void Update()
    {
        if (!_initialized || _mode == CommunicationMode.Legacy)
        {
            return;
        }

        if (_mode == CommunicationMode.ReliableActive)
        {
            ProcessPendingCriticalQueue();
            ProcessHeartbeat();
            CheckForIdleDegrade();
        }
    }

    public void InitializeIfNeeded()
    {
        if (_initialized)
        {
            return;
        }

        if (_client == null)
        {
            _client = GetComponent<WebSocketClientV6>();
        }

        _mode = _config != null ? _config.mode : CommunicationMode.Legacy;
        _criticalRegistry = new CriticalCommandRegistry();

        if (_config != null)
        {
            _criticalRegistry.ApplyOverrides(_config.extraCriticalCommandIds);
        }

        _sessionContext = new SessionContextStore(GetJournalFolderName(), GetSessionContextFileName());
        _sessionContext.Load();
        _journal = new ReliableCommandJournal(GetJournalFolderName(), GetJournalFileName());
        _recovery = new RecoveryStateMachine();
        _criticalQueue = new OutboundCriticalQueue(
            GetMaxCriticalRetries(),
            GetAckTimeoutSeconds(),
            GetRetryBaseDelaySeconds(),
            GetRetryMaxDelaySeconds());
        _inboundGate = new InboundMessageGate(_journal);
        _healthMonitor = new ConnectionHealthMonitor();

        _initialized = true;
        Log($"Initialized in mode {_mode}");
    }

    public bool TryHandleInbound(string rawMessage)
    {
        InitializeIfNeeded();

        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return false;
        }

        _healthMonitor.RecordInbound();
        _sessionContext.CaptureFromRawMessage(rawMessage);
        _client?.ObserveSessionBootstrapRaw(rawMessage);

        if (_mode == CommunicationMode.Legacy)
        {
            return false;
        }

        InboundDecision decision = _inboundGate.Evaluate(rawMessage);

        if (_mode == CommunicationMode.ReliableShadow)
        {
            ObserveShadowInbound(decision);
            return false;
        }

        switch (decision.DecisionType)
        {
            case InboundDecisionType.ConsumeOnly:
                HandleConsumeOnlyDecision(decision);
                return true;

            case InboundDecisionType.RejectDuplicate:
                HandleDuplicateDecision(decision);
                return true;

            case InboundDecisionType.AckAndRoute:
                HandleAckAndRouteDecision(decision);
                return true;

            default:
                return false;
        }
    }

    public bool TryHandleOutboundText(string rawMessage)
    {
        return TryHandleOutboundPayload(rawMessage, null);
    }

    public bool TryHandleOutboundJson(object payload)
    {
        if (_client == null)
        {
            return false;
        }

        string jsonPayload = _client.SerializeJsonPayloadLegacy(payload);
        if (string.IsNullOrWhiteSpace(jsonPayload))
        {
            return false;
        }

        return TryHandleOutboundPayload(jsonPayload, null);
    }

    public bool TryHandleOutboundCommand(string commandType, object data)
    {
        if (_client == null || string.IsNullOrWhiteSpace(commandType))
        {
            return false;
        }

        string jsonPayload = _client.BuildCommandJsonLegacy(commandType, data);
        return TryHandleOutboundPayload(jsonPayload, commandType);
    }

    public void OnControlConnected()
    {
        InitializeIfNeeded();

        _healthMonitor.ResetConnected();
        _recovery.MarkConnected();
        _sessionContext.BindTeacher(_client.ConnectedTeacherId, _client.ConnectedTeacherIP);
        _sessionContext.MarkConnected();

        if (_mode == CommunicationMode.ReliableActive &&
            GetEnableSessionResume() &&
            ShouldAttemptResume())
        {
            ReliableResumeRequest resumeRequest = _sessionContext.BuildResumeRequest(_client.VrAppId);
            string payload = SimpleJsonSerializer.Serialize(resumeRequest);
            if (_client.SendJsonStringLegacyInternal(payload))
            {
                _recovery.BeginReattach();
                Log($"Sent resume request for session {_sessionContext.SessionId}");
            }
        }
    }

    public void OnControlDisconnected(string source, string reason)
    {
        InitializeIfNeeded();

        string composedReason = string.IsNullOrWhiteSpace(source)
            ? reason
            : $"{source}:{reason}";

        _recovery.MarkDisconnected(composedReason);
        _sessionContext.MarkDisconnected(composedReason);
        Log($"Control disconnected ({composedReason})", LogType.Warning);
    }

    public void OnApplicationPauseChanged(bool paused)
    {
        InitializeIfNeeded();
        _sessionContext.Save();

        if (paused)
        {
            Log("Application paused; session context persisted");
        }
    }

    public void OnApplicationFocusChanged(bool hasFocus)
    {
        InitializeIfNeeded();

        if (hasFocus)
        {
            _sessionContext.Save();
        }
    }

    public void Shutdown()
    {
        if (!_initialized)
        {
            return;
        }

        _sessionContext.Save();
        _journal.Flush();
        _journal.Dispose();
    }

    private bool TryHandleOutboundPayload(string rawPayload, string explicitCommandId)
    {
        InitializeIfNeeded();

        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return false;
        }

        _healthMonitor.RecordOutbound();
        _sessionContext.CaptureFromRawMessage(rawPayload);

        string commandId = !string.IsNullOrWhiteSpace(explicitCommandId)
            ? explicitCommandId
            : _criticalRegistry.ResolveCommandId(rawPayload);
        bool isCritical = _criticalRegistry.IsCriticalCommandId(commandId) || _criticalRegistry.IsCriticalRawMessage(rawPayload);

        if (_mode == CommunicationMode.Legacy)
        {
            return false;
        }

        if (_mode == CommunicationMode.ReliableShadow)
        {
            if (isCritical)
            {
                Log($"Shadow mode observed critical outbound command {commandId}");
            }

            return false;
        }

        if (!GetEnableReliableControl() || !isCritical)
        {
            return false;
        }

        ReliableCommandEnvelope envelope = BuildEnvelope(commandId, rawPayload);
        _criticalQueue.Enqueue(envelope, DateTime.UtcNow);
        ProcessPendingCriticalQueue();
        return true;
    }

    private void ProcessPendingCriticalQueue()
    {
        if (_mode != CommunicationMode.ReliableActive || !GetEnableReliableControl())
        {
            return;
        }

        if (_client == null || !_client.IsReadyToSend())
        {
            return;
        }

        DateTime nowUtc = DateTime.UtcNow;
        List<QueuedCriticalEnvelope> droppedEntries = _criticalQueue.DropExpired(nowUtc);
        for (int i = 0; i < droppedEntries.Count; i++)
        {
            QueuedCriticalEnvelope dropped = droppedEntries[i];
            if (dropped?.Envelope == null)
            {
                continue;
            }

            if (GetEnableCriticalJournal())
            {
                _journal.RecordFailed(
                    dropped.Envelope.messageId,
                    dropped.Envelope.commandId,
                    dropped.Envelope.sessionId,
                    dropped.LastReasonCode);
            }
        }

        List<QueuedCriticalEnvelope> dueEntries = _criticalQueue.CollectDueEntries(nowUtc);
        for (int i = 0; i < dueEntries.Count; i++)
        {
            QueuedCriticalEnvelope entry = dueEntries[i];
            if (entry?.Envelope == null)
            {
                continue;
            }

            string envelopeJson = SimpleJsonSerializer.Serialize(entry.Envelope);
            _criticalQueue.MarkAttempt(entry.Envelope.messageId, nowUtc);
            bool sent = _client.SendJsonStringLegacyInternal(envelopeJson);

            if (!sent)
            {
                _criticalQueue.MarkSendFailure(
                    entry.Envelope.messageId,
                    DateTime.UtcNow,
                    ReliableProtocolConstants.ReasonTransportFailure);
            }
        }
    }

    private void ProcessHeartbeat()
    {
        if (_client == null || !_client.IsReadyToSend())
        {
            return;
        }

        if (!_healthMonitor.ShouldSendHeartbeat(GetHeartbeatIntervalSeconds()))
        {
            return;
        }

        ReliableHeartbeat heartbeat = new ReliableHeartbeat
        {
            sessionId = _sessionContext.SessionId,
            connectionState = _recovery.CurrentState.ToString(),
            queuedCriticalCount = _criticalQueue.Count,
            sentAtUtc = ReliableProtocolUtility.ToUtcString(DateTime.UtcNow)
        };

        if (_client.SendJsonStringLegacyInternal(SimpleJsonSerializer.Serialize(heartbeat)))
        {
            _healthMonitor.MarkHeartbeatSent();
        }
    }

    private void CheckForIdleDegrade()
    {
        if (_recovery.CurrentState == RecoveryState.Connected &&
            _healthMonitor.IsControlIdle(GetIdleTimeoutSeconds()))
        {
            _recovery.MarkDegraded("CONTROL_IDLE");
        }
    }

    private void HandleConsumeOnlyDecision(InboundDecision decision)
    {
        if (decision.Ack != null)
        {
            if (decision.Ack.status == ReliableProtocolConstants.AckStatusAccepted)
            {
                _criticalQueue.MarkAck(decision.Ack.messageId);
                _sessionContext.UpdateLastAcked(decision.Ack.messageId);

                if (GetEnableCriticalJournal())
                {
                    _journal.RecordApplied(
                        decision.Ack.messageId,
                        decision.Ack.commandId,
                        decision.Ack.sessionId);
                }
            }
            else
            {
                _criticalQueue.MarkNack(decision.Ack.messageId, decision.Ack.reasonCode);

                if (GetEnableCriticalJournal())
                {
                    _journal.RecordRejected(
                        decision.Ack.messageId,
                        decision.Ack.commandId,
                        decision.Ack.sessionId,
                        decision.Ack.reasonCode);
                }
            }

            return;
        }

        if (decision.ResumeResult != null)
        {
            if (decision.ResumeResult.accepted)
            {
                _recovery.MarkResumed();
                _sessionContext.BindSession(decision.ResumeResult.sessionId, decision.ResumeResult.studentId);
                _sessionContext.MarkConnected();
                ReplayPersistedBootstrapIfNeeded(decision.ResumeResult.shouldReplayPending);
            }
            else
            {
                _recovery.MarkFailed(decision.ResumeResult.reasonCode);

                if (decision.ResumeResult.reasonCode == ReliableProtocolConstants.ReasonSessionExpired)
                {
                    _sessionContext.MarkSessionEnded(decision.ResumeResult.reasonCode);
                }
            }
        }
    }

    private void HandleDuplicateDecision(InboundDecision decision)
    {
        if (decision.Envelope == null)
        {
            return;
        }

        if (GetEnableCriticalJournal())
        {
            _journal.RecordDuplicate(
                decision.Envelope.messageId,
                decision.Envelope.commandId,
                decision.Envelope.sessionId);
        }

        SendAckForEnvelope(
            decision.Envelope,
            ReliableProtocolConstants.AckStatusAccepted,
            ReliableProtocolConstants.ReasonDuplicateMessage);
    }

    private void HandleAckAndRouteDecision(InboundDecision decision)
    {
        if (decision.Envelope == null)
        {
            return;
        }

        try
        {
            if (GetEnableCriticalJournal())
            {
                _journal.RecordReceived(
                    decision.Envelope.messageId,
                    decision.Envelope.commandId,
                    decision.Envelope.sessionId);
            }

            _sessionContext.UpdateLastCriticalInbound(decision.Envelope.messageId);
            _sessionContext.CaptureFromRawMessage(decision.InnerPayload);

            string payloadToRoute = string.IsNullOrWhiteSpace(decision.InnerPayload)
                ? decision.Envelope.commandId
                : decision.InnerPayload;

            _client.RouteInboundLegacyInternal(payloadToRoute);

            if (GetEnableCriticalJournal())
            {
                _journal.RecordApplied(
                    decision.Envelope.messageId,
                    decision.Envelope.commandId,
                    decision.Envelope.sessionId);
            }

            if (decision.Envelope.requiresAck && GetEnableCriticalAck())
            {
                SendAckForEnvelope(
                    decision.Envelope,
                    ReliableProtocolConstants.AckStatusAccepted,
                    ReliableProtocolConstants.ReasonNone);
            }
        }
        catch (Exception exception)
        {
            if (GetEnableCriticalJournal())
            {
                _journal.RecordRejected(
                    decision.Envelope.messageId,
                    decision.Envelope.commandId,
                    decision.Envelope.sessionId,
                    ReliableProtocolConstants.ReasonExecutionFailed);
            }

            SendAckForEnvelope(
                decision.Envelope,
                ReliableProtocolConstants.AckStatusRejected,
                ReliableProtocolConstants.ReasonExecutionFailed);

            Log($"Failed to route reliable inbound message: {exception.Message}", LogType.Error);
        }
    }

    private void ObserveShadowInbound(InboundDecision decision)
    {
        if (decision.DecisionType == InboundDecisionType.AckAndRoute && decision.Envelope != null)
        {
            Log($"Shadow mode observed reliable inbound command {decision.Envelope.commandId}");
        }
        else if (decision.DecisionType == InboundDecisionType.ConsumeOnly && decision.Ack != null)
        {
            Log($"Shadow mode observed reliable ACK {decision.Ack.messageId}");
        }
    }

    private ReliableCommandEnvelope BuildEnvelope(string commandId, string payloadJson)
    {
        DateTime issuedAtUtc = DateTime.UtcNow;
        DateTime expiresAtUtc = issuedAtUtc.AddMinutes(5);

        return new ReliableCommandEnvelope
        {
            messageId = ReliableProtocolUtility.CreateMessageId(),
            commandId = commandId ?? string.Empty,
            sessionId = _sessionContext.SessionId,
            issuedAtUtc = ReliableProtocolUtility.ToUtcString(issuedAtUtc),
            expiresAtUtc = ReliableProtocolUtility.ToUtcString(expiresAtUtc),
            payloadJson = payloadJson ?? string.Empty,
            requiresAck = GetEnableCriticalAck()
        };
    }

    private void SendAckForEnvelope(ReliableCommandEnvelope envelope, string status, string reasonCode)
    {
        if (_client == null || envelope == null || !_client.IsReadyToSend())
        {
            return;
        }

        ReliableCommandAck ack = ReliableProtocolUtility.BuildAck(envelope, status, reasonCode);
        _client.SendJsonStringLegacyInternal(SimpleJsonSerializer.Serialize(ack));
    }

    private bool ShouldAttemptResume()
    {
        if (!_sessionContext.HasSessionContext)
        {
            return false;
        }

        DateTime? disconnectedAtUtc = _sessionContext.LastConnectionLostAtUtc;
        if (!disconnectedAtUtc.HasValue)
        {
            return true;
        }

        return (DateTime.UtcNow - disconnectedAtUtc.Value).TotalSeconds <= GetReconnectRecoveryWindowSeconds();
    }

    private float GetReconnectRecoveryWindowSeconds()
    {
        return _config != null ? Mathf.Max(1f, _config.reconnectRecoveryWindowSeconds) : 30f;
    }

    private float GetHeartbeatIntervalSeconds()
    {
        return _config != null ? Mathf.Max(1f, _config.heartbeatIntervalSeconds) : 5f;
    }

    private float GetIdleTimeoutSeconds()
    {
        return _config != null ? Mathf.Max(1f, _config.idleTimeoutSeconds) : 10f;
    }

    private float GetAckTimeoutSeconds()
    {
        return _config != null ? Mathf.Max(0.5f, _config.ackTimeoutSeconds) : 2f;
    }

    private int GetMaxCriticalRetries()
    {
        return _config != null ? Mathf.Max(1, _config.maxCriticalRetries) : 3;
    }

    private float GetRetryBaseDelaySeconds()
    {
        return _config != null ? Mathf.Max(0.5f, _config.retryBaseDelaySeconds) : 1f;
    }

    private float GetRetryMaxDelaySeconds()
    {
        return _config != null ? Mathf.Max(GetRetryBaseDelaySeconds(), _config.retryMaxDelaySeconds) : 8f;
    }

    private string GetJournalFolderName()
    {
        return _config != null ? _config.journalFolderName : "network-adapter";
    }

    private string GetJournalFileName()
    {
        return _config != null ? _config.journalFileName : "reliable-command-journal.ndjson";
    }

    private string GetSessionContextFileName()
    {
        return _config != null ? _config.sessionContextFileName : "session-context.json";
    }

    private bool GetEnableReliableControl()
    {
        return _config == null || _config.enableReliableControl;
    }

    private bool GetEnableSessionResume()
    {
        return _config == null || _config.enableSessionResume;
    }

    private bool GetEnableCriticalAck()
    {
        return _config == null || _config.enableCriticalAck;
    }

    private bool GetEnableCriticalJournal()
    {
        return _config == null || _config.enableCriticalJournal;
    }

    private void ReplayPersistedBootstrapIfNeeded(bool forceReplay)
    {
        if (_client == null || !_sessionContext.HasBootstrapPayloads)
        {
            return;
        }

        if (!forceReplay && !_client.ShouldReplayPersistedSessionBootstrap())
        {
            return;
        }

        int replayedMessages = 0;

        if (!string.IsNullOrWhiteSpace(_sessionContext.LastSelectedSessionRaw))
        {
            _client.RouteInboundLegacyInternal(_sessionContext.LastSelectedSessionRaw);
            replayedMessages++;
        }

        if (replayedMessages > 0)
        {
            _client.MarkPersistedSessionBootstrapReplayed();
            Log($"Replayed {replayedMessages} persisted session bootstrap message(s)");
        }
    }

    private void Log(string message, LogType logType = LogType.Log)
    {
        bool shouldLog = _logVerbose || (_config != null && _config.logVerbose);
        if (!shouldLog)
        {
            return;
        }

        switch (logType)
        {
            case LogType.Warning:
                Debug.LogWarning($"[NetworkAdapter] {message}");
                break;
            case LogType.Error:
                Debug.LogError($"[NetworkAdapter] {message}");
                break;
            default:
                Debug.Log($"[NetworkAdapter] {message}");
                break;
        }
    }
}
