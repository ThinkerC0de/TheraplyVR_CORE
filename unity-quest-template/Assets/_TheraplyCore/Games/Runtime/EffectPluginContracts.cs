using System.Collections.Generic;
using GameContracts = TheraplyCore.Games.Contracts;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Runtime effect plugin contract.
    /// </summary>
    public interface IEffectPlugin
    {
        string EffectId { get; }

        bool Execute(
            GameContracts.EffectDefinition effect,
            EffectExecutionContext context,
            EffectRuntimeServices services,
            out string reasonCode);
    }

    public sealed class EffectExecutionContext
    {
        public string gameId = string.Empty;
        public string flowId = string.Empty;
        public string sessionId = string.Empty;
        public string nodeId = string.Empty;
        public string trigger = string.Empty;
        public string transitionReason = string.Empty;
        public string actionId = string.Empty;
        public string actionDecision = string.Empty;
        public string actionReasonCode = string.Empty;
        public List<GameContracts.KeyValuePairString> state = new List<GameContracts.KeyValuePairString>();
    }

    public sealed class EffectRuntimeServices
    {
        public FlowBindingRegistry bindings;
        public SceneRuntimeController sceneRuntime;
        public GameFeedbackService feedback;
        public GameContracts.INarratorService narrator;
        public GameContracts.ILocalizationService localization;
        public GameContracts.ICalendarService calendar;
    }
}
