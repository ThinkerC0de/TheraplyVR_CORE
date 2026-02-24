using System.Collections.Generic;
using UnityEngine;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Interactions
{
    [DisallowMultipleComponent]
    public sealed class GazeProbe : MonoBehaviour
    {
        public enum GazeEventKind
        {
            HoldTick = 0,
            HoldCompleted = 1,
            HoldInvalid = 2,
            ToolSelection = 3,
            ToolSelectionInvalid = 4,
        }

        public struct GazeSample
        {
            public string gameId;
            public string inputSource;
            public string inputControl;
            public string sourceComponent;
            public string targetId;
            public string targetName;
            public float dwellSec;
            public float requiredDwellSec;
            public float inputValue;
            public bool? targetValid;
            public string reasonCode;
        }

        [Header("Behavior")]
        [SerializeField] private bool _emitTelemetry = true;
        [SerializeField] private bool _logTelemetry;

        [Header("Dependencies")]
        [SerializeField] private InteractionEventBridge _interactionEventBridge;

        public static GazeProbe Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (Instance != null)
            {
                return;
            }

            var existing = FindFirstObjectByType<GazeProbe>();
            if (existing != null)
            {
                Instance = existing;
                return;
            }

            var host = new GameObject("GazeProbe");
            host.AddComponent<GazeProbe>();
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

        public void RecordEvent(GazeEventKind eventKind, GazeSample sample)
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
            var normalizedTargetId = NormalizeOrFallback(sample.targetId, string.Empty);
            var normalizedTargetName = NormalizeOrFallback(sample.targetName, normalizedTargetId);

            var details = new Dictionary<string, object>
            {
                { "actionId", actionId },
                { "dwellSec", Mathf.Max(0f, sample.dwellSec) },
                { "requiredDwellSec", Mathf.Max(0f, sample.requiredDwellSec) },
                { "targetValid", targetValid },
            };

            var payload = new Dictionary<string, object>
            {
                { "gazeEventType", eventType },
                { "actionId", actionId },
                { "gameId", NormalizeOrFallback(sample.gameId, "unknown_game") },
                { "sourceComponent", NormalizeOrFallback(sample.sourceComponent, nameof(GazeProbe)) },
                { "inputSource", NormalizeOrFallback(sample.inputSource, "GAZE") },
                { "inputControl", NormalizeOrFallback(sample.inputControl, string.Empty) },
                { "inputValue", Mathf.Max(0f, sample.inputValue) },
                { "targetId", normalizedTargetId },
                { "targetName", normalizedTargetName },
                { "targetValid", targetValid },
                { "reasonCode", reasonCode },
                { "details", details },
            };

            _interactionEventBridge.RecordGazeTelemetry(payload);

            if (_logTelemetry)
            {
                Logger.Info(
                    $"[GazeProbe] eventType={eventType} actionId={actionId} targetValid={targetValid} targetId={normalizedTargetId}");
            }
        }

        public void RecordHoldTick(GazeSample sample)
        {
            RecordEvent(GazeEventKind.HoldTick, sample);
        }

        public void RecordHoldCompleted(GazeSample sample)
        {
            RecordEvent(GazeEventKind.HoldCompleted, sample);
        }

        public void RecordHoldInvalid(GazeSample sample)
        {
            RecordEvent(GazeEventKind.HoldInvalid, sample);
        }

        public void RecordToolSelection(GazeSample sample)
        {
            RecordEvent(GazeEventKind.ToolSelection, sample);
        }

        public void RecordToolSelectionInvalid(GazeSample sample)
        {
            RecordEvent(GazeEventKind.ToolSelectionInvalid, sample);
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

        private static string ResolveActionId(GazeEventKind eventKind)
        {
            switch (eventKind)
            {
                case GazeEventKind.HoldTick:
                case GazeEventKind.HoldCompleted:
                case GazeEventKind.HoldInvalid:
                    return "hold_gaze_on_target";
                case GazeEventKind.ToolSelection:
                case GazeEventKind.ToolSelectionInvalid:
                    return "select_target_with_gaze_and_tool";
                default:
                    return string.Empty;
            }
        }

        private static string ResolveEventType(GazeEventKind eventKind)
        {
            switch (eventKind)
            {
                case GazeEventKind.HoldTick:
                    return "GAZE_HOLD_TICK";
                case GazeEventKind.HoldCompleted:
                    return "GAZE_HOLD_COMPLETED";
                case GazeEventKind.HoldInvalid:
                    return "GAZE_HOLD_INVALID";
                case GazeEventKind.ToolSelection:
                    return "GAZE_TOOL_SELECT";
                case GazeEventKind.ToolSelectionInvalid:
                    return "GAZE_TOOL_SELECT_INVALID";
                default:
                    return "GAZE_EVENT";
            }
        }

        private static bool ResolveTargetValid(GazeEventKind eventKind, GazeSample sample)
        {
            if (sample.targetValid.HasValue)
            {
                return sample.targetValid.Value;
            }

            return eventKind != GazeEventKind.HoldInvalid && eventKind != GazeEventKind.ToolSelectionInvalid;
        }

        private static string ResolveReasonCode(
            GazeEventKind eventKind,
            string explicitReasonCode,
            bool targetValid)
        {
            if (!string.IsNullOrWhiteSpace(explicitReasonCode))
            {
                return explicitReasonCode.Trim();
            }

            if (!targetValid)
            {
                return "GAZE_TARGET_INVALID";
            }

            switch (eventKind)
            {
                case GazeEventKind.HoldTick:
                    return "GAZE_DWELL_PROGRESS";
                case GazeEventKind.HoldCompleted:
                    return "GAZE_DWELL_REACHED";
                case GazeEventKind.HoldInvalid:
                    return "GAZE_TARGET_INVALID";
                case GazeEventKind.ToolSelection:
                    return "GAZE_TOOL_CONFIRMED";
                case GazeEventKind.ToolSelectionInvalid:
                    return "GAZE_TOOL_INVALID";
                default:
                    return "GAZE_EVENT_OBSERVED";
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
