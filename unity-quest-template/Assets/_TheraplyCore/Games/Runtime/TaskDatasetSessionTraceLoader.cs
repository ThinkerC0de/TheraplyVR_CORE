using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Loads training-relevant canonical events from durable session NDJSON traces.
    /// </summary>
    public sealed class TaskDatasetSessionTraceLoader
    {
        private const string TraceEventType = "interaction_event";

        private static readonly HashSet<string> SupportedDatasetEventTypes =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "TASK_OUTCOME_SUMMARY",
                "TASK_LABEL_GENERATED",
                "ADAPTIVE_DIFFICULTY_ADJUSTED",
            };

        [Serializable]
        public struct LoadOptions
        {
            public string durableTracePath;
            public string sessionId;
        }

        [Serializable]
        public struct LoadReport
        {
            public bool loaded;
            public string reasonCode;
            public string durableTracePath;
            public string sessionIdFilter;
            public int totalLines;
            public int importedEvents;
            public int skippedBySession;
            public int skippedUnsupportedEventType;
            public int invalidLines;
        }

        public sealed class LoadResult
        {
            public LoadReport report;
            public IReadOnlyList<IReadOnlyDictionary<string, object>> records;
        }

        [Serializable]
        private sealed class DurableSessionTraceRecord
        {
            public string eventId;
            public string sessionId;
            public string patientId;
            public string therapistId;
            public string deviceId;
            public long sequence;
            public string eventType;
            public int eventVersion;
            public string createdAtUtc;
            public string payloadJson;
            public string checksum;
        }

        [Serializable]
        private sealed class CanonicalEventPayload
        {
            public string eventId;
            public long sequenceNumber;
            public string eventType;
            public string taskRunId;
            public string gameId;
            public string sourceOfTruth;
            public string ownerKey;
            public string sessionKey;
            public string sessionId;
            public string occurredAtUtc;
            public string actionOutcome;
            public string reasonCode;
            public CanonicalEventDetails details;
        }

        [Serializable]
        private sealed class CanonicalEventDetails
        {
            public string taskRunId;
            public float completionRatio;
            public float averageReactionSec;
            public float confidence;
            public float difficultyScore;
            public int cuesPresented;
            public int actionsObserved;
            public int correctCount;
            public int incorrectCount;
            public int lateCount;
            public int omittedCount;
            public int redundantCount;
            public int recommendedDifficultyLevel;
        }

        public LoadResult LoadFromDurableTrace(LoadOptions options)
        {
            var records = new List<IReadOnlyDictionary<string, object>>();
            var path = ResolveTracePath(options.durableTracePath);
            var sessionFilter = Normalize(options.sessionId);

            var report = new LoadReport
            {
                loaded = false,
                reasonCode = "TRACE_UNINITIALIZED",
                durableTracePath = path,
                sessionIdFilter = sessionFilter,
                totalLines = 0,
                importedEvents = 0,
                skippedBySession = 0,
                skippedUnsupportedEventType = 0,
                invalidLines = 0,
            };

            if (string.IsNullOrWhiteSpace(path))
            {
                report.reasonCode = "TRACE_PATH_REQUIRED";
                return new LoadResult { report = report, records = records };
            }

            if (!File.Exists(path))
            {
                report.reasonCode = "TRACE_FILE_NOT_FOUND";
                return new LoadResult { report = report, records = records };
            }

            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                report.totalLines++;

                DurableSessionTraceRecord durableRecord;
                try
                {
                    durableRecord = JsonUtility.FromJson<DurableSessionTraceRecord>(line);
                }
                catch (Exception)
                {
                    report.invalidLines++;
                    continue;
                }

                if (durableRecord == null ||
                    !string.Equals(durableRecord.eventType, TraceEventType, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(durableRecord.payloadJson))
                {
                    report.skippedUnsupportedEventType++;
                    continue;
                }

                CanonicalEventPayload canonicalPayload;
                try
                {
                    canonicalPayload = JsonUtility.FromJson<CanonicalEventPayload>(durableRecord.payloadJson);
                }
                catch (Exception)
                {
                    report.invalidLines++;
                    continue;
                }

                if (canonicalPayload == null)
                {
                    report.invalidLines++;
                    continue;
                }

                var normalizedEventType = NormalizeEventType(canonicalPayload.eventType);
                if (!SupportedDatasetEventTypes.Contains(normalizedEventType))
                {
                    report.skippedUnsupportedEventType++;
                    continue;
                }

                var payloadSessionId = Normalize(canonicalPayload.sessionId);
                var durableSessionId = Normalize(durableRecord.sessionId);
                if (!string.IsNullOrWhiteSpace(sessionFilter) &&
                    !string.Equals(payloadSessionId, sessionFilter, StringComparison.Ordinal) &&
                    !string.Equals(durableSessionId, sessionFilter, StringComparison.Ordinal))
                {
                    report.skippedBySession++;
                    continue;
                }

                var taskRunId = Normalize(canonicalPayload.details != null ? canonicalPayload.details.taskRunId : string.Empty);
                if (string.IsNullOrWhiteSpace(taskRunId))
                {
                    taskRunId = Normalize(canonicalPayload.taskRunId);
                }

                var details = canonicalPayload.details ?? new CanonicalEventDetails();
                var record = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "eventId", Normalize(canonicalPayload.eventId, durableRecord.eventId) },
                    { "sequenceNumber", canonicalPayload.sequenceNumber > 0 ? canonicalPayload.sequenceNumber : durableRecord.sequence },
                    { "eventType", normalizedEventType },
                    { "taskRunId", taskRunId },
                    { "gameId", Normalize(canonicalPayload.gameId) },
                    { "sourceOfTruth", Normalize(canonicalPayload.sourceOfTruth) },
                    { "ownerKey", Normalize(canonicalPayload.ownerKey) },
                    { "sessionKey", Normalize(canonicalPayload.sessionKey) },
                    { "sessionId", Normalize(canonicalPayload.sessionId, durableRecord.sessionId) },
                    { "occurredAtUtc", Normalize(canonicalPayload.occurredAtUtc, durableRecord.createdAtUtc) },
                    { "actionOutcome", Normalize(canonicalPayload.actionOutcome) },
                    { "reasonCode", Normalize(canonicalPayload.reasonCode) },
                    {
                        "details",
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            { "taskRunId", taskRunId },
                            { "completionRatio", details.completionRatio },
                            { "averageReactionSec", details.averageReactionSec },
                            { "confidence", details.confidence },
                            { "difficultyScore", details.difficultyScore },
                            { "cuesPresented", details.cuesPresented },
                            { "actionsObserved", details.actionsObserved },
                            { "correctCount", details.correctCount },
                            { "incorrectCount", details.incorrectCount },
                            { "lateCount", details.lateCount },
                            { "omittedCount", details.omittedCount },
                            { "redundantCount", details.redundantCount },
                            { "recommendedDifficultyLevel", details.recommendedDifficultyLevel },
                        }
                    },
                };

                records.Add(record);
                report.importedEvents++;
            }

            report.loaded = report.importedEvents > 0;
            report.reasonCode = report.loaded ? "TRACE_LOADED" : "TRACE_NO_DATASET_EVENTS";
            return new LoadResult
            {
                report = report,
                records = records,
            };
        }

        private static string ResolveTracePath(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return string.Empty;
            }

            var trimmed = configuredPath.Trim();
            return Path.IsPathRooted(trimmed)
                ? Path.GetFullPath(trimmed)
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), trimmed));
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string Normalize(string first, string second)
        {
            if (!string.IsNullOrWhiteSpace(first))
            {
                return first.Trim();
            }

            return Normalize(second);
        }

        private static string NormalizeEventType(string eventType)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                return string.Empty;
            }

            return eventType.Trim().Replace(' ', '_').ToUpperInvariant();
        }
    }
}
