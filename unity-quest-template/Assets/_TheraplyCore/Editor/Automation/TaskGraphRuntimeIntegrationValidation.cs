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
            ValidateBranchTrueFalseRouting();
            ValidateBranchPrecedence();
            ValidateNoMatchFailurePath();
            ValidateCalendarConditionRouting();
            ValidateConditionTelemetryEvents();
            ValidateRejectedActionPath();
            return "successPath=OK; timeoutPath=OK; branchTrueFalse=OK; precedence=OK; noMatch=OK; calendarConditions=OK; conditionTelemetry=OK; rejectPath=OK";
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

        private static void ValidateBranchTrueFalseRouting()
        {
            var graph = CreateScoreBranchGraph();

            var highScoreSnapshot = new ScoringRuntime.ScoringSnapshot
            {
                scoreTotal = 3,
            };
            var highScoreRunner = CreateConditionRunner(
                highScoreSnapshot,
                branchPrecedence: BranchPrecedenceModes.FirstMatch);

            AssertTrue(
                highScoreRunner.Initialize(graph, out var highInitReason),
                "High-score branch graph should initialize. reason=" + highInitReason);
            AssertTrue(
                highScoreRunner.Start(0f, out var highStartReason),
                "High-score branch graph should start. reason=" + highStartReason);
            AssertEqual(
                TaskGraphRunState.Completed,
                highScoreRunner.State,
                "High-score branch graph should route to complete.");

            var lowScoreSnapshot = new ScoringRuntime.ScoringSnapshot
            {
                scoreTotal = 1,
            };
            var lowScoreRunner = CreateConditionRunner(
                lowScoreSnapshot,
                branchPrecedence: BranchPrecedenceModes.FirstMatch);

            AssertTrue(
                lowScoreRunner.Initialize(graph, out var lowInitReason),
                "Low-score branch graph should initialize. reason=" + lowInitReason);
            AssertTrue(
                lowScoreRunner.Start(0f, out var lowStartReason),
                "Low-score branch graph should start. reason=" + lowStartReason);
            AssertEqual(
                TaskGraphRunState.Failed,
                lowScoreRunner.State,
                "Low-score branch graph should route to fail.");
        }

        private static void ValidateBranchPrecedence()
        {
            var graph = new TaskGraphDefinition
            {
                entryNodeId = "n_branch",
                nodes = new List<TaskGraphNodeDefinition>
                {
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "n_branch",
                        nodeType = TaskGraphNodeTypes.Branch,
                        conditions = new List<ConditionDefinition>
                        {
                            new ConditionDefinition
                            {
                                conditionId = SessionFlowConditionIds.StateFlagEquals,
                                subject = "session_flag",
                                op = "eq",
                                value = "ready",
                                nextNodeId = "n_complete",
                            },
                            new ConditionDefinition
                            {
                                conditionId = SessionFlowConditionIds.ControlModeEquals,
                                op = "eq",
                                value = SessionFlowControlModes.Hybrid,
                                nextNodeId = "n_fail",
                            },
                        },
                    },
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "n_complete",
                        nodeType = TaskGraphNodeTypes.Complete,
                    },
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "n_fail",
                        nodeType = TaskGraphNodeTypes.Fail,
                    },
                },
            };

            var stateFlags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "session_flag", "ready" }
            };

            var firstMatchRunner = CreateConditionRunner(
                default(ScoringRuntime.ScoringSnapshot),
                branchPrecedence: BranchPrecedenceModes.FirstMatch,
                stateFlagsByKey: stateFlags,
                controlMode: SessionFlowControlModes.Hybrid);
            AssertTrue(
                firstMatchRunner.Initialize(graph, out var firstInitReason),
                "First-match branch graph should initialize. reason=" + firstInitReason);
            AssertTrue(
                firstMatchRunner.Start(0f, out var firstStartReason),
                "First-match branch graph should start. reason=" + firstStartReason);
            AssertEqual(
                TaskGraphRunState.Completed,
                firstMatchRunner.State,
                "First-match branch graph should pick the first matching condition.");

            var lastMatchRunner = CreateConditionRunner(
                default(ScoringRuntime.ScoringSnapshot),
                branchPrecedence: BranchPrecedenceModes.LastMatch,
                stateFlagsByKey: stateFlags,
                controlMode: SessionFlowControlModes.Hybrid);
            AssertTrue(
                lastMatchRunner.Initialize(graph, out var lastInitReason),
                "Last-match branch graph should initialize. reason=" + lastInitReason);
            AssertTrue(
                lastMatchRunner.Start(0f, out var lastStartReason),
                "Last-match branch graph should start. reason=" + lastStartReason);
            AssertEqual(
                TaskGraphRunState.Failed,
                lastMatchRunner.State,
                "Last-match branch graph should pick the last matching condition.");
        }

        private static void ValidateNoMatchFailurePath()
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
                        conditions = new List<ConditionDefinition>
                        {
                            new ConditionDefinition
                            {
                                conditionId = SessionFlowConditionIds.ScoreThreshold,
                                subject = "score_total",
                                op = "gte",
                                value = "10",
                                nextNodeId = "n_complete",
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

            var runner = CreateConditionRunner(
                new ScoringRuntime.ScoringSnapshot
                {
                    scoreTotal = 1,
                },
                branchPrecedence: BranchPrecedenceModes.FirstMatch);

            var terminalReasonCode = string.Empty;
            runner.GraphCompleted += (_, reasonCode) => terminalReasonCode = reasonCode ?? string.Empty;

            AssertTrue(
                runner.Initialize(graph, out var initReason),
                "No-match graph should initialize. reason=" + initReason);
            AssertTrue(
                runner.Start(0f, out var startReason),
                "No-match graph should start and fail deterministically. reason=" + startReason);
            AssertEqual(
                TaskGraphRunState.Failed,
                runner.State,
                "No-match graph should fail when no condition route is matched.");
            AssertEqual(
                "CONDITION_NO_MATCH",
                terminalReasonCode,
                "Unexpected terminal reason code for no-match failure path.");
        }

        private static void ValidateCalendarConditionRouting()
        {
            var graph = new TaskGraphDefinition
            {
                entryNodeId = "n_branch",
                nodes = new List<TaskGraphNodeDefinition>
                {
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "n_branch",
                        nodeType = TaskGraphNodeTypes.Branch,
                        conditions = new List<ConditionDefinition>
                        {
                            new ConditionDefinition
                            {
                                conditionId = SessionFlowConditionIds.IsEventActive,
                                subject = "holiday_event",
                                op = "eq",
                                value = "true",
                                nextNodeId = "n_complete",
                            },
                            new ConditionDefinition
                            {
                                conditionId = SessionFlowConditionIds.IsWithinDateWindow,
                                subject = "2020-12-20T00:00:00Z",
                                value = "2021-01-10T23:59:59Z",
                                op = "between_yearly",
                                nextNodeId = "n_fail",
                            },
                        },
                    },
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "n_complete",
                        nodeType = TaskGraphNodeTypes.Complete,
                    },
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "n_fail",
                        nodeType = TaskGraphNodeTypes.Fail,
                    },
                },
            };

            var calendarService = new MockCalendarService();
            calendarService.SetEventActive("holiday_event", true);

            var runner = CreateConditionRunner(
                default(ScoringRuntime.ScoringSnapshot),
                branchPrecedence: BranchPrecedenceModes.FirstMatch,
                calendarService: calendarService);

            AssertTrue(
                runner.Initialize(graph, out var initReason),
                "Calendar condition graph should initialize. reason=" + initReason);
            AssertTrue(
                runner.Start(0f, out var startReason),
                "Calendar condition graph should start. reason=" + startReason);
            AssertEqual(
                TaskGraphRunState.Completed,
                runner.State,
                "Calendar condition graph should route to complete.");
        }

        private static void ValidateConditionTelemetryEvents()
        {
            var graph = new TaskGraphDefinition
            {
                entryNodeId = "n_branch",
                nodes = new List<TaskGraphNodeDefinition>
                {
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "n_branch",
                        nodeType = TaskGraphNodeTypes.Branch,
                        conditions = new List<ConditionDefinition>
                        {
                            new ConditionDefinition
                            {
                                conditionId = SessionFlowConditionIds.ChannelEnabled,
                                subject = SessionFlowChannelIds.Pointer,
                                op = "eq",
                                value = "true",
                                nextNodeId = "n_complete",
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

            var channelStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                { SessionFlowChannelIds.Pointer, true },
            };

            var runner = CreateConditionRunner(
                default(ScoringRuntime.ScoringSnapshot),
                branchPrecedence: BranchPrecedenceModes.FirstMatch,
                channelEnabledById: channelStates);

            var conditionEventCount = 0;
            var branchEventCount = 0;
            var latestConditionTrace = default(ConditionEvaluationTrace);
            var latestBranchTrace = default(BranchRoutingTrace);

            runner.ConditionEvaluated += trace =>
            {
                conditionEventCount++;
                latestConditionTrace = trace;
            };

            runner.BranchRouted += trace =>
            {
                branchEventCount++;
                latestBranchTrace = trace;
            };

            AssertTrue(
                runner.Initialize(graph, out var initReason),
                "Condition telemetry graph should initialize. reason=" + initReason);
            AssertTrue(
                runner.Start(0f, out var startReason),
                "Condition telemetry graph should start. reason=" + startReason);
            AssertEqual(
                TaskGraphRunState.Completed,
                runner.State,
                "Condition telemetry graph should complete.");
            AssertTrue(conditionEventCount == 1, "Expected exactly one condition evaluation event.");
            AssertTrue(branchEventCount == 1, "Expected exactly one branch routed event.");
            AssertTrue(latestConditionTrace.matched, "Condition trace should be matched.");
            AssertTrue(latestBranchTrace.matched, "Branch trace should be matched.");
            AssertEqual("n_complete", latestBranchTrace.selectedNextNodeId, "Unexpected selected next node id.");
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

        private static TaskGraphDefinition CreateScoreBranchGraph()
        {
            return new TaskGraphDefinition
            {
                entryNodeId = "n_branch",
                nodes = new List<TaskGraphNodeDefinition>
                {
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "n_branch",
                        nodeType = TaskGraphNodeTypes.Branch,
                        conditions = new List<ConditionDefinition>
                        {
                            new ConditionDefinition
                            {
                                conditionId = SessionFlowConditionIds.ScoreThreshold,
                                subject = "score_total",
                                op = "gte",
                                value = "2",
                                nextNodeId = "n_complete",
                            },
                            new ConditionDefinition
                            {
                                conditionId = SessionFlowConditionIds.ScoreThreshold,
                                subject = "score_total",
                                op = "lt",
                                value = "2",
                                nextNodeId = "n_fail",
                            },
                        },
                    },
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "n_complete",
                        nodeType = TaskGraphNodeTypes.Complete,
                    },
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "n_fail",
                        nodeType = TaskGraphNodeTypes.Fail,
                    },
                },
            };
        }

        private static TaskGraphRunner CreateConditionRunner(
            ScoringRuntime.ScoringSnapshot scoringSnapshot,
            string branchPrecedence,
            IReadOnlyDictionary<string, bool> channelEnabledById = null,
            IReadOnlyDictionary<string, string> stateFlagsByKey = null,
            string controlMode = SessionFlowControlModes.Hybrid,
            ICalendarService calendarService = null)
        {
            var runner = new TaskGraphRunner();
            var conditionRegistry = new ConditionEvaluatorRegistry();
            SessionFlowBuiltInConditionEvaluators.RegisterBuiltIns(conditionRegistry, replaceExisting: true);
            runner.SetConditionEvaluatorRegistry(conditionRegistry);
            runner.SetRuntimeContext(
                gameId: "task_graph_validation_game",
                flowId: "task_graph_validation_flow",
                sessionId: "task_graph_validation_session",
                controlMode: controlMode);
            runner.SetConditionRuntimeState(
                scoringSnapshot,
                channelEnabledById,
                stateFlagsByKey,
                branchPrecedence,
                calendarService);
            return runner;
        }

        private sealed class MockCalendarService : ICalendarService
        {
            private readonly Dictionary<string, bool> _activeEvents =
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            public string ActiveTimezoneId => "UTC";
            public DateTime CurrentUtc => DateTime.UtcNow;

            public void SetRuntimeContext(string gameId, string flowId, string sessionId)
            {
            }

            public void SetTimeSource(ICalendarTimeSource timeSource)
            {
            }

            public bool TryApplyPolicy(
                CalendarPolicy policy,
                IReadOnlyDictionary<string, string> profileDatesByKey,
                out string reasonCode)
            {
                reasonCode = string.Empty;
                return true;
            }

            public bool TrySetOverrideUtc(string utcIso, out string reasonCode)
            {
                reasonCode = string.Empty;
                return true;
            }

            public void ClearOverrideUtc()
            {
            }

            public void TickRuntime()
            {
            }

            public bool TryEvaluateEventActive(string eventId, out bool isActive, out string reasonCode)
            {
                isActive = !string.IsNullOrWhiteSpace(eventId) &&
                           _activeEvents.TryGetValue(eventId.Trim(), out var active) &&
                           active;
                reasonCode = isActive ? "CALENDAR_EVENT_ACTIVE" : "CALENDAR_EVENT_INACTIVE";
                return true;
            }

            public bool TryEvaluateDateWindow(
                string start,
                string end,
                bool yearlyRecurring,
                string timezoneId,
                out bool matched,
                out string reasonCode)
            {
                matched = true;
                reasonCode = "CALENDAR_WINDOW_MATCHED";
                return true;
            }

            public bool TryEvaluateProfileDate(
                string profileDateKey,
                int daysBefore,
                int daysAfter,
                string timezoneId,
                out bool matched,
                out string reasonCode)
            {
                matched = true;
                reasonCode = "CALENDAR_PROFILE_DATE_MATCHED";
                return true;
            }

            public void SetEventActive(string eventId, bool active)
            {
                if (string.IsNullOrWhiteSpace(eventId))
                {
                    return;
                }

                _activeEvents[eventId.Trim()] = active;
            }
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
