using System.Collections.Generic;
using UnityEngine;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Interactions
{
    [DisallowMultipleComponent]
    public sealed class AudioSourceProbe : MonoBehaviour
    {
        public enum AudioSourceEventKind
        {
            CueActive = 0,
            SourceSelected = 1,
            SourceSelectedInvalid = 2,
        }

        public struct AudioSourceSample
        {
            public string gameId;
            public string inputHand;
            public string inputSource;
            public string inputControl;
            public string sourceComponent;
            public string cueId;
            public string activeSourceId;
            public string activeSourceName;
            public string selectedSourceId;
            public string selectedSourceName;
            public float inputValue;
            public bool? targetValid;
            public string reasonCode;
        }

        [Header("Behavior")]
        [SerializeField] private bool _emitTelemetry = true;
        [SerializeField] private bool _logTelemetry;

        [Header("Dependencies")]
        [SerializeField] private InteractionEventBridge _interactionEventBridge;

        public static AudioSourceProbe Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (Instance != null)
            {
                return;
            }

            var existing = FindFirstObjectByType<AudioSourceProbe>();
            if (existing != null)
            {
                Instance = existing;
                return;
            }

            var host = new GameObject("AudioSourceProbe");
            host.AddComponent<AudioSourceProbe>();
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

        public void RecordEvent(AudioSourceEventKind eventKind, AudioSourceSample sample)
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
            var actionId = ResolveActionId(eventKind);

            var targetId = NormalizeOrFallback(
                sample.selectedSourceId,
                NormalizeOrFallback(sample.activeSourceId, string.Empty));
            var targetName = NormalizeOrFallback(
                sample.selectedSourceName,
                NormalizeOrFallback(sample.activeSourceName, string.Empty));

            var details = new Dictionary<string, object>
            {
                { "cueId", NormalizeOrFallback(sample.cueId, string.Empty) },
                { "activeSourceId", NormalizeOrFallback(sample.activeSourceId, string.Empty) },
                { "activeSourceName", NormalizeOrFallback(sample.activeSourceName, string.Empty) },
                { "selectedSourceId", NormalizeOrFallback(sample.selectedSourceId, string.Empty) },
                { "selectedSourceName", NormalizeOrFallback(sample.selectedSourceName, string.Empty) },
                { "targetValid", targetValid },
            };

            var payload = new Dictionary<string, object>
            {
                { "audioSourceEventType", eventType },
                { "actionId", actionId },
                { "gameId", NormalizeOrFallback(sample.gameId, "unknown_game") },
                { "sourceComponent", NormalizeOrFallback(sample.sourceComponent, nameof(AudioSourceProbe)) },
                { "inputHand", NormalizeOrFallback(sample.inputHand, string.Empty) },
                { "inputSource", NormalizeOrFallback(sample.inputSource, "AUDIO_SOURCE") },
                { "inputControl", NormalizeOrFallback(sample.inputControl, string.Empty) },
                { "inputValue", Mathf.Max(0f, sample.inputValue) },
                { "targetId", targetId },
                { "targetName", targetName },
                { "targetValid", targetValid },
                { "reasonCode", reasonCode },
                { "cueId", NormalizeOrFallback(sample.cueId, string.Empty) },
                { "activeSourceId", NormalizeOrFallback(sample.activeSourceId, string.Empty) },
                { "activeSourceName", NormalizeOrFallback(sample.activeSourceName, string.Empty) },
                { "selectedSourceId", NormalizeOrFallback(sample.selectedSourceId, string.Empty) },
                { "selectedSourceName", NormalizeOrFallback(sample.selectedSourceName, string.Empty) },
                { "details", details },
            };

            _interactionEventBridge.RecordAudioSourceTelemetry(payload);

            if (_logTelemetry)
            {
                Logger.Info(
                    $"[AudioSourceProbe] eventType={eventType} actionId={actionId} targetValid={targetValid} targetId={targetId}");
            }
        }

        public void RecordCueActive(AudioSourceSample sample)
        {
            RecordEvent(AudioSourceEventKind.CueActive, sample);
        }

        public void RecordSourceSelected(AudioSourceSample sample)
        {
            RecordEvent(AudioSourceEventKind.SourceSelected, sample);
        }

        public void RecordSourceSelectedInvalid(AudioSourceSample sample)
        {
            RecordEvent(AudioSourceEventKind.SourceSelectedInvalid, sample);
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

        private static string ResolveEventType(AudioSourceEventKind eventKind)
        {
            switch (eventKind)
            {
                case AudioSourceEventKind.CueActive:
                    return "AUDIO_SOURCE_CUE_ACTIVE";
                case AudioSourceEventKind.SourceSelected:
                    return "AUDIO_SOURCE_SELECTED";
                case AudioSourceEventKind.SourceSelectedInvalid:
                    return "AUDIO_SOURCE_SELECTED_INVALID";
                default:
                    return "AUDIO_SOURCE_EVENT";
            }
        }

        private static string ResolveActionId(AudioSourceEventKind eventKind)
        {
            switch (eventKind)
            {
                case AudioSourceEventKind.SourceSelected:
                case AudioSourceEventKind.SourceSelectedInvalid:
                    return "identify_sound_source";
                default:
                    return string.Empty;
            }
        }

        private static bool ResolveTargetValid(AudioSourceEventKind eventKind, AudioSourceSample sample)
        {
            if (sample.targetValid.HasValue)
            {
                return sample.targetValid.Value;
            }

            return eventKind != AudioSourceEventKind.SourceSelectedInvalid;
        }

        private static string ResolveReasonCode(
            AudioSourceEventKind eventKind,
            string explicitReasonCode,
            bool targetValid)
        {
            if (!string.IsNullOrWhiteSpace(explicitReasonCode))
            {
                return explicitReasonCode.Trim();
            }

            if (!targetValid)
            {
                return "AUDIO_SOURCE_MISMATCH";
            }

            switch (eventKind)
            {
                case AudioSourceEventKind.CueActive:
                    return "AUDIO_CUE_ACTIVE";
                case AudioSourceEventKind.SourceSelected:
                    return "AUDIO_SOURCE_IDENTIFIED";
                case AudioSourceEventKind.SourceSelectedInvalid:
                    return "AUDIO_SOURCE_MISMATCH";
                default:
                    return "AUDIO_SOURCE_EVENT_OBSERVED";
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
