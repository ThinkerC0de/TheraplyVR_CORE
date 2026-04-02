using GameContracts = TheraplyCore.Games.Contracts;

/// <summary>
/// IGameConfig wrapper for legacy Playground games.
/// Carries the raw JSON payload from START_GAME.gameConfigJson
/// which gets forwarded to CommunicationAbstractClass.GetJsonFromPreviewApp.
/// </summary>
public sealed class LegacyGameConfig : GameContracts.IGameConfig
{
    public string GameId { get; }
    public int Version { get; }
    public string RawJson { get; }

    public LegacyGameConfig(string gameId, string rawJson, int version = 1)
    {
        GameId = gameId ?? string.Empty;
        Version = version;
        RawJson = rawJson ?? string.Empty;
    }
}
