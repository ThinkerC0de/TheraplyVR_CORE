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
    public static class SequenceStimulusTaskOutcomeValidation
    {
        private const string ValidationGameId = "pulse_target_tap";
        private const string ValidationSource = "SequenceStimulusTaskOutcomeValidation";

        public static void RunSequenceStimulusTaskOutcomeValidation()
        {
            try
            {
                var summary = ExecuteValidation();
                Debug.Log("[SequenceStimulusTaskOutcomeValidation] PASS: " + summary);
                PersistValidationResult("PASS", summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[SequenceStimulusTaskOutcomeValidation] FAIL: " + exception);
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

                var host = new GameObject("SequenceStimulusTaskOutcomeValidationHost");
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
                };

                var taskRunId = Guid.NewGuid().ToString();
                var scheduler = new StimulusScheduler();
                var engine = new SequenceTaskEngine();
                var aggregator = new TaskOutcomeAggregator();

                scheduler.Reset();
                engine.Reset(taskRunId);
                aggregator.Reset(taskRunId, ValidationGameId);

                scheduler.ScheduleCue(new StimulusScheduler.ScheduledCue
                {
                    taskRunId = taskRunId,
                    stepId = "sequence_step_1",
                    cueId = "sequence_cue_1",
                    stimulusId = "SEQUENCE_VISUAL_CUE",
                    stimulusChannel = "VISUAL",
                    expectedTargetId = "target_1",
                    requiredAction = "SELECT",
                    dueAtElapsedSec = 0f,
                    cueTimeoutSec = 1f,
                });

                scheduler.ScheduleCue(new StimulusScheduler.ScheduledCue
                {
                    taskRunId = taskRunId,
                    stepId = "sequence_step_2",
                    cueId = "sequence_cue_2",
                    stimulusId = "SEQUENCE_AUDIO_CUE",
                    stimulusChannel = "AUDIO",
                    expectedTargetId = "target_2",
                    requiredAction = "SELECT",
                    dueAtElapsedSec = 0.5f,
                    cueTimeoutSec = 0.2f,
                });

                var dueCues = new List<StimulusScheduler.ScheduledCue>(4);
                var timeoutOutcomes = new List<SequenceTaskEngine.SequenceOutcome>(4);

                DispatchDueCues(bridge, scheduler, engine, aggregator, dueCues, elapsedSec: 0f);

                engine.TryRecordAction(
                    new SequenceTaskEngine.SequenceAction
                    {
                        stepId = "sequence_step_1",
                        cueId = "sequence_cue_1",
                        expectedTargetId = "target_1",
                        targetId = "target_1",
                        inputSource = "QUEST_POINTER",
                        actionAtElapsedSec = 0.1f,
                    },
                    out var correctOutcome);
                aggregator.RecordOutcome(correctOutcome);
                EmitActionOutcome(bridge, correctOutcome);

                DispatchDueCues(bridge, scheduler, engine, aggregator, dueCues, elapsedSec: 0.5f);

                timeoutOutcomes.Clear();
                engine.CollectTimeoutOutcomes(elapsedSec: 0.8f, output: timeoutOutcomes);
                for (var i = 0; i < timeoutOutcomes.Count; i++)
                {
                    var timeoutOutcome = timeoutOutcomes[i];
                    aggregator.RecordOutcome(timeoutOutcome);
                    EmitActionOutcome(bridge, timeoutOutcome);
                }

                engine.TryRecordAction(
                    new SequenceTaskEngine.SequenceAction
                    {
                        stepId = "sequence_step_2",
                        cueId = "sequence_cue_2",
                        expectedTargetId = "target_2",
                        targetId = "target_2",
                        inputSource = "QUEST_POINTER",
                        actionAtElapsedSec = 0.9f,
                    },
                    out var redundantOutcome);
                aggregator.RecordOutcome(redundantOutcome);
                EmitActionOutcome(bridge, redundantOutcome);

                var summary = aggregator.BuildSummary(elapsedSec: 1f);
                bridge.RecordGameplayEvent(
                    ValidationGameId,
                    "task_outcome_summary",
                    "PLAYING",
                    new Dictionary<string, object>
                    {
                        { "taskRunId", summary.taskRunId },
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
                    },
                    ValidationSource);

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

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "events={0}; maxSequence={1}; eventTypes={2}",
                    capturedEvents.Count,
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

        private static void DispatchDueCues(
            InteractionEventBridge bridge,
            StimulusScheduler scheduler,
            SequenceTaskEngine engine,
            TaskOutcomeAggregator aggregator,
            List<StimulusScheduler.ScheduledCue> dueCues,
            float elapsedSec)
        {
            dueCues.Clear();
            scheduler.CollectDueCues(elapsedSec, dueCues);

            for (var i = 0; i < dueCues.Count; i++)
            {
                var cue = dueCues[i];
                engine.RegisterCue(new SequenceTaskEngine.SequenceCue
                {
                    taskRunId = cue.taskRunId,
                    stepId = cue.stepId,
                    cueId = cue.cueId,
                    expectedTargetId = cue.expectedTargetId,
                    requiredAction = cue.requiredAction,
                    cueAtElapsedSec = elapsedSec,
                    cueTimeoutSec = cue.cueTimeoutSec,
                });
                aggregator.RecordCue(cue);

                bridge.RecordGameplayEvent(
                    ValidationGameId,
                    "task_stimulus_presented",
                    "PLAYING",
                    new Dictionary<string, object>
                    {
                        { "taskRunId", cue.taskRunId },
                        { "stepId", cue.stepId },
                        { "cueId", cue.cueId },
                        { "stimulusId", cue.stimulusId },
                        { "stimulusChannel", cue.stimulusChannel },
                        { "requiredAction", cue.requiredAction },
                        { "expectedTargetId", cue.expectedTargetId },
                        { "cueTimeoutSec", cue.cueTimeoutSec },
                        { "scheduledDueAtSec", cue.dueAtElapsedSec },
                        { "actionOutcome", "REQUIRED" },
                        { "reasonCode", "TASK_STIMULUS_DISPATCHED" },
                    },
                    ValidationSource);
            }
        }

        private static void EmitActionOutcome(
            InteractionEventBridge bridge,
            SequenceTaskEngine.SequenceOutcome outcome)
        {
            bridge.RecordGameplayEvent(
                ValidationGameId,
                "task_action_outcome",
                "PLAYING",
                new Dictionary<string, object>
                {
                    { "taskRunId", outcome.taskRunId },
                    { "stepId", outcome.stepId },
                    { "cueId", outcome.cueId },
                    { "requiredAction", outcome.requiredAction },
                    { "expectedTargetId", outcome.expectedTargetId },
                    { "actualTargetId", outcome.actualTargetId },
                    { "reactionSec", outcome.reactionSec },
                    { "cueTimeoutSec", outcome.timeoutSec },
                    { "isResolvedStep", outcome.isResolvedStep },
                    { "inputSource", outcome.inputSource ?? string.Empty },
                    { "actionOutcome", MapOutcomeToken(outcome.outcomeType) },
                    { "reasonCode", string.IsNullOrWhiteSpace(outcome.reasonCode) ? "TASK_ACTION_OBSERVED" : outcome.reasonCode },
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
                "TASK_STIMULUS_PRESENTED",
                "TASK_ACTION_OUTCOME",
                "TASK_OUTCOME_SUMMARY",
            };

            var observedEventTypes = new HashSet<string>(StringComparer.Ordinal);
            var observedOutcomeTokens = new HashSet<string>(StringComparer.Ordinal);
            var observedTaskRunIds = new HashSet<string>(StringComparer.Ordinal);
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

                var taskRunId = TryReadString(record, "taskRunId");
                observedTaskRunIds.Add(taskRunId);

                var sequence = TryReadLong(record, "sequenceNumber");
                if (sequence <= previousSequence)
                {
                    throw new InvalidOperationException(
                        $"Non-monotonic sequenceNumber. previous={previousSequence}, current={sequence}");
                }

                previousSequence = sequence;

                var eventType = TryReadString(record, "eventType");
                observedEventTypes.Add(eventType);

                if (string.Equals(eventType, "TASK_ACTION_OUTCOME", StringComparison.Ordinal))
                {
                    observedOutcomeTokens.Add(TryReadString(record, "actionOutcome"));
                }
            }

            foreach (var expectedEventType in requiredEventTypes)
            {
                if (!observedEventTypes.Contains(expectedEventType))
                {
                    throw new InvalidOperationException($"Missing required task eventType: {expectedEventType}");
                }
            }

            if (observedTaskRunIds.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Expected one canonical taskRunId for validation stream, observed={observedTaskRunIds.Count}");
            }

            var requiredOutcomeTokens = new[] { "CORRECT", "OMITTED", "REDUNDANT" };
            for (var i = 0; i < requiredOutcomeTokens.Length; i++)
            {
                var token = requiredOutcomeTokens[i];
                if (!observedOutcomeTokens.Contains(token))
                {
                    throw new InvalidOperationException($"Missing required task actionOutcome: {token}");
                }
            }
        }

        private static string MapOutcomeToken(SequenceOutcomeType outcomeType)
        {
            switch (outcomeType)
            {
                case SequenceOutcomeType.Correct:
                    return "CORRECT";
                case SequenceOutcomeType.Incorrect:
                    return "INCORRECT";
                case SequenceOutcomeType.Late:
                    return "LATE";
                case SequenceOutcomeType.Redundant:
                    return "REDUNDANT";
                case SequenceOutcomeType.Omitted:
                    return "OMITTED";
                default:
                    return "OBSERVED";
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

        private static void EnsureRequiredString(
            IReadOnlyDictionary<string, object> record,
            string key)
        {
            var value = TryReadString(record, key);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Missing required field: {key}");
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
                "sequence_stimulus_task_outcome_result.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            var lines = new[]
            {
                $"status={status}",
                $"timestampUtc={DateTime.UtcNow:O}",
                $"details={details ?? string.Empty}",
            };

            File.WriteAllLines(outputPath, lines);
            Debug.Log("[SequenceStimulusTaskOutcomeValidation] Result file: " + outputPath);
        }
    }
}
