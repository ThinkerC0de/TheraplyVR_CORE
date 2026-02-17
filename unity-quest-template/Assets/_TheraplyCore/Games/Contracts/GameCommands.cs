using System;
using System.Collections.Generic;

namespace TheraplyCore.Games.Contracts
{
    /// <summary>
    /// Canonical command IDs used on the wire between controller and runtime.
    /// </summary>
    public static class MiniGameCommandIds
    {
        public const string StartGame = "START_GAME";
        public const string PauseGame = "PAUSE_GAME";
        public const string ResumeGame = "RESUME_GAME";
        public const string StopGame = "STOP_GAME";
        public const string EndSession = "END_SESSION";
        public const string CommandAck = "COMMAND_ACK";
        public const string UpdateConfig = "UPDATE_CONFIG";
        public const string SessionStateUpdate = "SESSION_STATE_UPDATE";
        public const string RuntimeStatusUpdate = "RUNTIME_STATUS_UPDATE";
        public const string SessionWatchdogHeartbeat = "SESSION_WATCHDOG_HEARTBEAT";
        public const string ManualResync = "MANUAL_RESYNC";
        public const string ManualResyncReport = "MANUAL_RESYNC_REPORT";
    }

    public static class CriticalCommandIds
    {
        private static readonly HashSet<string> Values =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                MiniGameCommandIds.StartGame,
                MiniGameCommandIds.PauseGame,
                MiniGameCommandIds.ResumeGame,
                MiniGameCommandIds.StopGame,
                MiniGameCommandIds.EndSession,
            };

        public static bool IsCritical(string commandId)
        {
            return !string.IsNullOrWhiteSpace(commandId) && Values.Contains(commandId);
        }
    }

    /// <summary>
    /// Marker interface for typed game commands.
    /// </summary>
    public interface IGameCommand
    {
        string CorrelationId { get; }
    }

    [Serializable]
    public sealed class StartGameCommand : IGameCommand
    {
        public string correlationId;
        public string gameId;

        public string CorrelationId => correlationId;
    }

    [Serializable]
    public sealed class PauseGameCommand : IGameCommand
    {
        public string correlationId;
        public string gameId;

        public string CorrelationId => correlationId;
    }

    [Serializable]
    public sealed class ResumeGameCommand : IGameCommand
    {
        public string correlationId;
        public string gameId;

        public string CorrelationId => correlationId;
    }

    [Serializable]
    public sealed class StopGameCommand : IGameCommand
    {
        public string correlationId;
        public string gameId;
        public string reason;

        public string CorrelationId => correlationId;
    }

    [Serializable]
    public sealed class EndSessionCommand : IGameCommand
    {
        public string correlationId;
        public string reason;

        public string CorrelationId => correlationId;
    }

    [Serializable]
    public sealed class UpdateConfigCommand<TConfig> : IGameCommand where TConfig : IMiniGameConfig
    {
        public string correlationId;
        public string gameId;
        public TConfig config;

        public string CorrelationId => correlationId;
        public TConfig Config => config;
    }

    [Serializable]
    public sealed class SessionStateUpdateCommand : IGameCommand
    {
        public string correlationId;
        public string sessionId;
        public string patientId;
        public string therapistId;
        public string state;
        public string previousState;
        public string reasonCode;
        public long changedAtUnixMs;

        public string CorrelationId => correlationId;
    }

    public static class RuntimeStatusValues
    {
        public const string Connected = "connected";
        public const string Playing = "playing";
        public const string Paused = "paused";
        public const string Interrupted = "interrupted";
        public const string SyncPending = "sync_pending";
    }

    [Serializable]
    public sealed class RuntimeStatusUpdateCommand : IGameCommand
    {
        public string correlationId;
        public string sessionId;
        public string patientId;
        public string therapistId;
        public string status;
        public string previousStatus;
        public string reasonCode;
        public long changedAtUnixMs;
        public int pendingQueueSize;

        public string CorrelationId => correlationId;
    }

    [Serializable]
    public sealed class SessionWatchdogHeartbeatCommand : IGameCommand
    {
        public string correlationId;
        public string sessionId;
        public string patientId;
        public string therapistId;
        public string sessionState;
        public string runtimeStatus;
        public string healthCode;
        public bool healthy;
        public string activeGameId;
        public string activeGameState;
        public long heartbeatUnixMs;
        public long lastHealthyUnixMs;
        public int pendingQueueSize;
        public int expectedIntervalMs;
        public int staleAfterMs;

        public string CorrelationId => correlationId;
    }

    [Serializable]
    public sealed class ManualResyncCommand : IGameCommand
    {
        public string correlationId;
        public string sessionId;
        public bool includeSyncedEvents;
        public string requestedBy;
        public string reasonCode;

        public string CorrelationId => correlationId;
    }

    [Serializable]
    public sealed class ManualResyncReportCommand : IGameCommand
    {
        public string correlationId;
        public bool success;
        public string reasonCode;
        public string sessionId;
        public string requestedBy;
        public string requestedReasonCode;
        public string requestedAtUtc;
        public bool includeSyncedEvents;
        public int targetedEvents;
        public int outboxRowsUpdated;
        public bool uploadCycleTriggered;
        public int localEvents;
        public string beforeReasonCode;
        public int beforeMissingOnServerCount;
        public int beforeMissingOnDeviceCount;
        public int beforeOutboxPending;
        public string afterReasonCode;
        public int afterMissingOnServerCount;
        public int afterMissingOnDeviceCount;
        public int afterOutboxPending;
        public string targetedSequencePreview;
        public string details;

        public string CorrelationId => correlationId;
    }

    [Serializable]
    public sealed class CriticalCommandEnvelope
    {
        public string messageId;
        public string sessionId;
        public string commandId;
        public string issuedAtUtc;
        public string expiresAtUtc;
        public string payloadJson;
    }

    public static class CommandAckStatus
    {
        public const string Ack = "ACK";
        public const string Nack = "NACK";
    }

    [Serializable]
    public sealed class CriticalCommandAckPayload
    {
        public string messageId;
        public string commandId;
        public string sessionId;
        public string status;
        public string reasonCode;
        public string processedAtUtc;
    }
}
