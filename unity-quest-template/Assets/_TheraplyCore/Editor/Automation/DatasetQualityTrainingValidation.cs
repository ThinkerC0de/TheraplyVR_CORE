using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using TheraplyCore.Interactions;
using UnityEditor;
using UnityEngine;

namespace TheraplyCore.Editor.Automation
{
    public static class DatasetQualityTrainingValidation
    {
        private const string ValidationGameId = "pulse_target_tap";
        private const string ValidationSource = "DatasetQualityTrainingValidation";

        public static void RunDatasetQualityTrainingValidation()
        {
            try
            {
                var summary = ExecuteValidation();
                Debug.Log("[DatasetQualityTrainingValidation] PASS: " + summary);
                PersistValidationResult("PASS", summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[DatasetQualityTrainingValidation] FAIL: " + exception);
                PersistValidationResult("FAIL", exception.ToString());
                EditorApplication.Exit(1);
            }
        }

        private static string ExecuteValidation()
        {
            var capturedEvents = new List<Dictionary<string, object>>();
            var datasetRecords = new List<IReadOnlyDictionary<string, object>>();
            var createdObjects = new List<UnityEngine.Object>();

            try
            {
                DestroyExistingSingleton<InteractionEventBridge>();

                var host = new GameObject("DatasetQualityTrainingValidationHost");
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
                    var safePayload = payload != null
                        ? new Dictionary<string, object>(payload)
                        : new Dictionary<string, object>();
                    capturedEvents.Add(safePayload);
                    datasetRecords.Add(safePayload);
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

                var qualityGate = new TaskDatasetQualityGate();
                var requirements = TaskDatasetQualityGate.CreateDefaultRequirements();
                var qualityReport = qualityGate.Evaluate(datasetRecords, requirements);

                if (!qualityReport.readyForTraining)
                {
                    throw new InvalidOperationException(
                        $"Dataset quality gate rejected stream: reason={qualityReport.reasonCode}");
                }

                if (qualityReport.uniqueTaskRuns < requirements.minTaskRuns)
                {
                    throw new InvalidOperationException(
                        $"Expected at least {requirements.minTaskRuns} task runs, observed={qualityReport.uniqueTaskRuns}");
                }

                if (qualityReport.labelCoverageRatio < requirements.minLabelCoverageRatio)
                {
                    throw new InvalidOperationException(
                        $"Label coverage too low: required={requirements.minLabelCoverageRatio}, actual={qualityReport.labelCoverageRatio}");
                }

                if (qualityReport.summaryCoverageRatio < requirements.minSummaryCoverageRatio)
                {
                    throw new InvalidOperationException(
                        $"Summary coverage too low: required={requirements.minSummaryCoverageRatio}, actual={qualityReport.summaryCoverageRatio}");
                }

                if (qualityReport.missingRequiredFields > 0 ||
                    qualityReport.duplicateEventIds > 0 ||
                    qualityReport.nonMonotonicSequenceCount > 0 ||
                    qualityReport.invalidSourceOfTruthCount > 0)
                {
                    throw new InvalidOperationException(
                        "Dataset quality report contains structural violations.");
                }

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "events={0}; taskRuns={1}; labels={2}; summaries={3}; adaptiveEvents={4}; labelCoverage={5:F3}; avgConfidence={6:F3}; qualityScore={7:F3}; reason={8}",
                    qualityReport.totalEvents,
                    qualityReport.uniqueTaskRuns,
                    qualityReport.taskLabels,
                    qualityReport.taskOutcomeSummaries,
                    qualityReport.adaptiveEvents,
                    qualityReport.labelCoverageRatio,
                    qualityReport.averageLabelConfidence,
                    qualityReport.qualityScore,
                    qualityReport.reasonCode ?? string.Empty);
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
                "dataset_quality_training_validation_result.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            var lines = new[]
            {
                "status=" + status,
                $"timestampUtc={DateTime.UtcNow:O}",
                "details=" + (details ?? string.Empty),
            };

            File.WriteAllLines(outputPath, lines);
            Debug.Log("[DatasetQualityTrainingValidation] Result file: " + outputPath);
        }
    }
}
