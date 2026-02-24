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
        [SerializeField] private InteractionEventBridge _interactionEventBridge;

        [Header("Startup")]
        [SerializeField] private bool _autoStartOnEnable = true;
        [SerializeField] private string _defaultPatientId = "unknown_patient";
        [SerializeField] private string _defaultTherapistId = "unknown_therapist";
        [SerializeField] private bool _interruptSessionOnDisable = true;

        [Header("Telemetry")]
        [SerializeField] private bool _emitFlowTelemetry = true;
        [SerializeField] private bool _logLifecycle;

        private readonly TaskGraphRunner _taskGraphRunner = new TaskGraphRunner();
        private GameContracts.GameDefinition _activeDefinition;
        private string _activeFlowId = string.Empty;

        public TaskGraphRunState GraphState => _taskGraphRunner.State;
        public string ActiveNodeId => _taskGraphRunner.ActiveNodeId;
        public string ActiveFlowId => _activeFlowId;
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
            if (!IsRunning)
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

            _taskGraphRunner.Reset();
            _taskGraphRunner.SetPluginRegistry(
                _actionAdapterRegistry == null ? null : _actionAdapterRegistry.PluginRegistry);
            _taskGraphRunner.SetRuntimeContext(
                ResolveGameId(),
                _activeFlowId,
                ResolveSessionId(),
                ResolveControlMode(_activeDefinition));

            if (!_taskGraphRunner.Initialize(definition.taskGraph, out reasonCode))
            {
                return false;
            }

            if (!_taskGraphRunner.Start(Time.realtimeSinceStartup, out reasonCode))
            {
                return false;
            }

            EmitFlowEvent("flow_started", "FLOW_STARTED");
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

            _taskGraphRunner.Reset();
            EmitFlowEvent("flow_stopped", "FLOW_INTERRUPTED");
            MaybeLog("FLOW_INTERRUPTED", reasonCode);
            return true;
        }

        private void HandleIntent(GameContracts.ActionIntent intent)
        {
            if (intent == null)
            {
                return;
            }

            var now = Time.realtimeSinceStartup;
            EmitActionTelemetry("action_received", intent, null, "ACTION_RECEIVED");

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
            EmitActionTelemetry(
                "action_evaluated",
                intent,
                accepted ? "accepted" : "rejected",
                reasonCode);

            if (!submitted && IsRunning)
            {
                HandleRuntimeFailure(submitReasonCode);
            }
        }

        private void HandleNodeEntered(GameContracts.TaskGraphNodeDefinition node, string transitionReason)
        {
            if (node == null)
            {
                return;
            }

            EmitFlowEvent(
                "step_entered",
                "STEP_ENTERED",
                new Dictionary<string, object>
                {
                    { "nodeId", node.nodeId ?? string.Empty },
                    { "nodeType", node.nodeType ?? string.Empty },
                    { "transitionReason", NormalizeOrFallback(transitionReason, "UNSPECIFIED_TRANSITION") },
                });
        }

        private void HandleGraphCompleted(TaskGraphRunState terminalState, string reasonCode)
        {
            if (terminalState == TaskGraphRunState.Completed)
            {
                if (_sessionRuntimeBridge != null)
                {
                    _sessionRuntimeBridge.TryCompleteSession(out _);
                }

                EmitFlowEvent(
                    "flow_completed",
                    NormalizeOrFallback(reasonCode, "FLOW_COMPLETED"));
            }
            else
            {
                if (_sessionRuntimeBridge != null)
                {
                    _sessionRuntimeBridge.TryFailTechnicalSession(out _);
                }

                EmitFlowEvent(
                    "flow_failed",
                    NormalizeOrFallback(reasonCode, "FLOW_FAILED"));
            }

            EmitSessionTerminal();
            MaybeLog("FLOW_TERMINAL", reasonCode);
        }

        private void HandleRuntimeFailure(string reasonCode)
        {
            var normalizedReason = NormalizeOrFallback(reasonCode, "FLOW_RUNTIME_FAILED");
            if (_sessionRuntimeBridge != null)
            {
                _sessionRuntimeBridge.TryFailTechnicalSession(out _);
            }

            EmitFlowEvent("flow_failed", normalizedReason);
            EmitSessionTerminal();
            _taskGraphRunner.Reset();
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

            if (_interactionEventBridge == null)
            {
                _interactionEventBridge = InteractionEventBridge.Instance;
                if (_interactionEventBridge == null)
                {
                    _interactionEventBridge = FindFirstObjectByType<InteractionEventBridge>();
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
                { "reasonCode", NormalizeOrFallback(reasonCode, "FLOW_EVENT") },
                { "controlMode", ResolveControlMode(_activeDefinition) },
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

        private void EmitActionTelemetry(
            string eventName,
            GameContracts.ActionIntent intent,
            string decision,
            string reasonCode)
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
                { "reasonCode", NormalizeOrFallback(reasonCode, "ACTION_OBSERVED") },
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

        private void EmitSessionTerminal()
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
                { "reasonCode", "SESSION_TERMINAL" },
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
