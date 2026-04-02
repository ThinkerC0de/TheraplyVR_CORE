public enum InboundDecisionType
{
    PassToLegacy = 0,
    ConsumeOnly = 1,
    AckAndRoute = 2,
    RejectDuplicate = 3
}

public class InboundDecision
{
    public InboundDecisionType DecisionType;
    public string RawMessage;
    public string InnerPayload;
    public ReliableCommandEnvelope Envelope;
    public ReliableCommandAck Ack;
    public ReliableResumeResult ResumeResult;
}

public class InboundMessageGate
{
    private readonly ReliableCommandJournal _journal;

    public InboundMessageGate(ReliableCommandJournal journal)
    {
        _journal = journal;
    }

    public InboundDecision Evaluate(string rawMessage)
    {
        InboundDecision decision = new InboundDecision
        {
            DecisionType = InboundDecisionType.PassToLegacy,
            RawMessage = rawMessage
        };

        if (!ReliableProtocolUtility.IsReliableProtocolMessage(rawMessage))
        {
            return decision;
        }

        if (TryParseAck(rawMessage, out ReliableCommandAck ack))
        {
            decision.DecisionType = InboundDecisionType.ConsumeOnly;
            decision.Ack = ack;
            return decision;
        }

        if (TryParseResumeResult(rawMessage, out ReliableResumeResult resumeResult))
        {
            decision.DecisionType = InboundDecisionType.ConsumeOnly;
            decision.ResumeResult = resumeResult;
            return decision;
        }

        string messageType = ReliableProtocolUtility.ResolveMessageType(rawMessage);
        if (messageType == ReliableProtocolConstants.HeartbeatMessageType)
        {
            decision.DecisionType = InboundDecisionType.ConsumeOnly;
            return decision;
        }

        if (!TryUnwrapEnvelope(rawMessage, out string innerPayload, out ReliableCommandEnvelope envelope))
        {
            return decision;
        }

        decision.Envelope = envelope;
        decision.InnerPayload = string.IsNullOrWhiteSpace(innerPayload) ? envelope.commandId : innerPayload;

        if (_journal != null &&
            _journal.TryGetLatest(envelope.messageId, out JournalRecord existing) &&
            (existing.status == ReliableCommandJournal.StatusApplied ||
             existing.status == ReliableCommandJournal.StatusDuplicate))
        {
            decision.DecisionType = InboundDecisionType.RejectDuplicate;
            return decision;
        }

        decision.DecisionType = InboundDecisionType.AckAndRoute;
        return decision;
    }

    public bool TryParseAck(string rawMessage, out ReliableCommandAck ack)
    {
        ack = null;
        return ReliableProtocolUtility.ResolveMessageType(rawMessage) == ReliableProtocolConstants.AckMessageType &&
               ReliableProtocolUtility.TryDeserialize(rawMessage, out ack);
    }

    public bool TryParseResumeResult(string rawMessage, out ReliableResumeResult result)
    {
        result = null;
        return ReliableProtocolUtility.ResolveMessageType(rawMessage) == ReliableProtocolConstants.ResumeResultMessageType &&
               ReliableProtocolUtility.TryDeserialize(rawMessage, out result);
    }

    public bool TryUnwrapEnvelope(string rawMessage, out string innerPayload, out ReliableCommandEnvelope envelope)
    {
        innerPayload = string.Empty;
        envelope = null;

        if (ReliableProtocolUtility.ResolveMessageType(rawMessage) != ReliableProtocolConstants.CommandMessageType)
        {
            return false;
        }

        if (!ReliableProtocolUtility.TryDeserialize(rawMessage, out envelope))
        {
            return false;
        }

        innerPayload = envelope.payloadJson ?? string.Empty;
        return true;
    }
}
