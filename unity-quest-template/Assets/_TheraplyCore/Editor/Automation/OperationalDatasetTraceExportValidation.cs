using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using TheraplyCore.Interactions;
using UnityEditor;
using UnityEngine;

namespace TheraplyCore.Editor.Automation
{
    public static class OperationalDatasetTraceExportValidation
    {
        private const string ValidationGameId = "pulse_target_tap";
        private const string ValidationSource = "OperationalDatasetTraceExportValidation";
        private const string TraceEventType = "interaction_event";
        private const string ExportOutputDirectoryEnvironmentVariable = "THERAPLY_DATASET_EXPORT_OUTPUT";
        private const string ExportNameEnvironmentVariable = "THERAPLY_DATASET_EXPORT_NAME";
        private const string TraceInputEnvironmentVariable = "THERAPLY_DATASET_TRACE_INPUT";
        private const string TraceSessionIdEnvironmentVariable = "THERAPLY_DATASET_TRACE_SESSION_ID";
        private const string TraceRequireProvidedEnvironmentVariable = "THERAPLY_DATASET_TRACE_REQUIRE_PROVIDED";

        [Serializable]
        private sealed class DurableTraceFixtureRecord
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

        public static void RunOperationalDatasetTraceExportValidation()
        {
            try
            {
                var summary = ExecuteValidation();
                Debug.Log("[OperationalDatasetTraceExportValidation] PASS: " + summary);
                PersistValidationResult("PASS", summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[OperationalDatasetTraceExportValidation] FAIL: " + exception);
                PersistValidationResult("FAIL", exception.ToString());
                EditorApplication.Exit(1);
            }
        }

        private static string ExecuteValidation()
        {
            var outputDirectory = ResolveExportOutputDirectory();
            Directory.CreateDirectory(outputDirectory);

            var traceInputPath = ResolveTraceInputPath();
            var traceSessionIdFilter = ResolveTraceSessionId();
            var requireProvidedTrace = ResolveRequireProvidedTrace();
            var providedTraceConfigured = !string.IsNullOrWhiteSpace(traceInputPath);
            var generatedTraceFixture = false;

            if (string.IsNullOrWhiteSpace(traceInputPath))
            {
                if (requireProvidedTrace)
                {
                    throw new InvalidOperationException(
                        "Trace input path is required when provided-trace mode is enabled.");
                }

                var fixtureRecords = BuildSyntheticDatasetRecords();
                traceInputPath = Path.Combine(outputDirectory, "session_trace_input.ndjson");
                WriteDurableTraceFixture(traceInputPath, fixtureRecords);
                generatedTraceFixture = true;

                if (string.IsNullOrWhiteSpace(traceSessionIdFilter))
                {
                    traceSessionIdFilter = "validation_session";
                }
            }

            if (!File.Exists(traceInputPath))
            {
                throw new FileNotFoundException("Trace input file does not exist.", traceInputPath);
            }

            var exporter = new TaskDatasetPreTrainExporter();
            var requirements = TaskDatasetQualityGate.CreateDefaultRequirements();
            var exportOptions = new TaskDatasetPreTrainExporter.ExportOptions
            {
                outputDirectory = outputDirectory,
                exportName = ResolveExportName(),
            };
            var traceOptions = new TaskDatasetSessionTraceLoader.LoadOptions
            {
                durableTracePath = traceInputPath,
                sessionId = traceSessionIdFilter,
            };

            var exportResult = exporter
                .ExportPreTrainArtifactsFromDurableTraceAsync(traceOptions, requirements, exportOptions)
                .GetAwaiter()
                .GetResult();

            if (!exportResult.exportSucceeded)
            {
                throw new InvalidOperationException(
                    $"Trace export returned unsuccessful status. reason={exportResult.reasonCode}");
            }

            if (!string.Equals(exportResult.exportMode, "DURABLE_SESSION_TRACE", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unexpected export mode for trace export lane.");
            }

            if (!exportResult.traceLoadReport.loaded || exportResult.traceLoadReport.importedEvents <= 0)
            {
                throw new InvalidOperationException(
                    $"Trace import did not load events. reason={exportResult.traceLoadReport.reasonCode}");
            }

            if (providedTraceConfigured && !ArePathsEquivalent(traceInputPath, exportResult.traceLoadReport.durableTracePath))
            {
                throw new InvalidOperationException(
                    $"Trace load report path mismatch. expected={traceInputPath}; actual={exportResult.traceLoadReport.durableTracePath}");
            }

            if (requireProvidedTrace && generatedTraceFixture)
            {
                throw new InvalidOperationException(
                    "Provided-trace mode must not fall back to generated fixture.");
            }

            if (!exportResult.readyForTraining)
            {
                throw new InvalidOperationException(
                    $"Trace export is not ready for training. reason={exportResult.reasonCode}");
            }

            if (!exportResult.sanityReport.passed ||
                exportResult.sanityReport.sourceOfTruthViolations > 0 ||
                exportResult.sanityReport.ownershipViolations > 0 ||
                exportResult.sanityReport.taskRunConsistencyViolations > 0)
            {
                throw new InvalidOperationException(
                    "Sanity checks reported violations in exported dataset from trace input.");
            }

            if (!File.Exists(exportResult.canonicalDatasetPath))
            {
                throw new FileNotFoundException("Canonical export file missing.", exportResult.canonicalDatasetPath);
            }

            if (!File.Exists(exportResult.preTrainManifestPath))
            {
                throw new FileNotFoundException("Pre-train manifest file missing.", exportResult.preTrainManifestPath);
            }

            if (!File.Exists(exportResult.operatorReportPath))
            {
                throw new FileNotFoundException("Operator report file missing.", exportResult.operatorReportPath);
            }

            var exportedLineCount = File.ReadLines(exportResult.canonicalDatasetPath)
                .Count(line => !string.IsNullOrWhiteSpace(line));
            if (exportedLineCount != exportResult.traceLoadReport.importedEvents)
            {
                throw new InvalidOperationException(
                    $"Unexpected exported event count. imported={exportResult.traceLoadReport.importedEvents}, actual={exportedLineCount}");
            }

            var manifestContent = File.ReadAllText(exportResult.preTrainManifestPath);
            if (string.IsNullOrWhiteSpace(manifestContent) ||
                manifestContent.IndexOf("THERAPLY_PRETRAIN_MANIFEST", StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException("Manifest content missing expected schema marker.");
            }

            if (manifestContent.IndexOf("\"exportMode\": \"DURABLE_SESSION_TRACE\"", StringComparison.Ordinal) < 0 &&
                manifestContent.IndexOf("\"exportMode\":\"DURABLE_SESSION_TRACE\"", StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException("Manifest does not declare durable trace export mode.");
            }

            if (manifestContent.IndexOf("\"readyForTraining\": true", StringComparison.Ordinal) < 0 &&
                manifestContent.IndexOf("\"readyForTraining\":true", StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException("Manifest does not declare readyForTraining=true.");
            }

            var projectRoot = Directory.GetCurrentDirectory();
            var relativeTracePath = ToRelativePath(projectRoot, traceInputPath);
            var relativeManifestPath = ToRelativePath(projectRoot, exportResult.preTrainManifestPath);
            var relativeReportPath = ToRelativePath(projectRoot, exportResult.operatorReportPath);

            return string.Format(
                CultureInfo.InvariantCulture,
                "events={0}; taskRuns={1}; labels={2}; summaries={3}; sourceOfTruthViolations={4}; ownershipViolations={5}; taskRunConsistencyViolations={6}; traceImported={7}; traceLines={8}; traceReason={9}; traceSource={10}; exportReason={11}; qualityReason={12}; tracePath={13}; manifest={14}; report={15}; requireProvidedTrace={16}",
                exportResult.exportedEvents,
                exportResult.sanityReport.uniqueTaskRuns,
                exportResult.sanityReport.taskLabels,
                exportResult.sanityReport.taskOutcomeSummaries,
                exportResult.sanityReport.sourceOfTruthViolations,
                exportResult.sanityReport.ownershipViolations,
                exportResult.sanityReport.taskRunConsistencyViolations,
                exportResult.traceLoadReport.importedEvents,
                exportResult.traceLoadReport.totalLines,
                exportResult.traceLoadReport.reasonCode ?? string.Empty,
                generatedTraceFixture ? "GENERATED_FIXTURE" : "PROVIDED_TRACE",
                exportResult.reasonCode ?? string.Empty,
                exportResult.qualityReport.reasonCode ?? string.Empty,
                relativeTracePath,
                relativeManifestPath,
                relativeReportPath,
                requireProvidedTrace ? "TRUE" : "FALSE");
        }

        private static List<IReadOnlyDictionary<string, object>> BuildSyntheticDatasetRecords()
        {
            var datasetRecords = new List<IReadOnlyDictionary<string, object>>();
            var createdObjects = new List<UnityEngine.Object>();

            try
            {
                DestroyExistingSingleton<InteractionEventBridge>();

                var host = new GameObject("OperationalDatasetTraceExportValidationHost");
                createdObjects.Add(host);

                var sessionContext = host.AddComponent<GameSessionContext>();
                SetNonPublicField(sessionContext, "_autoStartSessionOnAwake", false);
                InvokeNonPublic(sessionContext, "Awake");
                sessionContext.BeginSession("validation_student", "validation_therapist", "validation_session");
                sessionContext.TryTransitionTo(SessionLifecycleState.IN_PROGRESS, "VALIDATION_START");

                var telemetryService = host.AddComponent<GameTelemetryService>();
                SetNonPublicField(telemetryService, "_sessionContext", sessionContext);
                SetNonPublicField(telemetryService, "_firebaseDataService", null);
                SetNonPublicField(telemetryService, "_logTelemetry", false);
                InvokeNonPublic(telemetryService, "Awake");

                var runtimeService = host.AddComponent<GameRuntimeService>();
                SetNonPublicField(runtimeService, "_sessionContext", sessionContext);

                var bridge = host.AddComponent<InteractionEventBridge>();
                SetNonPublicField(bridge, "_gameTelemetryService", telemetryService);
                SetNonPublicField(bridge, "_sessionContext", sessionContext);
                SetNonPublicField(bridge, "_gameRuntimeService", runtimeService);
                SetNonPublicField(bridge, "_emitCanonicalEvents", true);
                SetNonPublicField(bridge, "_logCanonicalEvents", false);
                InvokeNonPublic(bridge, "Awake");

                bridge.EventPublished += payload =>
                {
                    var record = payload != null
                        ? new Dictionary<string, object>(payload)
                        : new Dictionary<string, object>();
                    datasetRecords.Add(record);
                };

                var adaptiveController = new AdaptiveDifficultyController();
                var adaptivePolicy = AdaptiveDifficultyController.CreateDefaultPolicy();
                adaptivePolicy.enabled = true;
                adaptivePolicy.sensitivity = 0.82f;
                adaptivePolicy.minTargetSpeed = 0.35f;
                adaptivePolicy.maxTargetSpeed = 1.7f;
                adaptivePolicy.minTargetScale = 0.16f;
                adaptivePolicy.maxTargetScale = 0.66f;
                adaptivePolicy.minCueTimeoutSec = 0.6f;
                adaptivePolicy.maxCueTimeoutSec = 3.2f;
                adaptiveController.Configure(adaptivePolicy, 0.5f);

                var labelPipeline = new TaskLabelPipeline();

                EmitTaskRun(
                    bridge,
                    adaptiveController,
                    labelPipeline,
                    taskRunId: Guid.NewGuid().ToString(),
                    summary: new TaskOutcomeAggregator.TaskOutcomeSummary
                    {
                        gameId = ValidationGameId,
                        cuesPresented = 8,
                        actionsObserved = 8,
                        correctCount = 2,
                        incorrectCount = 2,
                        lateCount = 2,
                        omittedCount = 2,
                        redundantCount = 0,
                        firstActionLatencySec = 1.2f,
                        averageReactionSec = 1.08f,
                        completionRatio = 0.25f,
                        elapsedSec = 18.1f,
                    },
                    signalReasonCode: "TASK_RUN_1");

                EmitTaskRun(
                    bridge,
                    adaptiveController,
                    labelPipeline,
                    taskRunId: Guid.NewGuid().ToString(),
                    summary: new TaskOutcomeAggregator.TaskOutcomeSummary
                    {
                        gameId = ValidationGameId,
                        cuesPresented = 8,
                        actionsObserved = 8,
                        correctCount = 5,
                        incorrectCount = 1,
                        lateCount = 1,
                        omittedCount = 1,
                        redundantCount = 0,
                        firstActionLatencySec = 0.6f,
                        averageReactionSec = 0.72f,
                        completionRatio = 0.63f,
                        elapsedSec = 12.4f,
                    },
                    signalReasonCode: "TASK_RUN_2");

                EmitTaskRun(
                    bridge,
                    adaptiveController,
                    labelPipeline,
                    taskRunId: Guid.NewGuid().ToString(),
                    summary: new TaskOutcomeAggregator.TaskOutcomeSummary
                    {
                        gameId = ValidationGameId,
                        cuesPresented = 8,
                        actionsObserved = 8,
                        correctCount = 8,
                        incorrectCount = 0,
                        lateCount = 0,
                        omittedCount = 0,
                        redundantCount = 0,
                        firstActionLatencySec = 0.22f,
                        averageReactionSec = 0.31f,
                        completionRatio = 1f,
                        elapsedSec = 7.2f,
                    },
                    signalReasonCode: "TASK_RUN_3");

                return datasetRecords;
            }
            finally
            {
                for (var i = createdObjects.Count - 1; i >= 0; i--)
                {
                    if (createdObjects[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(createdObjects[i]);
                    }
                }
            }
        }

        private static void WriteDurableTraceFixture(
            string tracePath,
            IReadOnlyList<IReadOnlyDictionary<string, object>> datasetRecords)
        {
            var safeRecords = datasetRecords ?? Array.Empty<IReadOnlyDictionary<string, object>>();
            var directory = Path.GetDirectoryName(tracePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var writer = new StreamWriter(tracePath, false, Encoding.UTF8))
            {
                for (var i = 0; i < safeRecords.Count; i++)
                {
                    var record = safeRecords[i];
                    if (record == null)
                    {
                        continue;
                    }

                    var durable = new DurableTraceFixtureRecord
                    {
                        eventId = ReadString(record, "eventId"),
                        sessionId = ReadString(record, "sessionId"),
                        patientId = ReadString(record, "patientId"),
                        therapistId = ReadString(record, "therapistId"),
                        deviceId = "validation_device",
                        sequence = ReadLong(record, "sequenceNumber"),
                        eventType = TraceEventType,
                        eventVersion = 1,
                        createdAtUtc = ReadString(record, "occurredAtUtc"),
                        payloadJson = SerializePayloadDictionary(record),
                        checksum = string.Empty,
                    };

                    if (string.IsNullOrWhiteSpace(durable.createdAtUtc))
                    {
                        durable.createdAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                    }

                    writer.WriteLine(JsonUtility.ToJson(durable));
                }
            }
        }
        private static void EmitTaskRun(
            InteractionEventBridge bridge,
            AdaptiveDifficultyController adaptiveController,
            TaskLabelPipeline labelPipeline,
            string taskRunId,
            TaskOutcomeAggregator.TaskOutcomeSummary summary,
            string signalReasonCode)
        {
            var normalizedTaskRunId = string.IsNullOrWhiteSpace(taskRunId)
                ? Guid.NewGuid().ToString()
                : taskRunId.Trim();
            summary.taskRunId = normalizedTaskRunId;
            summary.gameId = string.IsNullOrWhiteSpace(summary.gameId) ? ValidationGameId : summary.gameId;

            var decision = adaptiveController.Evaluate(summary);
            var label = labelPipeline.CreateTaskLabel(summary, decision);

            bridge.RecordGameplayEvent(
                ValidationGameId,
                "task_outcome_summary",
                "PLAYING",
                new Dictionary<string, object>
                {
                    { "taskRunId", normalizedTaskRunId },
                    { "cuesPresented", summary.cuesPresented },
                    { "actionsObserved", summary.actionsObserved },
                    { "correctCount", summary.correctCount },
                    { "incorrectCount", summary.incorrectCount },
                    { "lateCount", summary.lateCount },
                    { "omittedCount", summary.omittedCount },
                    { "redundantCount", summary.redundantCount },
                    { "firstActionLatencySec", summary.firstActionLatencySec },
                    { "averageReactionSec", summary.averageReactionSec },
                    { "completionRatio", summary.completionRatio },
                    { "elapsedSec", summary.elapsedSec },
                    { "actionOutcome", summary.completionRatio >= 0.999f ? "CORRECT" : "OBSERVED" },
                    { "reasonCode", "TASK_SUMMARY_EMITTED" },
                    { "adaptiveDifficultyScore", decision.nextDifficulty },
                    { "adaptiveDifficultyReasonCode", decision.reasonCode ?? string.Empty },
                },
                ValidationSource);

            bridge.RecordGameplayEvent(
                ValidationGameId,
                "adaptive_difficulty_adjusted",
                "PLAYING",
                new Dictionary<string, object>
                {
                    { "taskRunId", normalizedTaskRunId },
                    { "signalReasonCode", string.IsNullOrWhiteSpace(signalReasonCode) ? "TASK_SIGNAL" : signalReasonCode.Trim() },
                    { "previousDifficulty", decision.previousDifficulty },
                    { "nextDifficulty", decision.nextDifficulty },
                    { "difficultyDelta", decision.delta },
                    { "targetSpeed", decision.targetSpeed },
                    { "targetScale", decision.targetScale },
                    { "cueTimeoutSec", decision.cueTimeoutSec },
                    { "completionRatio", decision.completionRatio },
                    { "averageReactionSec", decision.averageReactionSec },
                    { "omittedRatio", decision.omittedRatio },
                    { "lateRatio", decision.lateRatio },
                    { "actionOutcome", decision.delta >= 0f ? "CORRECT" : "INCORRECT" },
                    { "reasonCode", string.IsNullOrWhiteSpace(decision.reasonCode) ? "KEEP_DIFFICULTY" : decision.reasonCode },
                },
                ValidationSource);

            bridge.RecordGameplayEvent(
                ValidationGameId,
                "task_label_generated",
                "PLAYING",
                new Dictionary<string, object>
                {
                    { "taskRunId", normalizedTaskRunId },
                    { "labelId", label.labelId ?? string.Empty },
                    { "labelSchema", label.labelSchema ?? string.Empty },
                    { "labelVersion", label.labelVersion ?? string.Empty },
                    { "labelType", label.labelType ?? string.Empty },
                    { "performanceBand", label.performanceBand ?? string.Empty },
                    { "paceBand", label.paceBand ?? string.Empty },
                    { "fatigueBand", label.fatigueBand ?? string.Empty },
                    { "adaptationRecommendation", label.adaptationRecommendation ?? string.Empty },
                    { "confidence", label.confidence },
                    { "completionRatio", label.completionRatio },
                    { "averageReactionSec", label.averageReactionSec },
                    { "omittedRatio", label.omittedRatio },
                    { "lateRatio", label.lateRatio },
                    { "difficultyScore", label.difficultyScore },
                    { "recommendedDifficultyLevel", label.recommendedDifficultyLevel },
                    {
                        "actionOutcome",
                        string.Equals(label.performanceBand, "PERFORMANCE_HIGH", StringComparison.Ordinal)
                            ? "CORRECT"
                            : "OBSERVED"
                    },
                    { "reasonCode", label.reasonCode ?? string.Empty },
                },
                ValidationSource);
        }

        private static string ResolveExportOutputDirectory()
        {
            var configured = Environment.GetEnvironmentVariable(ExportOutputDirectoryEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configured))
            {
                return Path.Combine(Directory.GetCurrentDirectory(), "Temp", "CliValidation", "ops_dataset_trace_export");
            }

            return Path.IsPathRooted(configured)
                ? Path.GetFullPath(configured)
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configured));
        }

        private static string ResolveExportName()
        {
            var configured = Environment.GetEnvironmentVariable(ExportNameEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.Trim();
            }

            return "ops_dataset_trace_export_validation_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        }

        private static string ResolveTraceInputPath()
        {
            var configured = Environment.GetEnvironmentVariable(TraceInputEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configured))
            {
                return string.Empty;
            }

            return Path.IsPathRooted(configured)
                ? Path.GetFullPath(configured.Trim())
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configured.Trim()));
        }

        private static string ResolveTraceSessionId()
        {
            var configured = Environment.GetEnvironmentVariable(TraceSessionIdEnvironmentVariable);
            return string.IsNullOrWhiteSpace(configured) ? string.Empty : configured.Trim();
        }

        private static bool ResolveRequireProvidedTrace()
        {
            var configured = Environment.GetEnvironmentVariable(TraceRequireProvidedEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configured))
            {
                return false;
            }

            var normalized = configured.Trim();
            return string.Equals(normalized, "1", StringComparison.Ordinal) ||
                   string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string ToRelativePath(string rootPath, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return string.Empty;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(rootPath))
                {
                    return Path.GetRelativePath(rootPath, fullPath);
                }
            }
            catch (Exception)
            {
                // Fall through to absolute path.
            }

            return fullPath;
        }

        private static bool ArePathsEquivalent(string firstPath, string secondPath)
        {
            if (string.IsNullOrWhiteSpace(firstPath) || string.IsNullOrWhiteSpace(secondPath))
            {
                return false;
            }

            try
            {
                var left = Path.GetFullPath(firstPath);
                var right = Path.GetFullPath(secondPath);
                return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return string.Equals(firstPath, secondPath, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void DestroyExistingSingleton<TComponent>() where TComponent : Component
        {
            var existing = UnityEngine.Object.FindFirstObjectByType<TComponent>();
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }
        }

        private static void InvokeNonPublic(object target, string methodName, params object[] arguments)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(target.GetType().FullName, methodName);
            }

            method.Invoke(target, arguments);
        }

        private static void SetNonPublicField(object target, string fieldName, object value)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(target.GetType().FullName, fieldName);
            }

            field.SetValue(target, value);
        }

        private static void PersistValidationResult(string status, string details)
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var outputPath = Path.Combine(
                projectRoot,
                "Temp",
                "CliValidation",
                "operational_dataset_trace_export_validation_result.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            var lines = new[]
            {
                "status=" + status,
                $"timestampUtc={DateTime.UtcNow:O}",
                "details=" + (details ?? string.Empty),
            };

            File.WriteAllLines(outputPath, lines);
            Debug.Log("[OperationalDatasetTraceExportValidation] Result file: " + outputPath);
        }

        private static string ReadString(IReadOnlyDictionary<string, object> payload, string key)
        {
            if (payload == null || string.IsNullOrWhiteSpace(key) || !payload.TryGetValue(key, out var value) || value == null)
            {
                return string.Empty;
            }

            var converted = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(converted) ? string.Empty : converted.Trim();
        }

        private static long ReadLong(IReadOnlyDictionary<string, object> payload, string key)
        {
            if (payload == null || string.IsNullOrWhiteSpace(key) || !payload.TryGetValue(key, out var value) || value == null)
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

            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0L;
            }

            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0L;
        }

        private static string SerializePayloadDictionary(IReadOnlyDictionary<string, object> payload)
        {
            if (payload == null || payload.Count == 0)
            {
                return "{}";
            }

            var builder = new StringBuilder(256);
            AppendJsonObject(builder, payload);
            return builder.ToString();
        }

        private static void AppendJsonObject(StringBuilder builder, IReadOnlyDictionary<string, object> values)
        {
            builder.Append('{');

            var first = true;
            foreach (var kvp in values)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                AppendJsonString(builder, kvp.Key);
                builder.Append(':');
                AppendJsonValue(builder, kvp.Value);
            }

            builder.Append('}');
        }
        private static void AppendJsonValue(StringBuilder builder, object value)
        {
            if (value == null)
            {
                builder.Append("null");
                return;
            }

            switch (value)
            {
                case string text:
                    AppendJsonString(builder, text);
                    return;
                case bool boolValue:
                    builder.Append(boolValue ? "true" : "false");
                    return;
                case byte _:
                case sbyte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                case float _:
                case double _:
                case decimal _:
                    builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                case DateTime dateTimeValue:
                    var utc = dateTimeValue.Kind == DateTimeKind.Utc ? dateTimeValue : dateTimeValue.ToUniversalTime();
                    AppendJsonString(builder, utc.ToString("O", CultureInfo.InvariantCulture));
                    return;
                case IReadOnlyDictionary<string, object> dictionaryValue:
                    AppendJsonObject(builder, dictionaryValue);
                    return;
                case IDictionary<string, object> mutableDictionaryValue:
                    AppendJsonObject(builder, new Dictionary<string, object>(mutableDictionaryValue));
                    return;
                case IDictionary dictionary:
                    AppendJsonDictionary(builder, dictionary);
                    return;
                case IEnumerable enumerableValue when !(value is string):
                    AppendJsonArray(builder, enumerableValue);
                    return;
                default:
                    AppendJsonString(builder, Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
            }
        }

        private static void AppendJsonDictionary(StringBuilder builder, IDictionary dictionary)
        {
            builder.Append('{');
            var first = true;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                AppendJsonString(builder, Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty);
                builder.Append(':');
                AppendJsonValue(builder, entry.Value);
            }

            builder.Append('}');
        }

        private static void AppendJsonArray(StringBuilder builder, IEnumerable values)
        {
            builder.Append('[');

            var first = true;
            foreach (var item in values)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                AppendJsonValue(builder, item);
            }

            builder.Append(']');
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');

            if (!string.IsNullOrEmpty(value))
            {
                foreach (var ch in value)
                {
                    switch (ch)
                    {
                        case '"':
                            builder.Append("\\\"");
                            break;
                        case '\\':
                            builder.Append("\\\\");
                            break;
                        case '\b':
                            builder.Append("\\b");
                            break;
                        case '\f':
                            builder.Append("\\f");
                            break;
                        case '\n':
                            builder.Append("\\n");
                            break;
                        case '\r':
                            builder.Append("\\r");
                            break;
                        case '\t':
                            builder.Append("\\t");
                            break;
                        default:
                            if (ch < 32)
                            {
                                builder.Append("\\u");
                                builder.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                builder.Append(ch);
                            }
                            break;
                    }
                }
            }

            builder.Append('"');
        }
    }
}
