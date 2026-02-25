using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheraplyCore.Editor.Automation
{
    public static class SceneLifecycleEffectsValidation
    {
        public static void RunSceneLifecycleEffectsValidation()
        {
            try
            {
                var summary = ExecuteValidation();
                Debug.Log("[SceneLifecycleEffectsValidation] PASS: " + summary);
                PersistValidationResult("PASS", summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[SceneLifecycleEffectsValidation] FAIL: " + exception);
                PersistValidationResult("FAIL", exception.ToString());
                EditorApplication.Exit(1);
            }
        }

        private static string ExecuteValidation()
        {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            GameObject runtimeHost = null;
            try
            {
                runtimeHost = new GameObject("SceneLifecycleEffectsValidationHost");
                runtimeHost.SetActive(false);

                var sessionContext = runtimeHost.AddComponent<GameSessionContext>();
                var sessionBridge = runtimeHost.AddComponent<SessionRuntimeBridge>();
                var sceneRuntime = runtimeHost.AddComponent<SceneRuntimeController>();
                var effectRunner = runtimeHost.AddComponent<EffectRunner>();

                SetNonPublicField(sessionContext, "_autoStartSessionOnAwake", false);
                SetNonPublicField(sessionContext, "_deferAutoStartWhenRecoverySnapshotExists", false);
                SetNonPublicField(sceneRuntime, "_emitSceneTelemetry", false);
                SetNonPublicField(effectRunner, "_emitEffectTelemetry", false);
                SetNonPublicField(effectRunner, "_registerBuiltInsOnAwake", true);

                runtimeHost.SetActive(true);

                SessionFlowBuiltInEffectPlugins.RegisterBuiltIns(effectRunner.PluginRegistry, replaceExisting: true);
                AssertTrue(
                    effectRunner.PluginRegistry.TryResolve("load_scene", out var loadScenePlugin) && loadScenePlugin != null,
                    "Built-in load_scene effect plugin should be registered.");

                AssertTrue(
                    sessionBridge.TryStartSession("p", "t", "scene_fx_validation_session", out var startReason),
                    "Session should start for scene lifecycle validation. reason=" + startReason);

                ValidateEffectToRuntimeWiring(effectRunner);
                ValidateInvalidOperationRejection(effectRunner, sessionBridge);
                ValidateRepeatedIdempotency(effectRunner, sessionBridge);

                return "effectWiring=OK; invalidStateRejection=OK; repeatedIdempotency=OK";
            }
            finally
            {
                if (runtimeHost != null)
                {
                    UnityEngine.Object.DestroyImmediate(runtimeHost);
                }
            }
        }

        private static void ValidateEffectToRuntimeWiring(EffectRunner effectRunner)
        {
            AssertTrue(effectRunner != null, "EffectRunner is required.");

            var activeSceneName = SceneManager.GetActiveScene().name;
            if (string.IsNullOrWhiteSpace(activeSceneName))
            {
                activeSceneName = "placeholder_scene";
            }

            var invalidModeEffects = new List<EffectDefinition>
            {
                new EffectDefinition
                {
                    effectId = "load_scene",
                    parameters = new List<KeyValuePairString>
                    {
                        new KeyValuePairString { key = "sceneName", value = activeSceneName },
                        new KeyValuePairString { key = "mode", value = "unsupported_mode" },
                    },
                },
            };

            AssertFalse(
                effectRunner.ExecuteEffects(
                    "n_scene",
                    "on_enter",
                    invalidModeEffects,
                    "SCENE_EFFECT_TEST",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    out var invalidModeReason),
                "Invalid load mode should fail through effect wiring.");
            AssertEqual(
                "SCENE_LOAD_MODE_INVALID",
                invalidModeReason,
                "Unexpected reason code for invalid load mode.");
        }

        private static void ValidateInvalidOperationRejection(
            EffectRunner effectRunner,
            SessionRuntimeBridge sessionBridge)
        {
            AssertTrue(effectRunner != null, "EffectRunner is required.");
            AssertTrue(sessionBridge != null, "SessionRuntimeBridge is required.");

            AssertTrue(
                sessionBridge.TryCompleteSession(out var completeReason),
                "Session should complete for invalid-state rejection test. reason=" + completeReason);

            var unloadEffects = new List<EffectDefinition>
            {
                new EffectDefinition
                {
                    effectId = "unload_scene",
                    parameters = new List<KeyValuePairString>
                    {
                        new KeyValuePairString { key = "sceneName", value = "scene_not_loaded_validation" },
                    },
                },
            };

            AssertFalse(
                effectRunner.ExecuteEffects(
                    "n_scene",
                    "on_enter",
                    unloadEffects,
                    "SCENE_EFFECT_TEST",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    out var unloadReason),
                "Scene operation should be rejected in terminal session state.");
            AssertEqual(
                "SCENE_OPERATION_NOT_ALLOWED_IN_SESSION_STATE",
                unloadReason,
                "Unexpected rejection reason for terminal session state.");
        }

        private static void ValidateRepeatedIdempotency(
            EffectRunner effectRunner,
            SessionRuntimeBridge sessionBridge)
        {
            AssertTrue(effectRunner != null, "EffectRunner is required.");
            AssertTrue(sessionBridge != null, "SessionRuntimeBridge is required.");

            AssertTrue(
                sessionBridge.TryStartSession("p2", "t2", "scene_fx_validation_session_2", out var restartReason),
                "Session should restart for idempotency test. reason=" + restartReason);

            var unloadEffects = new List<EffectDefinition>
            {
                new EffectDefinition
                {
                    effectId = "unload_scene",
                    parameters = new List<KeyValuePairString>
                    {
                        new KeyValuePairString { key = "sceneName", value = "scene_not_loaded_idempotent" },
                    },
                },
            };

            AssertTrue(
                effectRunner.ExecuteEffects(
                    "n_scene",
                    "on_enter",
                    unloadEffects,
                    "SCENE_EFFECT_TEST",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    out var firstReason),
                "First unload of already unloaded scene should be idempotent success. reason=" + firstReason);
            AssertTrue(
                effectRunner.ExecuteEffects(
                    "n_scene",
                    "on_enter",
                    unloadEffects,
                    "SCENE_EFFECT_TEST",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    out var secondReason),
                "Second unload of already unloaded scene should remain idempotent success. reason=" + secondReason);
        }

        private static void SetNonPublicField(object target, string fieldName, object value)
        {
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
                "scene_lifecycle_effects_validation_result.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);
            File.WriteAllLines(
                outputPath,
                new[]
                {
                    $"status={status}",
                    $"timestampUtc={DateTime.UtcNow:O}",
                    $"details={details ?? string.Empty}",
                });

            Debug.Log("[SceneLifecycleEffectsValidation] Result file: " + outputPath);
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertFalse(bool condition, string message)
        {
            if (condition)
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
    }
}
