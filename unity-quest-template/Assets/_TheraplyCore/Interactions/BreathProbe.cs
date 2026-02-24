using System.Collections.Generic;
using UnityEngine;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Interactions
{
    [DisallowMultipleComponent]
    public sealed class BreathProbe : MonoBehaviour
    {
        public enum BreathEventKind
        {
            PhaseInhale = 0,
            PhaseHold = 1,
            PhaseExhale = 2,
            CycleCompleted = 3,
            CycleInvalid = 4,
        }

        public struct BreathSample
        {
            public string gameId;
            public string inputSource;
            public string inputControl;
            public string sourceComponent;
            public string targetId;
            public string targetName;
            public int cycleIndex;
            public int targetCycleCount;
            public float phaseDurationSec;
            public float inputValue;
            public bool? targetValid;
            public string reasonCode;
        }

        [Header("Behavior")]
        [SerializeField] private bool _emitTelemetry = true;
        [SerializeField] private bool _logTelemetry;

        [Header("Dependencies")]
        [SerializeField] private InteractionEventBridge _interactionEventBridge;

        public static BreathProbe Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (Instance != null)
            {
                return;
            }

            var existing = FindFirstObjectByType<BreathProbe>();
            if (existing != null)
            {
                Instance = existing;
                return;
            }

            var host = new GameObject("BreathProbe");
            host.AddComponent<BreathProbe>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ResolveDependencies();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void RecordEvent(BreathEventKind eventKind, BreathSample sample)
        {
            if (!_emitTelemetry)
            {
                return;
            }

            ResolveDependencies();
            if (_interactionEventBridge == null)
            {
                return;
            }

            var eventType = ResolveEventType(eventKind);
            var targetValid = ResolveTargetValid(eventKind, sample);
            var reasonCode = ResolveReasonCode(eventKind, sample.reasonCode, targetValid);

            var details = new Dictionary<string, object>
            {
                { "actionId", "perform_breath_cycle" },
                { "cycleIndex", Mathf.Max(0, sample.cycleIndex) },
                { "targetCycleCount", Mathf.Max(0, sample.targetCycleCount) },
                { "phaseDurationSec", Mathf.Max(0f, sample.phaseDurationSec) },
                { "targetValid", targetValid },
            };

            var payload = new Dictionary<string, object>
            {
                { "breathEventType", eventType },
                { "actionId", "perform_breath_cycle" },
                { "gameId", NormalizeOrFallback(sample.gameId, "unknown_game") },
                { "sourceComponent", NormalizeOrFallback(sample.sourceComponent, nameof(BreathProbe)) },
                { "inputSource", NormalizeOrFallback(sample.inputSource, "BREATH") },
                { "inputControl", NormalizeOrFallback(sample.inputControl, string.Empty) },
                { "inputValue", Mathf.Max(0f, sample.inputValue) },
                { "targetId", NormalizeOrFallback(sample.targetId, string.Empty) },
                { "targetName", NormalizeOrFallback(sample.targetName, string.Empty) },
                { "targetValid", targetValid },
                { "reasonCode", reasonCode },
                { "details", details },
            };

            _interactionEventBridge.RecordBreathTelemetry(payload);

            if (_logTelemetry)
            {
                Logger.Info(
                    $"[BreathProbe] eventType={eventType} cycleIndex={sample.cycleIndex} targetValid={targetValid}");
            }
        }

        public void RecordPhaseInhale(BreathSample sample)
        {
            RecordEvent(BreathEventKind.PhaseInhale, sample);
        }

        public void RecordPhaseHold(BreathSample sample)
        {
            RecordEvent(BreathEventKind.PhaseHold, sample);
        }

        public void RecordPhaseExhale(BreathSample sample)
        {
            RecordEvent(BreathEventKind.PhaseExhale, sample);
        }

        public void RecordCycleCompleted(BreathSample sample)
        {
            RecordEvent(BreathEventKind.CycleCompleted, sample);
        }

        public void RecordCycleInvalid(BreathSample sample)
        {
            RecordEvent(BreathEventKind.CycleInvalid, sample);
        }

        private void ResolveDependencies()
        {
            if (_interactionEventBridge == null)
            {
                _interactionEventBridge = InteractionEventBridge.Instance;
                if (_interactionEventBridge == null)
                {
                    _interactionEventBridge = FindFirstObjectByType<InteractionEventBridge>();
                }
            }
        }

        private static string ResolveEventType(BreathEventKind eventKind)
        {
            switch (eventKind)
            {
                case BreathEventKind.PhaseInhale:
                    return "BREATH_PHASE_INHALE";
                case BreathEventKind.PhaseHold:
                    return "BREATH_PHASE_HOLD";
                case BreathEventKind.PhaseExhale:
                    return "BREATH_PHASE_EXHALE";
                case BreathEventKind.CycleCompleted:
                    return "BREATH_CYCLE_COMPLETED";
                case BreathEventKind.CycleInvalid:
                    return "BREATH_CYCLE_INVALID";
                default:
                    return "BREATH_EVENT";
            }
        }

        private static bool ResolveTargetValid(BreathEventKind eventKind, BreathSample sample)
        {
            if (sample.targetValid.HasValue)
            {
                return sample.targetValid.Value;
            }

            return eventKind != BreathEventKind.CycleInvalid;
        }

        private static string ResolveReasonCode(
            BreathEventKind eventKind,
            string explicitReasonCode,
            bool targetValid)
        {
            if (!string.IsNullOrWhiteSpace(explicitReasonCode))
            {
                return explicitReasonCode.Trim();
            }

            if (!targetValid)
            {
                return "BREATH_CYCLE_INVALID";
            }

            switch (eventKind)
            {
                case BreathEventKind.PhaseInhale:
                    return "BREATH_PHASE_INHALE";
                case BreathEventKind.PhaseHold:
                    return "BREATH_PHASE_HOLD";
                case BreathEventKind.PhaseExhale:
                    return "BREATH_PHASE_EXHALE";
                case BreathEventKind.CycleCompleted:
                    return "BREATH_CYCLE_COMPLETED";
                case BreathEventKind.CycleInvalid:
                    return "BREATH_CYCLE_INVALID";
                default:
                    return "BREATH_EVENT_OBSERVED";
            }
        }

        private static string NormalizeOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? (fallback ?? string.Empty)
                : value.Trim();
        }
    }
}
