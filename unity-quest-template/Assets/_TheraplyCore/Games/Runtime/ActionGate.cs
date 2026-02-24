using System;
using System.Collections.Generic;
using GameContracts = TheraplyCore.Games.Contracts;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Gate component deciding whether an incoming action is allowed in the active node.
    /// </summary>
    public sealed class ActionGate
    {
        private readonly ActionValidator _actionValidator = new ActionValidator();

        public GameContracts.ActionValidationResult Evaluate(
            GameContracts.ActionIntent intent,
            GameContracts.TaskGraphNodeDefinition activeNode,
            GameContracts.ActionContext context)
        {
            if (activeNode == null)
            {
                return GameContracts.ActionValidationResult.Rejected("ACTIVE_NODE_MISSING");
            }

            if (!string.Equals(
                    Normalize(activeNode.nodeType),
                    Normalize(GameContracts.TaskGraphNodeTypes.Action),
                    StringComparison.OrdinalIgnoreCase))
            {
                return GameContracts.ActionValidationResult.Rejected("ACTION_NOT_ALLOWED_IN_NODE");
            }

            if (intent == null || string.IsNullOrWhiteSpace(intent.actionId))
            {
                return GameContracts.ActionValidationResult.Rejected("ACTION_ID_REQUIRED");
            }

            if (intent.channelId != null && intent.channelId.Length > 0)
            {
                if (!GameContracts.SessionFlowChannelIds.IsSupported(intent.channelId))
                {
                    return GameContracts.ActionValidationResult.Rejected("CHANNEL_ID_UNSUPPORTED");
                }
            }

            var allowedActions = activeNode.allowedActions;
            if (allowedActions == null || allowedActions.Count == 0)
            {
                return GameContracts.ActionValidationResult.Rejected("ACTION_NOT_ALLOWED_IN_NODE");
            }

            var normalizedActionId = Normalize(intent.actionId);
            for (var i = 0; i < allowedActions.Count; i++)
            {
                var allowed = allowedActions[i];
                if (allowed == null || string.IsNullOrWhiteSpace(allowed.actionId))
                {
                    continue;
                }

                if (!string.Equals(
                        Normalize(allowed.actionId),
                        normalizedActionId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return _actionValidator.Validate(intent, context, allowed);
            }

            return GameContracts.ActionValidationResult.Rejected("ACTION_NOT_ALLOWED_IN_NODE");
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
