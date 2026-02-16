using System;

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
        public const string UpdateConfig = "UPDATE_CONFIG";
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
    public sealed class UpdateConfigCommand<TConfig> : IGameCommand where TConfig : IMiniGameConfig
    {
        public string correlationId;
        public string gameId;
        public TConfig config;

        public string CorrelationId => correlationId;
        public TConfig Config => config;
    }
}
