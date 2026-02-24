using System;
using System.Collections.Generic;
using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;

namespace TheraplyCore.Games.Runtime
{
    public enum TaskGraphRunState
    {
        NotInitialized = 0,
        Ready = 1,
        Running = 2,
        Completed = 3,
        Failed = 4,
    }

    /// <summary>
    /// Deterministic task graph runtime.
    /// Executes one active node at a time and routes by success/fail/timeout contracts.
    /// </summary>
    public sealed class TaskGraphRunner
    {
        private readonly Dictionary<string, GameContracts.TaskGraphNodeDefinition> _nodesById =
            new Dictionary<string, GameContracts.TaskGraphNodeDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly ActionGate _actionGate = new ActionGate();
        private readonly TransitionEngine _transitionEngine = new TransitionEngine();
        private ActionPluginRegistry _pluginRegistry;
        private string _runtimeGameId = string.Empty;
        private string _runtimeFlowId = string.Empty;
        private string _runtimeSessionId = string.Empty;
        private string _runtimeControlMode = GameContracts.SessionFlowControlModes.Hybrid;

        private GameContracts.TaskGraphDefinition _graph;
        private GameContracts.TaskGraphNodeDefinition _activeNode;
        private float _activeNodeEnteredAtSec;
        private TaskGraphRunState _state = TaskGraphRunState.NotInitialized;

        public event Action<GameContracts.TaskGraphNodeDefinition, string> NodeEntered;
        public event Action<TaskGraphRunState, string> GraphCompleted;

        public TaskGraphRunState State => _state;
        public string ActiveNodeId => _activeNode == null ? string.Empty : _activeNode.nodeId;
        public GameContracts.TaskGraphNodeDefinition ActiveNode => _activeNode;

        public void SetPluginRegistry(ActionPluginRegistry pluginRegistry)
        {
            _pluginRegistry = pluginRegistry;
        }

        public void SetRuntimeContext(
            string gameId,
            string flowId,
            string sessionId,
            string controlMode)
        {
            _runtimeGameId = Normalize(gameId);
            _runtimeFlowId = Normalize(flowId);
            _runtimeSessionId = Normalize(sessionId);
            _runtimeControlMode = GameContracts.SessionFlowControlModes.NormalizeOrDefault(controlMode);
        }

        public bool Initialize(GameContracts.TaskGraphDefinition graph, out string reasonCode)
        {
            reasonCode = string.Empty;
            _nodesById.Clear();
            _graph = null;
            _activeNode = null;
            _activeNodeEnteredAtSec = 0f;

            if (graph == null)
            {
                _state = TaskGraphRunState.NotInitialized;
                reasonCode = GameContracts.SessionFlowDefinitionReasonCodes.TaskGraphRequired;
                return false;
            }

            if (string.IsNullOrWhiteSpace(graph.entryNodeId))
            {
                _state = TaskGraphRunState.NotInitialized;
                reasonCode = GameContracts.SessionFlowDefinitionReasonCodes.EntryNodeIdRequired;
                return false;
            }

            if (graph.nodes != null)
            {
                for (var i = 0; i < graph.nodes.Count; i++)
                {
                    var node = graph.nodes[i];
                    if (node == null || string.IsNullOrWhiteSpace(node.nodeId))
                    {
                        _state = TaskGraphRunState.NotInitialized;
                        reasonCode = GameContracts.SessionFlowDefinitionReasonCodes.NodeIdRequired;
                        return false;
                    }

                    var normalizedId = node.nodeId.Trim();
                    if (_nodesById.ContainsKey(normalizedId))
                    {
                        _state = TaskGraphRunState.NotInitialized;
                        reasonCode = GameContracts.SessionFlowDefinitionReasonCodes.NodeIdDuplicate;
                        return false;
                    }

                    _nodesById[normalizedId] = node;
                }
            }

            if (!_nodesById.ContainsKey(graph.entryNodeId.Trim()))
            {
                _state = TaskGraphRunState.NotInitialized;
                reasonCode = GameContracts.SessionFlowDefinitionReasonCodes.EntryNodeNotFound;
                return false;
            }

            _graph = graph;
            _state = TaskGraphRunState.Ready;
            return true;
        }

        public void Reset()
        {
            _nodesById.Clear();
            _graph = null;
            _activeNode = null;
            _activeNodeEnteredAtSec = 0f;
            _runtimeGameId = string.Empty;
            _runtimeFlowId = string.Empty;
            _runtimeSessionId = string.Empty;
            _runtimeControlMode = GameContracts.SessionFlowControlModes.Hybrid;
            _state = TaskGraphRunState.NotInitialized;
        }

        public bool Start(float nowElapsedSec, out string reasonCode)
        {
            reasonCode = string.Empty;
            if (_state != TaskGraphRunState.Ready || _graph == null)
            {
                reasonCode = "TASK_GRAPH_NOT_READY";
                return false;
            }

            if (!TryEnterNode(_graph.entryNodeId, nowElapsedSec, "GRAPH_START", out reasonCode))
            {
                return false;
            }

            _state = TaskGraphRunState.Running;
            EvaluateImmediateNodeRouting(nowElapsedSec, out _);
            return true;
        }

        public bool Tick(float nowElapsedSec, out string reasonCode)
        {
            reasonCode = string.Empty;
            if (_state != TaskGraphRunState.Running || _activeNode == null)
            {
                return false;
            }

            var timeoutSec = Mathf.Max(0f, _activeNode.timeoutSec);
            if (timeoutSec > 0f && nowElapsedSec - _activeNodeEnteredAtSec >= timeoutSec)
            {
                if (!RouteByTrigger(TransitionTrigger.Timeout, nowElapsedSec, "NODE_TIMEOUT", out reasonCode))
                {
                    return false;
                }
            }

            EvaluateImmediateNodeRouting(nowElapsedSec, out _);
            return true;
        }

        public bool SubmitAction(
            GameContracts.ActionIntent intent,
            float nowElapsedSec,
            out GameContracts.ActionValidationResult validationResult,
            out string reasonCode)
        {
            validationResult = GameContracts.ActionValidationResult.Rejected("ACTION_NOT_ALLOWED_IN_STATE");
            reasonCode = string.Empty;

            if (_state != TaskGraphRunState.Running || _activeNode == null)
            {
                reasonCode = "TASK_GRAPH_NOT_RUNNING";
                return false;
            }

            if (!string.Equals(_activeNode.nodeType, GameContracts.TaskGraphNodeTypes.Action, StringComparison.OrdinalIgnoreCase))
            {
                validationResult = GameContracts.ActionValidationResult.Rejected("ACTION_NOT_ALLOWED_IN_NODE");
                reasonCode = "ACTION_NOT_ALLOWED_IN_NODE";
                return true;
            }
            var actionContext = new GameContracts.ActionContext
            {
                gameId = _runtimeGameId,
                flowId = _runtimeFlowId,
                sessionId = _runtimeSessionId,
                controlMode = _runtimeControlMode,
                nodeId = _activeNode == null ? string.Empty : _activeNode.nodeId,
                stepId = _activeNode == null ? string.Empty : _activeNode.nodeId,
            };
            validationResult = _actionGate.Evaluate(intent, _activeNode, actionContext);
            reasonCode = validationResult == null ? "ACTION_GATE_FAILED" : validationResult.reasonCode;
            if (validationResult == null || !validationResult.accepted)
            {
                return true;
            }

            var allowedAction = ResolveAllowedAction(_activeNode, intent == null ? string.Empty : intent.actionId);
            if (_pluginRegistry != null &&
                intent != null &&
                _pluginRegistry.TryResolve(intent.actionId, out var plugin) &&
                plugin != null)
            {
                validationResult = plugin.Validate(intent, actionContext, allowedAction);
                reasonCode = validationResult == null ? "PLUGIN_VALIDATE_FAILED" : validationResult.reasonCode;
                if (validationResult == null || !validationResult.accepted)
                {
                    return true;
                }

                var applyResult = plugin.Apply(intent, actionContext, allowedAction);
                if (applyResult != null)
                {
                    if (applyResult.stepFailed)
                    {
                        return RouteByTrigger(TransitionTrigger.Fail, nowElapsedSec, "NODE_PLUGIN_FAIL", out reasonCode);
                    }

                    if (applyResult.stepTimedOut)
                    {
                        return RouteByTrigger(TransitionTrigger.Timeout, nowElapsedSec, "NODE_PLUGIN_TIMEOUT", out reasonCode);
                    }

                    if (!string.IsNullOrWhiteSpace(applyResult.nextNodeId))
                    {
                        return TryEnterNode(applyResult.nextNodeId, nowElapsedSec, "NODE_PLUGIN_NEXT", out reasonCode);
                    }

                    if (!applyResult.stepCompleted)
                    {
                        return true;
                    }
                }
            }

            if (!RouteByTrigger(TransitionTrigger.Success, nowElapsedSec, "NODE_SUCCESS", out reasonCode))
            {
                return false;
            }

            EvaluateImmediateNodeRouting(nowElapsedSec, out _);
            return true;
        }

        private void EvaluateImmediateNodeRouting(float nowElapsedSec, out string reasonCode)
        {
            reasonCode = string.Empty;

            if (_state != TaskGraphRunState.Running || _activeNode == null)
            {
                return;
            }

            var nodeType = Normalize(_activeNode.nodeType);
            if (string.Equals(nodeType, Normalize(GameContracts.TaskGraphNodeTypes.Complete), StringComparison.OrdinalIgnoreCase))
            {
                _state = TaskGraphRunState.Completed;
                GraphCompleted?.Invoke(_state, "GRAPH_COMPLETED");
                return;
            }

            if (string.Equals(nodeType, Normalize(GameContracts.TaskGraphNodeTypes.Fail), StringComparison.OrdinalIgnoreCase))
            {
                _state = TaskGraphRunState.Failed;
                GraphCompleted?.Invoke(_state, "GRAPH_FAILED");
                return;
            }

            if (string.Equals(nodeType, Normalize(GameContracts.TaskGraphNodeTypes.Condition), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(nodeType, Normalize(GameContracts.TaskGraphNodeTypes.Branch), StringComparison.OrdinalIgnoreCase))
            {
                RouteByTrigger(TransitionTrigger.Branch, nowElapsedSec, "NODE_BRANCH", out _);
            }
        }

        private bool RouteByTrigger(
            TransitionTrigger trigger,
            float nowElapsedSec,
            string transitionReason,
            out string reasonCode)
        {
            reasonCode = string.Empty;

            var decision = _transitionEngine.Resolve(_activeNode, trigger);
            if (!decision.resolved)
            {
                reasonCode = string.IsNullOrWhiteSpace(decision.reasonCode)
                    ? "TRANSITION_NOT_RESOLVED"
                    : decision.reasonCode;
                return false;
            }

            if (decision.terminalComplete)
            {
                _state = TaskGraphRunState.Completed;
                GraphCompleted?.Invoke(_state, transitionReason);
                return true;
            }

            if (decision.terminalFail)
            {
                _state = TaskGraphRunState.Failed;
                GraphCompleted?.Invoke(_state, transitionReason);
                return true;
            }

            return TryEnterNode(decision.nextNodeId, nowElapsedSec, transitionReason, out reasonCode);
        }

        private bool TryEnterNode(
            string nodeId,
            float nowElapsedSec,
            string transitionReason,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            var normalizedNodeId = Normalize(nodeId);
            if (string.IsNullOrWhiteSpace(normalizedNodeId))
            {
                reasonCode = GameContracts.SessionFlowDefinitionReasonCodes.NodeIdRequired;
                return false;
            }

            if (!_nodesById.TryGetValue(normalizedNodeId, out var node) || node == null)
            {
                reasonCode = "NEXT_NODE_NOT_FOUND";
                return false;
            }

            _activeNode = node;
            _activeNodeEnteredAtSec = Mathf.Max(0f, nowElapsedSec);
            _state = TaskGraphRunState.Running;
            NodeEntered?.Invoke(_activeNode, transitionReason);
            return true;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static GameContracts.AllowedActionDefinition ResolveAllowedAction(
            GameContracts.TaskGraphNodeDefinition node,
            string actionId)
        {
            if (node == null || node.allowedActions == null || node.allowedActions.Count == 0 || string.IsNullOrWhiteSpace(actionId))
            {
                return null;
            }

            var normalizedActionId = Normalize(actionId);
            for (var i = 0; i < node.allowedActions.Count; i++)
            {
                var allowedAction = node.allowedActions[i];
                if (allowedAction == null || string.IsNullOrWhiteSpace(allowedAction.actionId))
                {
                    continue;
                }

                if (string.Equals(
                        Normalize(allowedAction.actionId),
                        normalizedActionId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return allowedAction;
                }
            }

            return null;
        }
    }
}
