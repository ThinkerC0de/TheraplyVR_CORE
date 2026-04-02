using System;

public enum RecoveryState
{
    Idle = 0,
    Connected = 1,
    Degraded = 2,
    Reconnecting = 3,
    Reattaching = 4,
    Resumed = 5,
    Failed = 6
}

public class RecoveryStateMachine
{
    public RecoveryState CurrentState { get; private set; } = RecoveryState.Idle;
    public string LastReason { get; private set; } = string.Empty;
    public DateTime LastChangedAtUtc { get; private set; } = DateTime.UtcNow;

    public void MarkConnected()
    {
        Transition(RecoveryState.Connected, string.Empty);
    }

    public void MarkDisconnected(string reason)
    {
        Transition(RecoveryState.Reconnecting, reason);
    }

    public void MarkDegraded(string reason)
    {
        Transition(RecoveryState.Degraded, reason);
    }

    public void BeginReattach()
    {
        Transition(RecoveryState.Reattaching, LastReason);
    }

    public void MarkResumed()
    {
        Transition(RecoveryState.Resumed, string.Empty);
    }

    public void MarkFailed(string reason)
    {
        Transition(RecoveryState.Failed, reason);
    }

    public bool IsRecovering()
    {
        return CurrentState == RecoveryState.Degraded ||
               CurrentState == RecoveryState.Reconnecting ||
               CurrentState == RecoveryState.Reattaching;
    }

    private void Transition(RecoveryState nextState, string reason)
    {
        CurrentState = nextState;
        LastReason = reason ?? string.Empty;
        LastChangedAtUtc = DateTime.UtcNow;
    }
}
