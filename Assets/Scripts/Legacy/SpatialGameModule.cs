using UnityEngine;

public sealed class SpatialGameModule : LegacyGameModuleBase
{
    public const string Id = "spatial";

    [SerializeField] private SpatialTrainingCommunication _communicator;

    public override string GameId => Id;

    protected override CommunicationAbstractClass GetCommunicator() => _communicator;
}
