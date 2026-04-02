using System.Collections.Generic;
using UnityEngine;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Interactions
{
    [DisallowMultipleComponent]
    public sealed class TimelineProbe : MonoBehaviour
    {
        public enum TimelineEventKind
        {
            SegmentTick = 0,
            SegmentCompleted = 1,
            SegmentInterrupted = 2,
            SegmentSkipped = 3,
        }

        public struct TimelineSample
        {
            public string gameId;
            public string inputSource;
            public string inputControl;
            public string sourceComponent;
            public string segmentId;
            public string segmentName;
            public float elapsedSec;
            public float requiredSec;
            public float progress01;
            public float attentionScore;
            public bool interrupted;
            public bool? targetValid;
            public string reasonCode;
        }

        [Header("Behavior")]
        [SerializeField] private bool _emitTelemetry = true;
        [SerializeField] private bool _logTelemetry;

        [Header("Dependencies")]
        [SerializeField] private InteractionEventBridge _interactionEventBridge;

        public static TimelineProbe Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (Instance != null)
            {
                return;
            }

            var existing = FindFirstObjectByType<TimelineProbe>();
            if (existing != null)
            {
                Instance = existing;
                return;
            }

            var host = new GameObject("TimelineProbe");
            host.AddComponent<TimelineProbe>();
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

        public void RecordEvent(TimelineEventKind eventKind, TimelineSample sample)
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
                { "actionId", "watch_timeline_segment" },
                { "segmentId", NormalizeOrFallback(sample.segmentId, string.Empty) },
                { "segmentName", NormalizeOrFallback(sample.segmentName, string.Empty) },
                { "elapsedSec", Mathf.Max(0f, sample.elapsedSec) },
                { "requiredSec", Mathf.Max(0f, sample.requiredSec) },
                { "progress01", Mathf.Clamp01(sample.progress01) },
                { "attentionScore", Mathf.Max(0f, sample.attentionScore) },
                { "interrupted", sample.interrupted },
                { "targetValid", targetValid },
            };

            var payload = new Dictionary<string, object>
            {
                { "timelineEventType", eventType },
                { "actionId", "watch_timeline_segment" },
                { "gameId", NormalizeOrFallback(sample.gameId, "unknown_game") },
                { "sourceComponent", NormalizeOrFallback(sample.sourceComponent, nameof(TimelineProbe)) },
                { "inputSource", NormalizeOrFallback(sample.inputSource, "TIMELINE") },
                { "inputControl", NormalizeOrFallback(sample.inputControl, string.Empty) },
                { "inputValue", Mathf.Clamp01(sample.progress01) },
                { "targetId", NormalizeOrFallback(sample.segmentId, string.Empty) },
                { "targetName", NormalizeOrFallback(sample.segmentName, string.Empty) },
                { "segmentId", NormalizeOrFallback(sample.segmentId, string.Empty) },
                { "segmentName", NormalizeOrFallback(sample.segmentName, string.Empty) },
                { "elapsedSec", Mathf.Max(0f, sample.elapsedSec) },
                { "requiredSec", Mathf.Max(0f, sample.requiredSec) },
                { "progress01", Mathf.Clamp01(sample.progress01) },
                { "attentionScore", Mathf.Max(0f, sample.attentionScore) },
                { "interrupted", sample.interrupted },
                { "targetValid", targetValid },
                { "reasonCode", reasonCode },
                { "details", details },
            };

            _interactionEventBridge.RecordTimelineTelemetry(payload);

            if (_logTelemetry)
            {
                Logger.Info(
                    "[TimelineProbe] eventType=" +
                    eventType +
                    " progress01=" +
                    Mathf.Clamp01(sample.progress01).ToString("F2") +
                    " interrupted=" +
                    sample.interrupted);
            }
        }

        public void RecordSegmentTick(TimelineSample sample)
        {
            RecordEvent(TimelineEventKind.SegmentTick, sample);
        }

        public void RecordSegmentCompleted(TimelineSample sample)
        {
            RecordEvent(TimelineEventKind.SegmentCompleted, sample);
        }

        public void RecordSegmentInterrupted(TimelineSample sample)
        {
            RecordEvent(TimelineEventKind.SegmentInterrupted, sample);
        }

        public void RecordSegmentSkipped(TimelineSample sample)
        {
            RecordEvent(TimelineEventKind.SegmentSkipped, sample);
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

        private static string ResolveEventType(TimelineEventKind eventKind)
        {
            switch (eventKind)
            {
                case TimelineEventKind.SegmentTick:
                    return "TIMELINE_SEGMENT_TICK";
                case TimelineEventKind.SegmentCompleted:
                    return "TIMELINE_SEGMENT_COMPLETED";
                case TimelineEventKind.SegmentInterrupted:
                    return "TIMELINE_SEGMENT_INTERRUPTED";
                case TimelineEventKind.SegmentSkipped:
                    return "TIMELINE_SEGMENT_SKIPPED";
                default:
                    return "TIMELINE_EVENT";
            }
        }

        private static bool ResolveTargetValid(TimelineEventKind eventKind, TimelineSample sample)
        {
            if (sample.targetValid.HasValue)
            {
                return sample.targetValid.Value;
            }

            return eventKind != TimelineEventKind.SegmentInterrupted &&
                   eventKind != TimelineEventKind.SegmentSkipped;
        }

        private static string ResolveReasonCode(
            TimelineEventKind eventKind,
            string explicitReasonCode,
            bool targetValid)
        {
            if (!string.IsNullOrWhiteSpace(explicitReasonCode))
            {
                return explicitReasonCode.Trim();
            }

            if (!targetValid)
            {
                return eventKind == TimelineEventKind.SegmentInterrupted
                    ? "TIMELINE_INTERRUPTED"
                    : "TIMELINE_SEGMENT_SKIPPED";
            }

            switch (eventKind)
            {
                case TimelineEventKind.SegmentTick:
                    return "TIMELINE_PROGRESS";
                case TimelineEventKind.SegmentCompleted:
                    return "TIMELINE_SEGMENT_WATCHED";
                case TimelineEventKind.SegmentInterrupted:
                    return "TIMELINE_INTERRUPTED";
                case TimelineEventKind.SegmentSkipped:
                    return "TIMELINE_SEGMENT_SKIPPED";
                default:
                    return "TIMELINE_EVENT_OBSERVED";
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
