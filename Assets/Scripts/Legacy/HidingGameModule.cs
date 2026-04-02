using UnityEngine;

public sealed class HidingGameModule : LegacyGameModuleBase
{
    public const string Id = "hiding_game";

    [SerializeField] private HidingGameCommunication _communicator;

    public override string GameId => Id;

    protected override CommunicationAbstractClass GetCommunicator() => _communicator;
}
