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
        private readonly Dictionary<string, bool> _channelEnabledById =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _stateFlagsByKey =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ActionGate _actionGate = new ActionGate();
        private readonly TransitionEngine _transitionEngine = new TransitionEngine();
        private ConditionEvaluatorRegistry _conditionEvaluatorRegistry = new ConditionEvaluatorRegistry();
        private ActionPluginRegistry _pluginRegistry;
        private string _runtimeGameId = string.Empty;
        private string _runtimeFlowId = string.Empty;
        private string _runtimeSessionId = string.Empty;
        private string _runtimeControlMode = GameContracts.SessionFlowControlModes.Hybrid;
        private string _runtimeBranchPrecedence = GameContracts.BranchPrecedenceModes.FirstMatch;
        private ScoringRuntime.ScoringSnapshot _runtimeScoringSnapshot;
        private GameContracts.ICalendarService _runtimeCalendarService;

        private GameContracts.TaskGraphDefinition _graph;
        private GameContracts.TaskGraphNodeDefinition _activeNode;
        private float _activeNodeEnteredAtSec;
        private TaskGraphRunState _state = TaskGraphRunState.NotInitialized;

        public event Action<GameContracts.TaskGraphNodeDefinition, string> NodeEntered;
        public event Action<TaskGraphRunState, string> GraphCompleted;
        public event Action<ConditionEvaluationTrace> ConditionEvaluated;
        public event Action<BranchRoutingTrace> BranchRouted;

        public TaskGraphRunState State => _state;
        public string ActiveNodeId => _activeNode == null ? string.Empty : _activeNode.nodeId;
        public GameContracts.TaskGraphNodeDefinition ActiveNode => _activeNode;

        public void SetPluginRegistry(ActionPluginRegistry pluginRegistry)
        {
            _pluginRegistry = pluginRegistry;
        }

        public void SetConditionEvaluatorRegistry(ConditionEvaluatorRegistry conditionEvaluatorRegistry)
        {
            _conditionEvaluatorRegistry = conditionEvaluatorRegistry ?? new ConditionEvaluatorRegistry();
        }

        public void SetConditionRuntimeState(
            ScoringRuntime.ScoringSnapshot scoringSnapshot,
            IReadOnlyDictionary<string, bool> channelEnabledById,
            IReadOnlyDictionary<string, string> stateFlagsByKey,
            string branchPrecedence,
            GameContracts.ICalendarService calendarService = null)
        {
            _runtimeScoringSnapshot = scoringSnapshot;
            _runtimeBranchPrecedence = GameContracts.BranchPrecedenceModes.NormalizeOrDefault(branchPrecedence);
            _runtimeCalendarService = calendarService;
            CopyChannelStates(channelEnabledById, _channelEnabledById);
            CopyStateFlags(stateFlagsByKey, _stateFlagsByKey);
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
            _channelEnabledById.Clear();
            _stateFlagsByKey.Clear();
            _graph = null;
            _activeNode = null;
            _activeNodeEnteredAtSec = 0f;
            _runtimeGameId = string.Empty;
            _runtimeFlowId = string.Empty;
            _runtimeSessionId = string.Empty;
            _runtimeControlMode = GameContracts.SessionFlowControlModes.Hybrid;
            _runtimeBranchPrecedence = GameContracts.BranchPrecedenceModes.FirstMatch;
            _runtimeScoringSnapshot = default(ScoringRuntime.ScoringSnapshot);
            _runtimeCalendarService = null;
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
            EvaluateImmediateNodeRouting(nowElapsedSec, evaluateConditionBranchNodes: true, out var immediateReasonCode);
            if (!string.IsNullOrWhiteSpace(immediateReasonCode))
            {
                reasonCode = immediateReasonCode;
                return false;
            }

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

            EvaluateImmediateNodeRouting(nowElapsedSec, evaluateConditionBranchNodes: true, out var immediateReasonCode);
            if (!string.IsNullOrWhiteSpace(immediateReasonCode))
            {
                reasonCode = immediateReasonCode;
                return false;
            }

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

            EvaluateImmediateNodeRouting(nowElapsedSec, evaluateConditionBranchNodes: false, out var immediateReasonCode);
            if (!string.IsNullOrWhiteSpace(immediateReasonCode))
            {
                reasonCode = immediateReasonCode;
                return false;
            }

            return true;
        }

        private void EvaluateImmediateNodeRouting(
            float nowElapsedSec,
            bool evaluateConditionBranchNodes,
            out string reasonCode)
        {
            reasonCode = string.Empty;

            if (_state != TaskGraphRunState.Running || _activeNode == null)
            {
                return;
            }

            var safeguardLimit = Mathf.Max(4, _nodesById.Count * 2);
            for (var guard = 0; guard < safeguardLimit; guard++)
            {
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

                if (IsConditionOrBranchNodeType(nodeType))
                {
                    if (!evaluateConditionBranchNodes)
                    {
                        return;
                    }

                    if (!EvaluateConditionOrBranchRouting(nowElapsedSec, out reasonCode))
                    {
                        return;
                    }

                    continue;
                }

                return;
            }

            reasonCode = "IMMEDIATE_ROUTE_LOOP_DETECTED";
            _state = TaskGraphRunState.Failed;
            GraphCompleted?.Invoke(_state, "NODE_BRANCH_LOOP_DETECTED");
        }

        private bool EvaluateConditionOrBranchRouting(float nowElapsedSec, out string reasonCode)
        {
            reasonCode = string.Empty;

            if (_activeNode == null)
            {
                reasonCode = "ACTIVE_NODE_MISSING";
                return false;
            }

            if (_activeNode.conditions == null || _activeNode.conditions.Count <= 0)
            {
                return HandleNoConditionMatch(nowElapsedSec, "CONDITION_SET_EMPTY", out reasonCode);
            }

            var firstMatchPrecedence = string.Equals(
                _runtimeBranchPrecedence,
                GameContracts.BranchPrecedenceModes.FirstMatch,
                StringComparison.OrdinalIgnoreCase);

            var selectedCondition = default(GameContracts.ConditionDefinition);
            for (var i = 0; i < _activeNode.conditions.Count; i++)
            {
                var condition = _activeNode.conditions[i];
                var evaluationResult = EvaluateCondition(condition, nowElapsedSec);
                EmitConditionEvaluated(condition, evaluationResult, nowElapsedSec);
                if (!evaluationResult.matched)
                {
                    continue;
                }

                selectedCondition = condition;
                if (firstMatchPrecedence)
                {
                    break;
                }
            }

            if (selectedCondition == null)
            {
                return HandleNoConditionMatch(nowElapsedSec, "CONDITION_NO_MATCH", out reasonCode);
            }

            var nextNodeId = Normalize(selectedCondition.nextNodeId);
            if (string.IsNullOrWhiteSpace(nextNodeId))
            {
                reasonCode = "CONDITION_MATCH_ROUTE_MISSING";
                EmitBranchRouted(
                    selectedCondition,
                    matched: true,
                    selectedNextNodeId: string.Empty,
                    reasonCode: reasonCode,
                    nowElapsedSec: nowElapsedSec);
                return false;
            }

            EmitBranchRouted(
                selectedCondition,
                matched: true,
                selectedNextNodeId: nextNodeId,
                reasonCode: "BRANCH_MATCH_ROUTE_SELECTED",
                nowElapsedSec: nowElapsedSec);
            return TryEnterNode(nextNodeId, nowElapsedSec, "NODE_BRANCH_MATCH", out reasonCode);
        }

        private ConditionEvaluationResult EvaluateCondition(
            GameContracts.ConditionDefinition condition,
            float nowElapsedSec)
        {
            if (condition == null)
            {
                return ConditionEvaluationResult.NotMatched("CONDITION_ENTRY_NULL");
            }

            var conditionId = Normalize(condition.conditionId);
            if (string.IsNullOrWhiteSpace(conditionId))
            {
                return ConditionEvaluationResult.NotMatched("CONDITION_ID_REQUIRED");
            }

            if (_conditionEvaluatorRegistry == null ||
                !_conditionEvaluatorRegistry.TryResolve(conditionId, out var evaluator) ||
                evaluator == null)
            {
                return ConditionEvaluationResult.NotMatched("CONDITION_EVALUATOR_NOT_FOUND");
            }

            var context = new ConditionEvaluationContext
            {
                gameId = _runtimeGameId,
                flowId = _runtimeFlowId,
                sessionId = _runtimeSessionId,
                nodeId = ActiveNodeId,
                controlMode = _runtimeControlMode,
                nowElapsedSec = Mathf.Max(0f, nowElapsedSec),
                nodeEnteredAtSec = Mathf.Max(0f, _activeNodeEnteredAtSec),
                scoringSnapshot = _runtimeScoringSnapshot,
                channelEnabledById = _channelEnabledById,
                stateFlagsByKey = _stateFlagsByKey,
                calendar = _runtimeCalendarService,
            };

            var result = evaluator.Evaluate(condition, context);
            if (string.IsNullOrWhiteSpace(result.reasonCode))
            {
                result.reasonCode = result.matched ? "CONDITION_MATCHED" : "CONDITION_NOT_MATCHED";
            }

            return result;
        }

        private bool HandleNoConditionMatch(float nowElapsedSec, string noMatchReasonCode, out string reasonCode)
        {
            reasonCode = string.Empty;
            var fallbackNextNodeId = Normalize(_activeNode == null ? string.Empty : _activeNode.nextOnFail);

            EmitBranchRouted(
                selectedCondition: null,
                matched: false,
                selectedNextNodeId: fallbackNextNodeId,
                reasonCode: noMatchReasonCode,
                nowElapsedSec: nowElapsedSec);

            if (!string.IsNullOrWhiteSpace(fallbackNextNodeId))
            {
                return TryEnterNode(fallbackNextNodeId, nowElapsedSec, "NODE_BRANCH_NO_MATCH", out reasonCode);
            }

            _state = TaskGraphRunState.Failed;
            GraphCompleted?.Invoke(_state, noMatchReasonCode);
            return true;
        }

        private void EmitConditionEvaluated(
            GameContracts.ConditionDefinition condition,
            ConditionEvaluationResult evaluationResult,
            float nowElapsedSec)
        {
            ConditionEvaluated?.Invoke(
                new ConditionEvaluationTrace
                {
                    nodeId = Normalize(ActiveNodeId),
                    conditionId = condition == null ? string.Empty : Normalize(condition.conditionId),
                    subject = condition == null ? string.Empty : Normalize(condition.subject),
                    op = condition == null ? string.Empty : Normalize(condition.op),
                    value = condition == null ? string.Empty : Normalize(condition.value),
                    matched = evaluationResult.matched,
                    reasonCode = Normalize(evaluationResult.reasonCode),
                    nowElapsedSec = Mathf.Max(0f, nowElapsedSec),
                    nodeElapsedSec = Mathf.Max(0f, nowElapsedSec - Mathf.Max(0f, _activeNodeEnteredAtSec)),
                });
        }

        private void EmitBranchRouted(
            GameContracts.ConditionDefinition selectedCondition,
            bool matched,
            string selectedNextNodeId,
            string reasonCode,
            float nowElapsedSec)
        {
            BranchRouted?.Invoke(
                new BranchRoutingTrace
                {
                    nodeId = Normalize(ActiveNodeId),
                    nodeType = Normalize(_activeNode == null ? string.Empty : _activeNode.nodeType),
                    precedence = Normalize(_runtimeBranchPrecedence),
                    matched = matched,
                    selectedConditionId = selectedCondition == null
                        ? string.Empty
                        : Normalize(selectedCondition.conditionId),
                    selectedNextNodeId = Normalize(selectedNextNodeId),
                    reasonCode = Normalize(reasonCode),
                    nowElapsedSec = Mathf.Max(0f, nowElapsedSec),
                });
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

        private static bool IsConditionOrBranchNodeType(string nodeType)
        {
            return string.Equals(
                       nodeType,
                       Normalize(GameContracts.TaskGraphNodeTypes.Condition),
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       nodeType,
                       Normalize(GameContracts.TaskGraphNodeTypes.Branch),
                       StringComparison.OrdinalIgnoreCase);
        }

        private static void CopyChannelStates(
            IReadOnlyDictionary<string, bool> source,
            IDictionary<string, bool> target)
        {
            target.Clear();
            if (source == null || source.Count <= 0)
            {
                return;
            }

            foreach (var kv in source)
            {
                var normalizedKey = Normalize(kv.Key);
                if (string.IsNullOrWhiteSpace(normalizedKey))
                {
                    continue;
                }

                target[normalizedKey] = kv.Value;
            }
        }

        private static void CopyStateFlags(
            IReadOnlyDictionary<string, string> source,
            IDictionary<string, string> target)
        {
            target.Clear();
            if (source == null || source.Count <= 0)
            {
                return;
            }

            foreach (var kv in source)
            {
                var normalizedKey = Normalize(kv.Key);
                if (string.IsNullOrWhiteSpace(normalizedKey))
                {
                    continue;
                }

                target[normalizedKey] = kv.Value == null ? string.Empty : kv.Value.Trim();
            }
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
