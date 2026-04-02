using System;

public class ConnectionHealthMonitor
{
    private DateTime _lastInboundUtc = DateTime.MinValue;
    private DateTime _lastOutboundUtc = DateTime.MinValue;
    private DateTime _lastHeartbeatUtc = DateTime.MinValue;

    public void Reset()
    {
        _lastInboundUtc = DateTime.MinValue;
        _lastOutboundUtc = DateTime.MinValue;
        _lastHeartbeatUtc = DateTime.MinValue;
    }

    public void ResetConnected()
    {
        DateTime nowUtc = DateTime.UtcNow;
        _lastInboundUtc = nowUtc;
        _lastOutboundUtc = nowUtc;
        _lastHeartbeatUtc = DateTime.MinValue;
    }

    public void RecordInbound()
    {
        _lastInboundUtc = DateTime.UtcNow;
    }

    public void RecordOutbound()
    {
        _lastOutboundUtc = DateTime.UtcNow;
    }

    public bool ShouldSendHeartbeat(float intervalSeconds)
    {
        if (intervalSeconds <= 0f)
        {
            return false;
        }

        DateTime nowUtc = DateTime.UtcNow;
        if (_lastHeartbeatUtc == DateTime.MinValue)
        {
            return true;
        }

        return (nowUtc - _lastHeartbeatUtc).TotalSeconds >= intervalSeconds;
    }

    public void MarkHeartbeatSent()
    {
        _lastHeartbeatUtc = DateTime.UtcNow;
        _lastOutboundUtc = _lastHeartbeatUtc;
    }

    public bool IsControlIdle(float idleTimeoutSeconds)
    {
        if (idleTimeoutSeconds <= 0f)
        {
            return false;
        }

        DateTime referenceUtc = _lastInboundUtc > _lastOutboundUtc ? _lastInboundUtc : _lastOutboundUtc;
        if (referenceUtc == DateTime.MinValue)
        {
            return false;
        }

        return (DateTime.UtcNow - referenceUtc).TotalSeconds >= idleTimeoutSeconds;
    }
}
