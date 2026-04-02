using UnityEngine;

public sealed class CodingGameModule : LegacyGameModuleBase
{
    public const string Id = "coding";

    [SerializeField] private CodingGameCommunication _communicator;

    public override string GameId => Id;

    protected override CommunicationAbstractClass GetCommunicator() => _communicator;
}
