using System;
using UnityEngine;

namespace TheraplyCore.Interactions
{
    [DisallowMultipleComponent]
    public sealed class TargetValidationZone : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string _targetId = string.Empty;
        [SerializeField] private string _semanticTag = "GENERIC_TARGET";

        [Header("Validation")]
        [SerializeField] private bool _defaultValidTarget = true;
        [SerializeField] private bool _preferPointerSuggestedValidity = true;
        [SerializeField] private string _requiredToolId = string.Empty;

        public string TargetId => string.IsNullOrWhiteSpace(_targetId)
            ? gameObject.name
            : _targetId.Trim();

        public string SemanticTag => string.IsNullOrWhiteSpace(_semanticTag)
            ? "GENERIC_TARGET"
            : _semanticTag.Trim();

        public string RequiredToolId => string.IsNullOrWhiteSpace(_requiredToolId)
            ? string.Empty
            : _requiredToolId.Trim();

        public bool DefaultValidTarget => _defaultValidTarget;
        public bool PreferPointerSuggestedValidity => _preferPointerSuggestedValidity;

        public void Configure(
            string targetId,
            string semanticTag,
            bool defaultValidTarget,
            string requiredToolId = "")
        {
            _targetId = NormalizeOrFallback(targetId, string.Empty);
            _semanticTag = NormalizeOrFallback(semanticTag, "GENERIC_TARGET");
            _defaultValidTarget = defaultValidTarget;
            _requiredToolId = NormalizeOrFallback(requiredToolId, string.Empty);
        }

        public TargetValidationResult Evaluate(string toolId, bool? pointerSuggestedValidity)
        {
            var normalizedToolId = NormalizeOrFallback(toolId, string.Empty);
            var zoneTargetId = TargetId;
            var zoneTargetName = string.IsNullOrWhiteSpace(gameObject.name) ? zoneTargetId : gameObject.name;
            var zoneSemanticTag = SemanticTag;

            if (!string.IsNullOrWhiteSpace(RequiredToolId) &&
                !string.Equals(RequiredToolId, normalizedToolId, StringComparison.OrdinalIgnoreCase))
            {
                return new TargetValidationResult(
                    zoneTargetId,
                    zoneTargetName,
                    zoneSemanticTag,
                    false,
                    "TOOL_ID_MISMATCH");
            }

            if (_preferPointerSuggestedValidity && pointerSuggestedValidity.HasValue)
            {
                return new TargetValidationResult(
                    zoneTargetId,
                    zoneTargetName,
                    zoneSemanticTag,
                    pointerSuggestedValidity.Value,
                    pointerSuggestedValidity.Value ? "POINTER_TARGET_VALID" : "POINTER_TARGET_INVALID");
            }

            var isValid = _defaultValidTarget;
            return new TargetValidationResult(
                zoneTargetId,
                zoneTargetName,
                zoneSemanticTag,
                isValid,
                isValid ? "TARGET_ZONE_VALID" : "TARGET_ZONE_INVALID");
        }

        public static bool TryResolve(Collider collider, out TargetValidationZone zone)
        {
            zone = null;
            if (collider == null)
            {
                return false;
            }

            zone = collider.GetComponentInParent<TargetValidationZone>();
            return zone != null;
        }

        private static string NormalizeOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? (fallback ?? string.Empty)
                : value.Trim();
        }
    }

    public readonly struct TargetValidationResult
    {
        public TargetValidationResult(
            string targetId,
            string targetName,
            string semanticTag,
            bool isValid,
            string reasonCode)
        {
            TargetId = string.IsNullOrWhiteSpace(targetId) ? string.Empty : targetId.Trim();
            TargetName = string.IsNullOrWhiteSpace(targetName) ? string.Empty : targetName.Trim();
            SemanticTag = string.IsNullOrWhiteSpace(semanticTag) ? string.Empty : semanticTag.Trim();
            IsValid = isValid;
            ReasonCode = string.IsNullOrWhiteSpace(reasonCode) ? string.Empty : reasonCode.Trim();
        }

        public string TargetId { get; }
        public string TargetName { get; }
        public string SemanticTag { get; }
        public bool IsValid { get; }
        public string ReasonCode { get; }
    }
}
