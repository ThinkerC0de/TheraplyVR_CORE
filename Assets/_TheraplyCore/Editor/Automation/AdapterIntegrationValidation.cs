using System;
using System.Collections.Generic;
using System.IO;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using UnityEditor;
using UnityEngine;

namespace TheraplyCore.Editor.Automation
{
    public static class AdapterIntegrationValidation
    {
        public static void RunAdapterIntegrationValidation()
        {
            try
            {
                var summary = ExecuteValidation();
                Debug.Log("[AdapterIntegrationValidation] PASS: " + summary);
                PersistValidationResult("PASS", summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[AdapterIntegrationValidation] FAIL: " + exception);
                PersistValidationResult("FAIL", exception.ToString());
                EditorApplication.Exit(1);
            }
        }

        private static string ExecuteValidation()
        {
            var pluginRegistry = new ActionPluginRegistry();
            SessionFlowBuiltInPlugins.RegisterBuiltIns(pluginRegistry, replaceExisting: true);

            var scenarios = CreateScenarios();
            var summaryParts = new List<string>(scenarios.Count);
            for (var i = 0; i < scenarios.Count; i++)
            {
                ValidateScenario(pluginRegistry, scenarios[i]);
                summaryParts.Add(scenarios[i].name + "=OK");
            }

            return string.Join("; ", summaryParts);
        }

        private static void ValidateScenario(ActionPluginRegistry pluginRegistry, AdapterScenario scenario)
        {
            AssertTrue(pluginRegistry != null, "Plugin registry is required.");
            AssertTrue(scenario != null, "Scenario is required.");
            AssertTrue(scenario.payload != null, "Scenario payload is required for " + scenario.name);

            var intent = ResolveIntent(pluginRegistry, scenario);
            AssertTrue(intent != null, "Resolved intent is required for " + scenario.name);
            AssertEqual(
                scenario.actionId,
                intent.actionId,
                "Resolved action id mismatch for " + scenario.name + ".");
            AssertEqual(
                scenario.channelId,
                intent.channelId,
                "Resolved channel id mismatch for " + scenario.name + ".");

            if (!string.IsNullOrWhiteSpace(scenario.expectedTargetId))
            {
                AssertEqual(
                    scenario.expectedTargetId,
                    intent.targetId,
                    "Resolved target id mismatch for " + scenario.name + ".");
            }

            var runner = new TaskGraphRunner();
            runner.SetPluginRegistry(pluginRegistry);
            runner.SetRuntimeContext(
                gameId: "adapter_validation_game",
                flowId: "adapter_validation_flow",
                sessionId: "adapter_validation_session",
                controlMode: SessionFlowControlModes.Hybrid);

            var graph = CreateGraphForScenario(scenario);
            AssertTrue(
                runner.Initialize(graph, out var initReason),
                "Graph should initialize for scenario " + scenario.name + ". reason=" + initReason);
            AssertTrue(
                runner.Start(0f, out var startReason),
                "Graph should start for scenario " + scenario.name + ". reason=" + startReason);

            var submitted = runner.SubmitAction(
                intent,
                nowElapsedSec: 0.25f,
                validationResult: out var validationResult,
                reasonCode: out var submitReasonCode);

            AssertTrue(
                submitted,
                "Action submission should succeed for scenario " + scenario.name + ". reason=" + submitReasonCode);
            AssertTrue(validationResult != null, "Validation result should exist for " + scenario.name + ".");
            AssertTrue(validationResult.accepted, "Action should be accepted for scenario " + scenario.name + ".");
            AssertEqual(
                "ACTION_ACCEPTED",
                validationResult.reasonCode,
                "Unexpected validation reason for " + scenario.name + ".");
            AssertEqual(
                TaskGraphRunState.Completed,
                runner.State,
                "Graph should be completed after accepted action for " + scenario.name + ".");

            ValidateDecisionTelemetryShape(scenario, intent, validationResult);
        }

        private static ActionIntent ResolveIntent(ActionPluginRegistry pluginRegistry, AdapterScenario scenario)
        {
            var plugins = pluginRegistry.GetPlugins();
            var matchedIntentCount = 0;
            ActionIntent resolvedIntent = null;

            for (var i = 0; i < plugins.Count; i++)
            {
                var plugin = plugins[i];
                if (plugin == null)
                {
                    continue;
                }

                if (!plugin.TryCreateIntent(scenario.payload, out var intent) || intent == null)
                {
                    continue;
                }

                matchedIntentCount++;
                resolvedIntent = intent;
            }

            AssertTrue(
                matchedIntentCount == 1,
                "Expected exactly one plugin intent match for " + scenario.name +
                ". matched=" + matchedIntentCount);

            return resolvedIntent;
        }

        private static TaskGraphDefinition CreateGraphForScenario(AdapterScenario scenario)
        {
            var constraints = new List<KeyValuePairString>
            {
                new KeyValuePairString
                {
                    key = "requiredChannelId",
                    value = scenario.channelId,
                },
            };

            if (scenario.additionalConstraints != null)
            {
                for (var i = 0; i < scenario.additionalConstraints.Count; i++)
                {
                    var constraint = scenario.additionalConstraints[i];
                    if (constraint == null)
                    {
                        continue;
                    }

                    constraints.Add(
                        new KeyValuePairString
                        {
                            key = constraint.key,
                            value = constraint.value,
                        });
                }
            }

            return new TaskGraphDefinition
            {
                entryNodeId = "n_action",
                nodes = new List<TaskGraphNodeDefinition>
                {
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "n_action",
                        nodeType = TaskGraphNodeTypes.Action,
                        nextOnSuccess = "n_complete",
                        allowedActions = new List<AllowedActionDefinition>
                        {
                            new AllowedActionDefinition
                            {
                                actionId = scenario.actionId,
                                constraints = constraints,
                            },
                        },
                    },
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "n_complete",
                        nodeType = TaskGraphNodeTypes.Complete,
                    },
                },
            };
        }

        private static void ValidateDecisionTelemetryShape(
            AdapterScenario scenario,
            ActionIntent intent,
            ActionValidationResult validationResult)
        {
            var sessionId = "adapter_session_" + scenario.name;
            var taskRunId = "adapter_task_run_" + scenario.name;
            var attemptId = "adapter_attempt_" + scenario.name;
            var actionAttemptId = Guid.NewGuid().ToString("N");

            var records = new List<Dictionary<string, object>>
            {
                CreateFlowRecord("FLOW_STARTED", 1, sessionId, taskRunId, attemptId, string.Empty, null),
                CreateFlowRecord(
                    "ACTION_RECEIVED",
                    2,
                    sessionId,
                    taskRunId,
                    attemptId,
                    actionAttemptId,
                    new Dictionary<string, object>
                    {
                        { "actionId", intent.actionId ?? string.Empty },
                        { "channelId", intent.channelId ?? string.Empty },
                        { "targetId", intent.targetId ?? string.Empty },
                    }),
                CreateFlowRecord(
                    "ACTION_EVALUATED",
                    3,
                    sessionId,
                    taskRunId,
                    attemptId,
                    actionAttemptId,
                    new Dictionary<string, object>
                    {
                        { "actionId", intent.actionId ?? string.Empty },
                        { "channelId", intent.channelId ?? string.Empty },
                        { "targetId", intent.targetId ?? string.Empty },
                        { "decision", validationResult.accepted ? "accepted" : "rejected" },
                        { "reasonCode", validationResult.reasonCode ?? string.Empty },
                    }),
                CreateFlowRecord("FLOW_COMPLETED", 4, sessionId, taskRunId, attemptId, string.Empty, null),
                CreateFlowRecord(
                    "SESSION_TERMINAL",
                    5,
                    sessionId,
                    taskRunId,
                    attemptId,
                    string.Empty,
                    new Dictionary<string, object>
                    {
                        { "terminalState", "COMPLETED" },
                    }),
            };

            var qualityGate = new CanonicalFlowTelemetryQualityGate();
            var report = qualityGate.Evaluate(
                records,
                CanonicalFlowTelemetryQualityGate.CreateDefaultRequirements());

            AssertTrue(
                report.readyForExport,
                "Telemetry quality gate should pass for scenario " + scenario.name +
                ". reason=" + report.reasonCode);
            AssertEqual(
                "FLOW_TELEMETRY_READY",
                report.reasonCode,
                "Unexpected telemetry quality gate reason for " + scenario.name + ".");
        }

        private static Dictionary<string, object> CreateFlowRecord(
            string eventType,
            long sequenceNumber,
            string sessionId,
            string taskRunId,
            string attemptId,
            string actionAttemptId,
            IReadOnlyDictionary<string, object> details)
        {
            var eventDetails = new Dictionary<string, object>
            {
                { "actionAttemptId", actionAttemptId ?? string.Empty },
            };

            if (details != null)
            {
                foreach (var kv in details)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key))
                    {
                        continue;
                    }

                    eventDetails[kv.Key] = kv.Value;
                }
            }

            return new Dictionary<string, object>
            {
                { "eventId", Guid.NewGuid().ToString("N") },
                { "sessionId", sessionId },
                { "taskRunId", taskRunId },
                { "attemptId", attemptId },
                { "sequenceNumber", sequenceNumber },
                { "gameId", "adapter_validation_game" },
                { "eventType", eventType },
                { "occurredAtUtc", DateTime.UtcNow.ToString("O") },
                { "sourceComponent", "AdapterIntegrationValidation" },
                { "payloadVersion", 1 },
                { "actionAttemptId", actionAttemptId ?? string.Empty },
                { "details", eventDetails },
            };
        }

        private static List<AdapterScenario> CreateScenarios()
        {
            return new List<AdapterScenario>
            {
                new AdapterScenario
                {
                    name = "pointer",
                    actionId = "point_and_select_target",
                    channelId = SessionFlowChannelIds.Pointer,
                    expectedTargetId = "target_pointer",
                    payload = new Dictionary<string, object>
                    {
                        { "eventType", "POINTER_SELECT" },
                        { "targetId", "target_pointer" },
                        { "inputSource", "POINTER" },
                        { "inputHand", "RIGHT" },
                        { "inputValue", 1f },
                        { "occurredAtElapsedSec", 0.1f },
                    },
                },
                new AdapterScenario
                {
                    name = "tool_impact",
                    actionId = "touch_target_with_tool",
                    channelId = SessionFlowChannelIds.ToolImpact,
                    expectedTargetId = "target_tool",
                    payload = new Dictionary<string, object>
                    {
                        { "eventType", "TOOL_IMPACT_HIT" },
                        { "targetId", "target_tool" },
                        { "inputSource", "TOOL" },
                        { "inputHand", "RIGHT" },
                        { "inputValue", 1f },
                        { "occurredAtElapsedSec", 0.15f },
                    },
                },
                new AdapterScenario
                {
                    name = "hand_contact",
                    actionId = "touch_target_with_hand",
                    channelId = SessionFlowChannelIds.HandContact,
                    expectedTargetId = "target_hand",
                    payload = new Dictionary<string, object>
                    {
                        { "eventType", "HAND_CONTACT_HIT" },
                        { "targetId", "target_hand" },
                        { "inputSource", "HAND" },
                        { "inputHand", "LEFT" },
                        { "inputValue", 1f },
                        { "occurredAtElapsedSec", 0.2f },
                    },
                },
                new AdapterScenario
                {
                    name = "grab_collect",
                    actionId = "collect_item_to_container",
                    channelId = SessionFlowChannelIds.HandGrab,
                    expectedTargetId = "zone_container",
                    payload = new Dictionary<string, object>
                    {
                        { "eventType", "GRAB_OBJECT_COLLECTED" },
                        { "objectId", "item_a" },
                        { "zoneId", "zone_container" },
                        { "inputSource", "HAND_GRAB" },
                        { "inputHand", "RIGHT" },
                        { "inputValue", 1f },
                        { "occurredAtElapsedSec", 0.25f },
                    },
                },
                new AdapterScenario
                {
                    name = "gaze_hold",
                    actionId = "hold_gaze_on_target",
                    channelId = SessionFlowChannelIds.Gaze,
                    expectedTargetId = "target_gaze",
                    payload = new Dictionary<string, object>
                    {
                        { "eventType", "GAZE_HOLD_COMPLETED" },
                        { "targetId", "target_gaze" },
                        { "dwellSec", "1.5" },
                        { "requiredDwellSec", "1.0" },
                        { "inputSource", "GAZE" },
                        { "inputValue", 1f },
                        { "occurredAtElapsedSec", 0.3f },
                    },
                },
                new AdapterScenario
                {
                    name = "breath_cycle",
                    actionId = "perform_breath_cycle",
                    channelId = SessionFlowChannelIds.Breath,
                    expectedTargetId = "breath_target",
                    payload = new Dictionary<string, object>
                    {
                        { "eventType", "BREATH_CYCLE_COMPLETED" },
                        { "targetId", "breath_target" },
                        { "cycleIndex", "3" },
                        { "targetCycleCount", "3" },
                        { "phaseDurationSec", "12.0" },
                        { "inputSource", "BREATH" },
                        { "inputValue", 0.9f },
                        { "occurredAtElapsedSec", 0.35f },
                    },
                },
                new AdapterScenario
                {
                    name = "audio_source",
                    actionId = "identify_sound_source",
                    channelId = SessionFlowChannelIds.AudioSource,
                    expectedTargetId = "source_a",
                    payload = new Dictionary<string, object>
                    {
                        { "eventType", "AUDIO_SOURCE_SELECTED" },
                        { "cueId", "cue_tree" },
                        { "activeSourceId", "source_a" },
                        { "selectedSourceId", "source_a" },
                        { "inputSource", "AUDIO_SOURCE" },
                        { "inputValue", 1f },
                        { "occurredAtElapsedSec", 0.4f },
                    },
                },
                new AdapterScenario
                {
                    name = "dual_hand",
                    actionId = "mark_left_and_right_targets",
                    channelId = SessionFlowChannelIds.DualHand,
                    expectedTargetId = "left_target|right_target",
                    payload = new Dictionary<string, object>
                    {
                        { "eventType", "DUAL_HAND_MARK_SYNC" },
                        { "leftTargetId", "left_target" },
                        { "rightTargetId", "right_target" },
                        { "leftMatched", true },
                        { "rightMatched", true },
                        { "syncSatisfied", true },
                        { "syncDeltaMs", "80" },
                        { "syncWindowMs", "120" },
                        { "inputSource", "DUAL_HAND" },
                        { "inputValue", 1f },
                        { "occurredAtElapsedSec", 0.45f },
                    },
                },
                new AdapterScenario
                {
                    name = "pose_hold",
                    actionId = "hold_pose",
                    channelId = SessionFlowChannelIds.PosePath,
                    expectedTargetId = "pose_anchor",
                    payload = new Dictionary<string, object>
                    {
                        { "eventType", "POSE_PATH_HOLD_COMPLETED" },
                        { "targetId", "pose_anchor" },
                        { "holdElapsedSec", "2.0" },
                        { "holdRequiredSec", "2.0" },
                        { "pathDeviation", "0.1" },
                        { "tolerance", "0.2" },
                        { "inputSource", "POSE_PATH" },
                        { "inputValue", 1f },
                        { "occurredAtElapsedSec", 0.5f },
                    },
                },
                new AdapterScenario
                {
                    name = "timeline_watch",
                    actionId = "watch_timeline_segment",
                    channelId = SessionFlowChannelIds.Timeline,
                    expectedTargetId = "segment_intro",
                    payload = new Dictionary<string, object>
                    {
                        { "eventType", "TIMELINE_SEGMENT_COMPLETED" },
                        { "segmentId", "segment_intro" },
                        { "elapsedSec", "10.0" },
                        { "requiredSec", "10.0" },
                        { "progress01", "1.0" },
                        { "attentionScore", "0.9" },
                        { "inputSource", "TIMELINE" },
                        { "inputValue", 1f },
                        { "occurredAtElapsedSec", 0.55f },
                    },
                },
                new AdapterScenario
                {
                    name = "sequence_order",
                    actionId = "select_sequence_in_order",
                    channelId = SessionFlowChannelIds.Sequence,
                    expectedTargetId = "A-B-C-D",
                    payload = new Dictionary<string, object>
                    {
                        { "eventType", "SEQUENCE_ORDER_COMPLETED" },
                        { "stepIndex", "4" },
                        { "expectedIndex", "4" },
                        { "expectedValue", "A-B-C-D" },
                        { "actualValue", "A-B-C-D" },
                        { "inputSource", "SEQUENCE" },
                        { "inputValue", 1f },
                        { "occurredAtElapsedSec", 0.6f },
                    },
                },
            };
        }

        private static void PersistValidationResult(string status, string details)
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var outputPath = Path.Combine(
                projectRoot,
                "Temp",
                "CliValidation",
                "adapter_integration_validation_result.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            var lines = new[]
            {
                $"status={status}",
                $"timestampUtc={DateTime.UtcNow:O}",
                $"details={details ?? string.Empty}",
            };

            File.WriteAllLines(outputPath, lines);
            Debug.Log("[AdapterIntegrationValidation] Result file: " + outputPath);
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertEqual(string expected, string actual, string message)
        {
            var safeExpected = expected ?? string.Empty;
            var safeActual = actual ?? string.Empty;
            if (!string.Equals(safeExpected, safeActual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{message} expected={safeExpected} actual={safeActual}");
            }
        }

        private static void AssertEqual(TaskGraphRunState expected, TaskGraphRunState actual, string message)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException(
                    $"{message} expected={expected} actual={actual}");
            }
        }

        private sealed class AdapterScenario
        {
            public string name = string.Empty;
            public string actionId = string.Empty;
            public string channelId = string.Empty;
            public string expectedTargetId = string.Empty;
            public Dictionary<string, object> payload = new Dictionary<string, object>();
            public List<KeyValuePairString> additionalConstraints = new List<KeyValuePairString>();
        }
    }
}
