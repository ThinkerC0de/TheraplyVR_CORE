using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Interactions
{
    [DisallowMultipleComponent]
    public sealed class HandContactProbe : MonoBehaviour
    {
        [Serializable]
        public struct HandContactSample
        {
            public string handId;
            public string gameId;
            public string inputHand;
            public string inputSource;
            public string inputControl;
            public string sourceComponent;
            public string targetId;
            public string targetName;
            public float inputValue;
            public bool hitAnyCollider;
            public bool hitInteractiveTarget;
            public bool? pointerSuggestedTargetValid;
            public Collider hitCollider;
            public TargetValidationZone targetValidationZone;
            public Vector3 contactPointWorld;
            public Vector3 contactNormalWorld;
        }

        [Header("Behavior")]
        [SerializeField] private bool _emitTelemetry = true;
        [SerializeField] private bool _logTelemetry;

        [Header("Dependencies")]
        [SerializeField] private InteractionEventBridge _interactionEventBridge;

        public static HandContactProbe Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (Instance != null)
            {
                return;
            }

            var existing = FindFirstObjectByType<HandContactProbe>();
            if (existing != null)
            {
                Instance = existing;
                return;
            }

            var host = new GameObject("HandContactProbe");
            host.AddComponent<HandContactProbe>();
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

        public void RecordContact(HandContactSample sample)
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

            var resolvedZone = ResolveZone(sample);
            var validation = ResolveValidationResult(sample, resolvedZone);

            ResolveContactClassification(
                sample.hitAnyCollider,
                validation,
                out var eventType,
                out var actionOutcome,
                out var reasonCode);

            var resolvedTargetId = NormalizeOrFallback(
                validation.TargetId,
                NormalizeOrFallback(sample.targetId, sample.targetName));
            var resolvedTargetName = NormalizeOrFallback(
                validation.TargetName,
                NormalizeOrFallback(sample.targetName, sample.targetId));
            var resolvedHandId = NormalizeOrFallback(sample.handId, "player_hand");

            var details = new Dictionary<string, object>
            {
                { "handId", resolvedHandId },
                { "hitAnyCollider", sample.hitAnyCollider },
                { "hitInteractiveTarget", sample.hitInteractiveTarget },
                { "contactPointWorld", FormatVector3(sample.contactPointWorld) },
                { "contactNormalWorld", FormatVector3(sample.contactNormalWorld) },
                { "targetSemanticTag", NormalizeOrFallback(validation.SemanticTag, string.Empty) },
                {
                    "requiredToolId",
                    resolvedZone == null
                        ? string.Empty
                        : NormalizeOrFallback(resolvedZone.RequiredToolId, string.Empty)
                },
            };

            if (sample.pointerSuggestedTargetValid.HasValue)
            {
                details["pointerSuggestedTargetValid"] = sample.pointerSuggestedTargetValid.Value;
            }

            var payload = new Dictionary<string, object>
            {
                { "handEventType", eventType },
                { "actionOutcome", actionOutcome },
                { "reasonCode", reasonCode },
                { "gameId", NormalizeOrFallback(sample.gameId, "unknown_game") },
                { "sourceComponent", NormalizeOrFallback(sample.sourceComponent, nameof(HandContactProbe)) },
                { "inputHand", NormalizeOrFallback(sample.inputHand, string.Empty) },
                { "inputSource", NormalizeOrFallback(sample.inputSource, "HAND") },
                { "inputControl", NormalizeOrFallback(sample.inputControl, string.Empty) },
                { "inputValue", Mathf.Clamp01(sample.inputValue) },
                { "targetId", resolvedTargetId },
                { "targetName", resolvedTargetName },
                { "targetValid", sample.hitAnyCollider && validation.IsValid },
                { "details", details },
            };

            _interactionEventBridge.RecordHandTelemetry(payload);

            if (_logTelemetry)
            {
                Logger.Info(
                    $"[HandContactProbe] eventType={eventType} targetValid={payload["targetValid"]} targetId={resolvedTargetId}");
            }
        }

        private static TargetValidationZone ResolveZone(HandContactSample sample)
        {
            if (sample.targetValidationZone != null)
            {
                return sample.targetValidationZone;
            }

            if (sample.hitCollider != null &&
                TargetValidationZone.TryResolve(sample.hitCollider, out var resolvedZone))
            {
                return resolvedZone;
            }

            return null;
        }

        private static TargetValidationResult ResolveValidationResult(
            HandContactSample sample,
            TargetValidationZone resolvedZone)
        {
            if (!sample.hitAnyCollider)
            {
                return new TargetValidationResult(
                    NormalizeOrFallback(sample.targetId, string.Empty),
                    NormalizeOrFallback(sample.targetName, string.Empty),
                    resolvedZone == null ? string.Empty : resolvedZone.SemanticTag,
                    false,
                    "TARGET_NOT_FOUND");
            }

            if (resolvedZone != null)
            {
                return resolvedZone.Evaluate("hand_contact", sample.pointerSuggestedTargetValid);
            }

            if (sample.pointerSuggestedTargetValid.HasValue)
            {
                return new TargetValidationResult(
                    NormalizeOrFallback(sample.targetId, string.Empty),
                    NormalizeOrFallback(sample.targetName, string.Empty),
                    "POINTER_TARGET",
                    sample.pointerSuggestedTargetValid.Value,
                    sample.pointerSuggestedTargetValid.Value ? "POINTER_TARGET_VALID" : "POINTER_TARGET_INVALID");
            }

            var isInteractive = sample.hitInteractiveTarget;
            return new TargetValidationResult(
                NormalizeOrFallback(sample.targetId, string.Empty),
                NormalizeOrFallback(sample.targetName, string.Empty),
                "HAND_TARGET",
                isInteractive,
                isInteractive ? "INTERACTIVE_TARGET_HIT" : "TARGET_NON_INTERACTIVE");
        }

        private static void ResolveContactClassification(
            bool hitAnyCollider,
            TargetValidationResult validation,
            out string eventType,
            out string actionOutcome,
            out string reasonCode)
        {
            if (!hitAnyCollider)
            {
                eventType = "HAND_CONTACT_MISS";
                actionOutcome = "OMITTED";
                reasonCode = "TARGET_NOT_FOUND";
                return;
            }

            if (validation.IsValid)
            {
                eventType = "HAND_CONTACT_HIT";
                actionOutcome = "CORRECT";
                reasonCode = NormalizeOrFallback(validation.ReasonCode, "TARGET_VALIDATED");
                return;
            }

            eventType = "HAND_CONTACT_INVALID";
            actionOutcome = "INCORRECT";
            reasonCode = NormalizeOrFallback(validation.ReasonCode, "TARGET_INVALID");
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

        private static string FormatVector3(Vector3 value)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:F3},{1:F3},{2:F3}",
                value.x,
                value.y,
                value.z);
        }

        private static string NormalizeOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? (fallback ?? string.Empty)
                : value.Trim();
        }
    }
}
