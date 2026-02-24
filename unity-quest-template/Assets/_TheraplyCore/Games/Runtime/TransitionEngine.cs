using System;
using GameContracts = TheraplyCore.Games.Contracts;

namespace TheraplyCore.Games.Runtime
{
    public enum TransitionTrigger
    {
        Success = 0,
        Fail = 1,
        Timeout = 2,
        Branch = 3,
    }

    public struct TransitionDecision
    {
        public bool resolved;
        public bool terminalComplete;
        public bool terminalFail;
        public string nextNodeId;
        public string reasonCode;
    }

    /// <summary>
    /// Deterministic transition resolver for task graph nodes.
    /// </summary>
    public sealed class TransitionEngine
    {
        public TransitionDecision Resolve(
            GameContracts.TaskGraphNodeDefinition node,
            TransitionTrigger trigger)
        {
            if (node == null)
            {
                return new TransitionDecision
                {
                    resolved = false,
                    terminalComplete = false,
                    terminalFail = true,
                    nextNodeId = string.Empty,
                    reasonCode = "ACTIVE_NODE_MISSING",
                };
            }

            var nodeType = Normalize(node.nodeType);
            if (string.Equals(nodeType, Normalize(GameContracts.TaskGraphNodeTypes.Complete), StringComparison.OrdinalIgnoreCase))
            {
                return new TransitionDecision
                {
                    resolved = true,
                    terminalComplete = true,
                    terminalFail = false,
                    nextNodeId = string.Empty,
                    reasonCode = "TERMINAL_COMPLETE_NODE",
                };
            }

            if (string.Equals(nodeType, Normalize(GameContracts.TaskGraphNodeTypes.Fail), StringComparison.OrdinalIgnoreCase))
            {
                return new TransitionDecision
                {
                    resolved = true,
                    terminalComplete = false,
                    terminalFail = true,
                    nextNodeId = string.Empty,
                    reasonCode = "TERMINAL_FAIL_NODE",
                };
            }

            var nextNodeId = string.Empty;
            switch (trigger)
            {
                case TransitionTrigger.Success:
                    nextNodeId = Normalize(node.nextOnSuccess);
                    break;
                case TransitionTrigger.Fail:
                    nextNodeId = Normalize(node.nextOnFail);
                    break;
                case TransitionTrigger.Timeout:
                    nextNodeId = Normalize(node.nextOnTimeout);
                    break;
                case TransitionTrigger.Branch:
                    nextNodeId = Normalize(node.nextOnSuccess);
                    break;
            }

            if (string.IsNullOrWhiteSpace(nextNodeId))
            {
                return new TransitionDecision
                {
                    resolved = true,
                    terminalComplete = trigger != TransitionTrigger.Fail,
                    terminalFail = trigger == TransitionTrigger.Fail,
                    nextNodeId = string.Empty,
                    reasonCode = "TERMINAL_BY_EMPTY_ROUTE",
                };
            }

            return new TransitionDecision
            {
                resolved = true,
                terminalComplete = false,
                terminalFail = false,
                nextNodeId = nextNodeId,
                reasonCode = "ROUTE_NEXT_NODE",
            };
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
