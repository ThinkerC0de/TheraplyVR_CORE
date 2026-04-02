using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using TheraplyCore.Interactions;
using UnityEditor;
using UnityEngine;

namespace TheraplyCore.Editor.Automation
{
    public static class AdaptiveDifficultyLabelPipelineValidation
    {
        private const string ValidationGameId = "pulse_target_tap";
        private const string ValidationSource = "AdaptiveDifficultyLabelPipelineValidation";

        public static void RunAdaptiveDifficultyLabelPipelineValidation()
        {
            try
            {
                var summary = ExecuteValidation();
                Debug.Log("[AdaptiveDifficultyLabelPipelineValidation] PASS: " + summary);
                PersistValidationResult("PASS", summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[AdaptiveDifficultyLabelPipelineValidation] FAIL: " + exception);
                PersistValidationResult("FAIL", exception.ToString());
                EditorApplication.Exit(1);
            }
        }

        private static string ExecuteValidation()
        {
            var capturedEvents = new List<Dictionary<string, object>>();
            var createdObjects = new List<UnityEngine.Object>();

            try
            {
                DestroyExistingSingleton<InteractionEventBridge>();

                var host = new GameObject("AdaptiveDifficultyLabelPipelineValidationHost");
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
                    capturedEvents.Add(payload != null
                        ? new Dictionary<string, object>(payload)
                        : new Dictionary<string, object>());
                };

                var controller = new AdaptiveDifficultyController();
                var policy = AdaptiveDifficultyController.CreateDefaultPolicy();
                policy.enabled = true;
                policy.sensitivity = 0.95f;
                policy.minTargetSpeed = 0.3f;
                policy.maxTargetSpeed = 1.7f;
                policy.minTargetScale = 0.16f;
                policy.maxTargetScale = 0.66f;
                policy.minCueTimeoutSec = 0.55f;
                policy.maxCueTimeoutSec = 3.4f;
                controller.Configure(policy, 0.56f);

                var labelPipeline = new TaskLabelPipeline();
                var firstTaskRunId = Guid.NewGuid().ToString();
                var secondTaskRunId = Guid.NewGuid().ToString();

                var degradedSummary = new TaskOutcomeAggregator.TaskOutcomeSummary
                {
                    taskRunId = firstTaskRunId,
                    gameId = ValidationGameId,
                    cuesPresented = 8,
                    actionsObserved = 8,
                    correctCount = 2,
                    incorrectCount = 2,
                    lateCount = 2,
                    omittedCount = 2,
                    redundantCount = 0,
                    firstActionLatencySec = 1.2f,
                    averageReactionSec = 1.1f,
                    completionRatio = 0.26f,
                    elapsedSec = 17.8f,
                };
                var degradeDecision = controller.Evaluate(degradedSummary);
                var degradeLabel = labelPipeline.CreateTaskLabel(degradedSummary, degradeDecision);
                EmitAdaptiveDecision(bridge, degradeDecision, degradedSummary, "TASK_DEGRADED_SIGNAL");
                EmitTaskLabel(bridge, degradeLabel);

                var improvedSummary = new TaskOutcomeAggregator.TaskOutcomeSummary
                {
                    taskRunId = secondTaskRunId,
                    gameId = ValidationGameId,
                    cuesPresented = 8,
                    actionsObserved = 8,
                    correctCount = 8,
                    incorrectCount = 0,
                    lateCount = 0,
                    omittedCount = 0,
                    redundantCount = 0,
                    firstActionLatencySec = 0.24f,
                    averageReactionSec = 0.31f,
                    completionRatio = 1f,
                    elapsedSec = 7.4f,
                };
                var promoteDecision = controller.Evaluate(improvedSummary);
                var promoteLabel = labelPipeline.CreateTaskLabel(improvedSummary, promoteDecision);
                EmitAdaptiveDecision(bridge, promoteDecision, improvedSummary, "TASK_IMPROVED_SIGNAL");
                EmitTaskLabel(bridge, promoteLabel);

                ValidateCapturedEvents(capturedEvents);

                var distinctEventTypes = capturedEvents
                    .Select(record => TryReadString(record, "eventType"))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                var maxSequence = capturedEvents
                    .Select(record => TryReadLong(record, "sequenceNumber"))
                    .Where(value => value > 0)
                    .DefaultIfEmpty(0L)
                    .Max();
                var adaptiveCount = capturedEvents.Count(record =>
                    string.Equals(TryReadString(record, "eventType"), "ADAPTIVE_DIFFICULTY_ADJUSTED", StringComparison.Ordinal));
                var labelCount = capturedEvents.Count(record =>
                    string.Equals(TryReadString(record, "eventType"), "TASK_LABEL_GENERATED", StringComparison.Ordinal));

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "events={0}; adaptiveEvents={1}; labelEvents={2}; maxSequence={3}; eventTypes={4}",
                    capturedEvents.Count,
                    adaptiveCount,
                    labelCount,
                    maxSequence,
                    string.Join(",", distinctEventTypes));
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

        private static void EmitAdaptiveDecision(
            InteractionEventBridge bridge,
            AdaptiveDifficultyController.DifficultyDecision decision,
            TaskOutcomeAggregator.TaskOutcomeSummary summary,
            string signalReasonCode)
        {
            bridge.RecordGameplayEvent(
                ValidationGameId,
                "adaptive_difficulty_adjusted",
                "PLAYING",
                new Dictionary<string, object>
                {
                    { "taskRunId", summary.taskRunId ?? string.Empty },
                    { "signalReasonCode", signalReasonCode ?? string.Empty },
                    { "previousDifficulty", decision.previousDifficulty },
                    { "nextDifficulty", decision.nextDifficulty },
                    { "difficultyDelta", decision.delta },
                    { "targetSpeed", decision.targetSpeed },
                    { "targetScale", decision.targetScale },
                    { "cueTimeoutSec", decision.cueTimeoutSec },
                    { "completionRatio", summary.completionRatio },
                    { "averageReactionSec", summary.averageReactionSec },
                    { "omittedRatio", ResolveRatio(summary.omittedCount, summary.cuesPresented) },
                    { "lateRatio", ResolveRatio(summary.lateCount, summary.cuesPresented) },
                    { "actionOutcome", decision.delta >= 0f ? "CORRECT" : "INCORRECT" },
                    { "reasonCode", string.IsNullOrWhiteSpace(decision.reasonCode) ? "KEEP_DIFFICULTY" : decision.reasonCode },
                },
                ValidationSource);
        }

        private static void EmitTaskLabel(
            InteractionEventBridge bridge,
            TaskLabelPipeline.TaskLabel label)
        {
            bridge.RecordGameplayEvent(
                ValidationGameId,
                "task_label_generated",
                "PLAYING",
                new Dictionary<string, object>
                {
                    { "taskRunId", label.taskRunId ?? string.Empty },
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

        private static void ValidateCapturedEvents(IReadOnlyList<Dictionary<string, object>> records)
        {
            if (records == null || records.Count == 0)
            {
                throw new InvalidOperationException("No canonical events were captured.");
            }

            var requiredEventTypes = new HashSet<string>(StringComparer.Ordinal)
            {
                "ADAPTIVE_DIFFICULTY_ADJUSTED",
                "TASK_LABEL_GENERATED",
            };
            var observedEventTypes = new HashSet<string>(StringComparer.Ordinal);
            var observedReasonCodes = new HashSet<string>(StringComparer.Ordinal);
            var observedDetailTaskRunIds = new HashSet<string>(StringComparer.Ordinal);
            var observedLabelBands = new HashSet<string>(StringComparer.Ordinal);
            var previousSequence = 0L;

            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                EnsureRequiredString(record, "eventId");
                EnsureRequiredString(record, "eventType");
                EnsureRequiredString(record, "sequenceNumber");
                EnsureRequiredString(record, "ownerKey");
                EnsureRequiredString(record, "sessionKey");
                EnsureRequiredString(record, "sourceOfTruth");
                EnsureRequiredString(record, "taskRunId");

                var sourceOfTruth = TryReadString(record, "sourceOfTruth");
                if (!string.Equals(sourceOfTruth, "MOBILE_CONTROLLER", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Unexpected sourceOfTruth: {sourceOfTruth}");
                }

                var sequence = TryReadLong(record, "sequenceNumber");
                if (sequence <= previousSequence)
                {
                    throw new InvalidOperationException(
                        $"Non-monotonic sequenceNumber. previous={previousSequence}, current={sequence}");
                }

                previousSequence = sequence;

                var detailTaskRunId = TryReadDetailsString(record, "taskRunId");
                if (!string.IsNullOrWhiteSpace(detailTaskRunId))
                {
                    observedDetailTaskRunIds.Add(detailTaskRunId);
                }

                var eventType = TryReadString(record, "eventType");
                observedEventTypes.Add(eventType);

                if (string.Equals(eventType, "ADAPTIVE_DIFFICULTY_ADJUSTED", StringComparison.Ordinal))
                {
                    observedReasonCodes.Add(TryReadString(record, "reasonCode"));
                }
                else if (string.Equals(eventType, "TASK_LABEL_GENERATED", StringComparison.Ordinal))
                {
                    observedLabelBands.Add(TryReadDetailsString(record, "performanceBand"));
                }
            }

            foreach (var eventType in requiredEventTypes)
            {
                if (!observedEventTypes.Contains(eventType))
                {
                    throw new InvalidOperationException("Missing required eventType: " + eventType);
                }
            }

            if (!observedReasonCodes.Contains("INCREASE_DIFFICULTY"))
            {
                throw new InvalidOperationException("Missing INCREASE_DIFFICULTY adaptive reason.");
            }

            if (!observedReasonCodes.Contains("DECREASE_DIFFICULTY"))
            {
                throw new InvalidOperationException("Missing DECREASE_DIFFICULTY adaptive reason.");
            }

            if (observedDetailTaskRunIds.Count < 2)
            {
                throw new InvalidOperationException(
                    "Expected at least two taskRunIds in details payload to validate per-run label pipeline.");
            }

            if (!observedLabelBands.Contains("PERFORMANCE_HIGH") ||
                !observedLabelBands.Contains("PERFORMANCE_LOW"))
            {
                throw new InvalidOperationException(
                    "Expected both PERFORMANCE_HIGH and PERFORMANCE_LOW labels.");
            }
        }

        private static float ResolveRatio(int numerator, int denominator)
        {
            var safeDenominator = Mathf.Max(1, denominator);
            var safeNumerator = Mathf.Max(0, numerator);
            return Mathf.Clamp01((float)safeNumerator / safeDenominator);
        }

        private static void DestroyExistingSingleton<TComponent>() where TComponent : Component
        {
            var existing = UnityEngine.Object.FindFirstObjectByType<TComponent>();
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }
        }

        private static void EnsureRequiredString(
            IReadOnlyDictionary<string, object> record,
            string key)
        {
            var value = TryReadString(record, key);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("Missing required field: " + key);
            }
        }

        private static string TryReadString(
            IReadOnlyDictionary<string, object> record,
            string key)
        {
            if (record == null || string.IsNullOrWhiteSpace(key) || !record.TryGetValue(key, out var value) || value == null)
            {
                return string.Empty;
            }

            var converted = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(converted) ? string.Empty : converted.Trim();
        }

        private static string TryReadDetailsString(
            IReadOnlyDictionary<string, object> record,
            string key)
        {
            if (record == null || !record.TryGetValue("details", out var detailsValue) || detailsValue == null)
            {
                return string.Empty;
            }

            if (!(detailsValue is IReadOnlyDictionary<string, object> details))
            {
                details = detailsValue as Dictionary<string, object>;
            }

            if (details == null || string.IsNullOrWhiteSpace(key) || !details.TryGetValue(key, out var value) || value == null)
            {
                return string.Empty;
            }

            var converted = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(converted) ? string.Empty : converted.Trim();
        }

        private static long TryReadLong(
            IReadOnlyDictionary<string, object> record,
            string key)
        {
            if (record == null || string.IsNullOrWhiteSpace(key) || !record.TryGetValue(key, out var value) || value == null)
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
                "adaptive_difficulty_label_pipeline_result.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            var lines = new[]
            {
                "status=" + status,
                $"timestampUtc={DateTime.UtcNow:O}",
                "details=" + (details ?? string.Empty),
            };

            File.WriteAllLines(outputPath, lines);
            Debug.Log("[AdaptiveDifficultyLabelPipelineValidation] Result file: " + outputPath);
        }
    }
}
