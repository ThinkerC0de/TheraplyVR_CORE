using System;
using System.Collections.Generic;
using UnityEngine;
using TheraplyCore.Interactions;
using GameContracts = TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Runtime host that wires definition, adapters, graph execution, session FSM, and telemetry.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionFlowRunner : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private FlowConfigProvider _flowConfigProvider;
        [SerializeField] private SessionRuntimeBridge _sessionRuntimeBridge;
        [SerializeField] private ActionAdapterRegistry _actionAdapterRegistry;
        [SerializeField] private EffectRunner _effectRunner;
        [SerializeField] private InteractionEventBridge _interactionEventBridge;
        [SerializeField] private MotionTraceRecorder _motionTraceRecorder;

        [Header("Startup")]
        [SerializeField] private bool _autoStartOnEnable = true;
        [SerializeField] private string _defaultPatientId = "unknown_patient";
        [SerializeField] private string _defaultTherapistId = "unknown_therapist";
        [SerializeField] private bool _interruptSessionOnDisable = true;

        [Header("Telemetry")]
        [SerializeField] private bool _emitFlowTelemetry = true;
        [SerializeField] private bool _enforceDecisionPairCoverage = true;
        [SerializeField] private bool _logLifecycle;

        private readonly TaskGraphRunner _taskGraphRunner = new TaskGraphRunner();
        private readonly ScoringRuntime _scoringRuntime = new ScoringRuntime();
        private readonly Dictionary<string, bool> _pendingActionDecisionByAttemptId =
            new Dictionary<string, bool>(StringComparer.Ordinal);
        private GameContracts.GameDefinition _activeDefinition;
        private string _activeFlowId = string.Empty;
        private GameContracts.TaskGraphNodeDefinition _lastEnteredNode;
        private bool _isFlowPaused;

        public TaskGraphRunState GraphState => _taskGraphRunner.State;
        public string ActiveNodeId => _taskGraphRunner.ActiveNodeId;
        public string ActiveFlowId => _activeFlowId;
        public bool IsPaused => _isFlowPaused;
        public bool IsRunning => _taskGraphRunner.State == TaskGraphRunState.Running;

        private void Awake()
        {
            ResolveDependencies();
            _taskGraphRunner.NodeEntered += HandleNodeEntered;
            _taskGraphRunner.GraphCompleted += HandleGraphCompleted;
        }

        private void OnEnable()
        {
            ResolveDependencies();
            SubscribeIntents();

            if (_autoStartOnEnable)
            {
                if (!TryStartFlow(out var reasonCode))
                {
                    MaybeLog("FLOW_AUTOSTART_FAILED", reasonCode);
                }
            }
        }

        private void OnDisable()
        {
            UnsubscribeIntents();

            if (_interruptSessionOnDisable && IsRunning)
            {
                TryInterruptFlow(out _);
            }
        }

        private void Update()
        {
            if (!IsRunning || _isFlowPaused)
            {
                return;
            }

            var now = Time.realtimeSinceStartup;
            _taskGraphRunner.SetRuntimeContext(
                ResolveGameId(),
                _activeFlowId,
                ResolveSessionId(),
                ResolveControlMode(_activeDefinition));

            if (!_taskGraphRunner.Tick(now, out var reasonCode) && !string.IsNullOrWhiteSpace(reasonCode))
            {
                HandleRuntimeFailure(reasonCode);
            }
        }

        public bool TryStartFlow(out string reasonCode)
        {
            return TryStartFlow(
                _defaultPatientId,
                _defaultTherapistId,
                string.Empty,
                out reasonCode);
        }

        public bool TryStartFlow(
            string patientId,
            string therapistId,
            string requestedSessionId,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            ResolveDependencies();

            if (IsRunning)
            {
                reasonCode = "FLOW_ALREADY_RUNNING";
                return false;
            }

            if (_flowConfigProvider == null)
            {
                reasonCode = "FLOW_CONFIG_PROVIDER_MISSING";
                return false;
            }

            if (!_flowConfigProvider.TryGetGameDefinition(out var definition, out reasonCode))
            {
                return false;
            }

            if (definition == null)
            {
                reasonCode = GameContracts.SessionFlowDefinitionReasonCodes.DefinitionNull;
                return false;
            }

            if (_sessionRuntimeBridge != null &&
                !_sessionRuntimeBridge.TryStartSession(
                    patientId,
                    therapistId,
                    requestedSessionId,
                    out reasonCode))
            {
                return false;
            }

            _activeDefinition = definition;
            _activeFlowId = string.IsNullOrWhiteSpace(definition.gameId)
                ? "session_flow"
                : definition.gameId.Trim();
            _scoringRuntime.Configure(definition);
            _pendingActionDecisionByAttemptId.Clear();
            _isFlowPaused = false;

            _taskGraphRunner.Reset();
            _taskGraphRunner.SetPluginRegistry(
                _actionAdapterRegistry == null ? null : _actionAdapterRegistry.PluginRegistry);
            _taskGraphRunner.SetRuntimeContext(
                ResolveGameId(),
                _activeFlowId,
                ResolveSessionId(),
                ResolveControlMode(_activeDefinition));
            if (_effectRunner != null)
            {
                _effectRunner.SetRuntimeContext(
                    ResolveGameId(),
                    _activeFlowId,
                    ResolveSessionId());
            }
            _lastEnteredNode = null;

            if (!_taskGraphRunner.Initialize(definition.taskGraph, out reasonCode))
            {
                return false;
            }

            if (!_taskGraphRunner.Start(Time.realtimeSinceStartup, out reasonCode))
            {
                return false;
            }

            EmitFlowEvent("flow_started", "FLOW_STARTED");
            if (_motionTraceRecorder != null)
            {
                _motionTraceRecorder.BeginTrace(ResolveGameId(), _activeFlowId, ResolveSessionId());
            }
            MaybeLog("FLOW_STARTED", reasonCode);
            return true;
        }

        public bool TryInterruptFlow(out string reasonCode)
        {
            reasonCode = string.Empty;
            if (!IsRunning)
            {
                reasonCode = "FLOW_NOT_RUNNING";
                return false;
            }

            if (_sessionRuntimeBridge != null &&
                !_sessionRuntimeBridge.TryInterruptSession(out reasonCode))
            {
                return false;
            }

            var activeNode = _taskGraphRunner.ActiveNode;
            ExecuteNodeEffects(
                activeNode,
                activeNode == null ? null : activeNode.onExitEffects,
                "on_exit",
                "FLOW_INTERRUPTED",
                string.Empty,
                string.Empty,
                "FLOW_INTERRUPTED");

            _taskGraphRunner.Reset();
            _lastEnteredNode = null;
            _pendingActionDecisionByAttemptId.Clear();
            _isFlowPaused = false;
            EmitFlowEvent("flow_stopped", "FLOW_INTERRUPTED");
            if (_motionTraceRecorder != null)
            {
                _motionTraceRecorder.StopAndPublish("FLOW_INTERRUPTED");
            }
            MaybeLog("FLOW_INTERRUPTED", reasonCode);
            return true;
        }

        public bool TryStopFlow(out string reasonCode)
        {
            reasonCode = string.Empty;
            if (!IsRunning)
            {
                reasonCode = "FLOW_NOT_RUNNING";
                return false;
            }

            if (_sessionRuntimeBridge != null &&
                !_sessionRuntimeBridge.TryAbortSession(out reasonCode))
            {
                return false;
            }

            var activeNode = _taskGraphRunner.ActiveNode;
            ExecuteNodeEffects(
                activeNode,
                activeNode == null ? null : activeNode.onExitEffects,
                "on_exit",
                "FLOW_STOPPED_BY_CONTROL",
                string.Empty,
                string.Empty,
                "FLOW_STOPPED_BY_CONTROL");

            _taskGraphRunner.Reset();
            _lastEnteredNode = null;
            _pendingActionDecisionByAttemptId.Clear();
            _isFlowPaused = false;
            EmitFlowEvent("flow_stopped", "FLOW_STOPPED_BY_CONTROL");
            EmitSessionTerminal("FLOW_STOPPED_BY_CONTROL");
            if (_motionTraceRecorder != null)
            {
                _motionTraceRecorder.StopAndPublish("FLOW_STOPPED_BY_CONTROL");
            }
            MaybeLog("FLOW_STOPPED_BY_CONTROL", reasonCode);
            return true;
        }

        public bool TryPauseFlow(out string reasonCode)
        {
            reasonCode = string.Empty;
            if (!IsRunning)
            {
                reasonCode = "FLOW_NOT_RUNNING";
                return false;
            }

            if (_isFlowPaused)
            {
                reasonCode = "FLOW_ALREADY_PAUSED";
                return false;
            }

            if (_sessionRuntimeBridge != null &&
                !_sessionRuntimeBridge.TryPauseSession(out reasonCode))
            {
                return false;
            }

            _isFlowPaused = true;
            EmitFlowEvent("flow_paused", "FLOW_PAUSED");
            MaybeLog("FLOW_PAUSED", reasonCode);
            return true;
        }

        public bool TryResumeFlow(out string reasonCode)
        {
            reasonCode = string.Empty;
            if (!IsRunning)
            {
                reasonCode = "FLOW_NOT_RUNNING";
                return false;
            }

            if (!_isFlowPaused)
            {
                reasonCode = "FLOW_NOT_PAUSED";
                return false;
            }

            if (_sessionRuntimeBridge != null &&
                !_sessionRuntimeBridge.TryResumeSession(out reasonCode))
            {
                return false;
            }

            _isFlowPaused = false;
            EmitFlowEvent("flow_resumed", "FLOW_RESUMED");
            MaybeLog("FLOW_RESUMED", reasonCode);
            return true;
        }

        private void HandleIntent(GameContracts.ActionIntent intent)
        {
            if (intent == null)
            {
                return;
            }

            if (!IsRunning)
            {
                EmitBlockedActionTelemetry(intent, "FLOW_NOT_RUNNING_ACTION_BLOCKED");
                return;
            }

            if (_isFlowPaused)
            {
                EmitBlockedActionTelemetry(intent, "FLOW_PAUSED_ACTION_BLOCKED");
                return;
            }

            var now = Time.realtimeSinceStartup;
            var nodeBeforeSubmit = _taskGraphRunner.ActiveNode;
            var actionAttemptId = Guid.NewGuid().ToString("N");
            MarkActionReceived(actionAttemptId);
            EmitActionTelemetry("action_received", intent, null, "ACTION_RECEIVED", actionAttemptId);
            try
            {
                _taskGraphRunner.SetRuntimeContext(
                    ResolveGameId(),
                    _activeFlowId,
                    ResolveSessionId(),
                    ResolveControlMode(_activeDefinition));

                var submitted = _taskGraphRunner.SubmitAction(
                    intent,
                    now,
                    out var validationResult,
                    out var submitReasonCode);

                var accepted = submitted && validationResult != null && validationResult.accepted;
                var reasonCode = ResolveDecisionReasonCode(validationResult, submitReasonCode, accepted);
                _scoringRuntime.RecordActionDecision(accepted, reasonCode, reactionSec: 0f);
                var scoringSnapshot = _scoringRuntime.GetSnapshot();
                ApplyAdaptiveDifficultyState(scoringSnapshot);
                ExecuteNodeEffects(
                    nodeBeforeSubmit,
                    nodeBeforeSubmit == null
                        ? null
                        : (accepted ? nodeBeforeSubmit.onAcceptedEffects : nodeBeforeSubmit.onRejectedEffects),
                    accepted ? "on_accepted" : "on_rejected",
                    accepted ? "NODE_ACTION_ACCEPTED" : "NODE_ACTION_REJECTED",
                    intent.actionId,
                    accepted ? "accepted" : "rejected",
                    reasonCode);
                if (scoringSnapshot.adaptiveChanged)
                {
                    EmitFlowEvent(
                        "adaptive_difficulty_updated",
                        NormalizeOrFallback(scoringSnapshot.adaptiveReasonCode, "KEEP_DIFFICULTY"),
                        BuildScoringDetails(scoringSnapshot));
                }
                EmitActionTelemetry(
                    "action_evaluated",
                    intent,
                    accepted ? "accepted" : "rejected",
                    reasonCode,
                    actionAttemptId);
                MarkActionEvaluated(actionAttemptId);

                if (!submitted && IsRunning)
                {
                    HandleRuntimeFailure(submitReasonCode);
                }
            }
            catch (Exception e)
            {
                const string exceptionReasonCode = "ACTION_EVALUATION_EXCEPTION";
                EmitActionTelemetry(
                    "action_evaluated",
                    intent,
                    "rejected",
                    exceptionReasonCode,
                    actionAttemptId);
                MarkActionEvaluated(actionAttemptId);
                MaybeLog("ACTION_EVALUATION_EXCEPTION", e.Message);

                if (IsRunning)
                {
                    HandleRuntimeFailure(exceptionReasonCode);
                }
            }
        }

        private void HandleNodeEntered(GameContracts.TaskGraphNodeDefinition node, string transitionReason)
        {
            if (node == null)
            {
                return;
            }

            var normalizedTransitionReason = NormalizeOrFallback(transitionReason, "UNSPECIFIED_TRANSITION");

            if (_lastEnteredNode != null &&
                !string.Equals(
                    NormalizeOrFallback(_lastEnteredNode.nodeId, string.Empty),
                    NormalizeOrFallback(node.nodeId, string.Empty),
                    StringComparison.OrdinalIgnoreCase))
            {
                if (IsTimeoutTransition(normalizedTransitionReason))
                {
                    ExecuteNodeEffects(
                        _lastEnteredNode,
                        _lastEnteredNode.onTimeoutEffects,
                        "on_timeout",
                        normalizedTransitionReason,
                        string.Empty,
                        string.Empty,
                        "NODE_TIMEOUT");
                }

                ExecuteNodeEffects(
                    _lastEnteredNode,
                    _lastEnteredNode.onExitEffects,
                    "on_exit",
                    normalizedTransitionReason,
                    string.Empty,
                    string.Empty,
                    string.Empty);
            }

            ExecuteNodeEffects(
                node,
                node.onEnterEffects,
                "on_enter",
                normalizedTransitionReason,
                string.Empty,
                string.Empty,
                string.Empty);
            _lastEnteredNode = node;

            EmitFlowEvent(
                "step_entered",
                "STEP_ENTERED",
                new Dictionary<string, object>
                {
                    { "nodeId", node.nodeId ?? string.Empty },
                    { "nodeType", node.nodeType ?? string.Empty },
                    { "transitionReason", normalizedTransitionReason },
                });
        }

        private void HandleGraphCompleted(TaskGraphRunState terminalState, string reasonCode)
        {
            var scoringSnapshot = _scoringRuntime.GetSnapshot();
            if (HasUnresolvedActionDecisions())
            {
                const string unresolvedReasonCode = "UNRESOLVED_ACTION_DECISIONS";
                if (_sessionRuntimeBridge != null)
                {
                    _sessionRuntimeBridge.TryFailTechnicalSession(out _);
                }

                EmitFlowEvent(
                    "flow_failed",
                    unresolvedReasonCode,
                    BuildFlowFailureDetails(scoringSnapshot, unresolvedReasonCode));
                EmitSessionTerminal(unresolvedReasonCode);
                if (_motionTraceRecorder != null)
                {
                    _motionTraceRecorder.StopAndPublish(unresolvedReasonCode);
                }
                _pendingActionDecisionByAttemptId.Clear();
                _lastEnteredNode = _taskGraphRunner.ActiveNode;
                MaybeLog("FLOW_TERMINAL", unresolvedReasonCode);
                return;
            }

            var activeNode = _taskGraphRunner.ActiveNode;
            if (IsTimeoutTransition(reasonCode))
            {
                ExecuteNodeEffects(
                    activeNode,
                    activeNode == null ? null : activeNode.onTimeoutEffects,
                    "on_timeout",
                    reasonCode,
                    string.Empty,
                    string.Empty,
                    "NODE_TIMEOUT");
            }

            if (terminalState == TaskGraphRunState.Completed)
            {
                if (_sessionRuntimeBridge != null)
                {
                    _sessionRuntimeBridge.TryCompleteSession(out _);
                }

                EmitFlowEvent(
                    "flow_completed",
                    NormalizeOrFallback(reasonCode, "FLOW_COMPLETED"),
                    BuildFlowSuccessDetails(scoringSnapshot));
            }
            else
            {
                if (_sessionRuntimeBridge != null)
                {
                    _sessionRuntimeBridge.TryFailTechnicalSession(out _);
                }

                EmitFlowEvent(
                    "flow_failed",
                    NormalizeOrFallback(reasonCode, "FLOW_FAILED"),
                    BuildFlowFailureDetails(
                        scoringSnapshot,
                        NormalizeOrFallback(reasonCode, "FLOW_FAILED")));
            }

            EmitSessionTerminal(reasonCode);
            if (_motionTraceRecorder != null)
            {
                _motionTraceRecorder.StopAndPublish(reasonCode);
            }
            _pendingActionDecisionByAttemptId.Clear();
            _isFlowPaused = false;
            _lastEnteredNode = activeNode;
            MaybeLog("FLOW_TERMINAL", reasonCode);
        }

        private void HandleRuntimeFailure(string reasonCode)
        {
            var normalizedReason = NormalizeOrFallback(reasonCode, "FLOW_RUNTIME_FAILED");
            var scoringSnapshot = _scoringRuntime.GetSnapshot();
            if (_sessionRuntimeBridge != null)
            {
                _sessionRuntimeBridge.TryFailTechnicalSession(out _);
            }

            var activeNode = _taskGraphRunner.ActiveNode;
            if (IsTimeoutTransition(normalizedReason))
            {
                ExecuteNodeEffects(
                    activeNode,
                    activeNode == null ? null : activeNode.onTimeoutEffects,
                    "on_timeout",
                    normalizedReason,
                    string.Empty,
                    string.Empty,
                    normalizedReason);
            }

            EmitFlowEvent(
                "flow_failed",
                normalizedReason,
                BuildFlowFailureDetails(scoringSnapshot, normalizedReason));
            EmitSessionTerminal(normalizedReason);
            if (_motionTraceRecorder != null)
            {
                _motionTraceRecorder.StopAndPublish(normalizedReason);
            }
            _taskGraphRunner.Reset();
            _pendingActionDecisionByAttemptId.Clear();
            _isFlowPaused = false;
            _lastEnteredNode = null;
            MaybeLog("FLOW_RUNTIME_FAILED", normalizedReason);
        }

        private void SubscribeIntents()
        {
            if (_actionAdapterRegistry != null)
            {
                _actionAdapterRegistry.IntentReceived -= HandleIntent;
                _actionAdapterRegistry.IntentReceived += HandleIntent;
            }
        }

        private void UnsubscribeIntents()
        {
            if (_actionAdapterRegistry != null)
            {
                _actionAdapterRegistry.IntentReceived -= HandleIntent;
            }
        }

        private void ResolveDependencies()
        {
            if (_flowConfigProvider == null)
            {
                _flowConfigProvider = GetComponent<FlowConfigProvider>();
                if (_flowConfigProvider == null)
                {
                    _flowConfigProvider = FindFirstObjectByType<FlowConfigProvider>();
                }
            }

            if (_sessionRuntimeBridge == null)
            {
                _sessionRuntimeBridge = GetComponent<SessionRuntimeBridge>();
                if (_sessionRuntimeBridge == null)
                {
                    _sessionRuntimeBridge = FindFirstObjectByType<SessionRuntimeBridge>();
                }
            }

            if (_actionAdapterRegistry == null)
            {
                _actionAdapterRegistry = GetComponent<ActionAdapterRegistry>();
                if (_actionAdapterRegistry == null)
                {
                    _actionAdapterRegistry = FindFirstObjectByType<ActionAdapterRegistry>();
                }
            }

            if (_effectRunner == null)
            {
                _effectRunner = GetComponent<EffectRunner>();
                if (_effectRunner == null)
                {
                    _effectRunner = FindFirstObjectByType<EffectRunner>();
                }
            }

            if (_interactionEventBridge == null)
            {
                _interactionEventBridge = InteractionEventBridge.Instance;
                if (_interactionEventBridge == null)
                {
                    _interactionEventBridge = FindFirstObjectByType<InteractionEventBridge>();
                }
            }

            if (_motionTraceRecorder == null)
            {
                _motionTraceRecorder = GetComponent<MotionTraceRecorder>();
                if (_motionTraceRecorder == null)
                {
                    _motionTraceRecorder = FindFirstObjectByType<MotionTraceRecorder>();
                }
            }
        }

        private void EmitFlowEvent(
            string eventName,
            string reasonCode,
            IReadOnlyDictionary<string, object> details = null)
        {
            if (!_emitFlowTelemetry || _interactionEventBridge == null)
            {
                return;
            }

            var payload = new Dictionary<string, object>
            {
                { "flowId", NormalizeOrFallback(_activeFlowId, "session_flow") },
                { "stepId", NormalizeOrFallback(_taskGraphRunner.ActiveNodeId, string.Empty) },
                { "nodeId", NormalizeOrFallback(_taskGraphRunner.ActiveNodeId, string.Empty) },
                { "eventType", NormalizeOrFallback(eventName, "flow_event") },
                { "reasonCode", NormalizeOrFallback(reasonCode, "FLOW_EVENT") },
                { "controlMode", ResolveControlMode(_activeDefinition) },
                { "payloadVersion", 1 },
                { "monotonicSec", Time.realtimeSinceStartup },
                { "actionOutcome", "OBSERVED" },
            };

            if (details != null)
            {
                foreach (var kv in details)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key))
                    {
                        continue;
                    }

                    payload[kv.Key] = kv.Value;
                }
            }

            _interactionEventBridge.RecordGameplayEvent(
                ResolveGameId(),
                eventName,
                ResolveSessionStateToken(),
                payload,
                nameof(SessionFlowRunner));
        }

        private void EmitBlockedActionTelemetry(GameContracts.ActionIntent intent, string reasonCode)
        {
            if (intent == null)
            {
                return;
            }

            var actionAttemptId = Guid.NewGuid().ToString("N");
            MarkActionReceived(actionAttemptId);
            EmitActionTelemetry("action_received", intent, null, "ACTION_RECEIVED", actionAttemptId);
            EmitActionTelemetry(
                "action_evaluated",
                intent,
                "rejected",
                NormalizeOrFallback(reasonCode, "ACTION_BLOCKED"),
                actionAttemptId);
            MarkActionEvaluated(actionAttemptId);
        }

        private void EmitActionTelemetry(
            string eventName,
            GameContracts.ActionIntent intent,
            string decision,
            string reasonCode,
            string actionAttemptId)
        {
            if (!_emitFlowTelemetry || _interactionEventBridge == null || intent == null)
            {
                return;
            }

            var payload = new Dictionary<string, object>
            {
                { "flowId", NormalizeOrFallback(_activeFlowId, "session_flow") },
                { "stepId", NormalizeOrFallback(_taskGraphRunner.ActiveNodeId, string.Empty) },
                { "nodeId", NormalizeOrFallback(_taskGraphRunner.ActiveNodeId, string.Empty) },
                { "actionId", NormalizeOrFallback(intent.actionId, string.Empty) },
                { "channelId", NormalizeOrFallback(intent.channelId, string.Empty) },
                { "targetId", NormalizeOrFallback(intent.targetId, string.Empty) },
                { "inputSource", NormalizeOrFallback(intent.inputSource, string.Empty) },
                { "inputHand", NormalizeOrFallback(intent.inputHand, string.Empty) },
                { "inputValue", intent.inputValue },
                { "reactionSec", Mathf.Max(0f, intent.occurredAtElapsedSec) },
                { "actionAttemptId", NormalizeOrFallback(actionAttemptId, string.Empty) },
                { "eventType", NormalizeOrFallback(eventName, "action_event") },
                { "reasonCode", NormalizeOrFallback(reasonCode, "ACTION_OBSERVED") },
                { "payloadVersion", 1 },
                { "monotonicSec", Time.realtimeSinceStartup },
            };

            if (!string.IsNullOrWhiteSpace(decision))
            {
                payload["decision"] = decision.Trim();
                payload["actionOutcome"] = string.Equals(decision, "accepted", StringComparison.OrdinalIgnoreCase)
                    ? "CORRECT"
                    : "INCORRECT";
            }
            else
            {
                payload["actionOutcome"] = "OBSERVED";
            }

            _interactionEventBridge.RecordGameplayEvent(
                ResolveGameId(),
                eventName,
                ResolveSessionStateToken(),
                payload,
                nameof(SessionFlowRunner));
        }

        private void EmitSessionTerminal(string reasonCode)
        {
            if (!_emitFlowTelemetry || _interactionEventBridge == null || _sessionRuntimeBridge == null)
            {
                return;
            }

            var state = _sessionRuntimeBridge.SessionState;
            if (state != GameContracts.SessionLifecycleState.COMPLETED &&
                state != GameContracts.SessionLifecycleState.ABORTED_BY_THERAPIST &&
                state != GameContracts.SessionLifecycleState.FAILED_TECHNICAL)
            {
                return;
            }

            var payload = new Dictionary<string, object>
            {
                { "flowId", NormalizeOrFallback(_activeFlowId, "session_flow") },
                { "terminalState", state.ToString() },
                { "reasonCode", NormalizeOrFallback(reasonCode, "SESSION_TERMINAL") },
                { "unresolvedActionDecisions", CountUnresolvedActionDecisions() },
                { "eventType", "session_terminal" },
                { "payloadVersion", 1 },
                { "monotonicSec", Time.realtimeSinceStartup },
                { "actionOutcome", "OBSERVED" },
            };

            _interactionEventBridge.RecordGameplayEvent(
                ResolveGameId(),
                "session_terminal",
                state.ToString(),
                payload,
                nameof(SessionFlowRunner));
        }

        private string ResolveGameId()
        {
            if (_activeDefinition != null && !string.IsNullOrWhiteSpace(_activeDefinition.gameId))
            {
                return _activeDefinition.gameId.Trim();
            }

            return "session_flow";
        }

        private string ResolveSessionId()
        {
            return _sessionRuntimeBridge == null
                ? string.Empty
                : NormalizeOrFallback(_sessionRuntimeBridge.ActiveSessionId, string.Empty);
        }

        private string ResolveControlMode(GameContracts.GameDefinition definition)
        {
            if (_sessionRuntimeBridge != null &&
                GameContracts.SessionFlowControlModes.IsSupported(_sessionRuntimeBridge.ActiveControlMode))
            {
                return _sessionRuntimeBridge.ActiveControlMode;
            }

            if (definition == null)
            {
                return GameContracts.SessionFlowControlModes.Hybrid;
            }

            return GameContracts.SessionFlowControlModes.NormalizeOrDefault(definition.controlMode);
        }

        private string ResolveSessionStateToken()
        {
            return _sessionRuntimeBridge == null
                ? string.Empty
                : _sessionRuntimeBridge.SessionState.ToString();
        }

        private void ExecuteNodeEffects(
            GameContracts.TaskGraphNodeDefinition node,
            IReadOnlyList<GameContracts.EffectDefinition> effects,
            string trigger,
            string transitionReason,
            string actionId,
            string actionDecision,
            string actionReasonCode)
        {
            if (_effectRunner == null || node == null || effects == null || effects.Count == 0)
            {
                return;
            }

            _effectRunner.SetRuntimeContext(
                ResolveGameId(),
                _activeFlowId,
                ResolveSessionId());
            if (!_effectRunner.ExecuteEffects(
                    node.nodeId,
                    trigger,
                    effects,
                    transitionReason,
                    actionId,
                    actionDecision,
                    actionReasonCode,
                    out var effectReasonCode) &&
                !string.IsNullOrWhiteSpace(effectReasonCode))
            {
                MaybeLog("EFFECT_EXECUTION_FAILED", effectReasonCode);
            }
        }

        private static bool IsTimeoutTransition(string reasonCode)
        {
            return !string.IsNullOrWhiteSpace(reasonCode) &&
                   reasonCode.IndexOf("TIMEOUT", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplyAdaptiveDifficultyState(ScoringRuntime.ScoringSnapshot scoringSnapshot)
        {
            if (!scoringSnapshot.adaptiveEnabled)
            {
                return;
            }

            var activeNode = _taskGraphRunner.ActiveNode;
            if (activeNode == null)
            {
                return;
            }

            if (!string.Equals(activeNode.nodeType, GameContracts.TaskGraphNodeTypes.Action, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (scoringSnapshot.cueTimeoutSec > 0f)
            {
                activeNode.timeoutSec = scoringSnapshot.cueTimeoutSec;
            }
        }

        private IReadOnlyDictionary<string, object> BuildFlowSuccessDetails(ScoringRuntime.ScoringSnapshot scoringSnapshot)
        {
            var details = BuildScoringDetails(scoringSnapshot);
            details["telemetryDecisionCoverageOk"] = !HasUnresolvedActionDecisions();
            details["unresolvedActionDecisions"] = CountUnresolvedActionDecisions();
            return details;
        }

        private IReadOnlyDictionary<string, object> BuildFlowFailureDetails(
            ScoringRuntime.ScoringSnapshot scoringSnapshot,
            string failureReasonCode)
        {
            var details = BuildScoringDetails(scoringSnapshot);
            details["telemetryDecisionCoverageOk"] = !HasUnresolvedActionDecisions();
            details["unresolvedActionDecisions"] = CountUnresolvedActionDecisions();
            details["failureReasonCode"] = NormalizeOrFallback(failureReasonCode, "FLOW_FAILED");
            return details;
        }

        private static Dictionary<string, object> BuildScoringDetails(ScoringRuntime.ScoringSnapshot scoringSnapshot)
        {
            var details = new Dictionary<string, object>
            {
                { "scoreTotal", scoringSnapshot.scoreTotal },
                { "correctCount", scoringSnapshot.correctCount },
                { "wrongCount", scoringSnapshot.wrongCount },
                { "livesRemaining", scoringSnapshot.livesRemaining },
                { "successThresholdReached", scoringSnapshot.successThresholdReached },
                { "adaptiveDifficultyState", NormalizeOrFallback(scoringSnapshot.adaptiveDifficultyState, string.Empty) },
                { "adaptiveEnabled", scoringSnapshot.adaptiveEnabled },
                { "adaptiveReasonCode", NormalizeOrFallback(scoringSnapshot.adaptiveReasonCode, string.Empty) },
                { "targetSpeed", scoringSnapshot.targetSpeed },
                { "targetScale", scoringSnapshot.targetScale },
                { "cueTimeoutSec", scoringSnapshot.cueTimeoutSec },
                { "difficulty", scoringSnapshot.difficulty },
            };
            return details;
        }

        private void MarkActionReceived(string actionAttemptId)
        {
            var normalizedAttemptId = NormalizeOrFallback(actionAttemptId, string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedAttemptId))
            {
                return;
            }

            if (!_pendingActionDecisionByAttemptId.ContainsKey(normalizedAttemptId))
            {
                _pendingActionDecisionByAttemptId[normalizedAttemptId] = false;
            }
        }

        private void MarkActionEvaluated(string actionAttemptId)
        {
            var normalizedAttemptId = NormalizeOrFallback(actionAttemptId, string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedAttemptId))
            {
                return;
            }

            _pendingActionDecisionByAttemptId[normalizedAttemptId] = true;
        }

        private bool HasUnresolvedActionDecisions()
        {
            if (!_enforceDecisionPairCoverage || !_emitFlowTelemetry)
            {
                return false;
            }

            if (_activeDefinition == null ||
                _activeDefinition.policies == null ||
                _activeDefinition.policies.telemetryPolicy == null)
            {
                return false;
            }

            if (!_activeDefinition.policies.telemetryPolicy.requireDecisionForEveryAction)
            {
                return false;
            }

            foreach (var decisionState in _pendingActionDecisionByAttemptId.Values)
            {
                if (!decisionState)
                {
                    return true;
                }
            }

            return false;
        }

        private int CountUnresolvedActionDecisions()
        {
            if (_pendingActionDecisionByAttemptId.Count <= 0)
            {
                return 0;
            }

            var unresolved = 0;
            foreach (var decisionState in _pendingActionDecisionByAttemptId.Values)
            {
                if (!decisionState)
                {
                    unresolved++;
                }
            }

            return unresolved;
        }

        private static string ResolveDecisionReasonCode(
            GameContracts.ActionValidationResult validationResult,
            string submitReasonCode,
            bool accepted)
        {
            if (validationResult != null && !string.IsNullOrWhiteSpace(validationResult.reasonCode))
            {
                return validationResult.reasonCode.Trim();
            }

            if (!string.IsNullOrWhiteSpace(submitReasonCode))
            {
                return submitReasonCode.Trim();
            }

            return accepted ? "ACTION_ACCEPTED" : "ACTION_REJECTED";
        }

        private void MaybeLog(string marker, string reasonCode)
        {
            if (!_logLifecycle)
            {
                return;
            }

            Logger.Info(
                "[SessionFlowRunner] marker=" + NormalizeOrFallback(marker, "UNSPECIFIED") +
                " reasonCode=" + NormalizeOrFallback(reasonCode, string.Empty) +
                " flowId=" + NormalizeOrFallback(_activeFlowId, string.Empty) +
                " state=" + _taskGraphRunner.State +
                " activeNodeId=" + NormalizeOrFallback(_taskGraphRunner.ActiveNodeId, string.Empty));
        }

        private static string NormalizeOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? (fallback ?? string.Empty)
                : value.Trim();
        }
    }
}
