using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using TheraplyCore.Interactions;
using GameContracts = TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Date-driven rule engine for seasonal/profile events with deterministic activation and telemetry.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CalendarRuntime : MonoBehaviour, GameContracts.ICalendarService
    {
        private sealed class DefaultCalendarTimeSource : GameContracts.ICalendarTimeSource
        {
            public DateTime UtcNow => DateTime.UtcNow;
        }

        private sealed class ActiveEventCandidate
        {
            public string eventId = string.Empty;
            public string variantGroup = string.Empty;
            public int priority;
        }

        [Header("Policy")]
        [SerializeField] private GameContracts.CalendarPolicy _policy = new GameContracts.CalendarPolicy();

        [Header("Runtime")]
        [SerializeField] private float _evaluationIntervalSec = 1f;
        [SerializeField] private bool _enableOverrideUtc;
        [SerializeField] private string _overrideUtcIso = string.Empty;

        [Header("Telemetry")]
        [SerializeField] private bool _emitCalendarTelemetry = true;
        [SerializeField] private bool _logCalendar;
        [SerializeField] private InteractionEventBridge _interactionEventBridge;

        private readonly Dictionary<string, GameContracts.CalendarRuleDefinition> _rulesById =
            new Dictionary<string, GameContracts.CalendarRuleDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GameContracts.CalendarEventDefinition> _eventsById =
            new Dictionary<string, GameContracts.CalendarEventDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _profileDatesByKey =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _activeEventByVariantGroup =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _activeEventIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly GameContracts.ICalendarTimeSource _defaultTimeSource = new DefaultCalendarTimeSource();
        private GameContracts.ICalendarTimeSource _timeSource;

        private string _runtimeGameId = string.Empty;
        private string _runtimeFlowId = string.Empty;
        private string _runtimeSessionId = string.Empty;
        private bool _overrideUtcActive;
        private DateTime _overrideUtc;
        private float _lastEvaluationRealtimeSec = -1f;

        public string ActiveTimezoneId => ResolvePolicyTimezoneId();

        public DateTime CurrentUtc
        {
            get
            {
                if (_overrideUtcActive)
                {
                    return _overrideUtc;
                }

                return (_timeSource ?? _defaultTimeSource).UtcNow;
            }
        }

        private void Awake()
        {
            ResolveDependencies();
            SetTimeSource(null);
            RebuildPolicyIndex();

            if (_enableOverrideUtc && !string.IsNullOrWhiteSpace(_overrideUtcIso))
            {
                TrySetOverrideUtc(_overrideUtcIso, out _);
            }

            RefreshActiveEvents(emitTransitions: false, emitRuleTelemetry: false);
        }

        public void SetRuntimeContext(string gameId, string flowId, string sessionId)
        {
            _runtimeGameId = NormalizeOrFallback(gameId, string.Empty);
            _runtimeFlowId = NormalizeOrFallback(flowId, string.Empty);
            _runtimeSessionId = NormalizeOrFallback(sessionId, string.Empty);
        }

        public void SetTimeSource(GameContracts.ICalendarTimeSource timeSource)
        {
            _timeSource = timeSource ?? _defaultTimeSource;
        }

        public bool TryApplyPolicy(
            GameContracts.CalendarPolicy policy,
            IReadOnlyDictionary<string, string> profileDatesByKey,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            _policy = ClonePolicy(policy ?? new GameContracts.CalendarPolicy());
            RebuildPolicyIndex();
            LoadProfileDates(profileDatesByKey);

            if (!TryResolveTimezone(string.Empty, out _, out var timezoneReasonCode))
            {
                reasonCode = timezoneReasonCode;
                return false;
            }

            if (_overrideUtcActive && !(_policy != null && _policy.allowQaTimeOverride))
            {
                _overrideUtcActive = false;
            }

            RefreshActiveEvents(emitTransitions: false, emitRuleTelemetry: false);
            return true;
        }

        public bool TrySetOverrideUtc(string utcIso, out string reasonCode)
        {
            reasonCode = string.Empty;
            if (_policy != null && !_policy.allowQaTimeOverride)
            {
                reasonCode = "CALENDAR_OVERRIDE_DISABLED";
                return false;
            }

            if (!TryParseDateTime(utcIso, out var parsed, out reasonCode))
            {
                return false;
            }

            _overrideUtc = EnsureUtc(parsed);
            _overrideUtcActive = true;
            _enableOverrideUtc = true;
            _overrideUtcIso = _overrideUtc.ToString("O", CultureInfo.InvariantCulture);
            RefreshActiveEvents(emitTransitions: true, emitRuleTelemetry: true);
            return true;
        }

        public void ClearOverrideUtc()
        {
            _overrideUtcActive = false;
            _enableOverrideUtc = false;
            _overrideUtcIso = string.Empty;
        }

        public void TickRuntime()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var intervalSec = Mathf.Max(0f, _evaluationIntervalSec);
            var nowRealtime = Time.realtimeSinceStartup;
            if (intervalSec > 0f &&
                _lastEvaluationRealtimeSec >= 0f &&
                nowRealtime - _lastEvaluationRealtimeSec < intervalSec)
            {
                return;
            }

            _lastEvaluationRealtimeSec = nowRealtime;
            RefreshActiveEvents(emitTransitions: true, emitRuleTelemetry: true);
        }

        public bool TryEvaluateEventActive(string eventId, out bool isActive, out string reasonCode)
        {
            isActive = false;
            reasonCode = string.Empty;

            var normalizedEventId = Normalize(eventId);
            if (string.IsNullOrWhiteSpace(normalizedEventId))
            {
                reasonCode = "CALENDAR_EVENT_ID_REQUIRED";
                return false;
            }

            if (!_eventsById.TryGetValue(normalizedEventId, out var eventDefinition) || eventDefinition == null)
            {
                reasonCode = "CALENDAR_EVENT_NOT_FOUND";
                return false;
            }

            if (!TryBuildActiveEventSnapshot(
                    CurrentUtc,
                    emitRuleTelemetry: false,
                    out var activeEventIds,
                    out _,
                    out reasonCode))
            {
                return false;
            }

            isActive = activeEventIds.Contains(normalizedEventId);
            reasonCode = isActive ? "CALENDAR_EVENT_ACTIVE" : "CALENDAR_EVENT_INACTIVE";
            return true;
        }

        public bool TryEvaluateDateWindow(
            string start,
            string end,
            bool yearlyRecurring,
            string timezoneId,
            out bool matched,
            out string reasonCode)
        {
            return TryEvaluateDateWindowInternal(
                CurrentUtc,
                start,
                end,
                yearlyRecurring,
                timezoneId,
                out matched,
                out reasonCode);
        }

        public bool TryEvaluateProfileDate(
            string profileDateKey,
            int daysBefore,
            int daysAfter,
            string timezoneId,
            out bool matched,
            out string reasonCode)
        {
            return TryEvaluateProfileDateInternal(
                CurrentUtc,
                profileDateKey,
                daysBefore,
                daysAfter,
                timezoneId,
                out matched,
                out reasonCode);
        }

        private bool TryBuildActiveEventSnapshot(
            DateTime nowUtc,
            bool emitRuleTelemetry,
            out HashSet<string> activeEventIds,
            out Dictionary<string, string> activeEventByVariantGroup,
            out string reasonCode)
        {
            activeEventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            activeEventByVariantGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            reasonCode = string.Empty;

            if (_eventsById.Count <= 0)
            {
                return true;
            }

            var selectedByGroup = new Dictionary<string, ActiveEventCandidate>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _eventsById)
            {
                var eventDefinition = pair.Value;
                if (eventDefinition == null)
                {
                    continue;
                }

                if (!EvaluateEventDefinition(
                        eventDefinition,
                        nowUtc,
                        emitRuleTelemetry,
                        out var eventActive,
                        out var evaluationReasonCode))
                {
                    if (string.IsNullOrWhiteSpace(reasonCode))
                    {
                        reasonCode = evaluationReasonCode;
                    }
                    continue;
                }

                if (!eventActive)
                {
                    continue;
                }

                var eventId = NormalizeOrFallback(eventDefinition.eventId, string.Empty);
                var variantGroup = NormalizeOrFallback(eventDefinition.variantGroup, string.Empty);
                if (string.IsNullOrWhiteSpace(variantGroup))
                {
                    activeEventIds.Add(eventId);
                    continue;
                }

                if (!selectedByGroup.TryGetValue(variantGroup, out var existingCandidate))
                {
                    selectedByGroup[variantGroup] = new ActiveEventCandidate
                    {
                        eventId = eventId,
                        variantGroup = variantGroup,
                        priority = eventDefinition.priority,
                    };
                    continue;
                }

                var shouldReplace = ShouldReplaceByConflictPolicy(
                    incomingPriority: eventDefinition.priority,
                    existingPriority: existingCandidate.priority);
                if (!shouldReplace)
                {
                    continue;
                }

                selectedByGroup[variantGroup] = new ActiveEventCandidate
                {
                    eventId = eventId,
                    variantGroup = variantGroup,
                    priority = eventDefinition.priority,
                };
            }

            foreach (var selected in selectedByGroup.Values)
            {
                activeEventIds.Add(selected.eventId);
                activeEventByVariantGroup[selected.variantGroup] = selected.eventId;
            }

            return true;
        }

        private void RefreshActiveEvents(bool emitTransitions, bool emitRuleTelemetry)
        {
            if (!TryBuildActiveEventSnapshot(
                    CurrentUtc,
                    emitRuleTelemetry,
                    out var snapshotActiveEvents,
                    out var snapshotVariantGroups,
                    out _))
            {
                return;
            }

            if (emitTransitions)
            {
                foreach (var activeEvent in snapshotActiveEvents)
                {
                    if (_activeEventIds.Contains(activeEvent))
                    {
                        continue;
                    }

                    EmitCalendarEventActivated(activeEvent);
                }

                foreach (var activeEvent in _activeEventIds)
                {
                    if (snapshotActiveEvents.Contains(activeEvent))
                    {
                        continue;
                    }

                    EmitCalendarEventExpired(activeEvent);
                }
            }

            _activeEventIds.Clear();
            foreach (var activeEvent in snapshotActiveEvents)
            {
                _activeEventIds.Add(activeEvent);
            }

            _activeEventByVariantGroup.Clear();
            foreach (var variant in snapshotVariantGroups)
            {
                _activeEventByVariantGroup[variant.Key] = variant.Value;
            }
        }

        private bool EvaluateEventDefinition(
            GameContracts.CalendarEventDefinition eventDefinition,
            DateTime nowUtc,
            bool emitRuleTelemetry,
            out bool eventActive,
            out string reasonCode)
        {
            eventActive = false;
            reasonCode = string.Empty;
            if (eventDefinition == null || string.IsNullOrWhiteSpace(eventDefinition.eventId))
            {
                reasonCode = "CALENDAR_EVENT_INVALID";
                return false;
            }

            if (eventDefinition.ruleIds == null || eventDefinition.ruleIds.Count <= 0)
            {
                reasonCode = "CALENDAR_EVENT_RULES_REQUIRED";
                return false;
            }

            var allMatched = true;
            var anyMatched = false;
            for (var i = 0; i < eventDefinition.ruleIds.Count; i++)
            {
                var normalizedRuleId = Normalize(eventDefinition.ruleIds[i]);
                if (string.IsNullOrWhiteSpace(normalizedRuleId) ||
                    !_rulesById.TryGetValue(normalizedRuleId, out var ruleDefinition) ||
                    ruleDefinition == null)
                {
                    if (emitRuleTelemetry)
                    {
                        EmitRuleEvaluatedTelemetry(
                            eventDefinition.eventId,
                            normalizedRuleId,
                            string.Empty,
                            matched: false,
                            reasonCode: "CALENDAR_RULE_NOT_FOUND");
                    }

                    allMatched = false;
                    continue;
                }

                var ruleEvaluated = EvaluateRuleDefinition(
                    ruleDefinition,
                    nowUtc,
                    out var ruleMatched,
                    out var ruleReasonCode);
                if (emitRuleTelemetry)
                {
                    EmitRuleEvaluatedTelemetry(
                        eventDefinition.eventId,
                        ruleDefinition.ruleId,
                        ruleDefinition.ruleType,
                        ruleMatched,
                        NormalizeOrFallback(ruleReasonCode, "CALENDAR_RULE_EVALUATED"));
                }

                if (!ruleEvaluated)
                {
                    allMatched = false;
                    continue;
                }

                anyMatched |= ruleMatched;
                allMatched &= ruleMatched;
            }

            eventActive = eventDefinition.requireAllRules ? allMatched : anyMatched;
            reasonCode = eventActive ? "CALENDAR_EVENT_ACTIVE" : "CALENDAR_EVENT_INACTIVE";
            return true;
        }

        private bool EvaluateRuleDefinition(
            GameContracts.CalendarRuleDefinition ruleDefinition,
            DateTime nowUtc,
            out bool matched,
            out string reasonCode)
        {
            matched = false;
            reasonCode = string.Empty;

            if (ruleDefinition == null || string.IsNullOrWhiteSpace(ruleDefinition.ruleId))
            {
                reasonCode = "CALENDAR_RULE_INVALID";
                return false;
            }

            var ruleType = GameContracts.CalendarRuleTypes.NormalizeOrDefault(ruleDefinition.ruleType);
            if (string.Equals(ruleType, GameContracts.CalendarRuleTypes.DateWindow, StringComparison.OrdinalIgnoreCase))
            {
                return TryEvaluateDateWindowInternal(
                    nowUtc,
                    ruleDefinition.start,
                    ruleDefinition.end,
                    ruleDefinition.yearlyRecurring,
                    ruleDefinition.timezoneId,
                    out matched,
                    out reasonCode);
            }

            if (string.Equals(ruleType, GameContracts.CalendarRuleTypes.ProfileBirthday, StringComparison.OrdinalIgnoreCase))
            {
                return TryEvaluateProfileDateInternal(
                    nowUtc,
                    ruleDefinition.profileDateKey,
                    ruleDefinition.daysBefore,
                    ruleDefinition.daysAfter,
                    ruleDefinition.timezoneId,
                    out matched,
                    out reasonCode);
            }

            reasonCode = "CALENDAR_RULE_TYPE_UNSUPPORTED";
            return false;
        }

        private bool TryEvaluateDateWindowInternal(
            DateTime nowUtc,
            string start,
            string end,
            bool yearlyRecurring,
            string timezoneId,
            out bool matched,
            out string reasonCode)
        {
            matched = false;
            reasonCode = string.Empty;

            if (!TryResolveTimezone(timezoneId, out var timezone, out reasonCode))
            {
                return false;
            }

            if (!TryParseDateTime(start, out var parsedStart, out reasonCode))
            {
                reasonCode = "CALENDAR_WINDOW_START_INVALID";
                return false;
            }

            if (!TryParseDateTime(end, out var parsedEnd, out reasonCode))
            {
                reasonCode = "CALENDAR_WINDOW_END_INVALID";
                return false;
            }

            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(nowUtc), timezone);
            var startLocal = ConvertToTimezoneLocal(parsedStart, timezone);
            var endLocal = ConvertToTimezoneLocal(parsedEnd, timezone);

            if (!yearlyRecurring)
            {
                if (endLocal < startLocal)
                {
                    reasonCode = "CALENDAR_WINDOW_RANGE_INVALID";
                    return false;
                }

                matched = nowLocal >= startLocal && nowLocal <= endLocal;
                reasonCode = matched ? "CALENDAR_WINDOW_MATCHED" : "CALENDAR_WINDOW_NOT_MATCHED";
                return true;
            }

            var recurringStart = CreateCalendarDate(nowLocal.Year, startLocal);
            var recurringEnd = CreateCalendarDate(nowLocal.Year, endLocal);
            if (recurringEnd < recurringStart)
            {
                if (nowLocal < recurringStart)
                {
                    recurringStart = recurringStart.AddYears(-1);
                }
                else
                {
                    recurringEnd = recurringEnd.AddYears(1);
                }
            }

            matched = nowLocal >= recurringStart && nowLocal <= recurringEnd;
            reasonCode = matched ? "CALENDAR_WINDOW_MATCHED" : "CALENDAR_WINDOW_NOT_MATCHED";
            return true;
        }

        private bool TryEvaluateProfileDateInternal(
            DateTime nowUtc,
            string profileDateKey,
            int daysBefore,
            int daysAfter,
            string timezoneId,
            out bool matched,
            out string reasonCode)
        {
            matched = false;
            reasonCode = string.Empty;

            if (!TryResolveTimezone(timezoneId, out var timezone, out reasonCode))
            {
                return false;
            }

            var normalizedProfileDateKey = Normalize(profileDateKey);
            if (string.IsNullOrWhiteSpace(normalizedProfileDateKey))
            {
                normalizedProfileDateKey = NormalizeOrFallback(
                    _policy == null ? string.Empty : _policy.defaultProfileDateKey,
                    "profile_birthday");
            }

            if (!_profileDatesByKey.TryGetValue(normalizedProfileDateKey, out var profileDate))
            {
                reasonCode = "CALENDAR_PROFILE_DATE_NOT_FOUND";
                return false;
            }

            var safeDaysBefore = Mathf.Max(0, daysBefore);
            var safeDaysAfter = Mathf.Max(0, daysAfter);
            var nowLocalDate = TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(nowUtc), timezone).Date;
            var profileLocal = ConvertToTimezoneLocal(profileDate, timezone);

            var candidateYears = new[] { nowLocalDate.Year - 1, nowLocalDate.Year, nowLocalDate.Year + 1 };
            for (var i = 0; i < candidateYears.Length; i++)
            {
                var candidateBirthday = CreateCalendarDate(candidateYears[i], profileLocal).Date;
                var rangeStart = candidateBirthday.AddDays(-safeDaysBefore);
                var rangeEnd = candidateBirthday.AddDays(safeDaysAfter);
                if (nowLocalDate >= rangeStart && nowLocalDate <= rangeEnd)
                {
                    matched = true;
                    reasonCode = "CALENDAR_PROFILE_DATE_MATCHED";
                    return true;
                }
            }

            reasonCode = "CALENDAR_PROFILE_DATE_NOT_MATCHED";
            return true;
        }

        private void EmitRuleEvaluatedTelemetry(
            string eventId,
            string ruleId,
            string ruleType,
            bool matched,
            string reasonCode)
        {
            EmitCalendarTelemetry(
                "calendar_rule_evaluated",
                reasonCode,
                new Dictionary<string, object>
                {
                    { "eventId", NormalizeOrFallback(eventId, string.Empty) },
                    { "ruleId", NormalizeOrFallback(ruleId, string.Empty) },
                    { "ruleType", NormalizeOrFallback(ruleType, string.Empty) },
                    { "matched", matched },
                    { "timezoneId", ResolvePolicyTimezoneId() },
                });
        }

        private void EmitCalendarEventActivated(string eventId)
        {
            EmitCalendarTelemetry(
                "calendar_event_activated",
                "CALENDAR_EVENT_ACTIVATED",
                BuildEventDetails(eventId));
        }

        private void EmitCalendarEventExpired(string eventId)
        {
            EmitCalendarTelemetry(
                "calendar_event_expired",
                "CALENDAR_EVENT_EXPIRED",
                BuildEventDetails(eventId));
        }

        private IReadOnlyDictionary<string, object> BuildEventDetails(string eventId)
        {
            var normalizedEventId = NormalizeOrFallback(eventId, string.Empty);
            var details = new Dictionary<string, object>
            {
                { "eventId", normalizedEventId },
            };

            if (!_eventsById.TryGetValue(normalizedEventId, out var definition) || definition == null)
            {
                return details;
            }

            details["variantGroup"] = NormalizeOrFallback(definition.variantGroup, string.Empty);
            details["priority"] = definition.priority;
            details["requireAllRules"] = definition.requireAllRules;
            return details;
        }

        private bool ShouldReplaceByConflictPolicy(int incomingPriority, int existingPriority)
        {
            var conflictPolicy = NormalizeOrFallback(
                _policy == null ? string.Empty : _policy.conflictPolicy,
                GameContracts.CalendarConflictPolicies.HighestPriority);

            if (string.Equals(
                    conflictPolicy,
                    GameContracts.CalendarConflictPolicies.FirstMatch,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return incomingPriority > existingPriority;
        }

        private void RebuildPolicyIndex()
        {
            _rulesById.Clear();
            _eventsById.Clear();

            if (_policy == null)
            {
                return;
            }

            if (_policy.rules != null)
            {
                for (var i = 0; i < _policy.rules.Count; i++)
                {
                    var rule = _policy.rules[i];
                    if (rule == null || string.IsNullOrWhiteSpace(rule.ruleId))
                    {
                        continue;
                    }

                    _rulesById[rule.ruleId.Trim()] = rule;
                }
            }

            if (_policy.events != null)
            {
                for (var i = 0; i < _policy.events.Count; i++)
                {
                    var calendarEvent = _policy.events[i];
                    if (calendarEvent == null || string.IsNullOrWhiteSpace(calendarEvent.eventId))
                    {
                        continue;
                    }

                    _eventsById[calendarEvent.eventId.Trim()] = calendarEvent;
                }
            }
        }

        private void LoadProfileDates(IReadOnlyDictionary<string, string> profileDatesByKey)
        {
            _profileDatesByKey.Clear();
            if (profileDatesByKey == null || profileDatesByKey.Count <= 0)
            {
                return;
            }

            foreach (var pair in profileDatesByKey)
            {
                var key = Normalize(pair.Key);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!TryParseDateTime(pair.Value, out var parsedDate, out _))
                {
                    continue;
                }

                _profileDatesByKey[key] = parsedDate;
            }
        }

        private bool TryResolveTimezone(string timezoneId, out TimeZoneInfo timezone, out string reasonCode)
        {
            reasonCode = string.Empty;
            timezone = TimeZoneInfo.Utc;

            var resolvedTimezoneId = Normalize(timezoneId);
            if (string.IsNullOrWhiteSpace(resolvedTimezoneId))
            {
                resolvedTimezoneId = ResolvePolicyTimezoneId();
            }

            if (string.IsNullOrWhiteSpace(resolvedTimezoneId))
            {
                resolvedTimezoneId = "UTC";
            }

            try
            {
                timezone = TimeZoneInfo.FindSystemTimeZoneById(resolvedTimezoneId);
                return true;
            }
            catch (Exception)
            {
                var allowFallback = _policy == null || _policy.fallbackToUtcWhenTimezoneInvalid;
                if (!allowFallback)
                {
                    reasonCode = "CALENDAR_TIMEZONE_INVALID";
                    return false;
                }

                timezone = TimeZoneInfo.Utc;
                return true;
            }
        }

        private string ResolvePolicyTimezoneId()
        {
            return NormalizeOrFallback(
                _policy == null ? string.Empty : _policy.timezoneId,
                "UTC");
        }

        private void EmitCalendarTelemetry(
            string eventName,
            string reasonCode,
            IReadOnlyDictionary<string, object> details)
        {
            if (!_emitCalendarTelemetry || _interactionEventBridge == null)
            {
                return;
            }

            var payload = new Dictionary<string, object>
            {
                { "flowId", NormalizeOrFallback(_runtimeFlowId, "session_flow") },
                { "eventType", NormalizeOrFallback(eventName, "calendar_event") },
                { "reasonCode", NormalizeOrFallback(reasonCode, "CALENDAR_EVENT") },
                { "timezoneId", ResolvePolicyTimezoneId() },
                { "payloadVersion", 1 },
                { "monotonicSec", Time.realtimeSinceStartup },
                { "actionOutcome", "OBSERVED" },
            };

            if (details != null)
            {
                foreach (var detail in details)
                {
                    if (string.IsNullOrWhiteSpace(detail.Key))
                    {
                        continue;
                    }

                    payload[detail.Key] = detail.Value;
                }
            }

            _interactionEventBridge.RecordGameplayEvent(
                NormalizeOrFallback(_runtimeGameId, "session_flow"),
                eventName,
                string.Empty,
                payload,
                nameof(CalendarRuntime));

            if (_logCalendar)
            {
                Logger.Info(
                    "[CalendarRuntime] event=" + NormalizeOrFallback(eventName, string.Empty) +
                    " reasonCode=" + NormalizeOrFallback(reasonCode, string.Empty));
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

        private static GameContracts.CalendarPolicy ClonePolicy(GameContracts.CalendarPolicy source)
        {
            if (source == null)
            {
                return new GameContracts.CalendarPolicy();
            }

            var clone = new GameContracts.CalendarPolicy
            {
                timezoneId = NormalizeOrFallback(source.timezoneId, "UTC"),
                fallbackToUtcWhenTimezoneInvalid = source.fallbackToUtcWhenTimezoneInvalid,
                allowQaTimeOverride = source.allowQaTimeOverride,
                defaultProfileDateKey = NormalizeOrFallback(source.defaultProfileDateKey, "profile_birthday"),
                conflictPolicy = GameContracts.CalendarConflictPolicies.NormalizeOrDefault(source.conflictPolicy),
                rules = new List<GameContracts.CalendarRuleDefinition>(),
                events = new List<GameContracts.CalendarEventDefinition>(),
            };

            if (source.rules != null)
            {
                for (var i = 0; i < source.rules.Count; i++)
                {
                    var rule = source.rules[i];
                    if (rule == null)
                    {
                        continue;
                    }

                    clone.rules.Add(
                        new GameContracts.CalendarRuleDefinition
                        {
                            ruleId = NormalizeOrFallback(rule.ruleId, string.Empty),
                            ruleType = GameContracts.CalendarRuleTypes.NormalizeOrDefault(rule.ruleType),
                            start = NormalizeOrFallback(rule.start, string.Empty),
                            end = NormalizeOrFallback(rule.end, string.Empty),
                            yearlyRecurring = rule.yearlyRecurring,
                            profileDateKey = NormalizeOrFallback(rule.profileDateKey, string.Empty),
                            daysBefore = Mathf.Max(0, rule.daysBefore),
                            daysAfter = Mathf.Max(0, rule.daysAfter),
                            timezoneId = NormalizeOrFallback(rule.timezoneId, string.Empty),
                        });
                }
            }

            if (source.events != null)
            {
                for (var i = 0; i < source.events.Count; i++)
                {
                    var calendarEvent = source.events[i];
                    if (calendarEvent == null)
                    {
                        continue;
                    }

                    var clonedEvent = new GameContracts.CalendarEventDefinition
                    {
                        eventId = NormalizeOrFallback(calendarEvent.eventId, string.Empty),
                        variantGroup = NormalizeOrFallback(calendarEvent.variantGroup, string.Empty),
                        priority = calendarEvent.priority,
                        requireAllRules = calendarEvent.requireAllRules,
                        ruleIds = new List<string>(),
                    };

                    if (calendarEvent.ruleIds != null)
                    {
                        for (var j = 0; j < calendarEvent.ruleIds.Count; j++)
                        {
                            var ruleId = Normalize(calendarEvent.ruleIds[j]);
                            if (string.IsNullOrWhiteSpace(ruleId))
                            {
                                continue;
                            }

                            if (!clonedEvent.ruleIds.Contains(ruleId))
                            {
                                clonedEvent.ruleIds.Add(ruleId);
                            }
                        }
                    }

                    clone.events.Add(clonedEvent);
                }
            }

            return clone;
        }

        private static bool TryParseDateTime(string raw, out DateTime parsed, out string reasonCode)
        {
            parsed = default(DateTime);
            reasonCode = string.Empty;

            var normalizedRaw = raw == null ? string.Empty : raw.Trim();
            if (string.IsNullOrWhiteSpace(normalizedRaw))
            {
                reasonCode = "CALENDAR_DATE_REQUIRED";
                return false;
            }

            var hasExplicitTimezone = HasExplicitTimezone(normalizedRaw);
            if (hasExplicitTimezone &&
                DateTimeOffset.TryParse(
                    normalizedRaw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                    out var parsedOffset))
            {
                parsed = parsedOffset.UtcDateTime;
                return true;
            }

            if (DateTime.TryParse(
                    normalizedRaw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var parsedDateTime))
            {
                parsed = hasExplicitTimezone
                    ? EnsureUtc(parsedDateTime)
                    : DateTime.SpecifyKind(parsedDateTime, DateTimeKind.Unspecified);
                return true;
            }

            reasonCode = "CALENDAR_DATE_PARSE_FAILED";
            return false;
        }

        private static bool HasExplicitTimezone(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var normalizedRaw = raw.Trim();
            if (normalizedRaw.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var separatorIndex = normalizedRaw.IndexOf('T');
            if (separatorIndex < 0)
            {
                separatorIndex = normalizedRaw.IndexOf(' ');
            }

            if (separatorIndex < 0)
            {
                return false;
            }

            for (var i = separatorIndex + 1; i < normalizedRaw.Length; i++)
            {
                var character = normalizedRaw[i];
                if (character == '+' || character == '-')
                {
                    return true;
                }
            }

            return false;
        }

        private static DateTime ConvertToTimezoneLocal(DateTime value, TimeZoneInfo timezone)
        {
            if (value.Kind == DateTimeKind.Utc)
            {
                return TimeZoneInfo.ConvertTimeFromUtc(value, timezone);
            }

            if (value.Kind == DateTimeKind.Local)
            {
                return TimeZoneInfo.ConvertTime(value, timezone);
            }

            return value;
        }

        private static DateTime EnsureUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc)
            {
                return value;
            }

            if (value.Kind == DateTimeKind.Local)
            {
                return value.ToUniversalTime();
            }

            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private static DateTime CreateCalendarDate(int year, DateTime template)
        {
            var month = Mathf.Clamp(template.Month, 1, 12);
            var maxDay = DateTime.DaysInMonth(year, month);
            var day = Mathf.Clamp(template.Day, 1, maxDay);
            return new DateTime(
                year,
                month,
                day,
                template.Hour,
                template.Minute,
                template.Second,
                template.Millisecond,
                DateTimeKind.Unspecified);
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
