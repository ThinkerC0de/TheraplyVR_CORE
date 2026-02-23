using System;
using System.Collections.Generic;
using UnityEngine;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Interactions
{
    [DisallowMultipleComponent]
    public sealed class ToolGripTracker : MonoBehaviour
    {
        [Serializable]
        public struct ToolGripSample
        {
            public string toolId;
            public string gameId;
            public string inputHand;
            public string inputSource;
            public string inputControl;
            public string sourceComponent;
            public float inputValue;
            public bool isPressed;
            public float realtimeSinceStartup;
        }

        private sealed class ActiveGripState
        {
            public bool isActive;
            public string toolId = string.Empty;
            public string gameId = string.Empty;
            public string inputHand = string.Empty;
            public string inputSource = string.Empty;
            public string inputControl = string.Empty;
            public string sourceComponent = "ToolGripTracker";
            public float startedAtRealtime;
            public float nextHoldEventAtRealtime;
            public float maxInputValue;
            public float lastInputValue;
        }

        [Header("Behavior")]
        [SerializeField] private bool _emitTelemetry = true;
        [SerializeField] private float _pressThreshold = 0.7f;
        [SerializeField] private float _holdHeartbeatIntervalSec = 0.6f;
        [SerializeField] private bool _logTelemetry;

        [Header("Dependencies")]
        [SerializeField] private InteractionEventBridge _interactionEventBridge;

        private readonly ActiveGripState _activeGrip = new ActiveGripState();

        public static ToolGripTracker Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (Instance != null)
            {
                return;
            }

            var existing = FindFirstObjectByType<ToolGripTracker>();
            if (existing != null)
            {
                Instance = existing;
                return;
            }

            var host = new GameObject("ToolGripTracker");
            host.AddComponent<ToolGripTracker>();
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

        public void RecordGripState(ToolGripSample sample)
        {
            var now = sample.realtimeSinceStartup >= 0f
                ? sample.realtimeSinceStartup
                : Time.realtimeSinceStartup;
            var threshold = Mathf.Clamp01(_pressThreshold);
            var normalizedValue = Mathf.Clamp01(sample.inputValue);
            var shouldTreatAsPressed = sample.isPressed && normalizedValue >= threshold;

            if (!_emitTelemetry)
            {
                if (!shouldTreatAsPressed && _activeGrip.isActive)
                {
                    ResetActiveGrip();
                }

                return;
            }

            ResolveDependencies();
            if (_interactionEventBridge == null)
            {
                return;
            }

            if (shouldTreatAsPressed)
            {
                if (_activeGrip.isActive && !IsSameGrip(sample))
                {
                    EmitGripEnd(now, "TOOL_GRIP_SWITCHED", sample.sourceComponent);
                    ResetActiveGrip();
                }

                if (!_activeGrip.isActive)
                {
                    BeginGrip(sample, normalizedValue, now);
                    EmitGripEvent(
                        "TOOL_GRIP_START",
                        "TOOL_GRIP_STARTED",
                        _activeGrip.gameId,
                        _activeGrip.sourceComponent,
                        _activeGrip.inputHand,
                        _activeGrip.inputSource,
                        _activeGrip.inputControl,
                        normalizedValue,
                        0f,
                        _activeGrip.maxInputValue,
                        isPressed: true,
                        _activeGrip.toolId);
                    return;
                }

                _activeGrip.lastInputValue = normalizedValue;
                _activeGrip.maxInputValue = Mathf.Max(_activeGrip.maxInputValue, normalizedValue);

                if (now >= _activeGrip.nextHoldEventAtRealtime)
                {
                    var duration = Mathf.Max(0f, now - _activeGrip.startedAtRealtime);
                    EmitGripEvent(
                        "TOOL_GRIP_HOLD",
                        "TOOL_GRIP_HOLDING",
                        _activeGrip.gameId,
                        _activeGrip.sourceComponent,
                        _activeGrip.inputHand,
                        _activeGrip.inputSource,
                        _activeGrip.inputControl,
                        normalizedValue,
                        duration,
                        _activeGrip.maxInputValue,
                        isPressed: true,
                        _activeGrip.toolId);
                    _activeGrip.nextHoldEventAtRealtime = now + Mathf.Max(0.1f, _holdHeartbeatIntervalSec);
                }

                return;
            }

            if (_activeGrip.isActive)
            {
                EmitGripEnd(now, "TOOL_GRIP_RELEASED", sample.sourceComponent);
                ResetActiveGrip();
            }
        }

        public void ForceRelease(string sourceComponent)
        {
            if (!_activeGrip.isActive)
            {
                return;
            }

            if (_emitTelemetry)
            {
                ResolveDependencies();
                if (_interactionEventBridge != null)
                {
                    EmitGripEnd(Time.realtimeSinceStartup, "TOOL_GRIP_FORCE_RELEASED", sourceComponent);
                }
            }

            ResetActiveGrip();
        }

        private void EmitGripEnd(float now, string reasonCode, string sourceComponent)
        {
            var duration = Mathf.Max(0f, now - _activeGrip.startedAtRealtime);
            EmitGripEvent(
                "TOOL_GRIP_END",
                NormalizeOrFallback(reasonCode, "TOOL_GRIP_RELEASED"),
                _activeGrip.gameId,
                NormalizeOrFallback(sourceComponent, _activeGrip.sourceComponent),
                _activeGrip.inputHand,
                _activeGrip.inputSource,
                _activeGrip.inputControl,
                _activeGrip.lastInputValue,
                duration,
                _activeGrip.maxInputValue,
                isPressed: false,
                _activeGrip.toolId);
        }

        private void EmitGripEvent(
            string eventType,
            string reasonCode,
            string gameId,
            string sourceComponent,
            string inputHand,
            string inputSource,
            string inputControl,
            float inputValue,
            float gripDurationSec,
            float gripPeakValue,
            bool isPressed,
            string toolId)
        {
            if (_interactionEventBridge == null)
            {
                return;
            }

            var details = new Dictionary<string, object>
            {
                { "toolId", NormalizeOrFallback(toolId, string.Empty) },
                { "gripDurationSec", Mathf.Max(0f, gripDurationSec) },
                { "gripPeakValue", Mathf.Clamp01(gripPeakValue) },
                { "gripThreshold", Mathf.Clamp01(_pressThreshold) },
                { "holdHeartbeatIntervalSec", Mathf.Max(0.1f, _holdHeartbeatIntervalSec) },
                { "isPressed", isPressed },
            };

            var payload = new Dictionary<string, object>
            {
                { "toolEventType", NormalizeOrFallback(eventType, "TOOL_GRIP_EVENT") },
                { "actionOutcome", "OBSERVED" },
                { "reasonCode", NormalizeOrFallback(reasonCode, "TOOL_GRIP_OBSERVED") },
                { "gameId", NormalizeOrFallback(gameId, "unknown_game") },
                { "sourceComponent", NormalizeOrFallback(sourceComponent, nameof(ToolGripTracker)) },
                { "inputHand", NormalizeOrFallback(inputHand, string.Empty) },
                { "inputSource", NormalizeOrFallback(inputSource, "QUEST_POINTER") },
                { "inputControl", NormalizeOrFallback(inputControl, string.Empty) },
                { "inputValue", Mathf.Clamp01(inputValue) },
                { "isPressed", isPressed },
                { "details", details },
            };

            _interactionEventBridge.RecordToolTelemetry(payload);

            if (_logTelemetry)
            {
                Logger.Info(
                    $"[ToolGripTracker] eventType={eventType} gameId={payload["gameId"]} hand={payload["inputHand"]} duration={gripDurationSec:F3}s");
            }
        }

        private bool IsSameGrip(ToolGripSample sample)
        {
            return string.Equals(
                       _activeGrip.toolId,
                       NormalizeOrFallback(sample.toolId, "quest_pointer_tool"),
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       _activeGrip.inputHand,
                       NormalizeOrFallback(sample.inputHand, "UNKNOWN"),
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       _activeGrip.inputControl,
                       NormalizeOrFallback(sample.inputControl, "UNKNOWN"),
                       StringComparison.OrdinalIgnoreCase);
        }

        private void BeginGrip(ToolGripSample sample, float normalizedValue, float now)
        {
            _activeGrip.isActive = true;
            _activeGrip.toolId = NormalizeOrFallback(sample.toolId, "quest_pointer_tool");
            _activeGrip.gameId = NormalizeOrFallback(sample.gameId, "unknown_game");
            _activeGrip.inputHand = NormalizeOrFallback(sample.inputHand, "UNKNOWN");
            _activeGrip.inputSource = NormalizeOrFallback(sample.inputSource, "QUEST_POINTER");
            _activeGrip.inputControl = NormalizeOrFallback(sample.inputControl, "UNKNOWN");
            _activeGrip.sourceComponent = NormalizeOrFallback(sample.sourceComponent, nameof(ToolGripTracker));
            _activeGrip.startedAtRealtime = now;
            _activeGrip.maxInputValue = normalizedValue;
            _activeGrip.lastInputValue = normalizedValue;
            _activeGrip.nextHoldEventAtRealtime = now + Mathf.Max(0.1f, _holdHeartbeatIntervalSec);
        }

        private void ResetActiveGrip()
        {
            _activeGrip.isActive = false;
            _activeGrip.toolId = string.Empty;
            _activeGrip.gameId = string.Empty;
            _activeGrip.inputHand = string.Empty;
            _activeGrip.inputSource = string.Empty;
            _activeGrip.inputControl = string.Empty;
            _activeGrip.sourceComponent = nameof(ToolGripTracker);
            _activeGrip.startedAtRealtime = 0f;
            _activeGrip.nextHoldEventAtRealtime = 0f;
            _activeGrip.maxInputValue = 0f;
            _activeGrip.lastInputValue = 0f;
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

        private static string NormalizeOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? (fallback ?? string.Empty)
                : value.Trim();
        }
    }
}
