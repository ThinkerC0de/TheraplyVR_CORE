using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Validates canonical flow telemetry consistency guarantees for export.
    /// </summary>
    public sealed class CanonicalFlowTelemetryQualityGate
    {
        private static readonly string[] MandatoryKeys =
        {
            "eventId",
            "sessionId",
            "taskRunId",
            "attemptId",
            "sequenceNumber",
            "gameId",
            "eventType",
            "occurredAtUtc",
            "sourceComponent",
            "payloadVersion",
        };

        [Serializable]
        public struct Requirements
        {
            public bool requireMandatoryKeys;
            public bool requireNoSequenceGaps;
            public bool requireActionDecisionPairs;
            public bool requireSessionTerminalForClosedSessions;
        }

        [Serializable]
        public struct Report
        {
            public bool readyForExport;
            public string reasonCode;
            public int totalEvents;
            public int missingMandatoryKeyCount;
            public int sequenceGapCount;
            public int actionReceivedCount;
            public int actionEvaluatedCount;
            public int unresolvedActionDecisionCount;
            public int duplicateActionDecisionCount;
            public int closedSessions;
            public int sessionsMissingTerminal;
        }

        public static Requirements CreateDefaultRequirements()
        {
            return new Requirements
            {
                requireMandatoryKeys = true,
                requireNoSequenceGaps = true,
                requireActionDecisionPairs = true,
                requireSessionTerminalForClosedSessions = true,
            };
        }

        public Report Evaluate(
            IReadOnlyList<IReadOnlyDictionary<string, object>> records,
            Requirements requirements)
        {
            if (records == null || records.Count <= 0)
            {
                return new Report
                {
                    readyForExport = false,
                    reasonCode = "FLOW_TELEMETRY_EMPTY",
                    totalEvents = 0,
                };
            }

            var missingMandatoryKeys = 0;
            var sequenceGapCount = 0;
            var actionReceivedCount = 0;
            var actionEvaluatedCount = 0;
            var unresolvedActionDecisionCount = 0;
            var duplicateActionDecisionCount = 0;

            var sequenceBySession = new Dictionary<string, List<long>>(StringComparer.Ordinal);
            var receivedByAttempt = new Dictionary<string, int>(StringComparer.Ordinal);
            var evaluatedByAttempt = new Dictionary<string, int>(StringComparer.Ordinal);
            var closedSessions = new HashSet<string>(StringComparer.Ordinal);
            var terminalSessions = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (record == null)
                {
                    missingMandatoryKeys += MandatoryKeys.Length;
                    continue;
                }

                if (requirements.requireMandatoryKeys)
                {
                    for (var keyIndex = 0; keyIndex < MandatoryKeys.Length; keyIndex++)
                    {
                        var key = MandatoryKeys[keyIndex];
                        if (string.IsNullOrWhiteSpace(ReadString(record, key)))
                        {
                            missingMandatoryKeys++;
                        }
                    }
                }

                var sessionId = ReadString(record, "sessionId");
                var sequence = ReadLong(record, "sequenceNumber");
                if (!string.IsNullOrWhiteSpace(sessionId) && sequence > 0)
                {
                    if (!sequenceBySession.TryGetValue(sessionId, out var sessionSequences))
                    {
                        sessionSequences = new List<long>(64);
                        sequenceBySession[sessionId] = sessionSequences;
                    }

                    sessionSequences.Add(sequence);
                }

                var eventType = NormalizeEventType(ReadString(record, "eventType"));
                if (string.IsNullOrWhiteSpace(eventType))
                {
                    continue;
                }

                if (eventType == "ACTION_RECEIVED")
                {
                    actionReceivedCount++;
                    var attemptKey = ResolveActionAttemptKey(record);
                    if (!string.IsNullOrWhiteSpace(attemptKey))
                    {
                        if (!receivedByAttempt.TryGetValue(attemptKey, out var count))
                        {
                            receivedByAttempt[attemptKey] = 1;
                        }
                        else
                        {
                            receivedByAttempt[attemptKey] = count + 1;
                        }
                    }
                }
                else if (eventType == "ACTION_EVALUATED")
                {
                    actionEvaluatedCount++;
                    var attemptKey = ResolveActionAttemptKey(record);
                    if (!string.IsNullOrWhiteSpace(attemptKey))
                    {
                        if (!evaluatedByAttempt.TryGetValue(attemptKey, out var count))
                        {
                            evaluatedByAttempt[attemptKey] = 1;
                        }
                        else
                        {
                            evaluatedByAttempt[attemptKey] = count + 1;
                        }
                    }
                }

                if (IsClosedSessionEvent(eventType) && !string.IsNullOrWhiteSpace(sessionId))
                {
                    closedSessions.Add(sessionId);
                }

                if (eventType == "SESSION_TERMINAL" && !string.IsNullOrWhiteSpace(sessionId))
                {
                    terminalSessions.Add(sessionId);
                }
            }

            if (requirements.requireNoSequenceGaps)
            {
                foreach (var pair in sequenceBySession)
                {
                    var sequences = pair.Value;
                    if (sequences == null || sequences.Count <= 1)
                    {
                        continue;
                    }

                    sequences.Sort();
                    var previous = sequences[0];
                    for (var i = 1; i < sequences.Count; i++)
                    {
                        var current = sequences[i];
                        if (current <= previous)
                        {
                            continue;
                        }

                        if (current != previous + 1)
                        {
                            sequenceGapCount += (int)Math.Max(1L, current - previous - 1L);
                        }

                        previous = current;
                    }
                }
            }

            if (requirements.requireActionDecisionPairs)
            {
                foreach (var pair in receivedByAttempt)
                {
                    var attemptKey = pair.Key;
                    var receivedCount = pair.Value;
                    var evaluatedCount = evaluatedByAttempt.TryGetValue(attemptKey, out var count) ? count : 0;

                    if (receivedCount <= 0)
                    {
                        continue;
                    }

                    if (evaluatedCount <= 0)
                    {
                        unresolvedActionDecisionCount += receivedCount;
                        continue;
                    }

                    if (evaluatedCount > receivedCount)
                    {
                        duplicateActionDecisionCount += evaluatedCount - receivedCount;
                    }
                    else if (receivedCount > evaluatedCount)
                    {
                        unresolvedActionDecisionCount += receivedCount - evaluatedCount;
                    }
                }
            }

            var sessionsMissingTerminal = 0;
            if (requirements.requireSessionTerminalForClosedSessions)
            {
                foreach (var sessionId in closedSessions)
                {
                    if (!terminalSessions.Contains(sessionId))
                    {
                        sessionsMissingTerminal++;
                    }
                }
            }

            var reasonCode = ResolveReasonCode(
                requirements,
                missingMandatoryKeys,
                sequenceGapCount,
                unresolvedActionDecisionCount,
                duplicateActionDecisionCount,
                sessionsMissingTerminal);

            return new Report
            {
                readyForExport = string.Equals(reasonCode, "FLOW_TELEMETRY_READY", StringComparison.Ordinal),
                reasonCode = reasonCode,
                totalEvents = records.Count,
                missingMandatoryKeyCount = Math.Max(0, missingMandatoryKeys),
                sequenceGapCount = Math.Max(0, sequenceGapCount),
                actionReceivedCount = Math.Max(0, actionReceivedCount),
                actionEvaluatedCount = Math.Max(0, actionEvaluatedCount),
                unresolvedActionDecisionCount = Math.Max(0, unresolvedActionDecisionCount),
                duplicateActionDecisionCount = Math.Max(0, duplicateActionDecisionCount),
                closedSessions = closedSessions.Count,
                sessionsMissingTerminal = Math.Max(0, sessionsMissingTerminal),
            };
        }

        private static string ResolveReasonCode(
            Requirements requirements,
            int missingMandatoryKeys,
            int sequenceGapCount,
            int unresolvedActionDecisionCount,
            int duplicateActionDecisionCount,
            int sessionsMissingTerminal)
        {
            if (requirements.requireMandatoryKeys && missingMandatoryKeys > 0)
            {
                return "FLOW_MISSING_MANDATORY_KEYS";
            }

            if (requirements.requireNoSequenceGaps && sequenceGapCount > 0)
            {
                return "FLOW_SEQUENCE_GAPS_DETECTED";
            }

            if (requirements.requireActionDecisionPairs &&
                (unresolvedActionDecisionCount > 0 || duplicateActionDecisionCount > 0))
            {
                return "FLOW_ACTION_DECISION_COVERAGE_FAILED";
            }

            if (requirements.requireSessionTerminalForClosedSessions && sessionsMissingTerminal > 0)
            {
                return "FLOW_SESSION_TERMINAL_MISSING";
            }

            return "FLOW_TELEMETRY_READY";
        }

        private static bool IsClosedSessionEvent(string eventType)
        {
            return eventType == "FLOW_COMPLETED" ||
                   eventType == "FLOW_FAILED" ||
                   eventType == "FLOW_STOPPED" ||
                   eventType == "GAME_STOPPED";
        }

        private static string ResolveActionAttemptKey(IReadOnlyDictionary<string, object> record)
        {
            var attemptId = ReadString(record, "actionAttemptId");
            if (!string.IsNullOrWhiteSpace(attemptId))
            {
                return attemptId;
            }

            var fallbackAttemptId = ReadString(record, "attemptId");
            if (!string.IsNullOrWhiteSpace(fallbackAttemptId))
            {
                return fallbackAttemptId + "|" + ReadString(record, "taskRunId");
            }

            var details = ReadDetailsMap(record);
            if (details != null)
            {
                attemptId = ReadString(details, "actionAttemptId");
                if (!string.IsNullOrWhiteSpace(attemptId))
                {
                    return attemptId;
                }
            }

            return string.Empty;
        }

        private static IReadOnlyDictionary<string, object> ReadDetailsMap(IReadOnlyDictionary<string, object> record)
        {
            if (record == null || !record.TryGetValue("details", out var detailsValue) || detailsValue == null)
            {
                return null;
            }

            if (detailsValue is IReadOnlyDictionary<string, object> readOnly)
            {
                return readOnly;
            }

            if (detailsValue is Dictionary<string, object> mutable)
            {
                return mutable;
            }

            return null;
        }

        private static string NormalizeEventType(string eventType)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                return string.Empty;
            }

            return eventType.Trim().Replace(' ', '_').ToUpperInvariant();
        }

        private static string ReadString(IReadOnlyDictionary<string, object> record, string key)
        {
            if (record == null || string.IsNullOrWhiteSpace(key) || !record.TryGetValue(key, out var raw) || raw == null)
            {
                return string.Empty;
            }

            var converted = Convert.ToString(raw, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(converted) ? string.Empty : converted.Trim();
        }

        private static long ReadLong(IReadOnlyDictionary<string, object> record, string key)
        {
            if (record == null || string.IsNullOrWhiteSpace(key) || !record.TryGetValue(key, out var raw) || raw == null)
            {
                return 0L;
            }

            switch (raw)
            {
                case int intValue:
                    return intValue;
                case long longValue:
                    return longValue;
                case float floatValue:
                    return (long)floatValue;
                case double doubleValue:
                    return (long)doubleValue;
            }

            var converted = Convert.ToString(raw, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(converted))
            {
                return 0L;
            }

            return long.TryParse(converted, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0L;
        }
    }
}
