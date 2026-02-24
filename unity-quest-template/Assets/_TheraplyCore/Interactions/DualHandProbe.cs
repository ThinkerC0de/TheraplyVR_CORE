using System.Collections.Generic;
using UnityEngine;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Interactions
{
    [DisallowMultipleComponent]
    public sealed class DualHandProbe : MonoBehaviour
    {
        public enum DualHandEventKind
        {
            LeftMarked = 0,
            RightMarked = 1,
            SynchronizedMarked = 2,
            OutOfSync = 3,
            Invalid = 4,
        }

        public struct DualHandSample
        {
            public string gameId;
            public string inputHand;
            public string inputSource;
            public string inputControl;
            public string sourceComponent;
            public string leftTargetId;
            public string leftTargetName;
            public string rightTargetId;
            public string rightTargetName;
            public bool leftMatched;
            public bool rightMatched;
            public float syncDeltaMs;
            public float syncWindowMs;
            public float inputValue;
            public bool? targetValid;
            public string reasonCode;
        }

        [Header("Behavior")]
        [SerializeField] private bool _emitTelemetry = true;
        [SerializeField] private bool _logTelemetry;

        [Header("Dependencies")]
        [SerializeField] private InteractionEventBridge _interactionEventBridge;

        public static DualHandProbe Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (Instance != null)
            {
                return;
            }

            var existing = FindFirstObjectByType<DualHandProbe>();
            if (existing != null)
            {
                Instance = existing;
                return;
            }

            var host = new GameObject("DualHandProbe");
            host.AddComponent<DualHandProbe>();
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

        public void RecordEvent(DualHandEventKind eventKind, DualHandSample sample)
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
            var leftMarked = eventKind == DualHandEventKind.LeftMarked ||
                             eventKind == DualHandEventKind.SynchronizedMarked ||
                             eventKind == DualHandEventKind.OutOfSync;
            var rightMarked = eventKind == DualHandEventKind.RightMarked ||
                              eventKind == DualHandEventKind.SynchronizedMarked ||
                              eventKind == DualHandEventKind.OutOfSync;
            var syncSatisfied = eventKind == DualHandEventKind.SynchronizedMarked;

            var pairTargetId = ResolvePairTargetId(sample.leftTargetId, sample.rightTargetId);
            var pairTargetName = ResolvePairTargetName(sample.leftTargetName, sample.rightTargetName);

            var details = new Dictionary<string, object>
            {
                { "actionId", "mark_left_and_right_targets" },
                { "leftTargetId", NormalizeOrFallback(sample.leftTargetId, string.Empty) },
                { "leftTargetName", NormalizeOrFallback(sample.leftTargetName, string.Empty) },
                { "rightTargetId", NormalizeOrFallback(sample.rightTargetId, string.Empty) },
                { "rightTargetName", NormalizeOrFallback(sample.rightTargetName, string.Empty) },
                { "leftMatched", sample.leftMatched },
                { "rightMatched", sample.rightMatched },
                { "leftMarked", leftMarked },
                { "rightMarked", rightMarked },
                { "syncDeltaMs", Mathf.Max(0f, sample.syncDeltaMs) },
                { "syncWindowMs", Mathf.Max(0f, sample.syncWindowMs) },
                { "syncSatisfied", syncSatisfied },
                { "targetValid", targetValid },
            };

            var payload = new Dictionary<string, object>
            {
                { "dualHandEventType", eventType },
                { "actionId", "mark_left_and_right_targets" },
                { "gameId", NormalizeOrFallback(sample.gameId, "unknown_game") },
                { "sourceComponent", NormalizeOrFallback(sample.sourceComponent, nameof(DualHandProbe)) },
                { "inputHand", NormalizeOrFallback(sample.inputHand, "BOTH_HANDS") },
                { "inputSource", NormalizeOrFallback(sample.inputSource, "DUAL_HAND") },
                { "inputControl", NormalizeOrFallback(sample.inputControl, string.Empty) },
                { "inputValue", Mathf.Clamp01(sample.inputValue) },
                { "targetId", pairTargetId },
                { "targetName", pairTargetName },
                { "leftTargetId", NormalizeOrFallback(sample.leftTargetId, string.Empty) },
                { "leftTargetName", NormalizeOrFallback(sample.leftTargetName, string.Empty) },
                { "rightTargetId", NormalizeOrFallback(sample.rightTargetId, string.Empty) },
                { "rightTargetName", NormalizeOrFallback(sample.rightTargetName, string.Empty) },
                { "leftMatched", sample.leftMatched },
                { "rightMatched", sample.rightMatched },
                { "leftMarked", leftMarked },
                { "rightMarked", rightMarked },
                { "syncDeltaMs", Mathf.Max(0f, sample.syncDeltaMs) },
                { "syncWindowMs", Mathf.Max(0f, sample.syncWindowMs) },
                { "syncSatisfied", syncSatisfied },
                { "targetValid", targetValid },
                { "reasonCode", reasonCode },
                { "details", details },
            };

            _interactionEventBridge.RecordDualHandTelemetry(payload);

            if (_logTelemetry)
            {
                Logger.Info(
                    "[DualHandProbe] eventType=" +
                    eventType +
                    " syncSatisfied=" +
                    syncSatisfied +
                    " syncDeltaMs=" +
                    Mathf.Max(0f, sample.syncDeltaMs).ToString("F1"));
            }
        }

        public void RecordLeftMarked(DualHandSample sample)
        {
            RecordEvent(DualHandEventKind.LeftMarked, sample);
        }

        public void RecordRightMarked(DualHandSample sample)
        {
            RecordEvent(DualHandEventKind.RightMarked, sample);
        }

        public void RecordSynchronizedMarked(DualHandSample sample)
        {
            RecordEvent(DualHandEventKind.SynchronizedMarked, sample);
        }

        public void RecordOutOfSync(DualHandSample sample)
        {
            RecordEvent(DualHandEventKind.OutOfSync, sample);
        }

        public void RecordInvalid(DualHandSample sample)
        {
            RecordEvent(DualHandEventKind.Invalid, sample);
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

        private static string ResolveEventType(DualHandEventKind eventKind)
        {
            switch (eventKind)
            {
                case DualHandEventKind.LeftMarked:
                    return "DUAL_HAND_MARK_LEFT";
                case DualHandEventKind.RightMarked:
                    return "DUAL_HAND_MARK_RIGHT";
                case DualHandEventKind.SynchronizedMarked:
                    return "DUAL_HAND_MARK_SYNC";
                case DualHandEventKind.OutOfSync:
                    return "DUAL_HAND_MARK_OUT_OF_SYNC";
                case DualHandEventKind.Invalid:
                    return "DUAL_HAND_MARK_INVALID";
                default:
                    return "DUAL_HAND_EVENT";
            }
        }

        private static bool ResolveTargetValid(DualHandEventKind eventKind, DualHandSample sample)
        {
            if (sample.targetValid.HasValue)
            {
                return sample.targetValid.Value;
            }

            return eventKind != DualHandEventKind.Invalid;
        }

        private static string ResolveReasonCode(
            DualHandEventKind eventKind,
            string explicitReasonCode,
            bool targetValid)
        {
            if (!string.IsNullOrWhiteSpace(explicitReasonCode))
            {
                return explicitReasonCode.Trim();
            }

            if (!targetValid)
            {
                return "DUAL_HAND_TARGET_INVALID";
            }

            switch (eventKind)
            {
                case DualHandEventKind.LeftMarked:
                case DualHandEventKind.RightMarked:
                    return "DUAL_HAND_WAITING_FOR_SECOND_HAND";
                case DualHandEventKind.SynchronizedMarked:
                    return "DUAL_HAND_SYNC_CONFIRMED";
                case DualHandEventKind.OutOfSync:
                    return "DUAL_HAND_SYNC_WINDOW_EXCEEDED";
                case DualHandEventKind.Invalid:
                    return "DUAL_HAND_TARGET_INVALID";
                default:
                    return "DUAL_HAND_EVENT_OBSERVED";
            }
        }

        private static string ResolvePairTargetId(string leftTargetId, string rightTargetId)
        {
            var normalizedLeft = NormalizeOrFallback(leftTargetId, string.Empty);
            var normalizedRight = NormalizeOrFallback(rightTargetId, string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedLeft))
            {
                return normalizedRight;
            }

            if (string.IsNullOrWhiteSpace(normalizedRight))
            {
                return normalizedLeft;
            }

            return normalizedLeft + "|" + normalizedRight;
        }

        private static string ResolvePairTargetName(string leftTargetName, string rightTargetName)
        {
            var normalizedLeft = NormalizeOrFallback(leftTargetName, string.Empty);
            var normalizedRight = NormalizeOrFallback(rightTargetName, string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedLeft))
            {
                return normalizedRight;
            }

            if (string.IsNullOrWhiteSpace(normalizedRight))
            {
                return normalizedLeft;
            }

            return normalizedLeft + "|" + normalizedRight;
        }

        private static string NormalizeOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? (fallback ?? string.Empty)
                : value.Trim();
        }
    }
}
