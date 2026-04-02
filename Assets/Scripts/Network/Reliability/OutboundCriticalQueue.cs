using System;
using System.Collections.Generic;

public class QueuedCriticalEnvelope
{
    public ReliableCommandEnvelope Envelope;
    public int AttemptCount;
    public bool AwaitingAck;
    public string LastReasonCode;
    public DateTime NextAttemptAtUtc;
    public DateTime ExpiresAtUtc;
}

public class OutboundCriticalQueue
{
    private readonly List<QueuedCriticalEnvelope> _entries = new List<QueuedCriticalEnvelope>();
    private readonly int _maxRetries;
    private readonly float _ackTimeoutSeconds;
    private readonly float _retryBaseDelaySeconds;
    private readonly float _retryMaxDelaySeconds;

    public OutboundCriticalQueue(
        int maxRetries,
        float ackTimeoutSeconds,
        float retryBaseDelaySeconds,
        float retryMaxDelaySeconds)
    {
        _maxRetries = Math.Max(1, maxRetries);
        _ackTimeoutSeconds = Math.Max(0.5f, ackTimeoutSeconds);
        _retryBaseDelaySeconds = Math.Max(0.5f, retryBaseDelaySeconds);
        _retryMaxDelaySeconds = Math.Max(_retryBaseDelaySeconds, retryMaxDelaySeconds);
    }

    public int Count => _entries.Count;

    public void Enqueue(ReliableCommandEnvelope envelope, DateTime nowUtc)
    {
        if (envelope == null || string.IsNullOrWhiteSpace(envelope.messageId))
        {
            return;
        }

        if (FindEntry(envelope.messageId) != null)
        {
            return;
        }

        _entries.Add(new QueuedCriticalEnvelope
        {
            Envelope = envelope,
            AttemptCount = 0,
            AwaitingAck = false,
            LastReasonCode = ReliableProtocolConstants.ReasonNone,
            NextAttemptAtUtc = nowUtc,
            ExpiresAtUtc = ResolveExpirationUtc(envelope, nowUtc)
        });
    }

    public List<QueuedCriticalEnvelope> CollectDueEntries(DateTime nowUtc)
    {
        List<QueuedCriticalEnvelope> due = new List<QueuedCriticalEnvelope>();

        for (int i = 0; i < _entries.Count; i++)
        {
            QueuedCriticalEnvelope entry = _entries[i];
            if (entry == null || entry.Envelope == null)
            {
                continue;
            }

            if (entry.AttemptCount >= _maxRetries)
            {
                continue;
            }

            if (entry.ExpiresAtUtc <= nowUtc)
            {
                continue;
            }

            if (entry.NextAttemptAtUtc <= nowUtc)
            {
                due.Add(entry);
            }
        }

        return due;
    }

    public List<QueuedCriticalEnvelope> DropExpired(DateTime nowUtc)
    {
        List<QueuedCriticalEnvelope> dropped = new List<QueuedCriticalEnvelope>();

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            QueuedCriticalEnvelope entry = _entries[i];
            if (entry == null)
            {
                _entries.RemoveAt(i);
                continue;
            }

            if (entry.ExpiresAtUtc <= nowUtc || entry.AttemptCount >= _maxRetries)
            {
                dropped.Add(entry);
                _entries.RemoveAt(i);
            }
        }

        return dropped;
    }

    public void MarkAttempt(string messageId, DateTime nowUtc)
    {
        QueuedCriticalEnvelope entry = FindEntry(messageId);
        if (entry == null)
        {
            return;
        }

        entry.AttemptCount++;
        entry.AwaitingAck = true;
        entry.NextAttemptAtUtc = nowUtc.AddSeconds(ResolveRetryDelay(entry.AttemptCount));
    }

    public void MarkSendFailure(string messageId, DateTime nowUtc, string reasonCode)
    {
        QueuedCriticalEnvelope entry = FindEntry(messageId);
        if (entry == null)
        {
            return;
        }

        entry.AwaitingAck = false;
        entry.LastReasonCode = string.IsNullOrWhiteSpace(reasonCode)
            ? ReliableProtocolConstants.ReasonTransportFailure
            : reasonCode;
        entry.NextAttemptAtUtc = nowUtc.AddSeconds(_retryBaseDelaySeconds);
    }

    public void MarkAck(string messageId)
    {
        Remove(messageId);
    }

    public void MarkNack(string messageId, string reasonCode)
    {
        QueuedCriticalEnvelope entry = FindEntry(messageId);
        if (entry != null)
        {
            entry.LastReasonCode = reasonCode ?? ReliableProtocolConstants.AckStatusRejected;
        }

        Remove(messageId);
    }

    private void Remove(string messageId)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_entries[i].Envelope.messageId, messageId, StringComparison.OrdinalIgnoreCase))
            {
                _entries.RemoveAt(i);
                return;
            }
        }
    }

    private QueuedCriticalEnvelope FindEntry(string messageId)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            QueuedCriticalEnvelope entry = _entries[i];
            if (entry != null &&
                entry.Envelope != null &&
                string.Equals(entry.Envelope.messageId, messageId, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private DateTime ResolveExpirationUtc(ReliableCommandEnvelope envelope, DateTime nowUtc)
    {
        if (!string.IsNullOrWhiteSpace(envelope.expiresAtUtc))
        {
            return ReliableProtocolUtility.ParseUtcOrDefault(envelope.expiresAtUtc, nowUtc.AddMinutes(5));
        }

        return nowUtc.AddMinutes(5);
    }

    private float ResolveRetryDelay(int attemptCount)
    {
        if (attemptCount <= 1)
        {
            return _ackTimeoutSeconds;
        }

        double exponentialDelay = _retryBaseDelaySeconds * Math.Pow(2d, attemptCount - 2);
        return (float)Math.Min(_retryMaxDelaySeconds, exponentialDelay);
    }
}
