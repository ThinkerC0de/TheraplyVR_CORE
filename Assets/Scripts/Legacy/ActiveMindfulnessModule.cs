using UnityEngine;

public sealed class ActiveMindfulnessModule : LegacyGameModuleBase
{
    public const string Id = "active_mindfulness";

    [SerializeField] private ActiveMindfulnessCommunication _communicator;

    public override string GameId => Id;

    protected override CommunicationAbstractClass GetCommunicator() => _communicator;
}
