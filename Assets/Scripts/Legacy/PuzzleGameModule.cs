using UnityEngine;

public sealed class PuzzleGameModule : LegacyGameModuleBase
{
    public const string Id = "puzzle";

    [SerializeField] private PuzzleCommunication _communicator;

    public override string GameId => Id;

    protected override CommunicationAbstractClass GetCommunicator() => _communicator;
}
