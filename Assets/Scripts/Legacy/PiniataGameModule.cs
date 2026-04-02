using UnityEngine;

/// <summary>
/// CORE adapter for the Piniata game.
/// PILOT implementation of LegacyGameModuleBase pattern.
///
/// Scene setup:
///   - Add this component to a GameObject in Piniata Scene.unity
///   - Drag PiniataCommunication reference into _communicator field
/// </summary>
public sealed class PiniataGameModule : LegacyGameModuleBase
{
    public const string Id = "piniata";

    [SerializeField] private PiniataCommunication _communicator;

    public override string GameId => Id;

    protected override CommunicationAbstractClass GetCommunicator() => _communicator;
}
