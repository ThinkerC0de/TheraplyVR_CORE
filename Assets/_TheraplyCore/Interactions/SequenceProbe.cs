using System;
using System.Collections.Generic;
using UnityEngine;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Interactions
{
    [DisallowMultipleComponent]
    public sealed class SequenceProbe : MonoBehaviour
    {
        public enum SequenceEventKind
        {
            StepCorrect = 0,
            Completed = 1,
            Wrong = 2,
        }

        public struct SequenceSample
        {
            public string gameId;
            public string actionId;
            public string inputHand;
            public string inputSource;
            public string inputControl;
            public string sourceComponent;
            public string targetId;
            public string targetName;
            public int stepIndex;
            public int expectedIndex;
            public string expectedValue;
            public string actualValue;
            public int matchedPairs;
            public int totalPairs;
            public float inputValue;
            public bool? targetValid;
            public string reasonCode;
        }

        [Header("Behavior")]
        [SerializeField] private bool _emitTelemetry = true;
        [SerializeField] private bool _logTelemetry;

        [Header("Dependencies")]
        [SerializeField] private InteractionEventBridge _interactionEventBridge;

        public static SequenceProbe Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (Instance != null)
            {
                return;
            }

            var existing = FindFirstObjectByType<SequenceProbe>();
            if (existing != null)
            {
                Instance = existing;
                return;
            }

            var host = new GameObject("SequenceProbe");
            host.AddComponent<SequenceProbe>();
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

        public void RecordEvent(SequenceEventKind eventKind, SequenceSample sample)
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

            var normalizedActionId = NormalizeActionId(sample.actionId);
            var eventType = ResolveEventType(normalizedActionId, eventKind);
            var targetValid = ResolveTargetValid(eventKind, sample);
            var reasonCode = ResolveReasonCode(normalizedActionId, eventKind, sample.reasonCode, targetValid);

            var details = new Dictionary<string, object>
            {
                { "actionId", normalizedActionId },
                { "stepIndex", Mathf.Max(0, sample.stepIndex) },
                { "expectedIndex", Mathf.Max(0, sample.expectedIndex) },
                { "expectedValue", NormalizeOrFallback(sample.expectedValue, string.Empty) },
                { "actualValue", NormalizeOrFallback(sample.actualValue, string.Empty) },
                { "matchedPairs", Mathf.Max(0, sample.matchedPairs) },
                { "totalPairs", Mathf.Max(0, sample.totalPairs) },
                { "targetValid", targetValid },
            };

            var payload = new Dictionary<string, object>
            {
                { "sequenceEventType", eventType },
                { "actionId", normalizedActionId },
                { "gameId", NormalizeOrFallback(sample.gameId, "unknown_game") },
                { "sourceComponent", NormalizeOrFallback(sample.sourceComponent, nameof(SequenceProbe)) },
                { "inputHand", NormalizeOrFallback(sample.inputHand, string.Empty) },
                { "inputSource", NormalizeOrFallback(sample.inputSource, "SEQUENCE") },
                { "inputControl", NormalizeOrFallback(sample.inputControl, string.Empty) },
                { "inputValue", Mathf.Clamp01(sample.inputValue) },
                { "targetId", NormalizeOrFallback(sample.targetId, string.Empty) },
                { "targetName", NormalizeOrFallback(sample.targetName, string.Empty) },
                { "stepIndex", Mathf.Max(0, sample.stepIndex) },
                { "expectedIndex", Mathf.Max(0, sample.expectedIndex) },
                { "expectedValue", NormalizeOrFallback(sample.expectedValue, string.Empty) },
                { "actualValue", NormalizeOrFallback(sample.actualValue, string.Empty) },
                { "matchedPairs", Mathf.Max(0, sample.matchedPairs) },
                { "totalPairs", Mathf.Max(0, sample.totalPairs) },
                { "targetValid", targetValid },
                { "reasonCode", reasonCode },
                { "details", details },
            };

            _interactionEventBridge.RecordSequenceTelemetry(payload);

            if (_logTelemetry)
            {
                Logger.Info(
                    "[SequenceProbe] eventType=" +
                    eventType +
                    " actionId=" +
                    normalizedActionId +
                    " stepIndex=" +
                    Mathf.Max(0, sample.stepIndex));
            }
        }

        public void RecordStepCorrect(SequenceSample sample)
        {
            RecordEvent(SequenceEventKind.StepCorrect, sample);
        }

        public void RecordCompleted(SequenceSample sample)
        {
            RecordEvent(SequenceEventKind.Completed, sample);
        }

        public void RecordWrong(SequenceSample sample)
        {
            RecordEvent(SequenceEventKind.Wrong, sample);
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

        private static string NormalizeActionId(string actionId)
        {
            var normalized = NormalizeOrFallback(actionId, string.Empty);
            if (string.Equals(normalized, "repeat_visual_sequence", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "repeat_audio_sequence", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "select_sequence_in_order", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "match_pair", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            return "repeat_visual_sequence";
        }

        private static string ResolveEventType(string actionId, SequenceEventKind eventKind)
        {
            switch (NormalizeOrFallback(actionId, string.Empty))
            {
                case "repeat_visual_sequence":
                    return ResolveVisualEventType(eventKind);
                case "repeat_audio_sequence":
                    return ResolveAudioEventType(eventKind);
                case "select_sequence_in_order":
                    return ResolveOrderEventType(eventKind);
                case "match_pair":
                    return ResolvePairEventType(eventKind);
                default:
                    return "SEQUENCE_EVENT";
            }
        }

        private static string ResolveVisualEventType(SequenceEventKind eventKind)
        {
            switch (eventKind)
            {
                case SequenceEventKind.StepCorrect:
                    return "SEQUENCE_VISUAL_STEP_CORRECT";
                case SequenceEventKind.Completed:
                    return "SEQUENCE_VISUAL_COMPLETED";
                case SequenceEventKind.Wrong:
                    return "SEQUENCE_VISUAL_WRONG";
                default:
                    return "SEQUENCE_EVENT";
            }
        }

        private static string ResolveAudioEventType(SequenceEventKind eventKind)
        {
            switch (eventKind)
            {
                case SequenceEventKind.StepCorrect:
                    return "SEQUENCE_AUDIO_STEP_CORRECT";
                case SequenceEventKind.Completed:
                    return "SEQUENCE_AUDIO_COMPLETED";
                case SequenceEventKind.Wrong:
                    return "SEQUENCE_AUDIO_WRONG";
                default:
                    return "SEQUENCE_EVENT";
            }
        }

        private static string ResolveOrderEventType(SequenceEventKind eventKind)
        {
            switch (eventKind)
            {
                case SequenceEventKind.StepCorrect:
                    return "SEQUENCE_ORDER_STEP_CORRECT";
                case SequenceEventKind.Completed:
                    return "SEQUENCE_ORDER_COMPLETED";
                case SequenceEventKind.Wrong:
                    return "SEQUENCE_ORDER_WRONG";
                default:
                    return "SEQUENCE_EVENT";
            }
        }

        private static string ResolvePairEventType(SequenceEventKind eventKind)
        {
            switch (eventKind)
            {
                case SequenceEventKind.StepCorrect:
                    return "SEQUENCE_PAIR_MATCHED";
                case SequenceEventKind.Completed:
                    return "SEQUENCE_PAIR_COMPLETED";
                case SequenceEventKind.Wrong:
                    return "SEQUENCE_PAIR_MISMATCH";
                default:
                    return "SEQUENCE_EVENT";
            }
        }

        private static bool ResolveTargetValid(SequenceEventKind eventKind, SequenceSample sample)
        {
            if (sample.targetValid.HasValue)
            {
                return sample.targetValid.Value;
            }

            return eventKind != SequenceEventKind.Wrong;
        }

        private static string ResolveReasonCode(
            string actionId,
            SequenceEventKind eventKind,
            string explicitReasonCode,
            bool targetValid)
        {
            if (!string.IsNullOrWhiteSpace(explicitReasonCode))
            {
                return explicitReasonCode.Trim();
            }

            if (!targetValid)
            {
                if (string.Equals(actionId, "match_pair", StringComparison.OrdinalIgnoreCase))
                {
                    return "PAIR_MISMATCH";
                }

                return "WRONG_SEQUENCE_ORDER";
            }

            switch (eventKind)
            {
                case SequenceEventKind.StepCorrect:
                    return "SEQUENCE_STEP_CORRECT";
                case SequenceEventKind.Completed:
                    return "SEQUENCE_COMPLETED";
                case SequenceEventKind.Wrong:
                    return "WRONG_SEQUENCE_ORDER";
                default:
                    return "SEQUENCE_EVENT_OBSERVED";
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
