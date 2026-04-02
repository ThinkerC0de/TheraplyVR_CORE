using UnityEngine;

/// <summary>
/// Christmas scene reuses BothHandsTrainingCommunication.
/// </summary>
public sealed class ChristmasGameModule : LegacyGameModuleBase
{
    public const string Id = "christmas";

    [SerializeField] private BothHandsTrainingCommunication _communicator;

    public override string GameId => Id;

    protected override CommunicationAbstractClass GetCommunicator() => _communicator;
}
