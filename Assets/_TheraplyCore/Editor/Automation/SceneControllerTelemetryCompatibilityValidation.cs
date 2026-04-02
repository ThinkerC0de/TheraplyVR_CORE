using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using TheraplyCore.Interactions;
using UnityEditor;
using UnityEngine;

namespace TheraplyCore.Editor.Automation
{
    public static class SceneControllerTelemetryCompatibilityValidation
    {
        private const string ValidationGameId = "scene_validation_game";

        public static void RunSceneControllerTelemetryCompatibilityValidation()
        {
            try
            {
                var summary = ExecuteValidation();
                Debug.Log("[SceneControllerTelemetryCompatibilityValidation] PASS: " + summary);
                PersistValidationResult("PASS", summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[SceneControllerTelemetryCompatibilityValidation] FAIL: " + exception);
                PersistValidationResult("FAIL", exception.ToString());
                EditorApplication.Exit(1);
            }
        }

        private static string ExecuteValidation()
        {
            var capturedEvents = new List<IReadOnlyDictionary<string, object>>();
            var createdObjects = new List<UnityEngine.Object>();

            try
            {
                DestroyExistingSingleton<InteractionEventBridge>();

                var runtimeHost = new GameObject("SceneControllerTelemetryValidationHost");
                createdObjects.Add(runtimeHost);

                var sessionContext = runtimeHost.AddComponent<GameSessionContext>();
                SetNonPublicField(sessionContext, "_autoStartSessionOnAwake", false);
                SetNonPublicField(sessionContext, "_deferAutoStartWhenRecoverySnapshotExists", false);
                InvokeNonPublic(sessionContext, "Awake");
                sessionContext.BeginSession("validation_student", "validation_therapist", "scene_validation_session");
                sessionContext.TryTransitionTo(SessionLifecycleState.IN_PROGRESS, "SCENE_VALIDATION_START");

                var telemetryService = runtimeHost.AddComponent<GameTelemetryService>();
                SetNonPublicField(telemetryService, "_sessionContext", sessionContext);
                SetNonPublicField(telemetryService, "_firebaseDataService", null);
                SetNonPublicField(telemetryService, "_logTelemetry", false);
                InvokeNonPublic(telemetryService, "Awake");

                var bridge = runtimeHost.AddComponent<InteractionEventBridge>();
                SetNonPublicField(bridge, "_gameTelemetryService", telemetryService);
                SetNonPublicField(bridge, "_sessionContext", sessionContext);
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

                var moduleHost = new GameObject("SceneControllerTelemetryValidationModule");
                createdObjects.Add(moduleHost);

                var controller = moduleHost.AddComponent<ValidationSceneGameController>();
                var context = new ValidationGameContext(sessionContext, telemetryService);
                var config = SceneGameConfig.CreateDefault(
                    ValidationGameId,
                    "scene_validation_config",
                    1,
                    "{}");

                controller.Initialize(config, context);
                controller.StartGame();
                controller.StopGame(GameStopReason.Completed);

                AssertContainsEventType(capturedEvents, "GAME_STARTED");
                AssertContainsEventType(capturedEvents, "GAME_STOPPED");
                AssertContainsEventType(capturedEvents, "SESSION_TERMINAL");

                var qualityGate = new CanonicalFlowTelemetryQualityGate();
                var requirements = CanonicalFlowTelemetryQualityGate.CreateDefaultRequirements();
                var report = qualityGate.Evaluate(capturedEvents, requirements);
                if (!report.readyForExport)
                {
                    throw new InvalidOperationException(
                        $"Flow telemetry quality gate rejected scene-controller stream. reason={report.reasonCode}");
                }

                return $"events={capturedEvents.Count}; qualityGate={report.reasonCode}";
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

        private static void AssertContainsEventType(
            IReadOnlyList<IReadOnlyDictionary<string, object>> records,
            string expectedEventType)
        {
            var normalizedExpected = NormalizeEventType(expectedEventType);
            for (var i = 0; i < records.Count; i++)
            {
                if (string.Equals(
                        NormalizeEventType(ReadString(records[i], "eventType")),
                        normalizedExpected,
                        StringComparison.Ordinal))
                {
                    return;
                }
            }

            throw new InvalidOperationException("Missing expected eventType: " + normalizedExpected);
        }

        private static string NormalizeEventType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Trim().Replace(' ', '_').ToUpperInvariant();
        }

        private static string ReadString(IReadOnlyDictionary<string, object> record, string key)
        {
            if (record == null ||
                string.IsNullOrWhiteSpace(key) ||
                !record.TryGetValue(key, out var value) ||
                value == null)
            {
                return string.Empty;
            }

            return Convert.ToString(value)?.Trim() ?? string.Empty;
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
                "scene_controller_telemetry_compatibility_validation_result.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            var lines = new[]
            {
                "status=" + status,
                $"timestampUtc={DateTime.UtcNow:O}",
                "details=" + (details ?? string.Empty),
            };

            File.WriteAllLines(outputPath, lines);
            Debug.Log("[SceneControllerTelemetryCompatibilityValidation] Result file: " + outputPath);
        }

        private sealed class ValidationSceneGameController : SceneGameController
        {
            public override string GameId => ValidationGameId;
        }

        private sealed class ValidationGameContext : IGameContext
        {
            public ValidationGameContext(ISessionContext session, ITelemetryService telemetry)
            {
                Session = session;
                Telemetry = telemetry;
            }

            public ISessionContext Session { get; }
            public ICommandBus CommandBus => null;
            public ITelemetryService Telemetry { get; }
            public IGameClock Clock => null;
            public IGameFeedback Feedback => null;
        }
    }
}
