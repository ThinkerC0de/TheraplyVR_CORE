using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using GameContracts = TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Keyed scene binding registry used by flow runtime.
    /// Supports lookup for object, zone, audio, and timeline binding keys.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlowBindingRegistry : MonoBehaviour
    {
        [Serializable]
        public sealed class ObjectBindingEntry
        {
            public string key = string.Empty;
            public GameObject value;
        }

        [Serializable]
        public sealed class AudioBindingEntry
        {
            public string key = string.Empty;
            public AudioSource value;
        }

        [Serializable]
        public sealed class TimelineBindingEntry
        {
            public string key = string.Empty;
            public PlayableDirector value;
        }

        [Header("Bindings")]
        [SerializeField] private List<ObjectBindingEntry> _objectBindings = new List<ObjectBindingEntry>();
        [SerializeField] private List<ObjectBindingEntry> _zoneBindings = new List<ObjectBindingEntry>();
        [SerializeField] private List<AudioBindingEntry> _audioBindings = new List<AudioBindingEntry>();
        [SerializeField] private List<TimelineBindingEntry> _timelineBindings = new List<TimelineBindingEntry>();

        [Header("Validation")]
        [SerializeField] private bool _rebuildOnAwake = true;
        [SerializeField] private bool _logValidationWarnings;

        private readonly Dictionary<string, GameObject> _objectsByKey =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GameObject> _zonesByKey =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AudioSource> _audioByKey =
            new Dictionary<string, AudioSource>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PlayableDirector> _timelineByKey =
            new Dictionary<string, PlayableDirector>(StringComparer.OrdinalIgnoreCase);

        public int ObjectBindingCount => _objectsByKey.Count;
        public int ZoneBindingCount => _zonesByKey.Count;
        public int AudioBindingCount => _audioByKey.Count;
        public int TimelineBindingCount => _timelineByKey.Count;

        private void Awake()
        {
            if (_rebuildOnAwake)
            {
                RebuildIndex();
            }
        }

        public void RebuildIndex()
        {
            _objectsByKey.Clear();
            _zonesByKey.Clear();
            _audioByKey.Clear();
            _timelineByKey.Clear();

            IndexObjectBindings(_objectBindings, _objectsByKey, "OBJECT");
            IndexObjectBindings(_zoneBindings, _zonesByKey, "ZONE");
            IndexAudioBindings(_audioBindings, _audioByKey);
            IndexTimelineBindings(_timelineBindings, _timelineByKey);
        }

        public bool TryGetObject(string key, out GameObject value)
        {
            return TryGetFromDictionary(_objectsByKey, key, out value);
        }

        public bool TryGetZone(string key, out GameObject value)
        {
            return TryGetFromDictionary(_zonesByKey, key, out value);
        }

        public bool TryGetAudio(string key, out AudioSource value)
        {
            return TryGetFromDictionary(_audioByKey, key, out value);
        }

        public bool TryGetTimeline(string key, out PlayableDirector value)
        {
            return TryGetFromDictionary(_timelineByKey, key, out value);
        }

        public bool TryValidateAgainstDefinition(GameContracts.GameDefinition definition, out string reasonCode)
        {
            reasonCode = string.Empty;

            if (definition == null)
            {
                reasonCode = "DEFINITION_NULL";
                return false;
            }

            RebuildIndex();

            if (!ValidateBindingList(definition.bindings == null ? null : definition.bindings.objects, _objectsByKey, "OBJECT_KEY_MISSING", out reasonCode))
            {
                return false;
            }

            if (!ValidateBindingList(definition.bindings == null ? null : definition.bindings.zones, _zonesByKey, "ZONE_KEY_MISSING", out reasonCode))
            {
                return false;
            }

            if (!ValidateBindingList(definition.bindings == null ? null : definition.bindings.audio, _audioByKey, "AUDIO_KEY_MISSING", out reasonCode))
            {
                return false;
            }

            if (!ValidateBindingList(definition.bindings == null ? null : definition.bindings.timeline, _timelineByKey, "TIMELINE_KEY_MISSING", out reasonCode))
            {
                return false;
            }

            return true;
        }

        private void IndexObjectBindings(
            List<ObjectBindingEntry> source,
            Dictionary<string, GameObject> target,
            string bindingKind)
        {
            if (source == null)
            {
                return;
            }

            for (var i = 0; i < source.Count; i++)
            {
                var entry = source[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.key))
                {
                    MaybeWarn($"{bindingKind}_KEY_REQUIRED");
                    continue;
                }

                var normalizedKey = entry.key.Trim();
                if (target.ContainsKey(normalizedKey))
                {
                    MaybeWarn($"{bindingKind}_KEY_DUPLICATE:{normalizedKey}");
                    continue;
                }

                target[normalizedKey] = entry.value;
            }
        }

        private void IndexAudioBindings(
            List<AudioBindingEntry> source,
            Dictionary<string, AudioSource> target)
        {
            if (source == null)
            {
                return;
            }

            for (var i = 0; i < source.Count; i++)
            {
                var entry = source[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.key))
                {
                    MaybeWarn("AUDIO_KEY_REQUIRED");
                    continue;
                }

                var normalizedKey = entry.key.Trim();
                if (target.ContainsKey(normalizedKey))
                {
                    MaybeWarn("AUDIO_KEY_DUPLICATE:" + normalizedKey);
                    continue;
                }

                target[normalizedKey] = entry.value;
            }
        }

        private void IndexTimelineBindings(
            List<TimelineBindingEntry> source,
            Dictionary<string, PlayableDirector> target)
        {
            if (source == null)
            {
                return;
            }

            for (var i = 0; i < source.Count; i++)
            {
                var entry = source[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.key))
                {
                    MaybeWarn("TIMELINE_KEY_REQUIRED");
                    continue;
                }

                var normalizedKey = entry.key.Trim();
                if (target.ContainsKey(normalizedKey))
                {
                    MaybeWarn("TIMELINE_KEY_DUPLICATE:" + normalizedKey);
                    continue;
                }

                target[normalizedKey] = entry.value;
            }
        }

        private static bool TryGetFromDictionary<TValue>(
            IReadOnlyDictionary<string, TValue> source,
            string key,
            out TValue value)
        {
            value = default(TValue);
            if (source == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            return source.TryGetValue(key.Trim(), out value);
        }

        private static bool ValidateBindingList<TValue>(
            List<GameContracts.BindingReference> expectedBindings,
            IReadOnlyDictionary<string, TValue> indexedBindings,
            string missingReasonCode,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            if (expectedBindings == null || expectedBindings.Count == 0)
            {
                return true;
            }

            if (indexedBindings == null)
            {
                reasonCode = missingReasonCode;
                return false;
            }

            for (var i = 0; i < expectedBindings.Count; i++)
            {
                var expected = expectedBindings[i];
                if (expected == null || string.IsNullOrWhiteSpace(expected.key))
                {
                    reasonCode = "BINDING_KEY_REQUIRED";
                    return false;
                }

                if (!indexedBindings.ContainsKey(expected.key.Trim()))
                {
                    reasonCode = missingReasonCode;
                    return false;
                }
            }

            return true;
        }

        private void MaybeWarn(string reasonCode)
        {
            if (_logValidationWarnings)
            {
                Logger.Warning("[FlowBindingRegistry] " + reasonCode);
            }
        }
    }
}
