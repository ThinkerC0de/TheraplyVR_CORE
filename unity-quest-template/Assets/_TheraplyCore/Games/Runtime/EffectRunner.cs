using System;
using System.Collections.Generic;
using UnityEngine;
using TheraplyCore.Interactions;
using GameContracts = TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Executes effect hooks via registered effect plugins.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EffectRunner : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private FlowBindingRegistry _flowBindingRegistry;
        [SerializeField] private SceneRuntimeController _sceneRuntimeController;
        [SerializeField] private GameFeedbackService _gameFeedbackService;
        [SerializeField] private NarratorRuntime _narratorRuntime;
        [SerializeField] private LocalizationRuntime _localizationRuntime;
        [SerializeField] private InteractionEventBridge _interactionEventBridge;

        [Header("Registration")]
        [SerializeField] private bool _registerBuiltInsOnAwake = true;

        [Header("Behavior")]
        [SerializeField] private bool _emitEffectTelemetry = true;
        [SerializeField] private bool _logEffects;

        private readonly EffectPluginRegistry _pluginRegistry = new EffectPluginRegistry();
        private readonly EffectRuntimeServices _services = new EffectRuntimeServices();

        private string _runtimeGameId = string.Empty;
        private string _runtimeFlowId = string.Empty;
        private string _runtimeSessionId = string.Empty;

        public EffectPluginRegistry PluginRegistry => _pluginRegistry;

        private void Awake()
        {
            ResolveDependencies();

            if (_registerBuiltInsOnAwake)
            {
                SessionFlowBuiltInEffectPlugins.RegisterBuiltIns(_pluginRegistry, replaceExisting: true);
            }
        }

        public void SetRuntimeContext(string gameId, string flowId, string sessionId)
        {
            _runtimeGameId = NormalizeOrFallback(gameId, string.Empty);
            _runtimeFlowId = NormalizeOrFallback(flowId, string.Empty);
            _runtimeSessionId = NormalizeOrFallback(sessionId, string.Empty);

            ResolveDependencies();
            if (_narratorRuntime != null)
            {
                _narratorRuntime.SetRuntimeContext(_runtimeGameId, _runtimeFlowId, _runtimeSessionId);
            }

            if (_localizationRuntime != null)
            {
                _localizationRuntime.SetRuntimeContext(_runtimeGameId, _runtimeFlowId, _runtimeSessionId);
            }
        }

        public bool RegisterPlugin(IEffectPlugin plugin, bool replaceExisting = true)
        {
            return _pluginRegistry.Register(plugin, replaceExisting);
        }

        public bool ExecuteEffects(
            string nodeId,
            string trigger,
            IReadOnlyList<GameContracts.EffectDefinition> effects,
            string transitionReason,
            string actionId,
            string actionDecision,
            string actionReasonCode,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            ResolveDependencies();
            RefreshServices();

            if (effects == null || effects.Count == 0)
            {
                return true;
            }

            for (var i = 0; i < effects.Count; i++)
            {
                var effect = effects[i];
                if (effect == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(effect.effectId))
                {
                    reasonCode = "EFFECT_ID_REQUIRED";
                    return false;
                }

                var normalizedEffectId = effect.effectId.Trim();
                if (!_pluginRegistry.TryResolve(normalizedEffectId, out var plugin) || plugin == null)
                {
                    reasonCode = "EFFECT_PLUGIN_NOT_FOUND";
                    EmitEffectTelemetry(
                        effect,
                        nodeId,
                        trigger,
                        actionId,
                        actionDecision,
                        actionReasonCode,
                        transitionReason,
                        success: false,
                        reasonCode: reasonCode);
                    return false;
                }

                var context = new EffectExecutionContext
                {
                    gameId = ResolveGameId(),
                    flowId = ResolveFlowId(),
                    sessionId = ResolveSessionId(),
                    nodeId = NormalizeOrFallback(nodeId, string.Empty),
                    trigger = NormalizeOrFallback(trigger, "UNSPECIFIED_TRIGGER"),
                    transitionReason = NormalizeOrFallback(transitionReason, string.Empty),
                    actionId = NormalizeOrFallback(actionId, string.Empty),
                    actionDecision = NormalizeOrFallback(actionDecision, string.Empty),
                    actionReasonCode = NormalizeOrFallback(actionReasonCode, string.Empty),
                };

                if (!plugin.Execute(effect, context, _services, out reasonCode))
                {
                    EmitEffectTelemetry(
                        effect,
                        nodeId,
                        trigger,
                        actionId,
                        actionDecision,
                        actionReasonCode,
                        transitionReason,
                        success: false,
                        reasonCode: reasonCode);
                    return false;
                }

                EmitEffectTelemetry(
                    effect,
                    nodeId,
                    trigger,
                    actionId,
                    actionDecision,
                    actionReasonCode,
                    transitionReason,
                    success: true,
                    reasonCode: "EFFECT_APPLIED");
            }

            reasonCode = string.Empty;
            return true;
        }

        private void ResolveDependencies()
        {
            if (_flowBindingRegistry == null)
            {
                _flowBindingRegistry = GetComponent<FlowBindingRegistry>();
                if (_flowBindingRegistry == null)
                {
                    _flowBindingRegistry = FindFirstObjectByType<FlowBindingRegistry>();
                }
            }

            if (_sceneRuntimeController == null)
            {
                _sceneRuntimeController = GetComponent<SceneRuntimeController>();
                if (_sceneRuntimeController == null)
                {
                    _sceneRuntimeController = FindFirstObjectByType<SceneRuntimeController>();
                }
            }

            if (_gameFeedbackService == null)
            {
                _gameFeedbackService = GetComponent<GameFeedbackService>();
                if (_gameFeedbackService == null)
                {
                    _gameFeedbackService = FindFirstObjectByType<GameFeedbackService>();
                }
            }

            if (_narratorRuntime == null)
            {
                _narratorRuntime = GetComponent<NarratorRuntime>();
                if (_narratorRuntime == null)
                {
                    _narratorRuntime = FindFirstObjectByType<NarratorRuntime>();
                }
            }

            if (_localizationRuntime == null)
            {
                _localizationRuntime = GetComponent<LocalizationRuntime>();
                if (_localizationRuntime == null)
                {
                    _localizationRuntime = FindFirstObjectByType<LocalizationRuntime>();
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

        private void RefreshServices()
        {
            _services.bindings = _flowBindingRegistry;
            _services.sceneRuntime = _sceneRuntimeController;
            _services.feedback = _gameFeedbackService;
            _services.narrator = _narratorRuntime;
            _services.localization = _localizationRuntime;
        }

        private void EmitEffectTelemetry(
            GameContracts.EffectDefinition effect,
            string nodeId,
            string trigger,
            string actionId,
            string actionDecision,
            string actionReasonCode,
            string transitionReason,
            bool success,
            string reasonCode)
        {
            if (!_emitEffectTelemetry || _interactionEventBridge == null || effect == null)
            {
                return;
            }

            var payload = new Dictionary<string, object>
            {
                { "flowId", ResolveFlowId() },
                { "stepId", NormalizeOrFallback(nodeId, string.Empty) },
                { "nodeId", NormalizeOrFallback(nodeId, string.Empty) },
                { "effectId", NormalizeOrFallback(effect.effectId, string.Empty) },
                { "binding", NormalizeOrFallback(effect.binding, string.Empty) },
                { "trigger", NormalizeOrFallback(trigger, string.Empty) },
                { "transitionReason", NormalizeOrFallback(transitionReason, string.Empty) },
                { "actionId", NormalizeOrFallback(actionId, string.Empty) },
                { "decision", NormalizeOrFallback(actionDecision, string.Empty) },
                { "actionReasonCode", NormalizeOrFallback(actionReasonCode, string.Empty) },
                { "effectSuccess", success },
                { "reasonCode", NormalizeOrFallback(reasonCode, success ? "EFFECT_APPLIED" : "EFFECT_FAILED") },
                { "actionOutcome", success ? "CORRECT" : "INCORRECT" },
            };

            _interactionEventBridge.RecordGameplayEvent(
                ResolveGameId(),
                "effect_executed",
                string.Empty,
                payload,
                nameof(EffectRunner));

            if (_logEffects)
            {
                Logger.Info(
                    "[EffectRunner] effectId=" + NormalizeOrFallback(effect.effectId, string.Empty) +
                    " trigger=" + NormalizeOrFallback(trigger, string.Empty) +
                    " success=" + success +
                    " reasonCode=" + NormalizeOrFallback(reasonCode, string.Empty));
            }
        }

        private string ResolveGameId()
        {
            return NormalizeOrFallback(_runtimeGameId, "session_flow");
        }

        private string ResolveFlowId()
        {
            return NormalizeOrFallback(_runtimeFlowId, "session_flow");
        }

        private string ResolveSessionId()
        {
            return NormalizeOrFallback(_runtimeSessionId, string.Empty);
        }

        private static string NormalizeOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? (fallback ?? string.Empty)
                : value.Trim();
        }
    }
}
