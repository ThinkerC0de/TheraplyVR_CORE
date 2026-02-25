using System;
using System.Collections.Generic;
using UnityEngine;
using TheraplyCore.Interactions;
using GameContracts = TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Narrator runtime for speech queue, priority interrupts, animation, and attachments.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NarratorRuntime : MonoBehaviour, GameContracts.INarratorService
    {
        private sealed class PlaybackRequest
        {
            public GameContracts.NarratorLineRequest request;
            public string lineKey = string.Empty;
            public string resolvedText = string.Empty;
            public string resolvedLocale = string.Empty;
            public string audioBindingKey = string.Empty;
            public AudioSource audioSource;
            public float startedAtSec;
            public float expectedEndAtSec;
            public bool waitForAudioPlaybackEnd;
        }

        [Header("Dependencies")]
        [SerializeField] private FlowBindingRegistry _flowBindingRegistry;
        [SerializeField] private LocalizationRuntime _localizationRuntime;
        [SerializeField] private AudioSource _fallbackNarratorAudioSource;
        [SerializeField] private InteractionEventBridge _interactionEventBridge;

        [Header("Narrator Actor")]
        [SerializeField] private string _defaultActorBindingKey = string.Empty;

        [Header("Behavior")]
        [SerializeField] private bool _stopAudioOnInterrupt = true;
        [SerializeField] private bool _emitNarratorTelemetry = true;
        [SerializeField] private bool _logNarrator;

        private readonly List<PlaybackRequest> _queue = new List<PlaybackRequest>();
        private PlaybackRequest _active;
        private string _runtimeGameId = string.Empty;
        private string _runtimeFlowId = string.Empty;
        private string _runtimeSessionId = string.Empty;

        public event Action<string> LineStarted;
        public event Action<string> LineCompleted;
        public event Action<string, string> LineInterrupted;

        public bool IsSpeaking => _active != null;
        public string ActiveLocale => _localizationRuntime == null
            ? string.Empty
            : NormalizeOrFallback(_localizationRuntime.CurrentLocale, string.Empty);
        public int PendingCount => _queue.Count;
        public string ActiveLineKey => _active == null ? string.Empty : NormalizeOrFallback(_active.lineKey, string.Empty);

        private void Awake()
        {
            ResolveDependencies();
        }

        private void Update()
        {
            TickRuntime(Time.realtimeSinceStartup);
        }

        public void SetRuntimeContext(string gameId, string flowId, string sessionId)
        {
            _runtimeGameId = NormalizeOrFallback(gameId, string.Empty);
            _runtimeFlowId = NormalizeOrFallback(flowId, string.Empty);
            _runtimeSessionId = NormalizeOrFallback(sessionId, string.Empty);

            if (_localizationRuntime != null)
            {
                _localizationRuntime.SetRuntimeContext(gameId, flowId, sessionId);
            }
        }

        public void TickRuntime(float nowElapsedSec)
        {
            if (_active == null)
            {
                return;
            }

            var now = Mathf.Max(0f, nowElapsedSec);
            var shouldComplete = false;
            if (_active.waitForAudioPlaybackEnd && _active.audioSource != null)
            {
                shouldComplete =
                    !_active.audioSource.isPlaying &&
                    now >= _active.expectedEndAtSec - 0.05f;
            }
            else
            {
                shouldComplete = now >= _active.expectedEndAtSec;
            }

            if (!shouldComplete)
            {
                return;
            }

            CompleteActiveLine(now, "NARRATOR_LINE_COMPLETED");
        }

        public bool TrySpeak(GameContracts.NarratorLineRequest request, out string reasonCode)
        {
            reasonCode = string.Empty;
            ResolveDependencies();

            if (request == null)
            {
                reasonCode = "NARRATOR_REQUEST_NULL";
                return false;
            }

            var normalizedLineKey = Normalize(request.lineKey);
            if (string.IsNullOrWhiteSpace(normalizedLineKey))
            {
                reasonCode = "NARRATOR_LINE_KEY_REQUIRED";
                return false;
            }

            if (!TryBuildPlaybackRequest(request, out var playbackRequest, out reasonCode))
            {
                return false;
            }

            if (_active != null)
            {
                var canInterrupt =
                    request.interruptIfBusy &&
                    request.priority >= (_active.request == null ? 0 : _active.request.priority);

                if (!canInterrupt)
                {
                    EnqueuePlayback(playbackRequest);
                    return true;
                }

                InterruptActiveLine("NARRATOR_INTERRUPTED_BY_PRIORITY");
            }

            StartPlayback(playbackRequest, Time.realtimeSinceStartup, startedFromQueue: false);
            return true;
        }

        public bool TrySpeakSequence(IReadOnlyList<GameContracts.NarratorLineRequest> requests, out string reasonCode)
        {
            reasonCode = string.Empty;
            if (requests == null || requests.Count <= 0)
            {
                reasonCode = "NARRATOR_SEQUENCE_EMPTY";
                return false;
            }

            for (var i = 0; i < requests.Count; i++)
            {
                if (requests[i] == null)
                {
                    continue;
                }

                if (!TrySpeak(requests[i], out reasonCode))
                {
                    return false;
                }
            }

            reasonCode = string.Empty;
            return true;
        }

        public bool TryPlayAnimation(string actorBindingKey, string trigger, out string reasonCode)
        {
            reasonCode = string.Empty;
            ResolveDependencies();

            var normalizedTrigger = Normalize(trigger);
            if (string.IsNullOrWhiteSpace(normalizedTrigger))
            {
                reasonCode = "NARRATOR_ANIMATION_TRIGGER_REQUIRED";
                return false;
            }

            var normalizedActorBindingKey = NormalizeOrFallback(actorBindingKey, Normalize(_defaultActorBindingKey));
            if (string.IsNullOrWhiteSpace(normalizedActorBindingKey))
            {
                reasonCode = "NARRATOR_ACTOR_BINDING_REQUIRED";
                return false;
            }

            if (!TryResolveBoundObject(normalizedActorBindingKey, out var actorObject, out reasonCode))
            {
                return false;
            }

            var animator = actorObject.GetComponent<Animator>();
            if (animator == null)
            {
                animator = actorObject.GetComponentInChildren<Animator>(includeInactive: true);
            }

            if (animator == null)
            {
                reasonCode = "NARRATOR_ACTOR_ANIMATOR_MISSING";
                return false;
            }

            animator.SetTrigger(normalizedTrigger);
            return true;
        }

        public bool TrySetAttachment(string attachmentBindingKey, bool visible, out string reasonCode)
        {
            reasonCode = string.Empty;
            ResolveDependencies();

            var normalizedAttachmentBindingKey = Normalize(attachmentBindingKey);
            if (string.IsNullOrWhiteSpace(normalizedAttachmentBindingKey))
            {
                reasonCode = "NARRATOR_ATTACHMENT_BINDING_REQUIRED";
                return false;
            }

            if (!TryResolveBoundObject(normalizedAttachmentBindingKey, out var attachmentObject, out reasonCode))
            {
                return false;
            }

            attachmentObject.SetActive(visible);
            return true;
        }

        public bool TryInterrupt(string reasonCode = "NARRATOR_INTERRUPTED")
        {
            if (_active == null)
            {
                return false;
            }

            InterruptActiveLine(NormalizeOrFallback(reasonCode, "NARRATOR_INTERRUPTED"));
            StartNextQueuedLine(Time.realtimeSinceStartup);
            return true;
        }

        private bool TryBuildPlaybackRequest(
            GameContracts.NarratorLineRequest request,
            out PlaybackRequest playbackRequest,
            out string reasonCode)
        {
            playbackRequest = null;
            reasonCode = string.Empty;

            var lineKey = Normalize(request.lineKey);
            if (string.IsNullOrWhiteSpace(lineKey))
            {
                reasonCode = "NARRATOR_LINE_KEY_REQUIRED";
                return false;
            }

            var resolvedText = ResolveNarratorText(request, lineKey, out var resolvedLocale);
            var resolvedAudioBindingKey = ResolveAudioBindingKey(request, lineKey, out var audioResolvedLocale);
            if (string.IsNullOrWhiteSpace(resolvedLocale) && !string.IsNullOrWhiteSpace(audioResolvedLocale))
            {
                resolvedLocale = audioResolvedLocale;
            }

            var resolvedAudioSource = ResolveAudioSource(resolvedAudioBindingKey);
            if (resolvedAudioSource == null)
            {
                resolvedAudioSource = _fallbackNarratorAudioSource;
            }

            var hasAudio = resolvedAudioSource != null && resolvedAudioSource.clip != null;
            if (!hasAudio && string.IsNullOrWhiteSpace(resolvedText))
            {
                reasonCode = "NARRATOR_LINE_CONTENT_MISSING";
                return false;
            }

            playbackRequest = new PlaybackRequest
            {
                request = request,
                lineKey = lineKey,
                resolvedText = resolvedText,
                resolvedLocale = NormalizeOrFallback(resolvedLocale, ActiveLocale),
                audioBindingKey = NormalizeOrFallback(resolvedAudioBindingKey, string.Empty),
                audioSource = resolvedAudioSource,
            };
            return true;
        }

        private void EnqueuePlayback(PlaybackRequest playbackRequest)
        {
            if (playbackRequest == null)
            {
                return;
            }

            var insertAt = _queue.Count;
            for (var i = 0; i < _queue.Count; i++)
            {
                var queuedPriority = _queue[i].request == null ? 0 : _queue[i].request.priority;
                var nextPriority = playbackRequest.request == null ? 0 : playbackRequest.request.priority;
                if (nextPriority > queuedPriority)
                {
                    insertAt = i;
                    break;
                }
            }

            _queue.Insert(insertAt, playbackRequest);
        }

        private void StartPlayback(PlaybackRequest playbackRequest, float nowElapsedSec, bool startedFromQueue)
        {
            if (playbackRequest == null)
            {
                return;
            }

            var now = Mathf.Max(0f, nowElapsedSec);
            playbackRequest.startedAtSec = now;

            var simulatedDurationSec = Mathf.Max(0f, playbackRequest.request == null ? 0f : playbackRequest.request.simulatedDurationSec);
            var hasAudioClip = playbackRequest.audioSource != null && playbackRequest.audioSource.clip != null;
            var clipLengthSec = hasAudioClip ? Mathf.Max(0f, playbackRequest.audioSource.clip.length) : 0f;
            var durationSec = simulatedDurationSec > 0f ? simulatedDurationSec : clipLengthSec;
            playbackRequest.expectedEndAtSec = now + durationSec;
            playbackRequest.waitForAudioPlaybackEnd = simulatedDurationSec <= 0f && hasAudioClip;

            _active = playbackRequest;

            if (hasAudioClip && playbackRequest.audioSource != null)
            {
                playbackRequest.audioSource.Play();
            }

            EmitNarratorEvent(
                "narrator_line_started",
                "NARRATOR_LINE_STARTED",
                playbackRequest,
                new Dictionary<string, object>
                {
                    { "startedFromQueue", startedFromQueue },
                    { "hasAudio", hasAudioClip },
                    { "hasText", !string.IsNullOrWhiteSpace(playbackRequest.resolvedText) },
                    { "priority", playbackRequest.request == null ? 0 : playbackRequest.request.priority },
                    { "pendingCount", _queue.Count },
                });

            LineStarted?.Invoke(playbackRequest.lineKey);

            if (durationSec <= 0.0001f && !playbackRequest.waitForAudioPlaybackEnd)
            {
                CompleteActiveLine(now, "NARRATOR_LINE_COMPLETED_IMMEDIATE");
            }
        }

        private void CompleteActiveLine(float nowElapsedSec, string reasonCode)
        {
            if (_active == null)
            {
                return;
            }

            var completed = _active;
            _active = null;

            EmitNarratorEvent(
                "narrator_line_completed",
                NormalizeOrFallback(reasonCode, "NARRATOR_LINE_COMPLETED"),
                completed,
                new Dictionary<string, object>
                {
                    { "elapsedSec", Mathf.Max(0f, nowElapsedSec - completed.startedAtSec) },
                    { "pendingCount", _queue.Count },
                });

            LineCompleted?.Invoke(completed.lineKey);
            StartNextQueuedLine(nowElapsedSec);
        }

        private void InterruptActiveLine(string reasonCode)
        {
            if (_active == null)
            {
                return;
            }

            var interrupted = _active;
            if (_stopAudioOnInterrupt &&
                interrupted.audioSource != null &&
                interrupted.audioSource.isPlaying)
            {
                interrupted.audioSource.Stop();
            }

            _active = null;

            EmitNarratorEvent(
                "narrator_interrupted",
                NormalizeOrFallback(reasonCode, "NARRATOR_INTERRUPTED"),
                interrupted,
                new Dictionary<string, object>
                {
                    { "pendingCount", _queue.Count },
                });

            LineInterrupted?.Invoke(interrupted.lineKey, NormalizeOrFallback(reasonCode, "NARRATOR_INTERRUPTED"));
        }

        private void StartNextQueuedLine(float nowElapsedSec)
        {
            if (_active != null || _queue.Count <= 0)
            {
                return;
            }

            var next = _queue[0];
            _queue.RemoveAt(0);
            StartPlayback(next, nowElapsedSec, startedFromQueue: true);
        }

        private void EmitNarratorEvent(
            string eventName,
            string reasonCode,
            PlaybackRequest playbackRequest,
            IReadOnlyDictionary<string, object> details)
        {
            if (!_emitNarratorTelemetry || _interactionEventBridge == null)
            {
                return;
            }

            var payload = new Dictionary<string, object>
            {
                { "flowId", NormalizeOrFallback(_runtimeFlowId, "session_flow") },
                { "eventType", NormalizeOrFallback(eventName, "narrator_event") },
                { "reasonCode", NormalizeOrFallback(reasonCode, "NARRATOR_EVENT") },
                { "lineKey", playbackRequest == null ? string.Empty : NormalizeOrFallback(playbackRequest.lineKey, string.Empty) },
                { "resolvedLocale", playbackRequest == null ? string.Empty : NormalizeOrFallback(playbackRequest.resolvedLocale, string.Empty) },
                { "text", playbackRequest == null ? string.Empty : NormalizeOrFallback(playbackRequest.resolvedText, string.Empty) },
                { "audioBindingKey", playbackRequest == null ? string.Empty : NormalizeOrFallback(playbackRequest.audioBindingKey, string.Empty) },
                { "payloadVersion", 1 },
                { "monotonicSec", Time.realtimeSinceStartup },
                { "actionOutcome", "OBSERVED" },
            };

            if (details != null)
            {
                foreach (var kv in details)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key))
                    {
                        continue;
                    }

                    payload[kv.Key] = kv.Value;
                }
            }

            _interactionEventBridge.RecordGameplayEvent(
                NormalizeOrFallback(_runtimeGameId, "session_flow"),
                eventName,
                string.Empty,
                payload,
                nameof(NarratorRuntime));

            if (_logNarrator)
            {
                Logger.Info(
                    "[NarratorRuntime] event=" + NormalizeOrFallback(eventName, string.Empty) +
                    " lineKey=" + (playbackRequest == null ? string.Empty : NormalizeOrFallback(playbackRequest.lineKey, string.Empty)) +
                    " reasonCode=" + NormalizeOrFallback(reasonCode, string.Empty));
            }
        }

        private string ResolveNarratorText(
            GameContracts.NarratorLineRequest request,
            string lineKey,
            out string resolvedLocale)
        {
            resolvedLocale = string.Empty;
            if (_localizationRuntime != null)
            {
                var primaryTextKey = NormalizeOrFallback(request == null ? string.Empty : request.textKey, lineKey + ".text");
                if (_localizationRuntime.TryResolve(primaryTextKey, out var localizedText, out resolvedLocale, out _))
                {
                    return NormalizeOrFallback(localizedText, string.Empty);
                }

                if (_localizationRuntime.TryResolve(lineKey, out localizedText, out resolvedLocale, out _))
                {
                    return NormalizeOrFallback(localizedText, string.Empty);
                }
            }

            return NormalizeOrFallback(request == null ? string.Empty : request.fallbackText, string.Empty);
        }

        private string ResolveAudioBindingKey(
            GameContracts.NarratorLineRequest request,
            string lineKey,
            out string resolvedLocale)
        {
            resolvedLocale = string.Empty;

            var explicitBinding = Normalize(request == null ? string.Empty : request.audioBindingKey);
            if (!string.IsNullOrWhiteSpace(explicitBinding))
            {
                return explicitBinding;
            }

            if (_localizationRuntime != null &&
                _localizationRuntime.TryResolve(lineKey + ".audio_binding", out var localizedBinding, out resolvedLocale, out _))
            {
                return NormalizeOrFallback(localizedBinding, string.Empty);
            }

            return lineKey;
        }

        private AudioSource ResolveAudioSource(string audioBindingKey)
        {
            if (_flowBindingRegistry == null || string.IsNullOrWhiteSpace(audioBindingKey))
            {
                return null;
            }

            if (_flowBindingRegistry.TryGetAudio(audioBindingKey, out var audioSource))
            {
                return audioSource;
            }

            return null;
        }

        private bool TryResolveBoundObject(string bindingKey, out GameObject value, out string reasonCode)
        {
            value = null;
            reasonCode = string.Empty;
            if (_flowBindingRegistry == null)
            {
                reasonCode = "FLOW_BINDING_REGISTRY_MISSING";
                return false;
            }

            if (!_flowBindingRegistry.TryGetObject(bindingKey, out value) || value == null)
            {
                reasonCode = "NARRATOR_BINDING_NOT_FOUND";
                return false;
            }

            return true;
        }

        private void ResolveDependencies()
        {
            if (_flowBindingRegistry == null)
            {
                _flowBindingRegistry = GetComponent<FlowBindingRegistry>();
                if (_flowBindingRegistry == null)
                {
                    _flowBindingRegistry = FindFirstObjectByType<FlowBindingRegistry>();
                }
            }

            if (_localizationRuntime == null)
            {
                _localizationRuntime = GetComponent<LocalizationRuntime>();
                if (_localizationRuntime == null)
                {
                    _localizationRuntime = FindFirstObjectByType<LocalizationRuntime>();
                }
            }

            if (_interactionEventBridge == null)
            {
                _interactionEventBridge = InteractionEventBridge.Instance;
                if (_interactionEventBridge == null)
                {
                    _interactionEventBridge = FindFirstObjectByType<InteractionEventBridge>();
                }
            }

            if (_fallbackNarratorAudioSource == null)
            {
                _fallbackNarratorAudioSource = GetComponent<AudioSource>();
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string NormalizeOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? (fallback ?? string.Empty)
                : value.Trim();
        }
    }
}
