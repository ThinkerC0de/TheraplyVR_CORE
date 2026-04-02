using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;

namespace TheraplyCore.Interactions
{
    /// <summary>
    /// Placed directly on a game object's collider. Detects when a tagged tool (e.g. stick)
    /// physically contacts this object and emits a telemetry interaction event via
    /// LegacyInteractionTelemetry. Fires a UnityEvent so game logic can react without
    /// coupling to this component.
    ///
    /// Works event-driven only — no Update() polling, safe for VR framerates.
    /// Handles both trigger and solid-collider physics modes automatically.
    ///
    /// Suppression: call PhysicsContactHitReporter.SuppressGroup(groupId, seconds)
    /// to block hits for a window (e.g. after a correct target hit, to avoid
    /// penalising the natural follow-through of the same swing).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhysicsContactHitReporter : MonoBehaviour
    {
        // ── Tool filtering ────────────────────────────────────────────────────
        [Header("Tool Filter")]
        [Tooltip("Tags that identify valid tools. All transform ancestors are checked.")]
        [SerializeField] private string[] _acceptedToolTags = { "Stick_Red", "Stick_Blue" };

        [Tooltip("Minimum velocity (m/s) required to register a hit. " +
                 "For trigger contacts the stick rigidbody velocity is used; " +
                 "for solid collisions the relative velocity is used. " +
                 "Set to 0 to accept any contact.")]
        [SerializeField] private float _minImpactVelocity = 0.08f;

        // ── Debounce ─────────────────────────────────────────────────────────
        [Header("Debounce")]
        [Tooltip("Minimum seconds between two registered hits from the same tool instance.")]
        [SerializeField] private float _perToolCooldownSeconds = 0.30f;

        [Tooltip("Minimum seconds between any two hits on this reporter regardless of tool.")]
        [SerializeField] private float _globalCooldownSeconds = 0.10f;

        // ── Suppression ───────────────────────────────────────────────────────
        [Header("Suppression")]
        [Tooltip("Suppression group id. Hits are blocked while SuppressGroup() has been called " +
                 "for this id within its window. Leave empty to disable suppression.")]
        [SerializeField] private string _suppressionGroupId = string.Empty;

        // ── Telemetry ─────────────────────────────────────────────────────────
        [Header("Telemetry")]
        [SerializeField] private string _gameId = string.Empty;
        [SerializeField] private string _targetId = string.Empty;
        [SerializeField] private string _targetName = string.Empty;
        [SerializeField] private string _semanticTag = "HIT_ZONE";
        [SerializeField] private string _eventType = "zone_hit";
        [SerializeField] private string _actionOutcome = "INCORRECT";
        [SerializeField] private string _reasonCode = string.Empty;

        // ── Callbacks ─────────────────────────────────────────────────────────
        [Header("Callbacks")]
        [Tooltip("Invoked after a hit is confirmed and telemetry emitted. " +
                 "Wire to PiniataGame.WrongAnswer() or equivalent game logic.")]
        [SerializeField] private UnityEvent _onHitConfirmed;


        // ── Runtime binding ───────────────────────────────────────────────────

        /// <summary>
        /// Configure telemetry fields from an existing TargetValidationZone on this object.
        /// Called by PiniataBodyHitBinder — no manual Inspector setup needed.
        /// </summary>
        public void BindFromZone(
            TargetValidationZone zone,
            string gameId,
            string eventType,
            string actionOutcome,
            string reasonCode)
        {
            _gameId = gameId;
            _targetId = zone.TargetId;
            _targetName = zone.gameObject.name;
            _semanticTag = zone.SemanticTag;
            _eventType = eventType;
            _actionOutcome = actionOutcome;
            _reasonCode = reasonCode;
            _suppressionGroupId = zone.SemanticTag.ToUpperInvariant();
        }

        /// <summary>
        /// Configure telemetry fields manually at runtime (when no TargetValidationZone exists).
        /// </summary>
        public void BindManual(
            string gameId,
            string targetId,
            string targetName,
            string semanticTag,
            string eventType,
            string actionOutcome,
            string reasonCode,
            string suppressionGroupId)
        {
            _gameId = gameId;
            _targetId = targetId;
            _targetName = targetName;
            _semanticTag = semanticTag;
            _eventType = eventType;
            _actionOutcome = actionOutcome;
            _reasonCode = reasonCode;
            _suppressionGroupId = suppressionGroupId;
        }

        /// <summary>Add a runtime listener invoked after a confirmed hit.</summary>
        public void AddHitListener(UnityAction action)
        {
            if (_onHitConfirmed == null)
            {
                _onHitConfirmed = new UnityEvent();
            }

            _onHitConfirmed.AddListener(action);
        }

        // ── Static suppression registry ───────────────────────────────────────
        private static readonly Dictionary<string, float> _suppressUntil =
            new Dictionary<string, float>();

        /// <summary>
        /// Suppress hits in the given group for the next <paramref name="windowSeconds"/>.
        /// Call this from a correct-hit handler to prevent double-penalising the same swing.
        /// </summary>
        public static void SuppressGroup(string groupId, float windowSeconds)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return;
            }

            var key = groupId.ToUpperInvariant();
            var suppressUntil = Time.unscaledTime + windowSeconds;
            if (_suppressUntil.TryGetValue(key, out var existing) && existing > suppressUntil)
            {
                return;
            }

            _suppressUntil[key] = suppressUntil;
        }

        private static bool IsGroupSuppressed(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return false;
            }

            var key = groupId.ToUpperInvariant();
            return _suppressUntil.TryGetValue(key, out var suppressUntil) &&
                   Time.unscaledTime < suppressUntil;
        }

        // ── Internal state ────────────────────────────────────────────────────
        private float _lastGlobalHitAt = -999f;
        private readonly Dictionary<int, float> _lastHitPerTool = new Dictionary<int, float>();

        // ── Unity physics callbacks ───────────────────────────────────────────

        private void OnTriggerEnter(Collider other)
        {
            var velocity = EstimateVelocityFromRigidbody(other);
            Debug.LogWarning($"[PhysicsContactHitReporter] OnTriggerEnter on={gameObject.name} other={other?.name} tag={other?.tag} v={velocity:F3}");
            TryRegisterHit(other, velocity, "trigger");
        }

        private void OnCollisionEnter(Collision collision)
        {
            Debug.LogWarning($"[PhysicsContactHitReporter] OnCollisionEnter on={gameObject.name} other={collision.collider?.name} tag={collision.collider?.tag} v={collision.relativeVelocity.magnitude:F3}");
            TryRegisterHit(collision.collider, collision.relativeVelocity.magnitude, "collision");
        }

        // ── Core logic ────────────────────────────────────────────────────────

        private void TryRegisterHit(Collider toolCollider, float impactVelocity, string contactMode)
        {
            if (!IsAcceptedTool(toolCollider))
            {
                Debug.Log($"[PhysicsContactHitReporter] reject collider={gameObject.name} mode={contactMode} reason=tool_tag_mismatch tag={toolCollider?.tag}");
                return;
            }

            if (impactVelocity < _minImpactVelocity)
            {
                Debug.Log($"[PhysicsContactHitReporter] reject collider={gameObject.name} mode={contactMode} reason=velocity_too_low v={impactVelocity:F3} min={_minImpactVelocity}");
                return;
            }

            // Defer suppression check + emission to next Update so that same-physics-step
            // correct-point callbacks (HitPoint.OnTriggerEnter) can set SuppressGroup first.
            StartCoroutine(DeferredTryEmit(toolCollider, impactVelocity, contactMode));
        }

        private IEnumerator DeferredTryEmit(Collider toolCollider, float impactVelocity, string contactMode)
        {
            // Wait long enough for a point trigger (HitPoint.OnTriggerEnter) that occurs in the
            // same swing to fire and call SuppressGroup before we emit the body hit.
            // 200 ms covers the full range of swing speeds: at low velocity the stick may graze
            // the body then reach the hit point 100-150 ms later; at high velocity everything
            // happens in the same physics step. 200 ms is still well below the minimum
            // perceivable reaction time (~300 ms), so the delayed WrongAnswer is not noticeable.
            yield return new WaitForSecondsRealtime(0.20f);

            if (toolCollider == null) yield break;

            if (IsGroupSuppressed(_suppressionGroupId)) yield break;

            var now = Time.unscaledTime;

            if (now - _lastGlobalHitAt < _globalCooldownSeconds) yield break;

            var toolInstanceId = toolCollider.GetInstanceID();
            if (_lastHitPerTool.TryGetValue(toolInstanceId, out var lastHit) &&
                now - lastHit < _perToolCooldownSeconds) yield break;

            _lastGlobalHitAt = now;
            _lastHitPerTool[toolInstanceId] = now;

            // Block sibling reporters sharing the same group (e.g. other body colliders).
            SuppressGroup(_suppressionGroupId, _perToolCooldownSeconds);

            EmitHit(toolCollider, impactVelocity, contactMode);
            _onHitConfirmed?.Invoke();
        }

        private void EmitHit(Collider toolCollider, float impactVelocity, string contactMode)
        {
            var stickTag = ResolveToolTag(toolCollider.transform);
            var handType = ResolveHandType(toolCollider);

            var resolvedTargetId = Normalize(_targetId, gameObject.name);
            var resolvedTargetName = Normalize(_targetName, gameObject.name);
            var resolvedGameId = Normalize(_gameId, "unknown_game");

            var details = new Dictionary<string, object>
            {
                { "targetCategory", Normalize(_semanticTag, "HIT_ZONE") },
                { "targetSemanticTag", Normalize(_semanticTag, "HIT_ZONE") },
                { "contactMode", contactMode },
                { "colliderName", gameObject.name },
                { "impactVelocity", impactVelocity.ToString("F3", CultureInfo.InvariantCulture) },
            };

            LegacyInteractionTelemetry.EmitOutcome(
                resolvedGameId,
                Normalize(_eventType, "zone_hit"),
                string.Empty,
                Normalize(_actionOutcome, "INCORRECT"),
                Normalize(_reasonCode, string.Empty),
                nameof(PhysicsContactHitReporter),
                targetId: resolvedTargetId,
                targetName: resolvedTargetName,
                inputHand: handType,
                inputSource: stickTag,
                inputValue: impactVelocity,
                extraDetails: details);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private bool IsAcceptedTool(Collider toolCollider)
        {
            if (toolCollider == null || _acceptedToolTags == null || _acceptedToolTags.Length == 0)
            {
                return false;
            }

            var tag = ResolveToolTag(toolCollider.transform);
            return !string.IsNullOrEmpty(tag);
        }

        private string ResolveToolTag(Transform current)
        {
            while (current != null)
            {
                for (var i = 0; i < _acceptedToolTags.Length; i++)
                {
                    var accepted = _acceptedToolTags[i];
                    if (!string.IsNullOrEmpty(accepted) && current.CompareTag(accepted))
                    {
                        return accepted;
                    }
                }

                current = current.parent;
            }

            return string.Empty;
        }

        private static float EstimateVelocityFromRigidbody(Collider collider)
        {
            if (collider == null)
            {
                return 0f;
            }

            var rb = collider.attachedRigidbody ?? collider.GetComponentInParent<Rigidbody>();
            return rb == null ? 0f : rb.linearVelocity.magnitude;
        }

        private static string ResolveHandType(Collider toolCollider)
        {
            if (toolCollider == null)
            {
                return string.Empty;
            }

            var stick = toolCollider.GetComponentInParent<HittingStick>();
            if (stick == null)
            {
                return string.Empty;
            }

            switch (stick.handType)
            {
                case HittingStick.HandType.LeftHand: return "LEFT";
                case HittingStick.HandType.RightHand: return "RIGHT";
                default: return string.Empty;
            }
        }

        private static string Normalize(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? (fallback ?? string.Empty)
                : value.Trim();
        }
    }
}
