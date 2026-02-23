using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using TheraplyCore.Interactions;
using TheraplyExamples;
using UnityEditor;
using UnityEngine;

namespace TheraplyCore.Editor.Automation
{
    public static class InteractionEventSchemaValidation
    {
        public static void RunInteractionEventSchemaValidation()
        {
            try
            {
                var summary = ExecuteValidation();
                Debug.Log("[InteractionEventSchemaValidation] PASS: " + summary);
                PersistValidationResult("PASS", summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[InteractionEventSchemaValidation] FAIL: " + exception);
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
                var existingBridge = UnityEngine.Object.FindFirstObjectByType<InteractionEventBridge>();
                if (existingBridge != null)
                {
                    UnityEngine.Object.DestroyImmediate(existingBridge.gameObject);
                }

                var host = new GameObject("InteractionEventSchemaValidationHost");
                createdObjects.Add(host);

                var cameraHost = new GameObject("InteractionEventSchemaValidationCamera");
                createdObjects.Add(cameraHost);
                var validationCamera = cameraHost.AddComponent<Camera>();
                validationCamera.tag = "MainCamera";
                validationCamera.transform.position = new Vector3(0f, 1.4f, -4f);
                validationCamera.transform.LookAt(Vector3.zero);

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
                SetNonPublicField(bridge, "_logCanonicalEvents", true);
                InvokeNonPublic(bridge, "Awake");

                bridge.EventPublished += payload =>
                {
                    var safePayload = payload != null
                        ? new Dictionary<string, object>(payload)
                        : new Dictionary<string, object>();
                    capturedEvents.Add(safePayload);
                };

                var gameContext = new ValidationGameContext(sessionContext, telemetryService);

                var smokeHost = new GameObject("ValidationSmokeGame");
                createdObjects.Add(smokeHost);
                var smokeGame = smokeHost.AddComponent<SmokeTestGameModule>();
                smokeGame.Initialize(SmokeTestGameConfig.CreateDefault(0f), gameContext);
                smokeGame.StartGame();
                smokeGame.StopGame(GameStopReason.Completed);

                var demoHost = new GameObject("ValidationDemoCubeGame");
                createdObjects.Add(demoHost);
                var demoGame = demoHost.AddComponent<DemoCubeGameModule>();
                demoGame.Initialize(DemoCubeGameConfig.CreateDefault(0.35f), gameContext);
                demoGame.StartGame();

                var demoTarget = UnityEngine.Object.FindFirstObjectByType<DemoCubeClickTarget>();
                if (demoTarget == null)
                {
                    throw new InvalidOperationException("Demo cube click target was not created.");
                }

                demoTarget.ActivateFromPointer("VALIDATION_POINTER|RIGHT|RIGHT_TRIGGER_BUTTON|1.000");
                demoGame.StopGame(GameStopReason.TherapistStop);

                var pulseHost = new GameObject("ValidationPulseTargetsGame");
                createdObjects.Add(pulseHost);
                var pulseGame = pulseHost.AddComponent<PulseTargetsGameModule>();
                pulseGame.Initialize(PulseTargetsGameConfig.CreateDefault(6, 0.8f, 0.3f), gameContext);
                pulseGame.StartGame();

                var pulseTarget = UnityEngine.Object.FindFirstObjectByType<PulseTargetClickTarget>();
                if (pulseTarget == null)
                {
                    throw new InvalidOperationException("Pulse target click target was not created.");
                }

                pulseTarget.ActivateFromPointer("VALIDATION_POINTER|LEFT|LEFT_TRIGGER_BUTTON|0.950");
                pulseGame.StopGame(GameStopReason.TherapistStop);

                var pointerTelemetryService = host.AddComponent<PointerTelemetryService>();
                SetNonPublicField(pointerTelemetryService, "_gameTelemetryService", telemetryService);
                SetNonPublicField(pointerTelemetryService, "_emitTelemetry", true);
                InvokeNonPublic(pointerTelemetryService, "Awake");

                pointerTelemetryService.RecordShot(new Dictionary<string, object>
                {
                    { "inputHand", "RIGHT" },
                    { "inputSource", "QUEST_POINTER" },
                    { "inputControl", "RIGHT_TRIGGER_BUTTON" },
                    { "inputValue", "1.000" },
                    { "hitAnyCollider", true },
                    { "hitInteractiveTarget", true },
                    { "hoverIsValidTarget", true },
                    { "hitObjectName", "ValidationSphereTarget" },
                    { "hitDistanceMeters", "1.250" },
                    { "gameId", PulseTargetsGameConfig.DefaultGameId },
                    { "sourceComponent", "InteractionEventSchemaValidation" },
                });

                ValidateCapturedEvents(capturedEvents);

                var maxSequence = capturedEvents
                    .Select(record => TryReadLong(record, "sequenceNumber"))
                    .Where(value => value > 0)
                    .DefaultIfEmpty(0L)
                    .Max();

                var gameIds = capturedEvents
                    .Select(record => TryReadString(record, "gameId"))
                    .Where(gameId => !string.IsNullOrWhiteSpace(gameId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(gameId => gameId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "events={0}; maxSequence={1}; gameIds={2}",
                    capturedEvents.Count,
                    maxSequence,
                    string.Join(",", gameIds));
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

        private static void ValidateCapturedEvents(IReadOnlyList<Dictionary<string, object>> records)
        {
            if (records == null || records.Count == 0)
            {
                throw new InvalidOperationException("No canonical interaction events were captured.");
            }

            var expectedGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                SmokeTestGameConfig.DefaultGameId,
                DemoCubeGameConfig.DefaultGameId,
                PulseTargetsGameConfig.DefaultGameId,
            };
            var observedGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var observedEventIds = new HashSet<string>(StringComparer.Ordinal);
            var observedPointerEvent = false;
            var previousSequence = 0L;

            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                EnsureRequiredString(record, "schema");
                EnsureRequiredString(record, "schemaVersion");
                EnsureRequiredString(record, "eventId");
                EnsureRequiredString(record, "taskRunId");
                EnsureRequiredString(record, "attemptId");
                EnsureRequiredString(record, "gameId");
                EnsureRequiredString(record, "eventType");
                EnsureRequiredString(record, "actionOutcome");
                EnsureRequiredString(record, "sessionId");
                EnsureRequiredString(record, "ownerKey");
                EnsureRequiredString(record, "sessionKey");
                EnsureRequiredString(record, "sourceOfTruth");

                var sourceOfTruth = TryReadString(record, "sourceOfTruth");
                if (!string.Equals(sourceOfTruth, "MOBILE_CONTROLLER", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Unexpected sourceOfTruth at index {i}: {sourceOfTruth}");
                }

                var eventId = TryReadString(record, "eventId");
                if (!observedEventIds.Add(eventId))
                {
                    throw new InvalidOperationException($"Duplicate eventId detected: {eventId}");
                }

                var sequence = TryReadLong(record, "sequenceNumber");
                if (sequence <= 0)
                {
                    throw new InvalidOperationException(
                        $"Invalid sequenceNumber at index {i}: {sequence}");
                }

                if (sequence <= previousSequence)
                {
                    throw new InvalidOperationException(
                        $"Non-monotonic sequenceNumber at index {i}: previous={previousSequence}, current={sequence}");
                }

                previousSequence = sequence;

                var gameId = TryReadString(record, "gameId");
                observedGames.Add(gameId);

                var eventType = TryReadString(record, "eventType");
                if (eventType.StartsWith("POINTER_", StringComparison.Ordinal))
                {
                    observedPointerEvent = true;
                }
            }

            foreach (var expectedGame in expectedGames)
            {
                if (!observedGames.Contains(expectedGame))
                {
                    throw new InvalidOperationException(
                        $"Missing canonical events for active game: {expectedGame}");
                }
            }

            if (!observedPointerEvent)
            {
                throw new InvalidOperationException(
                    "Pointer interaction event was not captured.");
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
                "interaction_event_schema_result.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            var lines = new[]
            {
                $"status={status}",
                $"timestampUtc={DateTime.UtcNow:O}",
                $"details={details ?? string.Empty}",
            };

            File.WriteAllLines(outputPath, lines);
            Debug.Log("[InteractionEventSchemaValidation] Result file: " + outputPath);
        }

        private sealed class ValidationGameContext : IGameContext
        {
            private readonly ISessionContext _session;
            private readonly ITelemetryService _telemetry;
            private readonly ICommandBus _commandBus = new NullCommandBus();
            private readonly IGameClock _clock = new ValidationGameClock();
            private readonly IGameFeedback _feedback = new NullGameFeedback();

            public ValidationGameContext(
                ISessionContext session,
                ITelemetryService telemetry)
            {
                _session = session ?? throw new ArgumentNullException(nameof(session));
                _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            }

            public ISessionContext Session => _session;
            public ICommandBus CommandBus => _commandBus;
            public ITelemetryService Telemetry => _telemetry;
            public IGameClock Clock => _clock;
            public IGameFeedback Feedback => _feedback;
        }

        private sealed class NullCommandBus : ICommandBus
        {
            public void Subscribe<TCommand>(Action<TCommand> handler) where TCommand : IGameCommand
            {
            }

            public void Unsubscribe<TCommand>(Action<TCommand> handler) where TCommand : IGameCommand
            {
            }

            public Task PublishAsync<TCommand>(TCommand command) where TCommand : IGameCommand
            {
                return Task.CompletedTask;
            }
        }

        private sealed class ValidationGameClock : IGameClock
        {
            public float ElapsedSeconds => 0f;
        }

        private sealed class NullGameFeedback : IGameFeedback
        {
            public void PlaySfx(string id)
            {
            }

            public void HapticPulse(float amplitude, float durationSec)
            {
            }

            public void ShowHint(string messageKey)
            {
            }
        }
    }
}
