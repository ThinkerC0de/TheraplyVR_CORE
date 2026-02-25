using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;

namespace TheraplyCore.Games.Runtime
{
    public interface IConditionEvaluator
    {
        string ConditionId { get; }

        ConditionEvaluationResult Evaluate(
            GameContracts.ConditionDefinition condition,
            ConditionEvaluationContext context);
    }

    public struct ConditionEvaluationResult
    {
        public bool matched;
        public string reasonCode;

        public static ConditionEvaluationResult Matched(string reasonCode = "CONDITION_MATCHED")
        {
            return new ConditionEvaluationResult
            {
                matched = true,
                reasonCode = string.IsNullOrWhiteSpace(reasonCode) ? "CONDITION_MATCHED" : reasonCode.Trim(),
            };
        }

        public static ConditionEvaluationResult NotMatched(string reasonCode = "CONDITION_NOT_MATCHED")
        {
            return new ConditionEvaluationResult
            {
                matched = false,
                reasonCode = string.IsNullOrWhiteSpace(reasonCode) ? "CONDITION_NOT_MATCHED" : reasonCode.Trim(),
            };
        }
    }

    public sealed class ConditionEvaluationContext
    {
        public string gameId = string.Empty;
        public string flowId = string.Empty;
        public string sessionId = string.Empty;
        public string nodeId = string.Empty;
        public string controlMode = GameContracts.SessionFlowControlModes.Hybrid;
        public float nowElapsedSec;
        public float nodeEnteredAtSec;
        public ScoringRuntime.ScoringSnapshot scoringSnapshot;
        public IReadOnlyDictionary<string, bool> channelEnabledById;
        public IReadOnlyDictionary<string, string> stateFlagsByKey;
        public GameContracts.ICalendarService calendar;

        public float NodeElapsedSec => Mathf.Max(0f, nowElapsedSec - Mathf.Max(0f, nodeEnteredAtSec));
        public float FlowElapsedSec => Mathf.Max(0f, nowElapsedSec);

        public bool TryGetChannelEnabled(string channelId, out bool enabled)
        {
            enabled = false;
            if (channelEnabledById == null || string.IsNullOrWhiteSpace(channelId))
            {
                return false;
            }

            var normalizedChannelId = channelId.Trim();
            if (!channelEnabledById.TryGetValue(normalizedChannelId, out enabled))
            {
                return false;
            }

            return true;
        }

        public bool TryGetStateFlag(string key, out string value)
        {
            value = string.Empty;
            if (stateFlagsByKey == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            var normalizedKey = key.Trim();
            if (!stateFlagsByKey.TryGetValue(normalizedKey, out value))
            {
                value = string.Empty;
                return false;
            }

            value = value ?? string.Empty;
            return true;
        }
    }

    public struct ConditionEvaluationTrace
    {
        public string nodeId;
        public string conditionId;
        public string subject;
        public string op;
        public string value;
        public bool matched;
        public string reasonCode;
        public float nowElapsedSec;
        public float nodeElapsedSec;
    }

    public struct BranchRoutingTrace
    {
        public string nodeId;
        public string nodeType;
        public string precedence;
        public bool matched;
        public string selectedConditionId;
        public string selectedNextNodeId;
        public string reasonCode;
        public float nowElapsedSec;
    }

    public sealed class ConditionEvaluatorRegistry
    {
        private readonly Dictionary<string, IConditionEvaluator> _evaluatorsById =
            new Dictionary<string, IConditionEvaluator>(StringComparer.OrdinalIgnoreCase);
        private readonly List<IConditionEvaluator> _evaluators = new List<IConditionEvaluator>();

        public int Count => _evaluators.Count;

        public IReadOnlyList<IConditionEvaluator> GetEvaluators()
        {
            return _evaluators;
        }

        public bool Register(IConditionEvaluator evaluator, bool replaceExisting = true)
        {
            if (evaluator == null || string.IsNullOrWhiteSpace(evaluator.ConditionId))
            {
                return false;
            }

            var conditionId = evaluator.ConditionId.Trim();
            if (_evaluatorsById.TryGetValue(conditionId, out var existing))
            {
                if (!replaceExisting)
                {
                    return false;
                }

                _evaluatorsById[conditionId] = evaluator;
                for (var i = 0; i < _evaluators.Count; i++)
                {
                    if (ReferenceEquals(_evaluators[i], existing))
                    {
                        _evaluators[i] = evaluator;
                        return true;
                    }
                }

                _evaluators.Add(evaluator);
                return true;
            }

            _evaluatorsById[conditionId] = evaluator;
            _evaluators.Add(evaluator);
            return true;
        }

        public bool TryResolve(string conditionId, out IConditionEvaluator evaluator)
        {
            evaluator = null;
            if (string.IsNullOrWhiteSpace(conditionId))
            {
                return false;
            }

            return _evaluatorsById.TryGetValue(conditionId.Trim(), out evaluator) && evaluator != null;
        }

        public void Clear()
        {
            _evaluatorsById.Clear();
            _evaluators.Clear();
        }
    }

    public static class SessionFlowBuiltInConditionEvaluators
    {
        public static void RegisterBuiltIns(ConditionEvaluatorRegistry registry, bool replaceExisting = true)
        {
            if (registry == null)
            {
                return;
            }

            registry.Register(new ScoreThresholdConditionEvaluator(), replaceExisting);
            registry.Register(new StateFlagEqualsConditionEvaluator(), replaceExisting);
            registry.Register(new ControlModeEqualsConditionEvaluator(), replaceExisting);
            registry.Register(new ChannelEnabledConditionEvaluator(), replaceExisting);
            registry.Register(new ElapsedTimeWindowConditionEvaluator(), replaceExisting);
            registry.Register(new EventActiveConditionEvaluator(), replaceExisting);
            registry.Register(new DateWindowConditionEvaluator(), replaceExisting);
            registry.Register(new ProfileBirthdayConditionEvaluator(), replaceExisting);
        }

        private sealed class ScoreThresholdConditionEvaluator : IConditionEvaluator
        {
            public string ConditionId => GameContracts.SessionFlowConditionIds.ScoreThreshold;

            public ConditionEvaluationResult Evaluate(
                GameContracts.ConditionDefinition condition,
                ConditionEvaluationContext context)
            {
                if (!TryResolveScoreValue(condition, context, out var scoreValue))
                {
                    return ConditionEvaluationResult.NotMatched("SCORE_SUBJECT_UNSUPPORTED");
                }

                if (!ConditionComparison.TryParseFloat(condition == null ? string.Empty : condition.value, out var threshold))
                {
                    return ConditionEvaluationResult.NotMatched("SCORE_THRESHOLD_INVALID");
                }

                var op = ConditionComparison.NormalizeOrDefault(condition == null ? string.Empty : condition.op, "gte");
                if (!ConditionComparison.TryCompareNumeric(scoreValue, threshold, op, out var matched, out var reasonCode))
                {
                    return ConditionEvaluationResult.NotMatched(reasonCode);
                }

                return matched
                    ? ConditionEvaluationResult.Matched("CONDITION_MATCHED")
                    : ConditionEvaluationResult.NotMatched("CONDITION_NOT_MATCHED");
            }

            private static bool TryResolveScoreValue(
                GameContracts.ConditionDefinition condition,
                ConditionEvaluationContext context,
                out float scoreValue)
            {
                scoreValue = 0f;
                var subject = ConditionComparison.Normalize(
                    condition == null ? string.Empty : condition.subject);
                if (string.IsNullOrWhiteSpace(subject))
                {
                    subject = "score_total";
                }

                var snapshot = context == null
                    ? default(ScoringRuntime.ScoringSnapshot)
                    : context.scoringSnapshot;

                switch (subject)
                {
                    case "score":
                    case "score_total":
                        scoreValue = snapshot.scoreTotal;
                        return true;
                    case "correct":
                    case "correct_count":
                        scoreValue = snapshot.correctCount;
                        return true;
                    case "wrong":
                    case "wrong_count":
                        scoreValue = snapshot.wrongCount;
                        return true;
                    case "lives":
                    case "lives_remaining":
                        scoreValue = snapshot.livesRemaining;
                        return true;
                    case "difficulty":
                        scoreValue = snapshot.difficulty;
                        return true;
                    case "target_speed":
                        scoreValue = snapshot.targetSpeed;
                        return true;
                    case "target_scale":
                        scoreValue = snapshot.targetScale;
                        return true;
                    case "cue_timeout_sec":
                        scoreValue = snapshot.cueTimeoutSec;
                        return true;
                    default:
                        return false;
                }
            }
        }

        private sealed class StateFlagEqualsConditionEvaluator : IConditionEvaluator
        {
            public string ConditionId => GameContracts.SessionFlowConditionIds.StateFlagEquals;

            public ConditionEvaluationResult Evaluate(
                GameContracts.ConditionDefinition condition,
                ConditionEvaluationContext context)
            {
                var subject = ConditionComparison.Normalize(condition == null ? string.Empty : condition.subject);
                if (string.IsNullOrWhiteSpace(subject))
                {
                    return ConditionEvaluationResult.NotMatched("STATE_FLAG_SUBJECT_REQUIRED");
                }

                if (context == null || !context.TryGetStateFlag(subject, out var actualValue))
                {
                    return ConditionEvaluationResult.NotMatched("STATE_FLAG_MISSING");
                }

                var expectedValue = ConditionComparison.Normalize(condition == null ? string.Empty : condition.value);
                var op = ConditionComparison.NormalizeOrDefault(condition == null ? string.Empty : condition.op, "eq");
                if (!ConditionComparison.TryCompareText(actualValue, expectedValue, op, out var matched, out var reasonCode))
                {
                    return ConditionEvaluationResult.NotMatched(reasonCode);
                }

                return matched
                    ? ConditionEvaluationResult.Matched("CONDITION_MATCHED")
                    : ConditionEvaluationResult.NotMatched("CONDITION_NOT_MATCHED");
            }
        }

        private sealed class ControlModeEqualsConditionEvaluator : IConditionEvaluator
        {
            public string ConditionId => GameContracts.SessionFlowConditionIds.ControlModeEquals;

            public ConditionEvaluationResult Evaluate(
                GameContracts.ConditionDefinition condition,
                ConditionEvaluationContext context)
            {
                var expectedControlMode = ConditionComparison.Normalize(
                    condition == null ? string.Empty : condition.value);
                if (string.IsNullOrWhiteSpace(expectedControlMode))
                {
                    expectedControlMode = ConditionComparison.Normalize(
                        condition == null ? string.Empty : condition.subject);
                }

                if (string.IsNullOrWhiteSpace(expectedControlMode))
                {
                    return ConditionEvaluationResult.NotMatched("CONTROL_MODE_EXPECTED_VALUE_REQUIRED");
                }

                var actualControlMode = GameContracts.SessionFlowControlModes.NormalizeOrDefault(
                    context == null ? string.Empty : context.controlMode);
                var op = ConditionComparison.NormalizeOrDefault(condition == null ? string.Empty : condition.op, "eq");
                if (!ConditionComparison.TryCompareText(actualControlMode, expectedControlMode, op, out var matched, out var reasonCode))
                {
                    return ConditionEvaluationResult.NotMatched(reasonCode);
                }

                return matched
                    ? ConditionEvaluationResult.Matched("CONDITION_MATCHED")
                    : ConditionEvaluationResult.NotMatched("CONDITION_NOT_MATCHED");
            }
        }

        private sealed class ChannelEnabledConditionEvaluator : IConditionEvaluator
        {
            public string ConditionId => GameContracts.SessionFlowConditionIds.ChannelEnabled;

            public ConditionEvaluationResult Evaluate(
                GameContracts.ConditionDefinition condition,
                ConditionEvaluationContext context)
            {
                var subject = ConditionComparison.Normalize(condition == null ? string.Empty : condition.subject);
                if (string.IsNullOrWhiteSpace(subject))
                {
                    return ConditionEvaluationResult.NotMatched("CHANNEL_ID_REQUIRED");
                }

                var actualEnabled = context != null &&
                                    context.TryGetChannelEnabled(subject, out var enabled) &&
                                    enabled;

                var expectedEnabled = true;
                if (!string.IsNullOrWhiteSpace(condition == null ? string.Empty : condition.value))
                {
                    if (!ConditionComparison.TryParseBool(condition.value, out expectedEnabled))
                    {
                        return ConditionEvaluationResult.NotMatched("CHANNEL_ENABLED_EXPECTED_BOOL_REQUIRED");
                    }
                }

                var op = ConditionComparison.NormalizeOrDefault(condition == null ? string.Empty : condition.op, "eq");
                if (!ConditionComparison.TryCompareBoolean(actualEnabled, expectedEnabled, op, out var matched, out var reasonCode))
                {
                    return ConditionEvaluationResult.NotMatched(reasonCode);
                }

                return matched
                    ? ConditionEvaluationResult.Matched("CONDITION_MATCHED")
                    : ConditionEvaluationResult.NotMatched("CONDITION_NOT_MATCHED");
            }
        }

        private sealed class ElapsedTimeWindowConditionEvaluator : IConditionEvaluator
        {
            public string ConditionId => GameContracts.SessionFlowConditionIds.ElapsedTimeWindow;

            public ConditionEvaluationResult Evaluate(
                GameContracts.ConditionDefinition condition,
                ConditionEvaluationContext context)
            {
                var elapsedSec = ResolveElapsedSec(condition, context, out var resolvedReasonCode);
                if (!string.IsNullOrWhiteSpace(resolvedReasonCode))
                {
                    return ConditionEvaluationResult.NotMatched(resolvedReasonCode);
                }

                var op = ConditionComparison.NormalizeOrDefault(condition == null ? string.Empty : condition.op, "between");
                var rawValue = condition == null ? string.Empty : condition.value;

                switch (op)
                {
                    case "between":
                    case "in":
                    case "outside":
                    case "not_between":
                        if (!ConditionComparison.TryParseRange(rawValue, out var minSec, out var maxSec))
                        {
                            return ConditionEvaluationResult.NotMatched("ELAPSED_RANGE_INVALID");
                        }

                        var inRange = elapsedSec >= minSec && elapsedSec <= maxSec;
                        var matched = op == "outside" || op == "not_between"
                            ? !inRange
                            : inRange;
                        return matched
                            ? ConditionEvaluationResult.Matched("CONDITION_MATCHED")
                            : ConditionEvaluationResult.NotMatched("CONDITION_NOT_MATCHED");

                    default:
                        if (!ConditionComparison.TryParseFloat(rawValue, out var thresholdSec))
                        {
                            return ConditionEvaluationResult.NotMatched("ELAPSED_THRESHOLD_INVALID");
                        }

                        if (!ConditionComparison.TryCompareNumeric(elapsedSec, thresholdSec, op, out var numericMatch, out var reasonCode))
                        {
                            return ConditionEvaluationResult.NotMatched(reasonCode);
                        }

                        return numericMatch
                            ? ConditionEvaluationResult.Matched("CONDITION_MATCHED")
                            : ConditionEvaluationResult.NotMatched("CONDITION_NOT_MATCHED");
                }
            }

            private static float ResolveElapsedSec(
                GameContracts.ConditionDefinition condition,
                ConditionEvaluationContext context,
                out string reasonCode)
            {
                reasonCode = string.Empty;
                var subject = ConditionComparison.Normalize(condition == null ? string.Empty : condition.subject);
                if (string.IsNullOrWhiteSpace(subject))
                {
                    subject = "node_elapsed_sec";
                }

                if (context == null)
                {
                    reasonCode = "CONDITION_CONTEXT_MISSING";
                    return 0f;
                }

                switch (subject)
                {
                    case "node":
                    case "step":
                    case "node_elapsed_sec":
                        return context.NodeElapsedSec;
                    case "flow":
                    case "flow_elapsed_sec":
                        return context.FlowElapsedSec;
                    default:
                        reasonCode = "ELAPSED_SUBJECT_UNSUPPORTED";
                        return 0f;
                }
            }
        }

        private sealed class EventActiveConditionEvaluator : IConditionEvaluator
        {
            public string ConditionId => GameContracts.SessionFlowConditionIds.IsEventActive;

            public ConditionEvaluationResult Evaluate(
                GameContracts.ConditionDefinition condition,
                ConditionEvaluationContext context)
            {
                if (context == null || context.calendar == null)
                {
                    return ConditionEvaluationResult.NotMatched("CALENDAR_SERVICE_MISSING");
                }

                var eventId = ConditionComparison.Normalize(condition == null ? string.Empty : condition.subject);
                if (string.IsNullOrWhiteSpace(eventId))
                {
                    eventId = ConditionComparison.Normalize(condition == null ? string.Empty : condition.value);
                }

                if (string.IsNullOrWhiteSpace(eventId))
                {
                    return ConditionEvaluationResult.NotMatched("CALENDAR_EVENT_ID_REQUIRED");
                }

                if (!context.calendar.TryEvaluateEventActive(eventId, out var isActive, out var reasonCode))
                {
                    return ConditionEvaluationResult.NotMatched(
                        string.IsNullOrWhiteSpace(reasonCode) ? "CALENDAR_EVENT_EVALUATION_FAILED" : reasonCode);
                }

                var op = ConditionComparison.NormalizeOrDefault(condition == null ? string.Empty : condition.op, "eq");
                var expectedActive = true;
                var rawValue = ConditionComparison.Normalize(condition == null ? string.Empty : condition.value);
                if (!string.IsNullOrWhiteSpace(rawValue) && ConditionComparison.TryParseBool(rawValue, out var parsedExpected))
                {
                    expectedActive = parsedExpected;
                }

                if (!ConditionComparison.TryCompareBoolean(isActive, expectedActive, op, out var matched, out var comparisonReason))
                {
                    return ConditionEvaluationResult.NotMatched(comparisonReason);
                }

                return matched
                    ? ConditionEvaluationResult.Matched("CONDITION_MATCHED")
                    : ConditionEvaluationResult.NotMatched("CONDITION_NOT_MATCHED");
            }
        }

        private sealed class DateWindowConditionEvaluator : IConditionEvaluator
        {
            public string ConditionId => GameContracts.SessionFlowConditionIds.IsWithinDateWindow;

            public ConditionEvaluationResult Evaluate(
                GameContracts.ConditionDefinition condition,
                ConditionEvaluationContext context)
            {
                if (context == null || context.calendar == null)
                {
                    return ConditionEvaluationResult.NotMatched("CALENDAR_SERVICE_MISSING");
                }

                var start = ConditionComparison.Normalize(condition == null ? string.Empty : condition.subject);
                var end = ConditionComparison.Normalize(condition == null ? string.Empty : condition.value);
                if (string.IsNullOrWhiteSpace(start) &&
                    TryParseRangePair(condition == null ? string.Empty : condition.value, out var parsedStart, out var parsedEnd))
                {
                    start = parsedStart;
                    end = parsedEnd;
                }

                if (string.IsNullOrWhiteSpace(start) || string.IsNullOrWhiteSpace(end))
                {
                    return ConditionEvaluationResult.NotMatched("CALENDAR_DATE_WINDOW_REQUIRED");
                }

                var normalizedOp = ConditionComparison.NormalizeOrDefault(condition == null ? string.Empty : condition.op, "between");
                var yearlyRecurring = string.Equals(normalizedOp, "between_yearly", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(normalizedOp, "yearly", StringComparison.OrdinalIgnoreCase);
                var expectInWindow = !string.Equals(normalizedOp, "outside", StringComparison.OrdinalIgnoreCase) &&
                                     !string.Equals(normalizedOp, "not_between", StringComparison.OrdinalIgnoreCase);

                if (!context.calendar.TryEvaluateDateWindow(
                        start,
                        end,
                        yearlyRecurring,
                        timezoneId: string.Empty,
                        out var matchedWindow,
                        out var reasonCode))
                {
                    return ConditionEvaluationResult.NotMatched(
                        string.IsNullOrWhiteSpace(reasonCode) ? "CALENDAR_DATE_WINDOW_EVALUATION_FAILED" : reasonCode);
                }

                var matched = expectInWindow ? matchedWindow : !matchedWindow;
                return matched
                    ? ConditionEvaluationResult.Matched("CONDITION_MATCHED")
                    : ConditionEvaluationResult.NotMatched("CONDITION_NOT_MATCHED");
            }

            private static bool TryParseRangePair(string raw, out string start, out string end)
            {
                start = string.Empty;
                end = string.Empty;

                if (string.IsNullOrWhiteSpace(raw))
                {
                    return false;
                }

                string[] parts;
                if (raw.Contains(".."))
                {
                    parts = raw.Split(new[] { ".." }, StringSplitOptions.None);
                }
                else if (raw.Contains("|"))
                {
                    parts = raw.Split('|');
                }
                else if (raw.Contains(","))
                {
                    parts = raw.Split(',');
                }
                else
                {
                    return false;
                }

                if (parts == null || parts.Length != 2)
                {
                    return false;
                }

                start = ConditionComparison.Normalize(parts[0]);
                end = ConditionComparison.Normalize(parts[1]);
                return !string.IsNullOrWhiteSpace(start) && !string.IsNullOrWhiteSpace(end);
            }
        }

        private sealed class ProfileBirthdayConditionEvaluator : IConditionEvaluator
        {
            public string ConditionId => GameContracts.SessionFlowConditionIds.IsProfileBirthday;

            public ConditionEvaluationResult Evaluate(
                GameContracts.ConditionDefinition condition,
                ConditionEvaluationContext context)
            {
                if (context == null || context.calendar == null)
                {
                    return ConditionEvaluationResult.NotMatched("CALENDAR_SERVICE_MISSING");
                }

                var profileDateKey = ConditionComparison.Normalize(condition == null ? string.Empty : condition.subject);
                var daysBefore = 0;
                var daysAfter = 0;
                ParseTolerance(condition == null ? string.Empty : condition.value, out daysBefore, out daysAfter);

                if (!context.calendar.TryEvaluateProfileDate(
                        profileDateKey,
                        daysBefore,
                        daysAfter,
                        timezoneId: string.Empty,
                        out var matchedBirthday,
                        out var reasonCode))
                {
                    return ConditionEvaluationResult.NotMatched(
                        string.IsNullOrWhiteSpace(reasonCode) ? "CALENDAR_PROFILE_DATE_EVALUATION_FAILED" : reasonCode);
                }

                var op = ConditionComparison.NormalizeOrDefault(condition == null ? string.Empty : condition.op, "eq");
                var expectedMatched = true;
                if (ConditionComparison.TryParseBool(condition == null ? string.Empty : condition.value, out var boolExpected))
                {
                    expectedMatched = boolExpected;
                }

                if (!ConditionComparison.TryCompareBoolean(
                        matchedBirthday,
                        expectedMatched,
                        op,
                        out var matched,
                        out var comparisonReason))
                {
                    return ConditionEvaluationResult.NotMatched(comparisonReason);
                }

                return matched
                    ? ConditionEvaluationResult.Matched("CONDITION_MATCHED")
                    : ConditionEvaluationResult.NotMatched("CONDITION_NOT_MATCHED");
            }

            private static void ParseTolerance(string raw, out int daysBefore, out int daysAfter)
            {
                daysBefore = 0;
                daysAfter = 0;

                if (string.IsNullOrWhiteSpace(raw))
                {
                    return;
                }

                var normalized = ConditionComparison.Normalize(raw);
                if (ConditionComparison.TryParseBool(normalized, out _))
                {
                    return;
                }

                string[] parts;
                if (normalized.Contains(".."))
                {
                    parts = normalized.Split(new[] { ".." }, StringSplitOptions.None);
                }
                else if (normalized.Contains("|"))
                {
                    parts = normalized.Split('|');
                }
                else if (normalized.Contains(","))
                {
                    parts = normalized.Split(',');
                }
                else
                {
                    parts = new[] { normalized };
                }

                if (parts.Length == 1)
                {
                    if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var symmetric))
                    {
                        daysBefore = Mathf.Max(0, symmetric);
                        daysAfter = Mathf.Max(0, symmetric);
                    }
                    return;
                }

                if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var before))
                {
                    daysBefore = Mathf.Max(0, before);
                }

                if (int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var after))
                {
                    daysAfter = Mathf.Max(0, after);
                }
            }
        }
    }

    internal static class ConditionComparison
    {
        private const float EqualityTolerance = 0.0001f;

        public static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }

        public static string NormalizeOrDefault(string value, string fallback)
        {
            var normalized = Normalize(value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }

            return Normalize(fallback);
        }

        public static bool TryParseFloat(string value, out float parsed)
        {
            return float.TryParse(
                value == null ? string.Empty : value.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out parsed);
        }

        public static bool TryParseBool(string value, out bool parsed)
        {
            parsed = false;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = Normalize(value);
            switch (normalized)
            {
                case "1":
                case "true":
                case "yes":
                case "enabled":
                    parsed = true;
                    return true;
                case "0":
                case "false":
                case "no":
                case "disabled":
                    parsed = false;
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryParseRange(string value, out float min, out float max)
        {
            min = 0f;
            max = 0f;
            var raw = value == null ? string.Empty : value.Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string[] parts;
            if (raw.Contains(".."))
            {
                parts = raw.Split(new[] { ".." }, StringSplitOptions.None);
            }
            else if (raw.Contains(","))
            {
                parts = raw.Split(',');
            }
            else if (raw.Contains("|"))
            {
                parts = raw.Split('|');
            }
            else if (raw.Contains(":"))
            {
                parts = raw.Split(':');
            }
            else
            {
                return false;
            }

            if (parts == null || parts.Length != 2)
            {
                return false;
            }

            if (!TryParseFloat(parts[0], out min) || !TryParseFloat(parts[1], out max))
            {
                return false;
            }

            if (min > max)
            {
                var swap = min;
                min = max;
                max = swap;
            }

            return true;
        }

        public static bool TryCompareNumeric(
            float actual,
            float expected,
            string op,
            out bool matched,
            out string reasonCode)
        {
            matched = false;
            reasonCode = "CONDITION_OP_UNSUPPORTED";
            switch (NormalizeOrDefault(op, "eq"))
            {
                case "eq":
                case "==":
                    matched = Mathf.Abs(actual - expected) <= EqualityTolerance;
                    reasonCode = "CONDITION_NUMERIC_COMPARE";
                    return true;
                case "neq":
                case "!=":
                    matched = Mathf.Abs(actual - expected) > EqualityTolerance;
                    reasonCode = "CONDITION_NUMERIC_COMPARE";
                    return true;
                case "gt":
                case ">":
                    matched = actual > expected;
                    reasonCode = "CONDITION_NUMERIC_COMPARE";
                    return true;
                case "gte":
                case ">=":
                    matched = actual >= expected;
                    reasonCode = "CONDITION_NUMERIC_COMPARE";
                    return true;
                case "lt":
                case "<":
                    matched = actual < expected;
                    reasonCode = "CONDITION_NUMERIC_COMPARE";
                    return true;
                case "lte":
                case "<=":
                    matched = actual <= expected;
                    reasonCode = "CONDITION_NUMERIC_COMPARE";
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryCompareText(
            string actual,
            string expected,
            string op,
            out bool matched,
            out string reasonCode)
        {
            matched = false;
            reasonCode = "CONDITION_OP_UNSUPPORTED";
            var normalizedActual = Normalize(actual);
            var normalizedExpected = Normalize(expected);

            switch (NormalizeOrDefault(op, "eq"))
            {
                case "eq":
                case "==":
                    matched = string.Equals(normalizedActual, normalizedExpected, StringComparison.OrdinalIgnoreCase);
                    reasonCode = "CONDITION_TEXT_COMPARE";
                    return true;
                case "neq":
                case "!=":
                    matched = !string.Equals(normalizedActual, normalizedExpected, StringComparison.OrdinalIgnoreCase);
                    reasonCode = "CONDITION_TEXT_COMPARE";
                    return true;
                case "contains":
                    matched = normalizedActual.IndexOf(normalizedExpected, StringComparison.OrdinalIgnoreCase) >= 0;
                    reasonCode = "CONDITION_TEXT_COMPARE";
                    return true;
                case "not_contains":
                    matched = normalizedActual.IndexOf(normalizedExpected, StringComparison.OrdinalIgnoreCase) < 0;
                    reasonCode = "CONDITION_TEXT_COMPARE";
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryCompareBoolean(
            bool actual,
            bool expected,
            string op,
            out bool matched,
            out string reasonCode)
        {
            matched = false;
            reasonCode = "CONDITION_OP_UNSUPPORTED";
            switch (NormalizeOrDefault(op, "eq"))
            {
                case "eq":
                case "==":
                    matched = actual == expected;
                    reasonCode = "CONDITION_BOOLEAN_COMPARE";
                    return true;
                case "neq":
                case "!=":
                    matched = actual != expected;
                    reasonCode = "CONDITION_BOOLEAN_COMPARE";
                    return true;
                default:
                    return false;
            }
        }
    }
}
