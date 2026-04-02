using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using TheraplyCore.Games.Runtime;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Interactions
{
    [DisallowMultipleComponent]
    public sealed class InteractionEventBridge : MonoBehaviour
    {
        private const string CanonicalEventName = "interaction_event";
        private const string SchemaName = "THERAPLY_INTERACTION_SCHEMA";
        private const string SchemaVersion = "2026-02-22";
        private const string SourceOfTruth = "MOBILE_CONTROLLER";

        [Serializable]
        private sealed class GameAttemptContext
        {
            public string taskRunId;
            public string attemptId;
            public int eventCount;
        }

        private struct SessionMetadata
        {
            public string sessionId;
            public string patientId;
            public string therapistId;
            public string ownerKey;
            public string sessionKey;
        }

        [Header("Dependencies")]
        [SerializeField] private GameTelemetryService _gameTelemetryService;
        [SerializeField] private GameSessionContext _sessionContext;
        [SerializeField] private GameRuntimeService _gameRuntimeService;

        [Header("Behavior")]
        [SerializeField] private bool _emitCanonicalEvents = true;
        [SerializeField] private bool _logCanonicalEvents;

        private readonly Dictionary<string, GameAttemptContext> _attemptByGameId =
            new Dictionary<string, GameAttemptContext>(StringComparer.OrdinalIgnoreCase);

        private long _sequenceNumber;
        private string _sequenceSessionKey = string.Empty;
        private int _eventsEmitted;
        private bool _missingTelemetryServiceLogged;

        public static InteractionEventBridge Instance { get; private set; }

        public event Action<IReadOnlyDictionary<string, object>> EventPublished;

        public int EventsEmitted => _eventsEmitted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (Instance != null)
            {
                return;
            }

            var existing = FindFirstObjectByType<InteractionEventBridge>();
            if (existing != null)
            {
                Instance = existing;
                DontDestroyOnLoad(existing.gameObject);
                return;
            }

            var host = new GameObject("InteractionEventBridge");
            DontDestroyOnLoad(host);
            host.AddComponent<InteractionEventBridge>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            ResolveDependencies();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public IReadOnlyDictionary<string, object> RecordGameplayEvent(
            string gameId,
            string moduleEventType,
            string gameState,
            IReadOnlyDictionary<string, object> payload,
            string sourceComponent)
        {
            var details = payload != null
                ? new Dictionary<string, object>(payload)
                : new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(gameState) && !details.ContainsKey("gameState"))
            {
                details["gameState"] = gameState.Trim();
            }

            var normalizedGameId = NormalizeOrFallback(gameId, ResolveActiveGameId(), "unknown_game");
            var normalizedEventType = NormalizeEventToken(moduleEventType, "GAMEPLAY_EVENT");
            var normalizedSourceComponent = NormalizeOrFallback(sourceComponent, "GameModuleBase");
            var attemptContext = ResolveAttemptContext(normalizedGameId, normalizedEventType);
            var actionOutcome = ResolveGameplayOutcome(normalizedEventType, details);
            var reasonCode = ResolveReasonCode(details, string.Empty);

            var eventPayload = EmitCanonicalEvent(
                normalizedGameId,
                normalizedEventType,
                "GAMEPLAY",
                actionOutcome,
                reasonCode,
                normalizedSourceComponent,
                attemptContext,
                details,
                inputHand: TryReadString(details, "inputHand"),
                inputSource: TryReadString(details, "inputSource"),
                inputControl: TryReadString(details, "inputControl"),
                inputValue: TryReadFloat(details, "inputValue"),
                targetId: TryReadString(details, "targetId"),
                targetName: TryReadString(details, "targetName"),
                targetValid: TryReadNullableBool(details, "targetValid"));

            if (IsTerminalGameplayEvent(normalizedEventType))
            {
                _attemptByGameId.Remove(normalizedGameId);
            }

            return eventPayload;
        }

        public IReadOnlyDictionary<string, object> RecordPointerShot(IReadOnlyDictionary<string, object> payload)
        {
            var details = payload != null
                ? new Dictionary<string, object>(payload)
                : new Dictionary<string, object>();

            var normalizedGameId = NormalizeOrFallback(
                TryReadString(details, "gameId"),
                ResolveActiveGameId(),
                "unknown_game");
            var eventType = ResolvePointerEventType(details);
            var sourceComponent = NormalizeOrFallback(
                TryReadString(details, "sourceComponent"),
                "QuestPointerClickInteractor");
            var attemptContext = ResolveAttemptContext(normalizedGameId, eventType);
            var reasonCode = ResolvePointerReasonCode(details, eventType);

            return EmitCanonicalEvent(
                normalizedGameId,
                eventType,
                "POINTER",
                ResolvePointerOutcome(eventType),
                reasonCode,
                sourceComponent,
                attemptContext,
                details,
                inputHand: TryReadString(details, "inputHand"),
                inputSource: NormalizeOrFallback(TryReadString(details, "inputSource"), "POINTER"),
                inputControl: TryReadString(details, "inputControl"),
                inputValue: TryReadFloat(details, "inputValue"),
                targetId: NormalizeOrFallback(TryReadString(details, "targetId"), TryReadString(details, "hitObjectName")),
                targetName: TryReadString(details, "hitObjectName"),
                targetValid: TryReadNullableBool(details, "hoverIsValidTarget"));
        }

        public IReadOnlyDictionary<string, object> RecordToolTelemetry(IReadOnlyDictionary<string, object> payload)
        {
            var details = payload != null
                ? new Dictionary<string, object>(payload)
                : new Dictionary<string, object>();

            var normalizedGameId = NormalizeOrFallback(
                TryReadString(details, "gameId"),
                ResolveActiveGameId(),
                "unknown_game");
            var eventType = ResolveToolEventType(details);
            var sourceComponent = NormalizeOrFallback(
                TryReadString(details, "sourceComponent"),
                "ToolTelemetry");
            var attemptContext = ResolveAttemptContext(normalizedGameId, eventType);
            var actionOutcome = ResolveToolOutcome(details, eventType);
            var reasonCode = ResolveToolReasonCode(details, eventType);
            var targetName = NormalizeOrFallback(
                TryReadString(details, "targetName"),
                TryReadString(details, "hitObjectName"));

            return EmitCanonicalEvent(
                normalizedGameId,
                eventType,
                "TOOL",
                actionOutcome,
                reasonCode,
                sourceComponent,
                attemptContext,
                details,
                inputHand: TryReadString(details, "inputHand"),
                inputSource: NormalizeOrFallback(TryReadString(details, "inputSource"), "TOOL"),
                inputControl: TryReadString(details, "inputControl"),
                inputValue: TryReadFloat(details, "inputValue"),
                targetId: NormalizeOrFallback(
                    TryReadString(details, "targetId"),
                    targetName,
                    string.Empty),
                targetName: targetName,
                targetValid: TryReadNullableBool(details, "targetValid"));
        }

        public IReadOnlyDictionary<string, object> RecordHandTelemetry(IReadOnlyDictionary<string, object> payload)
        {
            var details = payload != null
                ? new Dictionary<string, object>(payload)
                : new Dictionary<string, object>();

            var normalizedGameId = NormalizeOrFallback(
                TryReadString(details, "gameId"),
                ResolveActiveGameId(),
                "unknown_game");
            var eventType = ResolveHandEventType(details);
            var sourceComponent = NormalizeOrFallback(
                TryReadString(details, "sourceComponent"),
                "HandContactTelemetry");
            var attemptContext = ResolveAttemptContext(normalizedGameId, eventType);
            var actionOutcome = ResolveHandOutcome(details, eventType);
            var reasonCode = ResolveHandReasonCode(details, eventType);
            var targetName = NormalizeOrFallback(
                TryReadString(details, "targetName"),
                TryReadString(details, "hitObjectName"));

            return EmitCanonicalEvent(
                normalizedGameId,
                eventType,
                "HAND",
                actionOutcome,
                reasonCode,
                sourceComponent,
                attemptContext,
                details,
                inputHand: TryReadString(details, "inputHand"),
                inputSource: NormalizeOrFallback(TryReadString(details, "inputSource"), "HAND"),
                inputControl: TryReadString(details, "inputControl"),
                inputValue: TryReadFloat(details, "inputValue"),
                targetId: NormalizeOrFallback(
                    TryReadString(details, "targetId"),
                    targetName,
                    string.Empty),
                targetName: targetName,
                targetValid: TryReadNullableBool(details, "targetValid"));
        }

        public IReadOnlyDictionary<string, object> RecordGrabTelemetry(IReadOnlyDictionary<string, object> payload)
        {
            var details = payload != null
                ? new Dictionary<string, object>(payload)
                : new Dictionary<string, object>();

            var normalizedGameId = NormalizeOrFallback(
                TryReadString(details, "gameId"),
                ResolveActiveGameId(),
                "unknown_game");
            var eventType = ResolveGrabEventType(details);
            var sourceComponent = NormalizeOrFallback(
                TryReadString(details, "sourceComponent"),
                "GrabTelemetry");
            var attemptContext = ResolveAttemptContext(normalizedGameId, eventType);
            var actionOutcome = ResolveGrabOutcome(details, eventType);
            var reasonCode = ResolveGrabReasonCode(details, eventType);

            var zoneName = NormalizeOrFallback(
                TryReadString(details, "zoneName"),
                TryReadString(details, "targetName"));
            var zoneId = NormalizeOrFallback(
                TryReadString(details, "zoneId"),
                TryReadString(details, "targetId"));
            var objectName = NormalizeOrFallback(
                TryReadString(details, "objectName"),
                TryReadString(details, "itemName"));
            var objectId = NormalizeOrFallback(
                TryReadString(details, "objectId"),
                TryReadString(details, "itemId"));

            return EmitCanonicalEvent(
                normalizedGameId,
                eventType,
                "GRAB",
                actionOutcome,
                reasonCode,
                sourceComponent,
                attemptContext,
                details,
                inputHand: TryReadString(details, "inputHand"),
                inputSource: NormalizeOrFallback(TryReadString(details, "inputSource"), "HAND_GRAB"),
                inputControl: TryReadString(details, "inputControl"),
                inputValue: TryReadFloat(details, "inputValue"),
                targetId: NormalizeOrFallback(zoneId, objectId, string.Empty),
                targetName: NormalizeOrFallback(zoneName, objectName, string.Empty),
                targetValid: TryReadNullableBool(details, "targetValid"));
        }

        public IReadOnlyDictionary<string, object> RecordGazeTelemetry(IReadOnlyDictionary<string, object> payload)
        {
            var details = payload != null
                ? new Dictionary<string, object>(payload)
                : new Dictionary<string, object>();

            var normalizedGameId = NormalizeOrFallback(
                TryReadString(details, "gameId"),
                ResolveActiveGameId(),
                "unknown_game");
            var eventType = ResolveGazeEventType(details);
            var sourceComponent = NormalizeOrFallback(
                TryReadString(details, "sourceComponent"),
                "GazeTelemetry");
            var attemptContext = ResolveAttemptContext(normalizedGameId, eventType);
            var actionOutcome = ResolveGazeOutcome(details, eventType);
            var reasonCode = ResolveGazeReasonCode(details, eventType);
            var targetName = NormalizeOrFallback(
                TryReadString(details, "targetName"),
                TryReadString(details, "hitObjectName"));

            return EmitCanonicalEvent(
                normalizedGameId,
                eventType,
                "GAZE",
                actionOutcome,
                reasonCode,
                sourceComponent,
                attemptContext,
                details,
                inputHand: TryReadString(details, "inputHand"),
                inputSource: NormalizeOrFallback(TryReadString(details, "inputSource"), "GAZE"),
                inputControl: TryReadString(details, "inputControl"),
                inputValue: TryReadFloat(details, "inputValue"),
                targetId: NormalizeOrFallback(
                    TryReadString(details, "targetId"),
                    targetName,
                    string.Empty),
                targetName: targetName,
                targetValid: TryReadNullableBool(details, "targetValid"));
        }

        public IReadOnlyDictionary<string, object> RecordBreathTelemetry(IReadOnlyDictionary<string, object> payload)
        {
            var details = payload != null
                ? new Dictionary<string, object>(payload)
                : new Dictionary<string, object>();

            var normalizedGameId = NormalizeOrFallback(
                TryReadString(details, "gameId"),
                ResolveActiveGameId(),
                "unknown_game");
            var eventType = ResolveBreathEventType(details);
            var sourceComponent = NormalizeOrFallback(
                TryReadString(details, "sourceComponent"),
                "BreathTelemetry");
            var attemptContext = ResolveAttemptContext(normalizedGameId, eventType);
            var actionOutcome = ResolveBreathOutcome(details, eventType);
            var reasonCode = ResolveBreathReasonCode(details, eventType);

            return EmitCanonicalEvent(
                normalizedGameId,
                eventType,
                "BREATH",
                actionOutcome,
                reasonCode,
                sourceComponent,
                attemptContext,
                details,
                inputHand: TryReadString(details, "inputHand"),
                inputSource: NormalizeOrFallback(TryReadString(details, "inputSource"), "BREATH"),
                inputControl: TryReadString(details, "inputControl"),
                inputValue: TryReadFloat(details, "inputValue"),
                targetId: NormalizeOrFallback(TryReadString(details, "targetId"), string.Empty),
                targetName: NormalizeOrFallback(TryReadString(details, "targetName"), string.Empty),
                targetValid: TryReadNullableBool(details, "targetValid"));
        }

        public IReadOnlyDictionary<string, object> RecordAudioSourceTelemetry(IReadOnlyDictionary<string, object> payload)
        {
            var details = payload != null
                ? new Dictionary<string, object>(payload)
                : new Dictionary<string, object>();

            var normalizedGameId = NormalizeOrFallback(
                TryReadString(details, "gameId"),
                ResolveActiveGameId(),
                "unknown_game");
            var eventType = ResolveAudioSourceEventType(details);
            var sourceComponent = NormalizeOrFallback(
                TryReadString(details, "sourceComponent"),
                "AudioSourceTelemetry");
            var attemptContext = ResolveAttemptContext(normalizedGameId, eventType);
            var actionOutcome = ResolveAudioSourceOutcome(details, eventType);
            var reasonCode = ResolveAudioSourceReasonCode(details, eventType);
            var targetId = NormalizeOrFallback(
                TryReadString(details, "targetId"),
                TryReadString(details, "selectedSourceId"),
                TryReadString(details, "activeSourceId"));
            var targetName = NormalizeOrFallback(
                TryReadString(details, "targetName"),
                TryReadString(details, "selectedSourceName"),
                TryReadString(details, "activeSourceName"));

            return EmitCanonicalEvent(
                normalizedGameId,
                eventType,
                "AUDIO_SOURCE",
                actionOutcome,
                reasonCode,
                sourceComponent,
                attemptContext,
                details,
                inputHand: TryReadString(details, "inputHand"),
                inputSource: NormalizeOrFallback(TryReadString(details, "inputSource"), "AUDIO_SOURCE"),
                inputControl: TryReadString(details, "inputControl"),
                inputValue: TryReadFloat(details, "inputValue"),
                targetId: targetId,
                targetName: targetName,
                targetValid: TryReadNullableBool(details, "targetValid"));
        }

        public IReadOnlyDictionary<string, object> RecordDualHandTelemetry(IReadOnlyDictionary<string, object> payload)
        {
            var details = payload != null
                ? new Dictionary<string, object>(payload)
                : new Dictionary<string, object>();

            var normalizedGameId = NormalizeOrFallback(
                TryReadString(details, "gameId"),
                ResolveActiveGameId(),
                "unknown_game");
            var eventType = ResolveDualHandEventType(details);
            var sourceComponent = NormalizeOrFallback(
                TryReadString(details, "sourceComponent"),
                "DualHandTelemetry");
            var attemptContext = ResolveAttemptContext(normalizedGameId, eventType);
            var actionOutcome = ResolveDualHandOutcome(details, eventType);
            var reasonCode = ResolveDualHandReasonCode(details, eventType);
            var targetId = NormalizeOrFallback(
                TryReadString(details, "targetId"),
                TryReadString(details, "leftTargetId"),
                TryReadString(details, "rightTargetId"));
            var targetName = NormalizeOrFallback(
                TryReadString(details, "targetName"),
                TryReadString(details, "leftTargetName"),
                TryReadString(details, "rightTargetName"));

            return EmitCanonicalEvent(
                normalizedGameId,
                eventType,
                "DUAL_HAND",
                actionOutcome,
                reasonCode,
                sourceComponent,
                attemptContext,
                details,
                inputHand: NormalizeOrFallback(TryReadString(details, "inputHand"), "BOTH_HANDS"),
                inputSource: NormalizeOrFallback(TryReadString(details, "inputSource"), "DUAL_HAND"),
                inputControl: TryReadString(details, "inputControl"),
                inputValue: TryReadFloat(details, "inputValue"),
                targetId: targetId,
                targetName: targetName,
                targetValid: TryReadNullableBool(details, "targetValid"));
        }

        public IReadOnlyDictionary<string, object> RecordPosePathTelemetry(IReadOnlyDictionary<string, object> payload)
        {
            var details = payload != null
                ? new Dictionary<string, object>(payload)
                : new Dictionary<string, object>();

            var normalizedGameId = NormalizeOrFallback(
                TryReadString(details, "gameId"),
                ResolveActiveGameId(),
                "unknown_game");
            var eventType = ResolvePosePathEventType(details);
            var sourceComponent = NormalizeOrFallback(
                TryReadString(details, "sourceComponent"),
                "PosePathTelemetry");
            var attemptContext = ResolveAttemptContext(normalizedGameId, eventType);
            var actionOutcome = ResolvePosePathOutcome(details, eventType);
            var reasonCode = ResolvePosePathReasonCode(details, eventType);

            return EmitCanonicalEvent(
                normalizedGameId,
                eventType,
                "POSE_PATH",
                actionOutcome,
                reasonCode,
                sourceComponent,
                attemptContext,
                details,
                inputHand: NormalizeOrFallback(TryReadString(details, "inputHand"), string.Empty),
                inputSource: NormalizeOrFallback(TryReadString(details, "inputSource"), "POSE_PATH"),
                inputControl: TryReadString(details, "inputControl"),
                inputValue: TryReadFloat(details, "inputValue"),
                targetId: NormalizeOrFallback(TryReadString(details, "targetId"), string.Empty),
                targetName: NormalizeOrFallback(TryReadString(details, "targetName"), string.Empty),
                targetValid: TryReadNullableBool(details, "targetValid"));
        }

        public IReadOnlyDictionary<string, object> RecordTimelineTelemetry(IReadOnlyDictionary<string, object> payload)
        {
            var details = payload != null
                ? new Dictionary<string, object>(payload)
                : new Dictionary<string, object>();

            var normalizedGameId = NormalizeOrFallback(
                TryReadString(details, "gameId"),
                ResolveActiveGameId(),
                "unknown_game");
            var eventType = ResolveTimelineEventType(details);
            var sourceComponent = NormalizeOrFallback(
                TryReadString(details, "sourceComponent"),
                "TimelineTelemetry");
            var attemptContext = ResolveAttemptContext(normalizedGameId, eventType);
            var actionOutcome = ResolveTimelineOutcome(details, eventType);
            var reasonCode = ResolveTimelineReasonCode(details, eventType);
            var targetId = NormalizeOrFallback(
                TryReadString(details, "targetId"),
                TryReadString(details, "segmentId"),
                string.Empty);
            var targetName = NormalizeOrFallback(
                TryReadString(details, "targetName"),
                TryReadString(details, "segmentName"),
                string.Empty);

            return EmitCanonicalEvent(
                normalizedGameId,
                eventType,
                "TIMELINE",
                actionOutcome,
                reasonCode,
                sourceComponent,
                attemptContext,
                details,
                inputHand: NormalizeOrFallback(TryReadString(details, "inputHand"), string.Empty),
                inputSource: NormalizeOrFallback(TryReadString(details, "inputSource"), "TIMELINE"),
                inputControl: TryReadString(details, "inputControl"),
                inputValue: TryReadFloat(details, "inputValue"),
                targetId: targetId,
                targetName: targetName,
                targetValid: TryReadNullableBool(details, "targetValid"));
        }

        public IReadOnlyDictionary<string, object> RecordSequenceTelemetry(IReadOnlyDictionary<string, object> payload)
        {
            var details = payload != null
                ? new Dictionary<string, object>(payload)
                : new Dictionary<string, object>();

            var normalizedGameId = NormalizeOrFallback(
                TryReadString(details, "gameId"),
                ResolveActiveGameId(),
                "unknown_game");
            var eventType = ResolveSequenceEventType(details);
            var sourceComponent = NormalizeOrFallback(
                TryReadString(details, "sourceComponent"),
                "SequenceTelemetry");
            var attemptContext = ResolveAttemptContext(normalizedGameId, eventType);
            var actionOutcome = ResolveSequenceOutcome(details, eventType);
            var reasonCode = ResolveSequenceReasonCode(details, eventType);

            return EmitCanonicalEvent(
                normalizedGameId,
                eventType,
                "SEQUENCE",
                actionOutcome,
                reasonCode,
                sourceComponent,
                attemptContext,
                details,
                inputHand: NormalizeOrFallback(TryReadString(details, "inputHand"), string.Empty),
                inputSource: NormalizeOrFallback(TryReadString(details, "inputSource"), "SEQUENCE"),
                inputControl: TryReadString(details, "inputControl"),
                inputValue: TryReadFloat(details, "inputValue"),
                targetId: NormalizeOrFallback(TryReadString(details, "targetId"), string.Empty),
                targetName: NormalizeOrFallback(TryReadString(details, "targetName"), string.Empty),
                targetValid: TryReadNullableBool(details, "targetValid"));
        }

        private IReadOnlyDictionary<string, object> EmitCanonicalEvent(
            string gameId,
            string eventType,
            string interactionType,
            string actionOutcome,
            string reasonCode,
            string sourceComponent,
            GameAttemptContext attemptContext,
            IReadOnlyDictionary<string, object> details,
            string inputHand,
            string inputSource,
            string inputControl,
            float? inputValue,
            string targetId,
            string targetName,
            bool? targetValid)
        {
            if (!_emitCanonicalEvents)
            {
                return null;
            }

            ResolveDependencies();
            var telemetryAvailable = _gameTelemetryService != null;
            if (!telemetryAvailable)
            {
                if (!_missingTelemetryServiceLogged)
                {
                    Logger.Warning(
                        "[InteractionEventBridge] GameTelemetryService not found. " +
                        "Canonical events will still be published to local subscribers, " +
                        "but telemetry backend tracking is disabled.");
                    _missingTelemetryServiceLogged = true;
                }
            }
            else
            {
                _missingTelemetryServiceLogged = false;
            }

            var session = CaptureSessionMetadata();
            var sequenceNumber = NextSequenceNumber(session.sessionKey);

            if (attemptContext == null)
            {
                attemptContext = ResolveAttemptContext(gameId, eventType);
            }

            attemptContext.eventCount = Mathf.Max(0, attemptContext.eventCount) + 1;
            var normalizedFlowId = NormalizeOrFallback(
                TryReadString(details, "flowId"),
                TryReadString(details, "flow_id"),
                string.Empty);
            var normalizedStepId = NormalizeOrFallback(TryReadString(details, "stepId"), string.Empty);
            var normalizedNodeId = NormalizeOrFallback(TryReadString(details, "nodeId"), string.Empty);
            var normalizedControlMode = NormalizeOrFallback(TryReadString(details, "controlMode"), string.Empty);
            var normalizedActionAttemptId = NormalizeOrFallback(TryReadString(details, "actionAttemptId"), string.Empty);
            var monotonicSec = TryReadFloat(details, "monotonicSec");
            var payloadVersion = TryReadInt(details, "payloadVersion");
            var traceRef = NormalizeOrFallback(
                TryReadString(details, "trace_ref"),
                TryReadString(details, "traceRef"),
                string.Empty);

            var payload = new Dictionary<string, object>
            {
                { "schema", SchemaName },
                { "schemaVersion", SchemaVersion },
                { "eventId", Guid.NewGuid().ToString() },
                { "sequenceNumber", sequenceNumber },
                { "taskRunId", attemptContext.taskRunId ?? string.Empty },
                { "attemptId", attemptContext.attemptId ?? string.Empty },
                { "attemptSequenceNumber", attemptContext.eventCount },
                { "gameId", NormalizeOrFallback(gameId, "unknown_game") },
                { "eventType", NormalizeEventToken(eventType, "UNSPECIFIED_EVENT") },
                { "interactionType", NormalizeEventToken(interactionType, "UNSPECIFIED_INTERACTION") },
                { "actionOutcome", NormalizeEventToken(actionOutcome, "OBSERVED") },
                { "occurredAtUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) },
                { "monotonicSec", monotonicSec ?? Time.realtimeSinceStartup },
                { "sourceComponent", NormalizeOrFallback(sourceComponent, "UnknownComponent") },
                { "sourceOfTruth", SourceOfTruth },
                { "payloadVersion", payloadVersion > 0 ? payloadVersion.Value : 1 },
                { "sessionId", session.sessionId },
                { "patientId", session.patientId },
                { "studentId", session.patientId },
                { "therapistId", session.therapistId },
                { "ownerKey", session.ownerKey },
                { "sessionKey", session.sessionKey },
                { "flowId", normalizedFlowId },
                { "stepId", normalizedStepId },
                { "nodeId", normalizedNodeId },
                { "controlMode", normalizedControlMode },
                { "actionAttemptId", normalizedActionAttemptId },
                { "inputHand", NormalizeOrFallback(inputHand, string.Empty) },
                { "inputSource", NormalizeOrFallback(inputSource, string.Empty) },
                { "inputControl", NormalizeOrFallback(inputControl, string.Empty) },
                { "targetId", NormalizeOrFallback(targetId, string.Empty) },
                { "targetName", NormalizeOrFallback(targetName, string.Empty) },
                { "reasonCode", NormalizeOrFallback(reasonCode, string.Empty) },
                {
                    "details",
                    details != null
                        ? new Dictionary<string, object>(details)
                        : new Dictionary<string, object>()
                },
            };

            if (inputValue.HasValue)
            {
                payload["inputValue"] = inputValue.Value;
            }

            if (targetValid.HasValue)
            {
                payload["targetValid"] = targetValid.Value;
            }

            if (!string.IsNullOrWhiteSpace(traceRef))
            {
                payload["trace_ref"] = traceRef;
            }

            if (telemetryAvailable)
            {
                _gameTelemetryService.Track(CanonicalEventName, payload);
            }
            _eventsEmitted++;

            try
            {
                EventPublished?.Invoke(new Dictionary<string, object>(payload));
            }
            catch (Exception e)
            {
                Logger.Warning($"[InteractionEventBridge] EventPublished callback failed: {e.Message}");
            }

            if (_logCanonicalEvents)
            {
                Logger.Info(
                    $"[InteractionEventBridge] eventType={payload["eventType"]} gameId={payload["gameId"]} sequence={payload["sequenceNumber"]}");
            }

            return payload;
        }

        private long NextSequenceNumber(string sessionKey)
        {
            var normalizedSessionKey = NormalizeOrFallback(sessionKey, string.Empty);
            if (!string.Equals(_sequenceSessionKey, normalizedSessionKey, StringComparison.Ordinal))
            {
                _sequenceSessionKey = normalizedSessionKey;
                _sequenceNumber = 0;
            }

            _sequenceNumber = Math.Max(0L, _sequenceNumber) + 1L;
            return _sequenceNumber;
        }

        private GameAttemptContext ResolveAttemptContext(string gameId, string eventType)
        {
            var normalizedGameId = NormalizeOrFallback(gameId, "unknown_game");
            var shouldStartNewAttempt = IsAttemptStartEvent(eventType);

            if (shouldStartNewAttempt || !_attemptByGameId.TryGetValue(normalizedGameId, out var context) || context == null)
            {
                context = new GameAttemptContext
                {
                    taskRunId = Guid.NewGuid().ToString(),
                    attemptId = Guid.NewGuid().ToString(),
                    eventCount = 0,
                };
                _attemptByGameId[normalizedGameId] = context;
            }

            return context;
        }

        private void ResolveDependencies()
        {
            if (_gameTelemetryService == null)
            {
                _gameTelemetryService = FindFirstObjectByType<GameTelemetryService>();
            }

            if (_sessionContext == null)
            {
                _sessionContext = FindFirstObjectByType<GameSessionContext>();
            }

            if (_gameRuntimeService == null)
            {
                _gameRuntimeService = FindFirstObjectByType<GameRuntimeService>();
            }
        }

        private SessionMetadata CaptureSessionMetadata()
        {
            ResolveDependencies();

            var sessionId = string.Empty;
            var patientId = string.Empty;
            var therapistId = string.Empty;
            if (_sessionContext != null)
            {
                sessionId = NormalizeOrFallback(_sessionContext.SessionId, string.Empty);
                patientId = NormalizeOrFallback(_sessionContext.PatientId, string.Empty);
                therapistId = NormalizeOrFallback(_sessionContext.TherapistId, string.Empty);
            }

            var ownerKey = BuildOwnerKey(therapistId, patientId);
            var sessionKey = BuildSessionKey(ownerKey, sessionId);

            return new SessionMetadata
            {
                sessionId = sessionId,
                patientId = patientId,
                therapistId = therapistId,
                ownerKey = ownerKey,
                sessionKey = sessionKey,
            };
        }

        private string ResolveActiveGameId()
        {
            ResolveDependencies();
            if (_gameRuntimeService == null)
            {
                return string.Empty;
            }

            return NormalizeOrFallback(_gameRuntimeService.ActiveGameId, string.Empty);
        }

        private static bool IsAttemptStartEvent(string eventType)
        {
            return string.Equals(
                       NormalizeEventToken(eventType, string.Empty),
                       "GAME_STARTED",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       NormalizeEventToken(eventType, string.Empty),
                       "FLOW_STARTED",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       NormalizeEventToken(eventType, string.Empty),
                       "SESSION_START",
                       StringComparison.Ordinal);
        }

        private static bool IsTerminalGameplayEvent(string eventType)
        {
            return string.Equals(
                NormalizeEventToken(eventType, string.Empty),
                "GAME_STOPPED",
                StringComparison.Ordinal) ||
                   string.Equals(
                       NormalizeEventToken(eventType, string.Empty),
                       "SESSION_TERMINAL",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       NormalizeEventToken(eventType, string.Empty),
                       "FLOW_COMPLETED",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       NormalizeEventToken(eventType, string.Empty),
                       "FLOW_FAILED",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       NormalizeEventToken(eventType, string.Empty),
                       "FLOW_STOPPED",
                       StringComparison.Ordinal);
        }

        private static string ResolveGameplayOutcome(
            string eventType,
            IReadOnlyDictionary<string, object> payload)
        {
            var explicitOutcome = TryReadString(payload, "actionOutcome");
            if (!string.IsNullOrWhiteSpace(explicitOutcome))
            {
                return NormalizeEventToken(explicitOutcome, "OBSERVED");
            }

            var normalizedEventType = NormalizeEventToken(eventType, "GAMEPLAY_EVENT");
            if (normalizedEventType.IndexOf("WRONG", StringComparison.Ordinal) >= 0 ||
                normalizedEventType.IndexOf("MISS", StringComparison.Ordinal) >= 0 ||
                normalizedEventType.IndexOf("FAILED", StringComparison.Ordinal) >= 0)
            {
                return "INCORRECT";
            }

            if (normalizedEventType.IndexOf("HIT", StringComparison.Ordinal) >= 0 ||
                normalizedEventType.IndexOf("CLICKED", StringComparison.Ordinal) >= 0 ||
                normalizedEventType.IndexOf("COMPLETED", StringComparison.Ordinal) >= 0)
            {
                return "CORRECT";
            }

            return "OBSERVED";
        }

        private static string ResolvePointerEventType(IReadOnlyDictionary<string, object> payload)
        {
            var hitAny = TryReadBool(payload, "hitAnyCollider");
            var hitInteractive = TryReadBool(payload, "hitInteractiveTarget");
            var validTarget = TryReadBool(payload, "hoverIsValidTarget");

            if (hitInteractive && validTarget)
            {
                return "POINTER_SELECT";
            }

            if (hitInteractive && !validTarget)
            {
                return "POINTER_SELECT_INVALID";
            }

            if (hitAny)
            {
                return "POINTER_RAY_HIT";
            }

            return "POINTER_RAY_MISS";
        }

        private static string ResolvePointerOutcome(string pointerEventType)
        {
            switch (NormalizeEventToken(pointerEventType, "POINTER_RAY_MISS"))
            {
                case "POINTER_SELECT":
                    return "CORRECT";
                case "POINTER_SELECT_INVALID":
                    return "INCORRECT";
                case "POINTER_RAY_MISS":
                    return "OMITTED";
                default:
                    return "OBSERVED";
            }
        }

        private static string ResolvePointerReasonCode(
            IReadOnlyDictionary<string, object> payload,
            string pointerEventType)
        {
            var explicitReason = ResolveReasonCode(payload, string.Empty);
            if (!string.IsNullOrWhiteSpace(explicitReason))
            {
                return explicitReason;
            }

            switch (NormalizeEventToken(pointerEventType, "POINTER_RAY_MISS"))
            {
                case "POINTER_SELECT":
                    return "TARGET_ACTIVATED";
                case "POINTER_SELECT_INVALID":
                    return "TARGET_INVALID";
                case "POINTER_RAY_HIT":
                    return "TARGET_NON_INTERACTIVE";
                default:
                    return "TARGET_NOT_FOUND";
            }
        }

        private static string ResolveToolEventType(IReadOnlyDictionary<string, object> payload)
        {
            var explicitEventType = TryReadString(payload, "toolEventType");
            if (!string.IsNullOrWhiteSpace(explicitEventType))
            {
                return NormalizeEventToken(explicitEventType, "TOOL_EVENT");
            }

            var hitAny = TryReadBool(payload, "hitAnyCollider");
            var targetValid = TryReadNullableBool(payload, "targetValid");
            var isPressed = TryReadBool(payload, "isPressed");

            if (isPressed)
            {
                return "TOOL_GRIP_ACTIVE";
            }

            if (!hitAny)
            {
                return "TOOL_IMPACT_MISS";
            }

            if (targetValid.HasValue)
            {
                return targetValid.Value ? "TOOL_IMPACT_HIT" : "TOOL_IMPACT_INVALID";
            }

            return "TOOL_EVENT";
        }

        private static string ResolveToolOutcome(
            IReadOnlyDictionary<string, object> payload,
            string toolEventType)
        {
            var explicitOutcome = TryReadString(payload, "actionOutcome");
            if (!string.IsNullOrWhiteSpace(explicitOutcome))
            {
                return NormalizeEventToken(explicitOutcome, "OBSERVED");
            }

            switch (NormalizeEventToken(toolEventType, "TOOL_EVENT"))
            {
                case "TOOL_IMPACT_HIT":
                    return "CORRECT";
                case "TOOL_IMPACT_INVALID":
                    return "INCORRECT";
                case "TOOL_IMPACT_MISS":
                    return "OMITTED";
                default:
                    return "OBSERVED";
            }
        }

        private static string ResolveToolReasonCode(
            IReadOnlyDictionary<string, object> payload,
            string toolEventType)
        {
            var explicitReason = ResolveReasonCode(payload, string.Empty);
            if (!string.IsNullOrWhiteSpace(explicitReason))
            {
                return explicitReason;
            }

            switch (NormalizeEventToken(toolEventType, "TOOL_EVENT"))
            {
                case "TOOL_IMPACT_HIT":
                    return "TARGET_VALIDATED";
                case "TOOL_IMPACT_INVALID":
                    return "TARGET_INVALID";
                case "TOOL_IMPACT_MISS":
                    return "TARGET_NOT_FOUND";
                case "TOOL_GRIP_START":
                    return "TOOL_GRIP_STARTED";
                case "TOOL_GRIP_HOLD":
                    return "TOOL_GRIP_HOLDING";
                case "TOOL_GRIP_END":
                    return "TOOL_GRIP_RELEASED";
                default:
                    return "TOOL_EVENT_OBSERVED";
            }
        }

        private static string ResolveHandEventType(IReadOnlyDictionary<string, object> payload)
        {
            var explicitEventType = TryReadString(payload, "handEventType");
            if (!string.IsNullOrWhiteSpace(explicitEventType))
            {
                return NormalizeEventToken(explicitEventType, "HAND_EVENT");
            }

            var hitAny = TryReadBool(payload, "hitAnyCollider");
            var targetValid = TryReadNullableBool(payload, "targetValid");

            if (!hitAny)
            {
                return "HAND_CONTACT_MISS";
            }

            if (targetValid.HasValue)
            {
                return targetValid.Value ? "HAND_CONTACT_HIT" : "HAND_CONTACT_INVALID";
            }

            return "HAND_EVENT";
        }

        private static string ResolveHandOutcome(
            IReadOnlyDictionary<string, object> payload,
            string handEventType)
        {
            var explicitOutcome = TryReadString(payload, "actionOutcome");
            if (!string.IsNullOrWhiteSpace(explicitOutcome))
            {
                return NormalizeEventToken(explicitOutcome, "OBSERVED");
            }

            switch (NormalizeEventToken(handEventType, "HAND_EVENT"))
            {
                case "HAND_CONTACT_HIT":
                    return "CORRECT";
                case "HAND_CONTACT_INVALID":
                    return "INCORRECT";
                case "HAND_CONTACT_MISS":
                    return "OMITTED";
                default:
                    return "OBSERVED";
            }
        }

        private static string ResolveHandReasonCode(
            IReadOnlyDictionary<string, object> payload,
            string handEventType)
        {
            var explicitReason = ResolveReasonCode(payload, string.Empty);
            if (!string.IsNullOrWhiteSpace(explicitReason))
            {
                return explicitReason;
            }

            switch (NormalizeEventToken(handEventType, "HAND_EVENT"))
            {
                case "HAND_CONTACT_HIT":
                    return "TARGET_VALIDATED";
                case "HAND_CONTACT_INVALID":
                    return "TARGET_INVALID";
                case "HAND_CONTACT_MISS":
                    return "TARGET_NOT_FOUND";
                default:
                    return "HAND_EVENT_OBSERVED";
            }
        }

        private static string ResolveGrabEventType(IReadOnlyDictionary<string, object> payload)
        {
            var explicitEventType = TryReadString(payload, "grabEventType");
            if (!string.IsNullOrWhiteSpace(explicitEventType))
            {
                return NormalizeEventToken(explicitEventType, "GRAB_EVENT");
            }

            var explicitActionId = TryReadString(payload, "actionId");
            if (string.IsNullOrWhiteSpace(explicitActionId))
            {
                return "GRAB_EVENT";
            }

            switch (NormalizeEventToken(explicitActionId, string.Empty))
            {
                case "GRAB_OBJECT":
                    return "GRAB_OBJECT_START";
                case "RELEASE_OBJECT":
                    return "GRAB_OBJECT_RELEASE";
                case "PLACE_OBJECT_IN_ZONE":
                    return "GRAB_OBJECT_PLACED";
                case "REMOVE_OBJECT_FROM_ZONE":
                    return "GRAB_OBJECT_REMOVED";
                case "COLLECT_ITEM_TO_CONTAINER":
                    return "GRAB_OBJECT_COLLECTED";
                default:
                    return "GRAB_EVENT";
            }
        }

        private static string ResolveGrabOutcome(
            IReadOnlyDictionary<string, object> payload,
            string grabEventType)
        {
            var explicitOutcome = TryReadString(payload, "actionOutcome");
            if (!string.IsNullOrWhiteSpace(explicitOutcome))
            {
                return NormalizeEventToken(explicitOutcome, "OBSERVED");
            }

            var normalizedEventType = NormalizeEventToken(grabEventType, "GRAB_EVENT");
            if (normalizedEventType.IndexOf("INVALID", StringComparison.Ordinal) >= 0 ||
                normalizedEventType.IndexOf("FAILED", StringComparison.Ordinal) >= 0)
            {
                return "INCORRECT";
            }

            if (normalizedEventType.IndexOf("MISS", StringComparison.Ordinal) >= 0)
            {
                return "OMITTED";
            }

            if (normalizedEventType.IndexOf("GRAB_OBJECT_", StringComparison.Ordinal) >= 0 ||
                normalizedEventType.IndexOf("HAND_GRAB_", StringComparison.Ordinal) >= 0)
            {
                return "CORRECT";
            }

            return "OBSERVED";
        }

        private static string ResolveGrabReasonCode(
            IReadOnlyDictionary<string, object> payload,
            string grabEventType)
        {
            var explicitReason = ResolveReasonCode(payload, string.Empty);
            if (!string.IsNullOrWhiteSpace(explicitReason))
            {
                return explicitReason;
            }

            switch (NormalizeEventToken(grabEventType, "GRAB_EVENT"))
            {
                case "GRAB_OBJECT_START":
                    return "OBJECT_GRABBED";
                case "GRAB_OBJECT_RELEASE":
                    return "OBJECT_RELEASED";
                case "GRAB_OBJECT_PLACED":
                    return "OBJECT_PLACED";
                case "GRAB_OBJECT_REMOVED":
                    return "OBJECT_REMOVED";
                case "GRAB_OBJECT_COLLECTED":
                    return "OBJECT_COLLECTED";
                case "HAND_GRAB_START":
                    return "OBJECT_GRABBED";
                case "HAND_GRAB_RELEASE":
                    return "OBJECT_RELEASED";
                case "HAND_GRAB_PLACE":
                    return "OBJECT_PLACED";
                case "HAND_GRAB_REMOVE":
                    return "OBJECT_REMOVED";
                case "HAND_GRAB_COLLECT":
                    return "OBJECT_COLLECTED";
                default:
                    return "GRAB_EVENT_OBSERVED";
            }
        }

        private static string ResolveGazeEventType(IReadOnlyDictionary<string, object> payload)
        {
            var explicitEventType = TryReadString(payload, "gazeEventType");
            if (!string.IsNullOrWhiteSpace(explicitEventType))
            {
                return NormalizeEventToken(explicitEventType, "GAZE_EVENT");
            }

            var explicitActionId = TryReadString(payload, "actionId");
            if (!string.IsNullOrWhiteSpace(explicitActionId))
            {
                switch (NormalizeEventToken(explicitActionId, string.Empty))
                {
                    case "HOLD_GAZE_ON_TARGET":
                        return "GAZE_HOLD_COMPLETED";
                    case "SELECT_TARGET_WITH_GAZE_AND_TOOL":
                        return "GAZE_TOOL_SELECT";
                }
            }

            return "GAZE_EVENT";
        }

        private static string ResolveGazeOutcome(
            IReadOnlyDictionary<string, object> payload,
            string gazeEventType)
        {
            var explicitOutcome = TryReadString(payload, "actionOutcome");
            if (!string.IsNullOrWhiteSpace(explicitOutcome))
            {
                return NormalizeEventToken(explicitOutcome, "OBSERVED");
            }

            switch (NormalizeEventToken(gazeEventType, "GAZE_EVENT"))
            {
                case "GAZE_HOLD_COMPLETED":
                case "GAZE_TOOL_SELECT":
                    return "CORRECT";
                case "GAZE_HOLD_INVALID":
                case "GAZE_TOOL_SELECT_INVALID":
                    return "INCORRECT";
                case "GAZE_HOLD_TICK":
                    return "OBSERVED";
                default:
                    return "OBSERVED";
            }
        }

        private static string ResolveGazeReasonCode(
            IReadOnlyDictionary<string, object> payload,
            string gazeEventType)
        {
            var explicitReason = ResolveReasonCode(payload, string.Empty);
            if (!string.IsNullOrWhiteSpace(explicitReason))
            {
                return explicitReason;
            }

            switch (NormalizeEventToken(gazeEventType, "GAZE_EVENT"))
            {
                case "GAZE_HOLD_TICK":
                    return "GAZE_DWELL_PROGRESS";
                case "GAZE_HOLD_COMPLETED":
                    return "GAZE_DWELL_REACHED";
                case "GAZE_HOLD_INVALID":
                    return "GAZE_TARGET_INVALID";
                case "GAZE_TOOL_SELECT":
                    return "GAZE_TOOL_CONFIRMED";
                case "GAZE_TOOL_SELECT_INVALID":
                    return "GAZE_TOOL_INVALID";
                default:
                    return "GAZE_EVENT_OBSERVED";
            }
        }

        private static string ResolveBreathEventType(IReadOnlyDictionary<string, object> payload)
        {
            var explicitEventType = TryReadString(payload, "breathEventType");
            if (!string.IsNullOrWhiteSpace(explicitEventType))
            {
                return NormalizeEventToken(explicitEventType, "BREATH_EVENT");
            }

            var explicitActionId = TryReadString(payload, "actionId");
            if (string.Equals(
                    NormalizeEventToken(explicitActionId, string.Empty),
                    "PERFORM_BREATH_CYCLE",
                    StringComparison.Ordinal))
            {
                return "BREATH_CYCLE_COMPLETED";
            }

            return "BREATH_EVENT";
        }

        private static string ResolveBreathOutcome(
            IReadOnlyDictionary<string, object> payload,
            string breathEventType)
        {
            var explicitOutcome = TryReadString(payload, "actionOutcome");
            if (!string.IsNullOrWhiteSpace(explicitOutcome))
            {
                return NormalizeEventToken(explicitOutcome, "OBSERVED");
            }

            switch (NormalizeEventToken(breathEventType, "BREATH_EVENT"))
            {
                case "BREATH_CYCLE_COMPLETED":
                    return "CORRECT";
                case "BREATH_CYCLE_INVALID":
                    return "INCORRECT";
                case "BREATH_PHASE_INHALE":
                case "BREATH_PHASE_HOLD":
                case "BREATH_PHASE_EXHALE":
                    return "OBSERVED";
                default:
                    return "OBSERVED";
            }
        }

        private static string ResolveBreathReasonCode(
            IReadOnlyDictionary<string, object> payload,
            string breathEventType)
        {
            var explicitReason = ResolveReasonCode(payload, string.Empty);
            if (!string.IsNullOrWhiteSpace(explicitReason))
            {
                return explicitReason;
            }

            switch (NormalizeEventToken(breathEventType, "BREATH_EVENT"))
            {
                case "BREATH_PHASE_INHALE":
                    return "BREATH_PHASE_INHALE";
                case "BREATH_PHASE_HOLD":
                    return "BREATH_PHASE_HOLD";
                case "BREATH_PHASE_EXHALE":
                    return "BREATH_PHASE_EXHALE";
                case "BREATH_CYCLE_COMPLETED":
                    return "BREATH_CYCLE_COMPLETED";
                case "BREATH_CYCLE_INVALID":
                    return "BREATH_CYCLE_INVALID";
                default:
                    return "BREATH_EVENT_OBSERVED";
            }
        }

        private static string ResolveAudioSourceEventType(IReadOnlyDictionary<string, object> payload)
        {
            var explicitEventType = TryReadString(payload, "audioSourceEventType");
            if (!string.IsNullOrWhiteSpace(explicitEventType))
            {
                return NormalizeEventToken(explicitEventType, "AUDIO_SOURCE_EVENT");
            }

            var explicitActionId = TryReadString(payload, "actionId");
            if (string.Equals(
                    NormalizeEventToken(explicitActionId, string.Empty),
                    "IDENTIFY_SOUND_SOURCE",
                    StringComparison.Ordinal))
            {
                var targetValid = TryReadNullableBool(payload, "targetValid");
                return targetValid.HasValue && !targetValid.Value
                    ? "AUDIO_SOURCE_SELECTED_INVALID"
                    : "AUDIO_SOURCE_SELECTED";
            }

            return "AUDIO_SOURCE_EVENT";
        }

        private static string ResolveAudioSourceOutcome(
            IReadOnlyDictionary<string, object> payload,
            string audioEventType)
        {
            var explicitOutcome = TryReadString(payload, "actionOutcome");
            if (!string.IsNullOrWhiteSpace(explicitOutcome))
            {
                return NormalizeEventToken(explicitOutcome, "OBSERVED");
            }

            switch (NormalizeEventToken(audioEventType, "AUDIO_SOURCE_EVENT"))
            {
                case "AUDIO_SOURCE_CUE_ACTIVE":
                    return "OBSERVED";
                case "AUDIO_SOURCE_SELECTED":
                    return "CORRECT";
                case "AUDIO_SOURCE_SELECTED_INVALID":
                    return "INCORRECT";
                default:
                    return "OBSERVED";
            }
        }

        private static string ResolveAudioSourceReasonCode(
            IReadOnlyDictionary<string, object> payload,
            string audioEventType)
        {
            var explicitReason = ResolveReasonCode(payload, string.Empty);
            if (!string.IsNullOrWhiteSpace(explicitReason))
            {
                return explicitReason;
            }

            switch (NormalizeEventToken(audioEventType, "AUDIO_SOURCE_EVENT"))
            {
                case "AUDIO_SOURCE_CUE_ACTIVE":
                    return "AUDIO_CUE_ACTIVE";
                case "AUDIO_SOURCE_SELECTED":
                    return "AUDIO_SOURCE_IDENTIFIED";
                case "AUDIO_SOURCE_SELECTED_INVALID":
                    return "AUDIO_SOURCE_MISMATCH";
                default:
                    return "AUDIO_SOURCE_EVENT_OBSERVED";
            }
        }

        private static string ResolveDualHandEventType(IReadOnlyDictionary<string, object> payload)
        {
            var explicitEventType = TryReadString(payload, "dualHandEventType");
            if (!string.IsNullOrWhiteSpace(explicitEventType))
            {
                return NormalizeEventToken(explicitEventType, "DUAL_HAND_EVENT");
            }

            var explicitActionId = TryReadString(payload, "actionId");
            if (string.Equals(
                    NormalizeEventToken(explicitActionId, string.Empty),
                    "MARK_LEFT_AND_RIGHT_TARGETS",
                    StringComparison.Ordinal))
            {
                var leftMarked = TryReadNullableBool(payload, "leftMarked");
                var rightMarked = TryReadNullableBool(payload, "rightMarked");
                var syncSatisfied = TryReadNullableBool(payload, "syncSatisfied");
                var targetValid = TryReadNullableBool(payload, "targetValid");

                if (targetValid.HasValue && !targetValid.Value)
                {
                    return "DUAL_HAND_MARK_INVALID";
                }

                if (leftMarked.GetValueOrDefault() && rightMarked.GetValueOrDefault())
                {
                    if (syncSatisfied.HasValue && !syncSatisfied.Value)
                    {
                        return "DUAL_HAND_MARK_OUT_OF_SYNC";
                    }

                    return "DUAL_HAND_MARK_SYNC";
                }

                if (leftMarked.GetValueOrDefault())
                {
                    return "DUAL_HAND_MARK_LEFT";
                }

                if (rightMarked.GetValueOrDefault())
                {
                    return "DUAL_HAND_MARK_RIGHT";
                }
            }

            return "DUAL_HAND_EVENT";
        }

        private static string ResolveDualHandOutcome(
            IReadOnlyDictionary<string, object> payload,
            string dualHandEventType)
        {
            var explicitOutcome = TryReadString(payload, "actionOutcome");
            if (!string.IsNullOrWhiteSpace(explicitOutcome))
            {
                return NormalizeEventToken(explicitOutcome, "OBSERVED");
            }

            switch (NormalizeEventToken(dualHandEventType, "DUAL_HAND_EVENT"))
            {
                case "DUAL_HAND_MARK_SYNC":
                    return "CORRECT";
                case "DUAL_HAND_MARK_INVALID":
                case "DUAL_HAND_MARK_OUT_OF_SYNC":
                    return "INCORRECT";
                case "DUAL_HAND_MARK_LEFT":
                case "DUAL_HAND_MARK_RIGHT":
                    return "OBSERVED";
                default:
                    return "OBSERVED";
            }
        }

        private static string ResolveDualHandReasonCode(
            IReadOnlyDictionary<string, object> payload,
            string dualHandEventType)
        {
            var explicitReason = ResolveReasonCode(payload, string.Empty);
            if (!string.IsNullOrWhiteSpace(explicitReason))
            {
                return explicitReason;
            }

            switch (NormalizeEventToken(dualHandEventType, "DUAL_HAND_EVENT"))
            {
                case "DUAL_HAND_MARK_LEFT":
                case "DUAL_HAND_MARK_RIGHT":
                    return "DUAL_HAND_WAITING_FOR_SECOND_HAND";
                case "DUAL_HAND_MARK_SYNC":
                    return "DUAL_HAND_SYNC_CONFIRMED";
                case "DUAL_HAND_MARK_INVALID":
                    return "DUAL_HAND_TARGET_INVALID";
                case "DUAL_HAND_MARK_OUT_OF_SYNC":
                    return "DUAL_HAND_SYNC_WINDOW_EXCEEDED";
                default:
                    return "DUAL_HAND_EVENT_OBSERVED";
            }
        }

        private static string ResolvePosePathEventType(IReadOnlyDictionary<string, object> payload)
        {
            var explicitEventType = TryReadString(payload, "posePathEventType");
            if (!string.IsNullOrWhiteSpace(explicitEventType))
            {
                return NormalizeEventToken(explicitEventType, "POSE_PATH_EVENT");
            }

            var explicitActionId = TryReadString(payload, "actionId");
            var normalizedActionId = NormalizeEventToken(explicitActionId, string.Empty);
            var targetValid = TryReadNullableBool(payload, "targetValid");

            if (string.Equals(normalizedActionId, "HOLD_POSE", StringComparison.Ordinal))
            {
                if (targetValid.HasValue && !targetValid.Value)
                {
                    return "POSE_PATH_HOLD_INVALID";
                }

                var holdProgress = TryReadFloat(payload, "holdProgress01");
                var holdElapsedSec = TryReadFloat(payload, "holdElapsedSec");
                var holdRequiredSec = TryReadFloat(payload, "holdRequiredSec");
                if ((holdProgress.HasValue && holdProgress.Value >= 0.999f) ||
                    (holdElapsedSec.HasValue &&
                     holdRequiredSec.HasValue &&
                     holdRequiredSec.Value > 0f &&
                     holdElapsedSec.Value >= holdRequiredSec.Value))
                {
                    return "POSE_PATH_HOLD_COMPLETED";
                }

                return "POSE_PATH_HOLD_TICK";
            }

            if (string.Equals(normalizedActionId, "FOLLOW_PATH", StringComparison.Ordinal))
            {
                if (targetValid.HasValue && !targetValid.Value)
                {
                    return "POSE_PATH_FOLLOW_DEVIATION";
                }

                var pathProgress = TryReadFloat(payload, "pathProgress01");
                var pathCoverage = TryReadFloat(payload, "pathCoverage01");
                if ((pathCoverage.HasValue && pathCoverage.Value >= 0.999f) ||
                    (pathProgress.HasValue && pathProgress.Value >= 0.999f))
                {
                    return "POSE_PATH_FOLLOW_COMPLETED";
                }

                return "POSE_PATH_FOLLOW_TICK";
            }

            return "POSE_PATH_EVENT";
        }

        private static string ResolvePosePathOutcome(
            IReadOnlyDictionary<string, object> payload,
            string posePathEventType)
        {
            var explicitOutcome = TryReadString(payload, "actionOutcome");
            if (!string.IsNullOrWhiteSpace(explicitOutcome))
            {
                return NormalizeEventToken(explicitOutcome, "OBSERVED");
            }

            switch (NormalizeEventToken(posePathEventType, "POSE_PATH_EVENT"))
            {
                case "POSE_PATH_HOLD_COMPLETED":
                case "POSE_PATH_FOLLOW_COMPLETED":
                    return "CORRECT";
                case "POSE_PATH_HOLD_INVALID":
                case "POSE_PATH_FOLLOW_DEVIATION":
                    return "INCORRECT";
                case "POSE_PATH_HOLD_TICK":
                case "POSE_PATH_FOLLOW_TICK":
                    return "OBSERVED";
                default:
                    return "OBSERVED";
            }
        }

        private static string ResolvePosePathReasonCode(
            IReadOnlyDictionary<string, object> payload,
            string posePathEventType)
        {
            var explicitReason = ResolveReasonCode(payload, string.Empty);
            if (!string.IsNullOrWhiteSpace(explicitReason))
            {
                return explicitReason;
            }

            switch (NormalizeEventToken(posePathEventType, "POSE_PATH_EVENT"))
            {
                case "POSE_PATH_HOLD_TICK":
                    return "POSE_HOLD_PROGRESS";
                case "POSE_PATH_HOLD_COMPLETED":
                    return "POSE_HOLD_COMPLETED";
                case "POSE_PATH_HOLD_INVALID":
                    return "POSE_TOLERANCE_EXCEEDED";
                case "POSE_PATH_FOLLOW_TICK":
                    return "PATH_PROGRESS";
                case "POSE_PATH_FOLLOW_COMPLETED":
                    return "PATH_FOLLOW_COMPLETED";
                case "POSE_PATH_FOLLOW_DEVIATION":
                    return "PATH_DEVIATION_EXCEEDED";
                default:
                    return "POSE_PATH_EVENT_OBSERVED";
            }
        }

        private static string ResolveTimelineEventType(IReadOnlyDictionary<string, object> payload)
        {
            var explicitEventType = TryReadString(payload, "timelineEventType");
            if (!string.IsNullOrWhiteSpace(explicitEventType))
            {
                return NormalizeEventToken(explicitEventType, "TIMELINE_EVENT");
            }

            var explicitActionId = TryReadString(payload, "actionId");
            if (string.Equals(
                    NormalizeEventToken(explicitActionId, string.Empty),
                    "WATCH_TIMELINE_SEGMENT",
                    StringComparison.Ordinal))
            {
                var interrupted = TryReadBool(payload, "interrupted");
                var targetValid = TryReadNullableBool(payload, "targetValid");
                var progress01 = TryReadFloat(payload, "progress01");
                var elapsedSec = TryReadFloat(payload, "elapsedSec");
                var requiredSec = TryReadFloat(payload, "requiredSec");

                if (interrupted)
                {
                    return "TIMELINE_SEGMENT_INTERRUPTED";
                }

                if (targetValid.HasValue && !targetValid.Value)
                {
                    return "TIMELINE_SEGMENT_SKIPPED";
                }

                if ((progress01.HasValue && progress01.Value >= 0.999f) ||
                    (elapsedSec.HasValue &&
                     requiredSec.HasValue &&
                     requiredSec.Value > 0f &&
                     elapsedSec.Value >= requiredSec.Value))
                {
                    return "TIMELINE_SEGMENT_COMPLETED";
                }

                return "TIMELINE_SEGMENT_TICK";
            }

            return "TIMELINE_EVENT";
        }

        private static string ResolveTimelineOutcome(
            IReadOnlyDictionary<string, object> payload,
            string timelineEventType)
        {
            var explicitOutcome = TryReadString(payload, "actionOutcome");
            if (!string.IsNullOrWhiteSpace(explicitOutcome))
            {
                return NormalizeEventToken(explicitOutcome, "OBSERVED");
            }

            switch (NormalizeEventToken(timelineEventType, "TIMELINE_EVENT"))
            {
                case "TIMELINE_SEGMENT_COMPLETED":
                    return "CORRECT";
                case "TIMELINE_SEGMENT_INTERRUPTED":
                case "TIMELINE_SEGMENT_SKIPPED":
                    return "INCORRECT";
                case "TIMELINE_SEGMENT_TICK":
                    return "OBSERVED";
                default:
                    return "OBSERVED";
            }
        }

        private static string ResolveTimelineReasonCode(
            IReadOnlyDictionary<string, object> payload,
            string timelineEventType)
        {
            var explicitReason = ResolveReasonCode(payload, string.Empty);
            if (!string.IsNullOrWhiteSpace(explicitReason))
            {
                return explicitReason;
            }

            switch (NormalizeEventToken(timelineEventType, "TIMELINE_EVENT"))
            {
                case "TIMELINE_SEGMENT_TICK":
                    return "TIMELINE_PROGRESS";
                case "TIMELINE_SEGMENT_COMPLETED":
                    return "TIMELINE_SEGMENT_WATCHED";
                case "TIMELINE_SEGMENT_INTERRUPTED":
                    return "TIMELINE_INTERRUPTED";
                case "TIMELINE_SEGMENT_SKIPPED":
                    return "TIMELINE_SEGMENT_SKIPPED";
                default:
                    return "TIMELINE_EVENT_OBSERVED";
            }
        }

        private static string ResolveSequenceEventType(IReadOnlyDictionary<string, object> payload)
        {
            var explicitEventType = TryReadString(payload, "sequenceEventType");
            if (!string.IsNullOrWhiteSpace(explicitEventType))
            {
                return NormalizeEventToken(explicitEventType, "SEQUENCE_EVENT");
            }

            var normalizedActionId = NormalizeEventToken(TryReadString(payload, "actionId"), string.Empty);
            var targetValid = TryReadNullableBool(payload, "targetValid");
            var stepIndex = TryReadFloat(payload, "stepIndex");
            var expectedIndex = TryReadFloat(payload, "expectedIndex");
            var matchedPairs = TryReadFloat(payload, "matchedPairs");
            var totalPairs = TryReadFloat(payload, "totalPairs");

            switch (normalizedActionId)
            {
                case "REPEAT_VISUAL_SEQUENCE":
                    if (targetValid.HasValue && !targetValid.Value)
                    {
                        return "SEQUENCE_VISUAL_WRONG";
                    }

                    if (stepIndex.HasValue &&
                        expectedIndex.HasValue &&
                        stepIndex.Value >= expectedIndex.Value &&
                        expectedIndex.Value > 0f)
                    {
                        return "SEQUENCE_VISUAL_COMPLETED";
                    }

                    return "SEQUENCE_VISUAL_STEP_CORRECT";

                case "REPEAT_AUDIO_SEQUENCE":
                    if (targetValid.HasValue && !targetValid.Value)
                    {
                        return "SEQUENCE_AUDIO_WRONG";
                    }

                    if (stepIndex.HasValue &&
                        expectedIndex.HasValue &&
                        stepIndex.Value >= expectedIndex.Value &&
                        expectedIndex.Value > 0f)
                    {
                        return "SEQUENCE_AUDIO_COMPLETED";
                    }

                    return "SEQUENCE_AUDIO_STEP_CORRECT";

                case "SELECT_SEQUENCE_IN_ORDER":
                    if (targetValid.HasValue && !targetValid.Value)
                    {
                        return "SEQUENCE_ORDER_WRONG";
                    }

                    if (stepIndex.HasValue &&
                        expectedIndex.HasValue &&
                        stepIndex.Value >= expectedIndex.Value &&
                        expectedIndex.Value > 0f)
                    {
                        return "SEQUENCE_ORDER_COMPLETED";
                    }

                    return "SEQUENCE_ORDER_STEP_CORRECT";

                case "MATCH_PAIR":
                    if (targetValid.HasValue && !targetValid.Value)
                    {
                        return "SEQUENCE_PAIR_MISMATCH";
                    }

                    if (matchedPairs.HasValue &&
                        totalPairs.HasValue &&
                        totalPairs.Value > 0f &&
                        matchedPairs.Value >= totalPairs.Value)
                    {
                        return "SEQUENCE_PAIR_COMPLETED";
                    }

                    return "SEQUENCE_PAIR_MATCHED";
            }

            return "SEQUENCE_EVENT";
        }

        private static string ResolveSequenceOutcome(
            IReadOnlyDictionary<string, object> payload,
            string sequenceEventType)
        {
            var explicitOutcome = TryReadString(payload, "actionOutcome");
            if (!string.IsNullOrWhiteSpace(explicitOutcome))
            {
                return NormalizeEventToken(explicitOutcome, "OBSERVED");
            }

            switch (NormalizeEventToken(sequenceEventType, "SEQUENCE_EVENT"))
            {
                case "SEQUENCE_VISUAL_COMPLETED":
                case "SEQUENCE_AUDIO_COMPLETED":
                case "SEQUENCE_ORDER_COMPLETED":
                case "SEQUENCE_PAIR_COMPLETED":
                    return "CORRECT";
                case "SEQUENCE_VISUAL_WRONG":
                case "SEQUENCE_AUDIO_WRONG":
                case "SEQUENCE_ORDER_WRONG":
                case "SEQUENCE_PAIR_MISMATCH":
                    return "INCORRECT";
                case "SEQUENCE_VISUAL_STEP_CORRECT":
                case "SEQUENCE_AUDIO_STEP_CORRECT":
                case "SEQUENCE_ORDER_STEP_CORRECT":
                case "SEQUENCE_PAIR_MATCHED":
                    return "OBSERVED";
                default:
                    return "OBSERVED";
            }
        }

        private static string ResolveSequenceReasonCode(
            IReadOnlyDictionary<string, object> payload,
            string sequenceEventType)
        {
            var explicitReason = ResolveReasonCode(payload, string.Empty);
            if (!string.IsNullOrWhiteSpace(explicitReason))
            {
                return explicitReason;
            }

            switch (NormalizeEventToken(sequenceEventType, "SEQUENCE_EVENT"))
            {
                case "SEQUENCE_VISUAL_STEP_CORRECT":
                case "SEQUENCE_AUDIO_STEP_CORRECT":
                case "SEQUENCE_ORDER_STEP_CORRECT":
                    return "SEQUENCE_STEP_CORRECT";
                case "SEQUENCE_VISUAL_COMPLETED":
                case "SEQUENCE_AUDIO_COMPLETED":
                case "SEQUENCE_ORDER_COMPLETED":
                case "SEQUENCE_PAIR_COMPLETED":
                    return "SEQUENCE_COMPLETED";
                case "SEQUENCE_VISUAL_WRONG":
                case "SEQUENCE_AUDIO_WRONG":
                case "SEQUENCE_ORDER_WRONG":
                    return "WRONG_SEQUENCE_ORDER";
                case "SEQUENCE_PAIR_MATCHED":
                    return "PAIR_MATCHED";
                case "SEQUENCE_PAIR_MISMATCH":
                    return "PAIR_MISMATCH";
                default:
                    return "SEQUENCE_EVENT_OBSERVED";
            }
        }

        private static string ResolveReasonCode(
            IReadOnlyDictionary<string, object> payload,
            string fallback)
        {
            var reasonCode = TryReadString(payload, "reasonCode");
            if (!string.IsNullOrWhiteSpace(reasonCode))
            {
                return reasonCode.Trim();
            }

            reasonCode = TryReadString(payload, "reason");
            if (!string.IsNullOrWhiteSpace(reasonCode))
            {
                return reasonCode.Trim();
            }

            return NormalizeOrFallback(fallback, string.Empty);
        }

        private static bool TryGetPayloadValue(
            IReadOnlyDictionary<string, object> payload,
            string key,
            out object value)
        {
            value = null;
            if (payload == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            return payload.TryGetValue(key, out value);
        }

        private static string TryReadString(IReadOnlyDictionary<string, object> payload, string key)
        {
            if (!TryGetPayloadValue(payload, key, out var value) || value == null)
            {
                return string.Empty;
            }

            var converted = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(converted) ? string.Empty : converted.Trim();
        }

        private static float? TryReadFloat(IReadOnlyDictionary<string, object> payload, string key)
        {
            if (!TryGetPayloadValue(payload, key, out var value) || value == null)
            {
                return null;
            }

            switch (value)
            {
                case float floatValue:
                    return floatValue;
                case double doubleValue:
                    return (float)doubleValue;
                case decimal decimalValue:
                    return (float)decimalValue;
                case int intValue:
                    return intValue;
                case long longValue:
                    return longValue;
            }

            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (float?)null;
        }

        private static int? TryReadInt(IReadOnlyDictionary<string, object> payload, string key)
        {
            if (!TryGetPayloadValue(payload, key, out var value) || value == null)
            {
                return null;
            }

            switch (value)
            {
                case int intValue:
                    return intValue;
                case long longValue:
                    if (longValue > int.MaxValue)
                    {
                        return int.MaxValue;
                    }

                    if (longValue < int.MinValue)
                    {
                        return int.MinValue;
                    }

                    return (int)longValue;
                case float floatValue:
                    return Mathf.RoundToInt(floatValue);
                case double doubleValue:
                    return Mathf.RoundToInt((float)doubleValue);
            }

            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (int?)null;
        }

        private static bool TryReadBool(IReadOnlyDictionary<string, object> payload, string key)
        {
            if (!TryGetPayloadValue(payload, key, out var value) || value == null)
            {
                return false;
            }

            switch (value)
            {
                case bool boolValue:
                    return boolValue;
                case int intValue:
                    return intValue != 0;
                case long longValue:
                    return longValue != 0L;
                case float floatValue:
                    return Math.Abs(floatValue) > float.Epsilon;
                case double doubleValue:
                    return Math.Abs(doubleValue) > double.Epsilon;
            }

            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (bool.TryParse(text, out var parsedBool))
            {
                return parsedBool;
            }

            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedFloat))
            {
                return Math.Abs(parsedFloat) > float.Epsilon;
            }

            return false;
        }

        private static bool? TryReadNullableBool(IReadOnlyDictionary<string, object> payload, string key)
        {
            if (!TryGetPayloadValue(payload, key, out var value) || value == null)
            {
                return null;
            }

            return TryReadBool(payload, key);
        }

        private static string BuildOwnerKey(string therapistId, string studentId)
        {
            var normalizedTherapistId = NormalizeOrFallback(therapistId, string.Empty);
            var normalizedStudentId = NormalizeOrFallback(studentId, string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedTherapistId) ||
                string.IsNullOrWhiteSpace(normalizedStudentId))
            {
                return string.Empty;
            }

            return normalizedTherapistId + "|" + normalizedStudentId;
        }

        private static string BuildSessionKey(string ownerKey, string sessionId)
        {
            var normalizedOwnerKey = NormalizeOrFallback(ownerKey, string.Empty);
            var normalizedSessionId = NormalizeOrFallback(sessionId, string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedOwnerKey) ||
                string.IsNullOrWhiteSpace(normalizedSessionId))
            {
                return string.Empty;
            }

            return normalizedOwnerKey + "|" + normalizedSessionId;
        }

        private static string NormalizeEventToken(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return NormalizeOrFallback(fallback, "UNSPECIFIED");
            }

            return value.Trim().Replace(' ', '_').ToUpperInvariant();
        }

        private static string NormalizeOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? (fallback ?? string.Empty)
                : value.Trim();
        }

        private static string NormalizeOrFallback(string first, string second, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(first))
            {
                return first.Trim();
            }

            if (!string.IsNullOrWhiteSpace(second))
            {
                return second.Trim();
            }

            return NormalizeOrFallback(fallback, string.Empty);
        }
    }
}
