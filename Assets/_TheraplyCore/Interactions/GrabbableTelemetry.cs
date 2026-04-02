using System.Collections.Generic;
using Autohand;
using TheraplyCore.Interactions;
using UnityEngine;
using UnityEngine.Events;

namespace TheraplyCore.Interactions
{
    /// <summary>
    /// Add to any AutoHand Grabbable object to emit telemetry on grab and release.
    /// Fully configurable in the Inspector — no per-game code needed.
    ///
    /// Usage:
    ///   1. Add this component to a prefab that has a Grabbable component.
    ///   2. Fill in the Telemetry fields in the Inspector.
    ///   3. Optionally wire On Grabbed / On Released UnityEvents to game logic.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Grabbable))]
    public sealed class GrabbableTelemetry : MonoBehaviour
    {
        // ── Telemetry ─────────────────────────────────────────────────────────
        [Header("Telemetry")]
        [Tooltip("Game identifier, e.g. 'piniata', 'puzzle', 'magic_wand'")]
        [SerializeField] private string _gameId = string.Empty;

        [Tooltip("Stable identifier for this object type, e.g. 'sweet', 'puzzle_piece'")]
        [SerializeField] private string _targetId = string.Empty;

        [Tooltip("Semantic category tag, e.g. 'GRABBABLE_OBJECT'")]
        [SerializeField] private string _semanticTag = "GRABBABLE_OBJECT";

        [Tooltip("Optional extra label included in telemetry details, e.g. 'red', 'blue'. " +
                 "Use to describe color, variant, or any property not captured by targetId.")]
        [SerializeField] private string _variantLabel = string.Empty;

        [Header("Grab Event")]
        [SerializeField] private string _grabEventType = "object_grabbed";
        [SerializeField] private string _grabActionOutcome = "CORRECT";
        [SerializeField] private string _grabReasonCode = "OBJECT_PICKED_UP";

        [Header("Release Event")]
        [SerializeField] private string _releaseEventType = "object_released";
        [SerializeField] private string _releaseActionOutcome = "NEUTRAL";
        [SerializeField] private string _releaseReasonCode = "OBJECT_RELEASED";

        // ── Callbacks ─────────────────────────────────────────────────────────
        [Header("Callbacks")]
        [Tooltip("Invoked after grab telemetry is emitted.")]
        [SerializeField] private UnityEvent _onGrabbed;

        [Tooltip("Invoked after release telemetry is emitted.")]
        [SerializeField] private UnityEvent _onReleased;

        // ── Runtime state ─────────────────────────────────────────────────────
        private string _instanceId;
        private float _grabStartRealtime;

        // ── Unity lifecycle ───────────────────────────────────────────────────
        private void Awake()
        {
            _instanceId = LegacyInteractionTelemetry.CreateTargetInstanceId();

            var grabbable = GetComponent<Grabbable>();
            grabbable.OnGrabEvent += HandleGrab;
            grabbable.OnReleaseEvent += HandleRelease;
        }

        private void OnDestroy()
        {
            if (TryGetComponent<Grabbable>(out var grabbable))
            {
                grabbable.OnGrabEvent -= HandleGrab;
                grabbable.OnReleaseEvent -= HandleRelease;
            }
        }

        // ── Handlers ──────────────────────────────────────────────────────────
        private void HandleGrab(Hand hand, Grabbable grabbable)
        {
            _grabStartRealtime = Time.realtimeSinceStartup;

            var details = BuildDetails();
            LegacyInteractionTelemetry.EmitOutcome(
                _gameId,
                _grabEventType,
                string.Empty,
                _grabActionOutcome,
                _grabReasonCode,
                nameof(GrabbableTelemetry),
                targetId: _instanceId,
                targetName: gameObject.name,
                inputHand: ResolveHand(hand),
                inputSource: "HAND",
                extraDetails: details);

            _onGrabbed?.Invoke();
        }

        private void HandleRelease(Hand hand, Grabbable grabbable)
        {
            var heldSec = Time.realtimeSinceStartup - _grabStartRealtime;

            var details = BuildDetails();
            details["heldSeconds"] = heldSec.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

            LegacyInteractionTelemetry.EmitOutcome(
                _gameId,
                _releaseEventType,
                string.Empty,
                _releaseActionOutcome,
                _releaseReasonCode,
                nameof(GrabbableTelemetry),
                targetId: _instanceId,
                targetName: gameObject.name,
                inputHand: ResolveHand(hand),
                inputSource: "HAND",
                inputValue: heldSec,
                extraDetails: details);

            _onReleased?.Invoke();
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private Dictionary<string, object> BuildDetails()
        {
            var d = new Dictionary<string, object>
            {
                { "targetCategory", _semanticTag },
                { "targetSemanticTag", _semanticTag },
            };
            if (!string.IsNullOrWhiteSpace(_variantLabel))
                d["variantLabel"] = _variantLabel.Trim();
            return d;
        }

        private static string ResolveHand(Hand hand)
        {
            if (hand == null) return string.Empty;
            return hand.left ? "LEFT" : "RIGHT";
        }
    }
}
