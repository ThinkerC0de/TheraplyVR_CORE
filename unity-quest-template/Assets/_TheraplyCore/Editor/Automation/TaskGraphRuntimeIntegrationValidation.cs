using System;
using System.Collections.Generic;
using System.IO;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using UnityEditor;
using UnityEngine;

namespace TheraplyCore.Editor.Automation
{
    public static class TaskGraphRuntimeIntegrationValidation
    {
        public static void RunTaskGraphRuntimeIntegrationValidation()
        {
            try
            {
                var summary = ExecuteValidation();
                Debug.Log("[TaskGraphRuntimeIntegrationValidation] PASS: " + summary);
                PersistValidationResult("PASS", summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[TaskGraphRuntimeIntegrationValidation] FAIL: " + exception);
                PersistValidationResult("FAIL", exception.ToString());
                EditorApplication.Exit(1);
            }
        }

        private static string ExecuteValidation()
        {
            ValidateSuccessPath();
            ValidateTimeoutToFailPath();
            ValidateImmediateBranchCompletion();
            ValidateRejectedActionPath();
            return "successPath=OK; timeoutPath=OK; branchPath=OK; rejectPath=OK";
        }

        private static void ValidateSuccessPath()
        {
            var graph = new TaskGraphDefinition
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
                                actionId = "confirm_choice",
                                constraints = new List<KeyValuePairString>
                                {
                                    new KeyValuePairString
                                    {
                                        key = "requiredChannelId",
                                        value = SessionFlowChannelIds.Pointer,
                                    }
                                },
                            }
                        },
                    },
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "n_complete",
                        nodeType = TaskGraphNodeTypes.Complete,
                    },
                },
            };

            var runner = new TaskGraphRunner();
            AssertTrue(runner.Initialize(graph, out var initReason), "Success graph should initialize. reason=" + initReason);
            AssertTrue(runner.Start(0f, out var startReason), "Success graph should start. reason=" + startReason);
            AssertEqual(TaskGraphRunState.Running, runner.State, "Success graph should be running after start.");
            AssertEqual("n_action", runner.ActiveNodeId, "Unexpected active node for success graph.");

            var intent = new ActionIntent
            {
                actionId = "confirm_choice",
                channelId = SessionFlowChannelIds.Pointer,
                inputValue = 1f,
            };

            var submitted = runner.SubmitAction(intent, 0.1f, out var validationResult, out var submitReason);
            AssertTrue(submitted, "Action submission should complete. reason=" + submitReason);
            AssertTrue(validationResult != null && validationResult.accepted, "Action should be accepted in success path.");
            AssertEqual(TaskGraphRunState.Completed, runner.State, "Success graph should complete after accepted action.");
        }

        private static void ValidateTimeoutToFailPath()
        {
            var graph = new TaskGraphDefinition
            {
                entryNodeId = "n_action",
                nodes = new List<TaskGraphNodeDefinition>
                {
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "n_action",
                        nodeType = TaskGraphNodeTypes.Action,
                        timeoutSec = 0.5f,
                        nextOnTimeout = "n_fail",
                        allowedActions = new List<AllowedActionDefinition>
                        {
                            new AllowedActionDefinition { actionId = "confirm_choice" }
                        },
                    },
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "n_fail",
                        nodeType = TaskGraphNodeTypes.Fail,
                    },
                },
            };

            var runner = new TaskGraphRunner();
            AssertTrue(runner.Initialize(graph, out var initReason), "Timeout graph should initialize. reason=" + initReason);
            AssertTrue(runner.Start(0f, out var startReason), "Timeout graph should start. reason=" + startReason);
            AssertEqual(TaskGraphRunState.Running, runner.State, "Timeout graph should be running after start.");

            AssertTrue(runner.Tick(1.0f, out var tickReason), "Timeout graph tick should succeed. reason=" + tickReason);
            AssertEqual(TaskGraphRunState.Failed, runner.State, "Timeout graph should fail after timeout route.");
        }

        private static void ValidateImmediateBranchCompletion()
        {
            var graph = new TaskGraphDefinition
            {
                entryNodeId = "n_branch",
                nodes = new List<TaskGraphNodeDefinition>
                {
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "n_branch",
                        nodeType = TaskGraphNodeTypes.Condition,
                        nextOnSuccess = "n_complete",
                    },
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "n_complete",
                        nodeType = TaskGraphNodeTypes.Complete,
                    },
                },
            };

            var runner = new TaskGraphRunner();
            AssertTrue(runner.Initialize(graph, out var initReason), "Branch graph should initialize. reason=" + initReason);
            AssertTrue(runner.Start(0f, out var startReason), "Branch graph should start. reason=" + startReason);
            AssertEqual(TaskGraphRunState.Completed, runner.State, "Branch graph should complete immediately.");
        }

        private static void ValidateRejectedActionPath()
        {
            var graph = new TaskGraphDefinition
            {
                entryNodeId = "n_action",
                nodes = new List<TaskGraphNodeDefinition>
                {
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "n_action",
                        nodeType = TaskGraphNodeTypes.Action,
                        allowedActions = new List<AllowedActionDefinition>
                        {
                            new AllowedActionDefinition { actionId = "point_and_select_target" }
                        },
                    },
                },
            };

            var runner = new TaskGraphRunner();
            AssertTrue(runner.Initialize(graph, out var initReason), "Reject graph should initialize. reason=" + initReason);
            AssertTrue(runner.Start(0f, out var startReason), "Reject graph should start. reason=" + startReason);

            var wrongIntent = new ActionIntent
            {
                actionId = "confirm_choice",
                channelId = SessionFlowChannelIds.Pointer,
                inputValue = 1f,
            };

            var submitted = runner.SubmitAction(wrongIntent, 0.2f, out var validationResult, out var submitReason);
            AssertTrue(submitted, "Reject path submission should complete. reason=" + submitReason);
            AssertTrue(validationResult != null && !validationResult.accepted, "Wrong action should be rejected.");
            AssertEqual("ACTION_NOT_ALLOWED_IN_NODE", validationResult.reasonCode, "Unexpected rejection reason.");
            AssertEqual(TaskGraphRunState.Running, runner.State, "Reject graph should keep running after rejected action.");
        }

        private static void PersistValidationResult(string status, string details)
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var outputPath = Path.Combine(
                projectRoot,
                "Temp",
                "CliValidation",
                "task_graph_runtime_integration_validation_result.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            var lines = new[]
            {
                $"status={status}",
                $"timestampUtc={DateTime.UtcNow:O}",
                $"details={details ?? string.Empty}",
            };

            File.WriteAllLines(outputPath, lines);
            Debug.Log("[TaskGraphRuntimeIntegrationValidation] Result file: " + outputPath);
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
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
    }
}
