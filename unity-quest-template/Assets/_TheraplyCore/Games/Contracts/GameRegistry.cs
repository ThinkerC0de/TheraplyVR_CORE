namespace TheraplyCore.Games.Contracts
{
    /// <summary>
    /// Registry abstraction so new games can be added without changing core flow code.
    /// </summary>
    public interface IGameRegistry
    {
        bool TryResolve(string gameId, out IGameModule module);
    }
}
