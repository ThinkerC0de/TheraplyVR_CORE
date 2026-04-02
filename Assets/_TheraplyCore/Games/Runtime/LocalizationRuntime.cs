using System;
using System.Collections.Generic;
using UnityEngine;
using TheraplyCore.Interactions;
using GameContracts = TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Locale key/value resolver with deterministic fallback chain and telemetry.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocalizationRuntime : MonoBehaviour, GameContracts.ILocalizationService
    {
        [Serializable]
        public sealed class LocalizationEntry
        {
            public string locale = string.Empty;
            public string key = string.Empty;
            [TextArea] public string value = string.Empty;
        }

        [Header("Entries")]
        [SerializeField] private List<LocalizationEntry> _entries = new List<LocalizationEntry>();

        [Header("Fallback Policy")]
        [SerializeField] private string _defaultLocale = "en-US";
        [SerializeField] private bool _fallbackToLanguageCode = true;
        [SerializeField] private List<string> _fallbackLocales = new List<string>();

        [Header("Telemetry")]
        [SerializeField] private bool _emitLocalizationTelemetry = true;
        [SerializeField] private bool _logLocalization;
        [SerializeField] private InteractionEventBridge _interactionEventBridge;

        private readonly Dictionary<string, Dictionary<string, string>> _valuesByLocale =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private string _currentLocale = "en-US";
        private string _runtimeGameId = string.Empty;
        private string _runtimeFlowId = string.Empty;
        private string _runtimeSessionId = string.Empty;

        public event Action<string, string, string> LocalizationFallbackUsed;
        public event Action<string, string> LocalizationKeyMissing;

        public string CurrentLocale => NormalizeOrFallback(_currentLocale, NormalizeOrFallback(_defaultLocale, "en-US"));
        public string DefaultLocale => NormalizeOrFallback(_defaultLocale, "en-US");

        private void Awake()
        {
            ResolveDependencies();
            RebuildIndex();
            if (string.IsNullOrWhiteSpace(_currentLocale))
            {
                _currentLocale = DefaultLocale;
            }
        }

        public void SetRuntimeContext(string gameId, string flowId, string sessionId)
        {
            _runtimeGameId = NormalizeOrFallback(gameId, string.Empty);
            _runtimeFlowId = NormalizeOrFallback(flowId, string.Empty);
            _runtimeSessionId = NormalizeOrFallback(sessionId, string.Empty);
        }

        public void ApplyPolicy(GameContracts.LocalizationPolicy policy)
        {
            if (policy == null)
            {
                return;
            }

            _defaultLocale = NormalizeOrFallback(policy.defaultLocale, _defaultLocale);
            _fallbackToLanguageCode = policy.fallbackToLanguageCode;
            _fallbackLocales.Clear();
            if (policy.fallbackLocales != null)
            {
                for (var i = 0; i < policy.fallbackLocales.Count; i++)
                {
                    var locale = Normalize(policy.fallbackLocales[i]);
                    if (string.IsNullOrWhiteSpace(locale) || _fallbackLocales.Contains(locale))
                    {
                        continue;
                    }

                    _fallbackLocales.Add(locale);
                }
            }
        }

        public void RebuildIndex()
        {
            _valuesByLocale.Clear();

            if (_entries == null || _entries.Count <= 0)
            {
                return;
            }

            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry == null)
                {
                    continue;
                }

                var locale = Normalize(entry.locale);
                var key = Normalize(entry.key);
                if (string.IsNullOrWhiteSpace(locale) || string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!_valuesByLocale.TryGetValue(locale, out var valuesByKey) || valuesByKey == null)
                {
                    valuesByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    _valuesByLocale[locale] = valuesByKey;
                }

                valuesByKey[key] = entry.value == null ? string.Empty : entry.value;
            }
        }

        public void SetValue(string locale, string key, string value)
        {
            var normalizedLocale = Normalize(locale);
            var normalizedKey = Normalize(key);
            if (string.IsNullOrWhiteSpace(normalizedLocale) || string.IsNullOrWhiteSpace(normalizedKey))
            {
                return;
            }

            if (!_valuesByLocale.TryGetValue(normalizedLocale, out var valuesByKey) || valuesByKey == null)
            {
                valuesByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _valuesByLocale[normalizedLocale] = valuesByKey;
            }

            valuesByKey[normalizedKey] = value == null ? string.Empty : value;
        }

        public bool TrySetLocale(string locale, out string reasonCode)
        {
            reasonCode = string.Empty;
            var normalizedLocale = Normalize(locale);
            if (string.IsNullOrWhiteSpace(normalizedLocale))
            {
                reasonCode = "LOCALIZATION_LOCALE_REQUIRED";
                return false;
            }

            var fallbackChain = BuildFallbackChain(normalizedLocale);
            if (!HasAnyLocaleInChain(fallbackChain))
            {
                reasonCode = "LOCALIZATION_LOCALE_NOT_AVAILABLE";
                return false;
            }

            _currentLocale = normalizedLocale;
            return true;
        }

        public bool TryResolve(string key, out string value, out string resolvedLocale, out string reasonCode)
        {
            value = string.Empty;
            resolvedLocale = string.Empty;
            reasonCode = string.Empty;

            var normalizedKey = Normalize(key);
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                reasonCode = "LOCALIZATION_KEY_REQUIRED";
                return false;
            }

            var requestedLocale = CurrentLocale;
            var fallbackChain = BuildFallbackChain(requestedLocale);
            for (var i = 0; i < fallbackChain.Count; i++)
            {
                var candidateLocale = fallbackChain[i];
                if (string.IsNullOrWhiteSpace(candidateLocale))
                {
                    continue;
                }

                if (!_valuesByLocale.TryGetValue(candidateLocale, out var valuesByKey) || valuesByKey == null)
                {
                    continue;
                }

                if (!valuesByKey.TryGetValue(normalizedKey, out var candidateValue))
                {
                    continue;
                }

                value = candidateValue ?? string.Empty;
                resolvedLocale = candidateLocale;
                reasonCode = "LOCALIZATION_RESOLVED";

                if (!string.Equals(requestedLocale, resolvedLocale, StringComparison.OrdinalIgnoreCase))
                {
                    EmitLocalizationFallbackUsed(normalizedKey, requestedLocale, resolvedLocale);
                }

                return true;
            }

            reasonCode = "LOCALIZATION_KEY_NOT_FOUND";
            EmitLocalizationKeyMissing(normalizedKey, requestedLocale);
            return false;
        }

        public IReadOnlyList<string> BuildFallbackChain(string locale)
        {
            var chain = new List<string>();
            var primaryLocale = NormalizeOrFallback(locale, CurrentLocale);
            AddUniqueLocale(chain, primaryLocale);

            if (_fallbackToLanguageCode)
            {
                var separatorIndex = primaryLocale.IndexOf('-');
                if (separatorIndex > 0)
                {
                    AddUniqueLocale(chain, primaryLocale.Substring(0, separatorIndex));
                }
            }

            if (_fallbackLocales != null)
            {
                for (var i = 0; i < _fallbackLocales.Count; i++)
                {
                    AddUniqueLocale(chain, _fallbackLocales[i]);
                }
            }

            AddUniqueLocale(chain, DefaultLocale);
            return chain;
        }

        private bool HasAnyLocaleInChain(IReadOnlyList<string> fallbackChain)
        {
            if (fallbackChain == null || fallbackChain.Count <= 0)
            {
                return false;
            }

            for (var i = 0; i < fallbackChain.Count; i++)
            {
                if (_valuesByLocale.ContainsKey(fallbackChain[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private void EmitLocalizationFallbackUsed(string key, string requestedLocale, string resolvedLocale)
        {
            LocalizationFallbackUsed?.Invoke(key, requestedLocale, resolvedLocale);
            EmitLocalizationEvent(
                "localization_fallback_used",
                "LOCALIZATION_FALLBACK_USED",
                new Dictionary<string, object>
                {
                    { "key", NormalizeOrFallback(key, string.Empty) },
                    { "requestedLocale", NormalizeOrFallback(requestedLocale, string.Empty) },
                    { "resolvedLocale", NormalizeOrFallback(resolvedLocale, string.Empty) },
                    { "currentLocale", CurrentLocale },
                });
        }

        private void EmitLocalizationKeyMissing(string key, string requestedLocale)
        {
            LocalizationKeyMissing?.Invoke(key, requestedLocale);
            EmitLocalizationEvent(
                "localization_key_missing",
                "LOCALIZATION_KEY_NOT_FOUND",
                new Dictionary<string, object>
                {
                    { "key", NormalizeOrFallback(key, string.Empty) },
                    { "requestedLocale", NormalizeOrFallback(requestedLocale, string.Empty) },
                    { "fallbackChain", string.Join("|", BuildFallbackChain(requestedLocale)) },
                    { "currentLocale", CurrentLocale },
                });
        }

        private void EmitLocalizationEvent(
            string eventName,
            string reasonCode,
            IReadOnlyDictionary<string, object> details)
        {
            if (!_emitLocalizationTelemetry || _interactionEventBridge == null)
            {
                return;
            }

            var payload = new Dictionary<string, object>
            {
                { "flowId", NormalizeOrFallback(_runtimeFlowId, "session_flow") },
                { "eventType", NormalizeOrFallback(eventName, "localization_event") },
                { "reasonCode", NormalizeOrFallback(reasonCode, "LOCALIZATION_EVENT") },
                { "locale", CurrentLocale },
                { "defaultLocale", DefaultLocale },
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
                nameof(LocalizationRuntime));

            if (_logLocalization)
            {
                Logger.Info(
                    "[LocalizationRuntime] event=" + NormalizeOrFallback(eventName, string.Empty) +
                    " reasonCode=" + NormalizeOrFallback(reasonCode, string.Empty) +
                    " locale=" + CurrentLocale);
            }
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

        private static void AddUniqueLocale(ICollection<string> chain, string locale)
        {
            var normalizedLocale = Normalize(locale);
            if (string.IsNullOrWhiteSpace(normalizedLocale) || chain.Contains(normalizedLocale))
            {
                return;
            }

            chain.Add(normalizedLocale);
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
