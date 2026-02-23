using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Exports canonical task records into pre-train artifacts for operators.
    /// </summary>
    public sealed class TaskDatasetPreTrainExporter
    {
        private const string ExpectedSourceOfTruth = "MOBILE_CONTROLLER";
        private const string ManifestSchema = "THERAPLY_PRETRAIN_MANIFEST";
        private const string ManifestVersion = "2026-02-22";
        private const string ExportModeInlineRecords = "INLINE_RECORDS";
        private const string ExportModeDurableSessionTrace = "DURABLE_SESSION_TRACE";

        [Serializable]
        public struct ExportOptions
        {
            public string outputDirectory;
            public string exportName;
        }

        [Serializable]
        public struct SanityCheckReport
        {
            public bool passed;
            public int invalidRecords;
            public int sourceOfTruthViolations;
            public int ownershipViolations;
            public int taskRunConsistencyViolations;
            public int uniqueTaskRuns;
            public int taskOutcomeSummaries;
            public int taskLabels;
            public int adaptiveEvents;
        }

        [Serializable]
        public struct ExportResult
        {
            public bool exportSucceeded;
            public bool readyForTraining;
            public string reasonCode;
            public string exportMode;
            public string outputDirectory;
            public string canonicalDatasetPath;
            public string preTrainManifestPath;
            public string operatorReportPath;
            public int exportedEvents;
            public TaskDatasetQualityGate.DatasetQualityReport qualityReport;
            public SanityCheckReport sanityReport;
            public TaskDatasetSessionTraceLoader.LoadReport traceLoadReport;
        }

        [Serializable]
        private sealed class PreTrainManifest
        {
            public string manifestSchema;
            public string manifestVersion;
            public string generatedAtUtc;
            public string exportName;
            public string exportMode;
            public bool readyForTraining;
            public string reasonCode;
            public string expectedSourceOfTruth;
            public int totalEvents;
            public int uniqueTaskRuns;
            public int taskOutcomeSummaries;
            public int taskLabels;
            public int adaptiveEvents;
            public TaskRunManifestRow[] taskRuns;
            public EventTypeManifestRow[] eventTypes;
            public SanityCheckReport sanity;
            public QualityGateManifest quality;
            public TaskDatasetSessionTraceLoader.LoadReport traceLoad;
            public ArtifactManifest artifacts;
        }

        [Serializable]
        private sealed class TaskRunManifestRow
        {
            public string taskRunId;
            public int summaryCount;
            public int labelCount;
            public int adaptiveCount;
            public string ownerKey;
            public string sessionKey;
        }

        [Serializable]
        private sealed class EventTypeManifestRow
        {
            public string eventType;
            public int count;
        }

        [Serializable]
        private sealed class ArtifactManifest
        {
            public string canonicalDatasetFile;
            public string preTrainManifestFile;
            public string operatorReportFile;
        }

        [Serializable]
        private sealed class QualityGateManifest
        {
            public bool readyForTraining;
            public string reasonCode;
            public float qualityScore;
            public float labelCoverageRatio;
            public float summaryCoverageRatio;
            public float averageLabelConfidence;
            public int missingRequiredFields;
            public int duplicateEventIds;
            public int nonMonotonicSequenceCount;
            public int invalidSourceOfTruthCount;
            public int invalidLabelConfidenceCount;
            public int unmatchedLabelRuns;
        }

        [Serializable]
        private sealed class ExportEventRow
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

        private sealed class TaskRunAccumulator
        {
            public string taskRunId;
            public int summaryCount;
            public int labelCount;
            public int adaptiveCount;
            public string ownerKey;
            public string sessionKey;
        }

        public Task<ExportResult> ExportPreTrainArtifactsAsync(
            IReadOnlyList<IReadOnlyDictionary<string, object>> records,
            TaskDatasetQualityGate.Requirements requirements,
            ExportOptions options)
        {
            var safeRecords = records ?? Array.Empty<IReadOnlyDictionary<string, object>>();
            return Task.Run(() => ExportInternal(
                safeRecords,
                requirements,
                options,
                ExportModeInlineRecords,
                default));
        }

        public Task<ExportResult> ExportPreTrainArtifactsFromDurableTraceAsync(
            TaskDatasetSessionTraceLoader.LoadOptions traceOptions,
            TaskDatasetQualityGate.Requirements requirements,
            ExportOptions options)
        {
            return Task.Run(() =>
            {
                var loader = new TaskDatasetSessionTraceLoader();
                var loadResult = loader.LoadFromDurableTrace(traceOptions);
                var traceReport = loadResult != null ? loadResult.report : default;
                if (loadResult == null || !traceReport.loaded)
                {
                    return new ExportResult
                    {
                        exportSucceeded = false,
                        readyForTraining = false,
                        reasonCode = Normalize(traceReport.reasonCode, "TRACE_LOAD_FAILED"),
                        exportMode = ExportModeDurableSessionTrace,
                        outputDirectory = ResolveOutputDirectory(options.outputDirectory),
                        canonicalDatasetPath = string.Empty,
                        preTrainManifestPath = string.Empty,
                        operatorReportPath = string.Empty,
                        exportedEvents = 0,
                        qualityReport = default,
                        sanityReport = default,
                        traceLoadReport = traceReport,
                    };
                }

                var safeRecords = loadResult.records ?? Array.Empty<IReadOnlyDictionary<string, object>>();
                return ExportInternal(
                    safeRecords,
                    requirements,
                    options,
                    ExportModeDurableSessionTrace,
                    traceReport);
            });
        }

        private ExportResult ExportInternal(
            IReadOnlyList<IReadOnlyDictionary<string, object>> records,
            TaskDatasetQualityGate.Requirements requirements,
            ExportOptions options,
            string exportMode,
            TaskDatasetSessionTraceLoader.LoadReport traceLoadReport)
        {
            var outputDirectory = ResolveOutputDirectory(options.outputDirectory);
            var exportName = ResolveExportName(options.exportName);
            Directory.CreateDirectory(outputDirectory);

            var qualityGate = new TaskDatasetQualityGate();
            var qualityReport = qualityGate.Evaluate(records, requirements);
            var sanityReport = EvaluateSanity(records, out var taskRunRows, out var eventTypeRows);
            var readyForTraining = qualityReport.readyForTraining && sanityReport.passed;
            var reasonCode = ResolveReasonCode(qualityReport, sanityReport);

            var datasetPath = Path.Combine(outputDirectory, "canonical_events.ndjson");
            var manifestPath = Path.Combine(outputDirectory, "pretrain_manifest.json");
            var reportPath = Path.Combine(outputDirectory, "operator_report.md");

            WriteDatasetFile(datasetPath, records);
            WriteManifestFile(
                manifestPath,
                exportName,
                exportMode,
                readyForTraining,
                reasonCode,
                records.Count,
                sanityReport,
                qualityReport,
                taskRunRows,
                eventTypeRows,
                traceLoadReport,
                datasetPath,
                reportPath);
            WriteReportFile(
                reportPath,
                exportName,
                exportMode,
                outputDirectory,
                readyForTraining,
                reasonCode,
                sanityReport,
                qualityReport,
                taskRunRows,
                traceLoadReport,
                datasetPath,
                manifestPath);

            return new ExportResult
            {
                exportSucceeded = true,
                readyForTraining = readyForTraining,
                reasonCode = reasonCode,
                exportMode = exportMode,
                outputDirectory = outputDirectory,
                canonicalDatasetPath = datasetPath,
                preTrainManifestPath = manifestPath,
                operatorReportPath = reportPath,
                exportedEvents = records.Count,
                qualityReport = qualityReport,
                sanityReport = sanityReport,
                traceLoadReport = traceLoadReport,
            };
        }

        private static void WriteDatasetFile(
            string datasetPath,
            IReadOnlyList<IReadOnlyDictionary<string, object>> records)
        {
            using (var writer = new StreamWriter(datasetPath, false, Encoding.UTF8))
            {
                for (var i = 0; i < records.Count; i++)
                {
                    var record = records[i];
                    if (record == null)
                    {
                        continue;
                    }

                    var row = ToExportEventRow(record);
                    writer.WriteLine(JsonUtility.ToJson(row));
                }
            }
        }

        private static void WriteManifestFile(
            string manifestPath,
            string exportName,
            string exportMode,
            bool readyForTraining,
            string reasonCode,
            int totalEvents,
            SanityCheckReport sanityReport,
            TaskDatasetQualityGate.DatasetQualityReport qualityReport,
            TaskRunManifestRow[] taskRunRows,
            EventTypeManifestRow[] eventTypeRows,
            TaskDatasetSessionTraceLoader.LoadReport traceLoadReport,
            string datasetPath,
            string reportPath)
        {
            var manifest = new PreTrainManifest
            {
                manifestSchema = ManifestSchema,
                manifestVersion = ManifestVersion,
                generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                exportName = exportName,
                exportMode = Normalize(exportMode, ExportModeInlineRecords),
                readyForTraining = readyForTraining,
                reasonCode = reasonCode,
                expectedSourceOfTruth = ExpectedSourceOfTruth,
                totalEvents = totalEvents,
                uniqueTaskRuns = sanityReport.uniqueTaskRuns,
                taskOutcomeSummaries = sanityReport.taskOutcomeSummaries,
                taskLabels = sanityReport.taskLabels,
                adaptiveEvents = sanityReport.adaptiveEvents,
                taskRuns = taskRunRows ?? Array.Empty<TaskRunManifestRow>(),
                eventTypes = eventTypeRows ?? Array.Empty<EventTypeManifestRow>(),
                sanity = sanityReport,
                quality = new QualityGateManifest
                {
                    readyForTraining = qualityReport.readyForTraining,
                    reasonCode = Normalize(qualityReport.reasonCode, "DATASET_NOT_READY"),
                    qualityScore = qualityReport.qualityScore,
                    labelCoverageRatio = qualityReport.labelCoverageRatio,
                    summaryCoverageRatio = qualityReport.summaryCoverageRatio,
                    averageLabelConfidence = qualityReport.averageLabelConfidence,
                    missingRequiredFields = qualityReport.missingRequiredFields,
                    duplicateEventIds = qualityReport.duplicateEventIds,
                    nonMonotonicSequenceCount = qualityReport.nonMonotonicSequenceCount,
                    invalidSourceOfTruthCount = qualityReport.invalidSourceOfTruthCount,
                    invalidLabelConfidenceCount = qualityReport.invalidLabelConfidenceCount,
                    unmatchedLabelRuns = qualityReport.unmatchedLabelRuns,
                },
                traceLoad = traceLoadReport,
                artifacts = new ArtifactManifest
                {
                    canonicalDatasetFile = Path.GetFileName(datasetPath) ?? "canonical_events.ndjson",
                    preTrainManifestFile = Path.GetFileName(manifestPath) ?? "pretrain_manifest.json",
                    operatorReportFile = Path.GetFileName(reportPath) ?? "operator_report.md",
                },
            };

            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true), Encoding.UTF8);
        }

        private static void WriteReportFile(
            string reportPath,
            string exportName,
            string exportMode,
            string outputDirectory,
            bool readyForTraining,
            string reasonCode,
            SanityCheckReport sanityReport,
            TaskDatasetQualityGate.DatasetQualityReport qualityReport,
            TaskRunManifestRow[] taskRunRows,
            TaskDatasetSessionTraceLoader.LoadReport traceLoadReport,
            string datasetPath,
            string manifestPath)
        {
            var lines = new List<string>
            {
                "# OPS-002 Dataset Export Operator Report",
                string.Empty,
                "- generatedUtc: " + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                "- exportName: " + exportName,
                "- exportMode: " + Normalize(exportMode, ExportModeInlineRecords),
                "- outputDirectory: " + outputDirectory,
                "- readyForTraining: " + readyForTraining.ToString().ToUpperInvariant(),
                "- reasonCode: " + reasonCode,
                string.Empty,
                "## Artifact Paths",
                string.Empty,
                "- canonicalDataset: `" + datasetPath + "`",
                "- preTrainManifest: `" + manifestPath + "`",
                "- operatorReport: `" + reportPath + "`",
                string.Empty,
                "## Trace Load",
                string.Empty,
                "- loaded: " + traceLoadReport.loaded.ToString().ToUpperInvariant(),
                "- reasonCode: " + Normalize(traceLoadReport.reasonCode, "INLINE_RECORDS"),
                "- durableTracePath: " + Normalize(traceLoadReport.durableTracePath, "<inline_records>"),
                "- sessionIdFilter: " + Normalize(traceLoadReport.sessionIdFilter, "<none>"),
                "- totalLines: " + traceLoadReport.totalLines.ToString(CultureInfo.InvariantCulture),
                "- importedEvents: " + traceLoadReport.importedEvents.ToString(CultureInfo.InvariantCulture),
                "- skippedBySession: " + traceLoadReport.skippedBySession.ToString(CultureInfo.InvariantCulture),
                "- skippedUnsupportedEventType: " + traceLoadReport.skippedUnsupportedEventType.ToString(CultureInfo.InvariantCulture),
                "- invalidLines: " + traceLoadReport.invalidLines.ToString(CultureInfo.InvariantCulture),
                string.Empty,
                "## Sanity",
                string.Empty,
                "- sourceOfTruthViolations: " + sanityReport.sourceOfTruthViolations.ToString(CultureInfo.InvariantCulture),
                "- ownershipViolations: " + sanityReport.ownershipViolations.ToString(CultureInfo.InvariantCulture),
                "- taskRunConsistencyViolations: " + sanityReport.taskRunConsistencyViolations.ToString(CultureInfo.InvariantCulture),
                "- invalidRecords: " + sanityReport.invalidRecords.ToString(CultureInfo.InvariantCulture),
                string.Empty,
                "## Quality Gate",
                string.Empty,
                "- readyForTraining: " + qualityReport.readyForTraining.ToString().ToUpperInvariant(),
                "- reasonCode: " + Normalize(qualityReport.reasonCode, "DATASET_NOT_READY"),
                "- qualityScore: " + qualityReport.qualityScore.ToString("F3", CultureInfo.InvariantCulture),
                "- labelCoverageRatio: " + qualityReport.labelCoverageRatio.ToString("F3", CultureInfo.InvariantCulture),
                "- summaryCoverageRatio: " + qualityReport.summaryCoverageRatio.ToString("F3", CultureInfo.InvariantCulture),
                "- averageLabelConfidence: " + qualityReport.averageLabelConfidence.ToString("F3", CultureInfo.InvariantCulture),
                string.Empty,
                "## Task Runs",
                string.Empty,
                "| taskRunId | summaries | labels | adaptiveEvents | ownerKey | sessionKey |",
                "| --- | ---: | ---: | ---: | --- | --- |",
            };

            var safeTaskRunRows = taskRunRows ?? Array.Empty<TaskRunManifestRow>();
            for (var i = 0; i < safeTaskRunRows.Length; i++)
            {
                var row = safeTaskRunRows[i];
                lines.Add(
                    "| " + row.taskRunId +
                    " | " + row.summaryCount.ToString(CultureInfo.InvariantCulture) +
                    " | " + row.labelCount.ToString(CultureInfo.InvariantCulture) +
                    " | " + row.adaptiveCount.ToString(CultureInfo.InvariantCulture) +
                    " | " + row.ownerKey +
                    " | " + row.sessionKey +
                    " |");
            }

            File.WriteAllLines(reportPath, lines, Encoding.UTF8);
        }

        private static ExportEventRow ToExportEventRow(IReadOnlyDictionary<string, object> record)
        {
            return new ExportEventRow
            {
                eventId = ReadString(record, "eventId"),
                sequenceNumber = ReadLong(record, "sequenceNumber"),
                eventType = ReadString(record, "eventType"),
                taskRunId = ResolveTaskRunId(record),
                gameId = ReadString(record, "gameId"),
                sourceOfTruth = ReadString(record, "sourceOfTruth"),
                ownerKey = ReadString(record, "ownerKey"),
                sessionKey = ReadString(record, "sessionKey"),
                sessionId = ReadString(record, "sessionId"),
                occurredAtUtc = ReadString(record, "occurredAtUtc"),
                actionOutcome = ReadString(record, "actionOutcome"),
                reasonCode = ReadString(record, "reasonCode"),
                completionRatio = ReadDetailsFloat(record, "completionRatio"),
                averageReactionSec = ReadDetailsFloat(record, "averageReactionSec"),
                confidence = ReadDetailsFloat(record, "confidence"),
                difficultyScore = ReadDetailsFloat(record, "difficultyScore"),
                cuesPresented = ReadDetailsInt(record, "cuesPresented"),
                actionsObserved = ReadDetailsInt(record, "actionsObserved"),
                correctCount = ReadDetailsInt(record, "correctCount"),
                incorrectCount = ReadDetailsInt(record, "incorrectCount"),
                lateCount = ReadDetailsInt(record, "lateCount"),
                omittedCount = ReadDetailsInt(record, "omittedCount"),
                redundantCount = ReadDetailsInt(record, "redundantCount"),
                recommendedDifficultyLevel = ReadDetailsInt(record, "recommendedDifficultyLevel"),
            };
        }

        private static SanityCheckReport EvaluateSanity(
            IReadOnlyList<IReadOnlyDictionary<string, object>> records,
            out TaskRunManifestRow[] taskRunRows,
            out EventTypeManifestRow[] eventTypeRows)
        {
            var taskRuns = new Dictionary<string, TaskRunAccumulator>(StringComparer.Ordinal);
            var eventTypes = new Dictionary<string, int>(StringComparer.Ordinal);
            var invalidRecords = 0;
            var sourceOfTruthViolations = 0;
            var ownershipViolations = 0;
            var taskRunConsistencyViolations = 0;
            var summaries = 0;
            var labels = 0;
            var adaptive = 0;

            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (record == null)
                {
                    invalidRecords++;
                    continue;
                }

                var eventType = ReadString(record, "eventType");
                if (!string.IsNullOrWhiteSpace(eventType))
                {
                    if (!eventTypes.TryGetValue(eventType, out var count))
                    {
                        eventTypes[eventType] = 1;
                    }
                    else
                    {
                        eventTypes[eventType] = count + 1;
                    }
                }

                if (!string.Equals(ReadString(record, "sourceOfTruth"), ExpectedSourceOfTruth, StringComparison.Ordinal))
                {
                    sourceOfTruthViolations++;
                }

                var ownerKey = ReadString(record, "ownerKey");
                var sessionKey = ReadString(record, "sessionKey");
                var sessionId = ReadString(record, "sessionId");
                if (string.IsNullOrWhiteSpace(ownerKey) ||
                    string.IsNullOrWhiteSpace(sessionKey) ||
                    !sessionKey.StartsWith(ownerKey + "|", StringComparison.Ordinal) ||
                    (!string.IsNullOrWhiteSpace(sessionId) && !string.Equals(sessionKey, ownerKey + "|" + sessionId, StringComparison.Ordinal)))
                {
                    ownershipViolations++;
                }

                var taskRunId = ResolveTaskRunId(record);
                if (string.IsNullOrWhiteSpace(taskRunId))
                {
                    taskRunConsistencyViolations++;
                    continue;
                }

                if (!taskRuns.TryGetValue(taskRunId, out var accumulator))
                {
                    accumulator = new TaskRunAccumulator
                    {
                        taskRunId = taskRunId,
                        ownerKey = ownerKey,
                        sessionKey = sessionKey,
                    };
                    taskRuns[taskRunId] = accumulator;
                }

                if (string.Equals(eventType, "TASK_OUTCOME_SUMMARY", StringComparison.Ordinal))
                {
                    accumulator.summaryCount++;
                    summaries++;
                }
                else if (string.Equals(eventType, "TASK_LABEL_GENERATED", StringComparison.Ordinal))
                {
                    accumulator.labelCount++;
                    labels++;
                }
                else if (string.Equals(eventType, "ADAPTIVE_DIFFICULTY_ADJUSTED", StringComparison.Ordinal))
                {
                    accumulator.adaptiveCount++;
                    adaptive++;
                }
            }

            foreach (var run in taskRuns.Values)
            {
                if (run.summaryCount <= 0 || run.labelCount <= 0 || run.summaryCount != run.labelCount)
                {
                    taskRunConsistencyViolations++;
                }
            }

            if (taskRuns.Count <= 0)
            {
                taskRunConsistencyViolations++;
            }

            taskRunRows = taskRuns.Values
                .OrderBy(value => value.taskRunId, StringComparer.Ordinal)
                .Select(value => new TaskRunManifestRow
                {
                    taskRunId = value.taskRunId,
                    summaryCount = value.summaryCount,
                    labelCount = value.labelCount,
                    adaptiveCount = value.adaptiveCount,
                    ownerKey = Normalize(value.ownerKey, string.Empty),
                    sessionKey = Normalize(value.sessionKey, string.Empty),
                })
                .ToArray();

            eventTypeRows = eventTypes
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(value => new EventTypeManifestRow
                {
                    eventType = value.Key,
                    count = value.Value,
                })
                .ToArray();

            return new SanityCheckReport
            {
                passed = invalidRecords == 0 &&
                         sourceOfTruthViolations == 0 &&
                         ownershipViolations == 0 &&
                         taskRunConsistencyViolations == 0,
                invalidRecords = invalidRecords,
                sourceOfTruthViolations = sourceOfTruthViolations,
                ownershipViolations = ownershipViolations,
                taskRunConsistencyViolations = taskRunConsistencyViolations,
                uniqueTaskRuns = taskRuns.Count,
                taskOutcomeSummaries = summaries,
                taskLabels = labels,
                adaptiveEvents = adaptive,
            };
        }

        private static string ResolveReasonCode(
            TaskDatasetQualityGate.DatasetQualityReport qualityReport,
            SanityCheckReport sanityReport)
        {
            if (sanityReport.invalidRecords > 0)
            {
                return "EXPORT_INVALID_RECORDS";
            }

            if (sanityReport.sourceOfTruthViolations > 0)
            {
                return "EXPORT_SOURCE_OF_TRUTH_VIOLATIONS";
            }

            if (sanityReport.ownershipViolations > 0)
            {
                return "EXPORT_OWNERSHIP_VIOLATIONS";
            }

            if (sanityReport.taskRunConsistencyViolations > 0)
            {
                return "EXPORT_TASK_RUN_INCONSISTENT";
            }

            if (!qualityReport.readyForTraining)
            {
                return Normalize(qualityReport.reasonCode, "DATASET_NOT_READY");
            }

            return "EXPORT_READY_FOR_TRAINING";
        }

        private static string ResolveTaskRunId(IReadOnlyDictionary<string, object> record)
        {
            var detailsTaskRunId = ReadDetailsString(record, "taskRunId");
            if (!string.IsNullOrWhiteSpace(detailsTaskRunId))
            {
                return detailsTaskRunId;
            }

            return ReadString(record, "taskRunId");
        }

        private static string ResolveOutputDirectory(string configuredOutputDirectory)
        {
            if (!string.IsNullOrWhiteSpace(configuredOutputDirectory))
            {
                return Path.GetFullPath(configuredOutputDirectory.Trim());
            }

            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Temp", "CliValidation", "ops_dataset_export"));
        }

        private static string ResolveExportName(string configuredExportName)
        {
            if (!string.IsNullOrWhiteSpace(configuredExportName))
            {
                return configuredExportName.Trim();
            }

            return "ops_dataset_export_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        }

        private static string ReadString(IReadOnlyDictionary<string, object> record, string key)
        {
            if (record == null || !record.TryGetValue(key, out var value) || value == null)
            {
                return string.Empty;
            }

            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
        }

        private static long ReadLong(IReadOnlyDictionary<string, object> record, string key)
        {
            if (record == null || !record.TryGetValue(key, out var value) || value == null)
            {
                return 0L;
            }

            switch (value)
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

            return long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0L;
        }

        private static int ReadDetailsInt(IReadOnlyDictionary<string, object> record, string key)
        {
            if (!TryReadDetailsValue(record, key, out var value) || value == null)
            {
                return 0;
            }

            switch (value)
            {
                case int intValue:
                    return intValue;
                case long longValue:
                    return (int)longValue;
                case float floatValue:
                    return Mathf.RoundToInt(floatValue);
                case double doubleValue:
                    return (int)Math.Round(doubleValue, MidpointRounding.AwayFromZero);
            }

            return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
        }

        private static float ReadDetailsFloat(IReadOnlyDictionary<string, object> record, string key)
        {
            if (!TryReadDetailsValue(record, key, out var value) || value == null)
            {
                return 0f;
            }

            switch (value)
            {
                case int intValue:
                    return intValue;
                case long longValue:
                    return longValue;
                case float floatValue:
                    return floatValue;
                case double doubleValue:
                    return (float)doubleValue;
            }

            return float.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0f;
        }

        private static string ReadDetailsString(IReadOnlyDictionary<string, object> record, string key)
        {
            if (!TryReadDetailsValue(record, key, out var value) || value == null)
            {
                return string.Empty;
            }

            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
        }

        private static bool TryReadDetailsValue(
            IReadOnlyDictionary<string, object> record,
            string key,
            out object value)
        {
            value = null;
            if (record == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (!record.TryGetValue("details", out var detailsValue) || detailsValue == null)
            {
                return false;
            }

            if (detailsValue is IReadOnlyDictionary<string, object> readOnlyMap)
            {
                return readOnlyMap.TryGetValue(key, out value);
            }

            if (detailsValue is Dictionary<string, object> mutableMap)
            {
                return mutableMap.TryGetValue(key, out value);
            }

            if (detailsValue is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    var entryKey = Convert.ToString(entry.Key, CultureInfo.InvariantCulture);
                    if (string.Equals(entryKey, key, StringComparison.Ordinal))
                    {
                        value = entry.Value;
                        return true;
                    }
                }
            }

            return false;
        }

        private static string Normalize(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
