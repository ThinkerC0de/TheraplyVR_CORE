using System.Collections.Generic;
using UnityEngine;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Interactions
{
    [DisallowMultipleComponent]
    public sealed class GrabPlaceProbe : MonoBehaviour
    {
        public enum GrabPlaceEventKind
        {
            GrabStarted = 0,
            GrabReleased = 1,
            PlacedInZone = 2,
            RemovedFromZone = 3,
            CollectedToContainer = 4,
        }

        public struct GrabPlaceSample
        {
            public string gameId;
            public string inputHand;
            public string inputSource;
            public string inputControl;
            public string sourceComponent;
            public string objectId;
            public string objectName;
            public string zoneId;
            public string zoneName;
            public float inputValue;
            public bool? targetValid;
            public string reasonCode;
        }

        [Header("Behavior")]
        [SerializeField] private bool _emitTelemetry = true;
        [SerializeField] private bool _logTelemetry;

        [Header("Dependencies")]
        [SerializeField] private InteractionEventBridge _interactionEventBridge;

        public static GrabPlaceProbe Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (Instance != null)
            {
                return;
            }

            var existing = FindFirstObjectByType<GrabPlaceProbe>();
            if (existing != null)
            {
                Instance = existing;
                return;
            }

            var host = new GameObject("GrabPlaceProbe");
            host.AddComponent<GrabPlaceProbe>();
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

        public void RecordEvent(GrabPlaceEventKind eventKind, GrabPlaceSample sample)
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
            var actionId = ResolveActionId(eventKind);
            var targetId = ResolveTargetId(eventKind, sample);
            var targetName = ResolveTargetName(eventKind, sample);
            var targetValid = sample.targetValid ?? true;

            var details = new Dictionary<string, object>
            {
                { "actionId", actionId },
                { "objectId", NormalizeOrFallback(sample.objectId, string.Empty) },
                { "objectName", NormalizeOrFallback(sample.objectName, string.Empty) },
                { "zoneId", NormalizeOrFallback(sample.zoneId, string.Empty) },
                { "zoneName", NormalizeOrFallback(sample.zoneName, string.Empty) },
                { "targetValid", targetValid },
            };

            var payload = new Dictionary<string, object>
            {
                { "grabEventType", eventType },
                { "actionId", actionId },
                { "gameId", NormalizeOrFallback(sample.gameId, "unknown_game") },
                { "sourceComponent", NormalizeOrFallback(sample.sourceComponent, nameof(GrabPlaceProbe)) },
                { "inputHand", NormalizeOrFallback(sample.inputHand, string.Empty) },
                { "inputSource", NormalizeOrFallback(sample.inputSource, "HAND_GRAB") },
                { "inputControl", NormalizeOrFallback(sample.inputControl, string.Empty) },
                { "inputValue", Mathf.Clamp01(sample.inputValue) },
                { "targetId", targetId },
                { "targetName", targetName },
                { "targetValid", targetValid },
                { "reasonCode", ResolveReasonCode(eventKind, sample.reasonCode, targetValid) },
                { "details", details },
            };

            _interactionEventBridge.RecordGrabTelemetry(payload);

            if (_logTelemetry)
            {
                Logger.Info(
                    $"[GrabPlaceProbe] eventType={eventType} actionId={actionId} targetValid={targetValid} targetId={targetId}");
            }
        }

        public void RecordGrabStarted(GrabPlaceSample sample)
        {
            RecordEvent(GrabPlaceEventKind.GrabStarted, sample);
        }

        public void RecordGrabReleased(GrabPlaceSample sample)
        {
            RecordEvent(GrabPlaceEventKind.GrabReleased, sample);
        }

        public void RecordPlacedInZone(GrabPlaceSample sample)
        {
            RecordEvent(GrabPlaceEventKind.PlacedInZone, sample);
        }

        public void RecordRemovedFromZone(GrabPlaceSample sample)
        {
            RecordEvent(GrabPlaceEventKind.RemovedFromZone, sample);
        }

        public void RecordCollectedToContainer(GrabPlaceSample sample)
        {
            RecordEvent(GrabPlaceEventKind.CollectedToContainer, sample);
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

        private static string ResolveEventType(GrabPlaceEventKind eventKind)
        {
            switch (eventKind)
            {
                case GrabPlaceEventKind.GrabStarted:
                    return "GRAB_OBJECT_START";
                case GrabPlaceEventKind.GrabReleased:
                    return "GRAB_OBJECT_RELEASE";
                case GrabPlaceEventKind.PlacedInZone:
                    return "GRAB_OBJECT_PLACED";
                case GrabPlaceEventKind.RemovedFromZone:
                    return "GRAB_OBJECT_REMOVED";
                case GrabPlaceEventKind.CollectedToContainer:
                    return "GRAB_OBJECT_COLLECTED";
                default:
                    return "GRAB_EVENT";
            }
        }

        private static string ResolveActionId(GrabPlaceEventKind eventKind)
        {
            switch (eventKind)
            {
                case GrabPlaceEventKind.GrabStarted:
                    return "grab_object";
                case GrabPlaceEventKind.GrabReleased:
                    return "release_object";
                case GrabPlaceEventKind.PlacedInZone:
                    return "place_object_in_zone";
                case GrabPlaceEventKind.RemovedFromZone:
                    return "remove_object_from_zone";
                case GrabPlaceEventKind.CollectedToContainer:
                    return "collect_item_to_container";
                default:
                    return string.Empty;
            }
        }

        private static string ResolveTargetId(GrabPlaceEventKind eventKind, GrabPlaceSample sample)
        {
            switch (eventKind)
            {
                case GrabPlaceEventKind.PlacedInZone:
                case GrabPlaceEventKind.RemovedFromZone:
                case GrabPlaceEventKind.CollectedToContainer:
                    return NormalizeOrFallback(sample.zoneId, sample.objectId);
                default:
                    return NormalizeOrFallback(sample.objectId, sample.zoneId);
            }
        }

        private static string ResolveTargetName(GrabPlaceEventKind eventKind, GrabPlaceSample sample)
        {
            switch (eventKind)
            {
                case GrabPlaceEventKind.PlacedInZone:
                case GrabPlaceEventKind.RemovedFromZone:
                case GrabPlaceEventKind.CollectedToContainer:
                    return NormalizeOrFallback(sample.zoneName, sample.objectName);
                default:
                    return NormalizeOrFallback(sample.objectName, sample.zoneName);
            }
        }

        private static string ResolveReasonCode(
            GrabPlaceEventKind eventKind,
            string explicitReasonCode,
            bool targetValid)
        {
            if (!string.IsNullOrWhiteSpace(explicitReasonCode))
            {
                return explicitReasonCode.Trim();
            }

            if (!targetValid)
            {
                return "TARGET_INVALID";
            }

            switch (eventKind)
            {
                case GrabPlaceEventKind.GrabStarted:
                    return "OBJECT_GRABBED";
                case GrabPlaceEventKind.GrabReleased:
                    return "OBJECT_RELEASED";
                case GrabPlaceEventKind.PlacedInZone:
                    return "OBJECT_PLACED";
                case GrabPlaceEventKind.RemovedFromZone:
                    return "OBJECT_REMOVED";
                case GrabPlaceEventKind.CollectedToContainer:
                    return "OBJECT_COLLECTED";
                default:
                    return "GRAB_EVENT_OBSERVED";
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
