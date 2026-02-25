using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheraplyCore.Editor.Automation
{
    public static class SessionFlowSmokeTemplateValidation
    {
        public static void RunSessionFlowSmokeTemplateValidation()
        {
            try
            {
                var summary = ExecuteValidation();
                Debug.Log("[SessionFlowSmokeTemplateValidation] PASS: " + summary);
                PersistValidationResult("PASS", summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[SessionFlowSmokeTemplateValidation] FAIL: " + exception);
                PersistValidationResult("FAIL", exception.ToString());
                EditorApplication.Exit(1);
            }
        }

        private static string ExecuteValidation()
        {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            GameObject smokeHost = null;
            GameDefinitionAsset definitionAsset = null;
            try
            {
                smokeHost = new GameObject("SessionFlowSmokeRuntime");
                smokeHost.SetActive(false);

                var flowConfigProvider = smokeHost.AddComponent<FlowConfigProvider>();
                var actionAdapterRegistry = smokeHost.AddComponent<ActionAdapterRegistry>();
                var sessionFlowRunner = smokeHost.AddComponent<SessionFlowRunner>();

                definitionAsset = ScriptableObject.CreateInstance<GameDefinitionAsset>();
                definitionAsset.definition = CreateSmokeDefinition();

                SetNonPublicField(flowConfigProvider, "_definitionAsset", definitionAsset);
                SetNonPublicField(flowConfigProvider, "_validateOnAwake", false);
                SetNonPublicField(flowConfigProvider, "_fallbackToSampleWhenMissing", false);
                SetNonPublicField(flowConfigProvider, "_preferJsonSource", false);

                SetNonPublicField(actionAdapterRegistry, "_registerBuiltInPluginsOnAwake", true);
                SetNonPublicField(actionAdapterRegistry, "_emitIntentEvents", true);

                SetNonPublicField(sessionFlowRunner, "_flowConfigProvider", flowConfigProvider);
                SetNonPublicField(sessionFlowRunner, "_actionAdapterRegistry", actionAdapterRegistry);
                SetNonPublicField(sessionFlowRunner, "_autoStartOnEnable", false);
                SetNonPublicField(sessionFlowRunner, "_emitFlowTelemetry", false);
                SetNonPublicField(sessionFlowRunner, "_interruptSessionOnDisable", false);

                smokeHost.SetActive(true);

                AssertTrue(
                    sessionFlowRunner.TryStartFlow(out var firstStartReason),
                    "Smoke flow first start failed. reason=" + firstStartReason);
                CompleteRunningFlow(sessionFlowRunner);

                AssertTrue(
                    sessionFlowRunner.TryStartFlow(out var secondStartReason),
                    "Smoke flow second start failed. reason=" + secondStartReason);
                CompleteRunningFlow(sessionFlowRunner);

                return
                    "scene=generated_in_memory; " +
                    "runs=2; " +
                    "graphTerminal=COMPLETED; " +
                    "definitionOnly=true";
            }
            finally
            {
                if (definitionAsset != null)
                {
                    UnityEngine.Object.DestroyImmediate(definitionAsset);
                }

                if (smokeHost != null)
                {
                    UnityEngine.Object.DestroyImmediate(smokeHost);
                }
            }
        }

        private static void CompleteRunningFlow(SessionFlowRunner runner)
        {
            AssertTrue(runner != null, "SessionFlowRunner is required.");
            AssertTrue(runner.IsRunning, "Flow must be running before completion.");

            var intent = new ActionIntent
            {
                actionId = "confirm_choice",
                channelId = SessionFlowChannelIds.Pointer,
                targetId = "target_primary",
                inputSource = "POINTER",
                inputHand = "RIGHT",
                inputValue = 1f,
                occurredAtElapsedSec = 0.1f,
            };

            InvokeNonPublic(runner, "HandleIntent", intent);

            AssertEqual(
                TaskGraphRunState.Completed,
                runner.GraphState,
                "Smoke graph should complete after valid action.");
            AssertEqual(
                "n_complete",
                runner.ActiveNodeId,
                "Unexpected terminal node in smoke graph.");
            AssertTrue(!runner.IsRunning, "Runner should not remain in running state after completion.");
        }

        private static GameDefinition CreateSmokeDefinition()
        {
            var definition = GameDefinition.CreateSample();
            definition.gameId = "session_flow_smoke_template";
            definition.displayName = "Session Flow Smoke Template";
            definition.commentVersion = "2026-02-25: smoke template validation fixture";
            definition.controlMode = SessionFlowControlModes.Hybrid;
            definition.channels = SessionFlowDefaults.CreateDefaultChannels();
            definition.policies = SessionFlowPolicies.CreateDefault();
            definition.taskGraph = new TaskGraphDefinition
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
                            new AllowedActionDefinition
                            {
                                actionId = "confirm_choice",
                                constraints = new List<KeyValuePairString>
                                {
                                    new KeyValuePairString
                                    {
                                        key = "requiredChannelId",
                                        value = SessionFlowChannelIds.Pointer,
                                    },
                                    new KeyValuePairString
                                    {
                                        key = "requiredTargetId",
                                        value = "target_primary",
                                    },
                                },
                            },
                        },
                        nextOnSuccess = "n_complete",
                    },
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "n_complete",
                        nodeType = TaskGraphNodeTypes.Complete,
                    },
                },
            };

            return definition;
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
                "session_flow_smoke_template_validation_result.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            var lines = new[]
            {
                $"status={status}",
                $"timestampUtc={DateTime.UtcNow:O}",
                $"details={details ?? string.Empty}",
            };

            File.WriteAllLines(outputPath, lines);
            Debug.Log("[SessionFlowSmokeTemplateValidation] Result file: " + outputPath);
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
    }
}
