using UnityEngine;

public sealed class ButterfliesGameModule : LegacyGameModuleBase
{
    public const string Id = "butterflies";

    [SerializeField] private ButterfliesGameCommunicator _communicator;

    public override string GameId => Id;

    protected override CommunicationAbstractClass GetCommunicator() => _communicator;
}
