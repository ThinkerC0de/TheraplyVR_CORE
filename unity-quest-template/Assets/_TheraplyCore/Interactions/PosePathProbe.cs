using System.Collections.Generic;
using UnityEngine;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Interactions
{
    [DisallowMultipleComponent]
    public sealed class PosePathProbe : MonoBehaviour
    {
        public enum PosePathEventKind
        {
            HoldTick = 0,
            HoldCompleted = 1,
            HoldInvalid = 2,
            FollowTick = 3,
            FollowCompleted = 4,
            FollowDeviation = 5,
        }

        public struct PosePathSample
        {
            public string gameId;
            public string inputHand;
            public string inputSource;
            public string inputControl;
            public string sourceComponent;
            public string targetId;
            public string targetName;
            public float holdProgress01;
            public float holdElapsedSec;
            public float holdRequiredSec;
            public float pathProgress01;
            public float pathCoverage01;
            public float pathDeviation;
            public float tolerance;
            public float inputValue;
            public bool? targetValid;
            public string reasonCode;
        }

        [Header("Behavior")]
        [SerializeField] private bool _emitTelemetry = true;
        [SerializeField] private bool _logTelemetry;

        [Header("Dependencies")]
        [SerializeField] private InteractionEventBridge _interactionEventBridge;

        public static PosePathProbe Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (Instance != null)
            {
                return;
            }

            var existing = FindFirstObjectByType<PosePathProbe>();
            if (existing != null)
            {
                Instance = existing;
                return;
            }

            var host = new GameObject("PosePathProbe");
            host.AddComponent<PosePathProbe>();
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

        public void RecordEvent(PosePathEventKind eventKind, PosePathSample sample)
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

            var actionId = ResolveActionId(eventKind);
            var eventType = ResolveEventType(eventKind);
            var targetValid = ResolveTargetValid(eventKind, sample);
            var reasonCode = ResolveReasonCode(eventKind, sample.reasonCode, targetValid);

            var details = new Dictionary<string, object>
            {
                { "actionId", actionId },
                { "holdProgress01", Mathf.Clamp01(sample.holdProgress01) },
                { "holdElapsedSec", Mathf.Max(0f, sample.holdElapsedSec) },
                { "holdRequiredSec", Mathf.Max(0f, sample.holdRequiredSec) },
                { "pathProgress01", Mathf.Clamp01(sample.pathProgress01) },
                { "pathCoverage01", Mathf.Clamp01(sample.pathCoverage01) },
                { "pathDeviation", Mathf.Max(0f, sample.pathDeviation) },
                { "tolerance", Mathf.Max(0f, sample.tolerance) },
                { "targetValid", targetValid },
            };

            var payload = new Dictionary<string, object>
            {
                { "posePathEventType", eventType },
                { "actionId", actionId },
                { "gameId", NormalizeOrFallback(sample.gameId, "unknown_game") },
                { "sourceComponent", NormalizeOrFallback(sample.sourceComponent, nameof(PosePathProbe)) },
                { "inputHand", NormalizeOrFallback(sample.inputHand, string.Empty) },
                { "inputSource", NormalizeOrFallback(sample.inputSource, "POSE_PATH") },
                { "inputControl", NormalizeOrFallback(sample.inputControl, string.Empty) },
                { "inputValue", Mathf.Clamp01(sample.inputValue) },
                { "targetId", NormalizeOrFallback(sample.targetId, string.Empty) },
                { "targetName", NormalizeOrFallback(sample.targetName, string.Empty) },
                { "holdProgress01", Mathf.Clamp01(sample.holdProgress01) },
                { "holdElapsedSec", Mathf.Max(0f, sample.holdElapsedSec) },
                { "holdRequiredSec", Mathf.Max(0f, sample.holdRequiredSec) },
                { "pathProgress01", Mathf.Clamp01(sample.pathProgress01) },
                { "pathCoverage01", Mathf.Clamp01(sample.pathCoverage01) },
                { "pathDeviation", Mathf.Max(0f, sample.pathDeviation) },
                { "tolerance", Mathf.Max(0f, sample.tolerance) },
                { "targetValid", targetValid },
                { "reasonCode", reasonCode },
                { "details", details },
            };

            _interactionEventBridge.RecordPosePathTelemetry(payload);

            if (_logTelemetry)
            {
                Logger.Info(
                    "[PosePathProbe] eventType=" +
                    eventType +
                    " actionId=" +
                    actionId +
                    " targetValid=" +
                    targetValid);
            }
        }

        public void RecordHoldTick(PosePathSample sample)
        {
            RecordEvent(PosePathEventKind.HoldTick, sample);
        }

        public void RecordHoldCompleted(PosePathSample sample)
        {
            RecordEvent(PosePathEventKind.HoldCompleted, sample);
        }

        public void RecordHoldInvalid(PosePathSample sample)
        {
            RecordEvent(PosePathEventKind.HoldInvalid, sample);
        }

        public void RecordFollowTick(PosePathSample sample)
        {
            RecordEvent(PosePathEventKind.FollowTick, sample);
        }

        public void RecordFollowCompleted(PosePathSample sample)
        {
            RecordEvent(PosePathEventKind.FollowCompleted, sample);
        }

        public void RecordFollowDeviation(PosePathSample sample)
        {
            RecordEvent(PosePathEventKind.FollowDeviation, sample);
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

        private static string ResolveActionId(PosePathEventKind eventKind)
        {
            switch (eventKind)
            {
                case PosePathEventKind.HoldTick:
                case PosePathEventKind.HoldCompleted:
                case PosePathEventKind.HoldInvalid:
                    return "hold_pose";
                case PosePathEventKind.FollowTick:
                case PosePathEventKind.FollowCompleted:
                case PosePathEventKind.FollowDeviation:
                    return "follow_path";
                default:
                    return string.Empty;
            }
        }

        private static string ResolveEventType(PosePathEventKind eventKind)
        {
            switch (eventKind)
            {
                case PosePathEventKind.HoldTick:
                    return "POSE_PATH_HOLD_TICK";
                case PosePathEventKind.HoldCompleted:
                    return "POSE_PATH_HOLD_COMPLETED";
                case PosePathEventKind.HoldInvalid:
                    return "POSE_PATH_HOLD_INVALID";
                case PosePathEventKind.FollowTick:
                    return "POSE_PATH_FOLLOW_TICK";
                case PosePathEventKind.FollowCompleted:
                    return "POSE_PATH_FOLLOW_COMPLETED";
                case PosePathEventKind.FollowDeviation:
                    return "POSE_PATH_FOLLOW_DEVIATION";
                default:
                    return "POSE_PATH_EVENT";
            }
        }

        private static bool ResolveTargetValid(PosePathEventKind eventKind, PosePathSample sample)
        {
            if (sample.targetValid.HasValue)
            {
                return sample.targetValid.Value;
            }

            return eventKind != PosePathEventKind.HoldInvalid &&
                   eventKind != PosePathEventKind.FollowDeviation;
        }

        private static string ResolveReasonCode(
            PosePathEventKind eventKind,
            string explicitReasonCode,
            bool targetValid)
        {
            if (!string.IsNullOrWhiteSpace(explicitReasonCode))
            {
                return explicitReasonCode.Trim();
            }

            if (!targetValid)
            {
                return eventKind == PosePathEventKind.FollowDeviation
                    ? "PATH_DEVIATION_EXCEEDED"
                    : "POSE_TOLERANCE_EXCEEDED";
            }

            switch (eventKind)
            {
                case PosePathEventKind.HoldTick:
                    return "POSE_HOLD_PROGRESS";
                case PosePathEventKind.HoldCompleted:
                    return "POSE_HOLD_COMPLETED";
                case PosePathEventKind.HoldInvalid:
                    return "POSE_TOLERANCE_EXCEEDED";
                case PosePathEventKind.FollowTick:
                    return "PATH_PROGRESS";
                case PosePathEventKind.FollowCompleted:
                    return "PATH_FOLLOW_COMPLETED";
                case PosePathEventKind.FollowDeviation:
                    return "PATH_DEVIATION_EXCEEDED";
                default:
                    return "POSE_PATH_EVENT_OBSERVED";
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
