using UnityEngine;

public sealed class BothHandsGameModule : LegacyGameModuleBase
{
    public const string Id = "both_hands";

    [SerializeField] private BothHandsTrainingCommunication _communicator;

    public override string GameId => Id;

    protected override CommunicationAbstractClass GetCommunicator() => _communicator;
}
