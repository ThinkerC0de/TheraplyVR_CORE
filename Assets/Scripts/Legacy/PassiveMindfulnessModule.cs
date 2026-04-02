using UnityEngine;

public sealed class PassiveMindfulnessModule : LegacyGameModuleBase
{
    public const string Id = "passive_mindfulness";

    [SerializeField] private PassiveMindfulnessCommunication _communicator;

    public override string GameId => Id;

    protected override CommunicationAbstractClass GetCommunicator() => _communicator;
}
