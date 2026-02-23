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
    public static class ToolTelemetryStackValidation
    {
        private const string ValidationGameId = "pulse_target_tap";

        public static void RunToolTelemetryStackValidation()
        {
            try
            {
                var summary = ExecuteValidation();
                Debug.Log("[ToolTelemetryStackValidation] PASS: " + summary);
                PersistValidationResult("PASS", summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[ToolTelemetryStackValidation] FAIL: " + exception);
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
                DestroyExistingSingleton<ToolGripTracker>();
                DestroyExistingSingleton<ToolImpactProbe>();

                var host = new GameObject("ToolTelemetryStackValidationHost");
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

                var gripTracker = host.AddComponent<ToolGripTracker>();
                SetNonPublicField(gripTracker, "_interactionEventBridge", bridge);
                SetNonPublicField(gripTracker, "_emitTelemetry", true);
                SetNonPublicField(gripTracker, "_holdHeartbeatIntervalSec", 0.4f);
                SetNonPublicField(gripTracker, "_logTelemetry", false);
                InvokeNonPublic(gripTracker, "Awake");

                var impactProbe = host.AddComponent<ToolImpactProbe>();
                SetNonPublicField(impactProbe, "_interactionEventBridge", bridge);
                SetNonPublicField(impactProbe, "_emitTelemetry", true);
                SetNonPublicField(impactProbe, "_logTelemetry", false);
                InvokeNonPublic(impactProbe, "Awake");

                var validTarget = GameObject.CreatePrimitive(PrimitiveType.Cube);
                validTarget.name = "ToolTelemetryValidTarget";
                createdObjects.Add(validTarget);
                var validZone = validTarget.AddComponent<TargetValidationZone>();
                validZone.Configure("tool_target_valid", "TOOL_VALIDATION_ZONE", defaultValidTarget: true, requiredToolId: "quest_pointer_wand");

                var invalidTarget = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                invalidTarget.name = "ToolTelemetryInvalidTarget";
                createdObjects.Add(invalidTarget);
                var invalidZone = invalidTarget.AddComponent<TargetValidationZone>();
                invalidZone.Configure("tool_target_invalid", "TOOL_VALIDATION_ZONE", defaultValidTarget: false, requiredToolId: "quest_pointer_wand");

                var gripStart = new ToolGripTracker.ToolGripSample
                {
                    toolId = "quest_pointer_wand",
                    gameId = ValidationGameId,
                    inputHand = "RIGHT",
                    inputSource = "QUEST_POINTER",
                    inputControl = "RIGHT_GRIP_AXIS",
                    sourceComponent = nameof(ToolTelemetryStackValidation),
                    inputValue = 0.92f,
                    isPressed = true,
                    realtimeSinceStartup = 10f,
                };

                gripTracker.RecordGripState(gripStart);

                var gripHold = gripStart;
                gripHold.inputValue = 0.86f;
                gripHold.realtimeSinceStartup = 10.8f;
                gripTracker.RecordGripState(gripHold);

                var gripEnd = gripStart;
                gripEnd.isPressed = false;
                gripEnd.inputValue = 0f;
                gripEnd.realtimeSinceStartup = 11.2f;
                gripTracker.RecordGripState(gripEnd);

                impactProbe.RecordImpact(new ToolImpactProbe.ToolImpactSample
                {
                    toolId = "quest_pointer_wand",
                    gameId = ValidationGameId,
                    inputHand = "RIGHT",
                    inputSource = "QUEST_POINTER",
                    inputControl = "RIGHT_TRIGGER_BUTTON",
                    sourceComponent = nameof(ToolTelemetryStackValidation),
                    targetId = validZone.TargetId,
                    targetName = validTarget.name,
                    inputValue = 1f,
                    impactForce = 0.88f,
                    hitDistanceMeters = 1.2f,
                    hitAnyCollider = true,
                    hitInteractiveTarget = true,
                    pointerSuggestedTargetValid = true,
                    hitCollider = validTarget.GetComponent<Collider>(),
                    targetValidationZone = validZone,
                    impactPointWorld = new Vector3(0.1f, 1.4f, 2.5f),
                    impactNormalWorld = Vector3.back,
                });

                impactProbe.RecordImpact(new ToolImpactProbe.ToolImpactSample
                {
                    toolId = "quest_pointer_wand",
                    gameId = ValidationGameId,
                    inputHand = "LEFT",
                    inputSource = "QUEST_POINTER",
                    inputControl = "LEFT_TRIGGER_BUTTON",
                    sourceComponent = nameof(ToolTelemetryStackValidation),
                    targetId = invalidZone.TargetId,
                    targetName = invalidTarget.name,
                    inputValue = 1f,
                    impactForce = 0.91f,
                    hitDistanceMeters = 1.6f,
                    hitAnyCollider = true,
                    hitInteractiveTarget = true,
                    pointerSuggestedTargetValid = false,
                    hitCollider = invalidTarget.GetComponent<Collider>(),
                    targetValidationZone = invalidZone,
                    impactPointWorld = new Vector3(0.4f, 1.2f, 2.8f),
                    impactNormalWorld = Vector3.forward,
                });

                impactProbe.RecordImpact(new ToolImpactProbe.ToolImpactSample
                {
                    toolId = "quest_pointer_wand",
                    gameId = ValidationGameId,
                    inputHand = "RIGHT",
                    inputSource = "QUEST_POINTER",
                    inputControl = "RIGHT_TRIGGER_BUTTON",
                    sourceComponent = nameof(ToolTelemetryStackValidation),
                    targetId = string.Empty,
                    targetName = string.Empty,
                    inputValue = 1f,
                    impactForce = 0.7f,
                    hitDistanceMeters = 0f,
                    hitAnyCollider = false,
                    hitInteractiveTarget = false,
                    pointerSuggestedTargetValid = null,
                    hitCollider = null,
                    targetValidationZone = null,
                    impactPointWorld = Vector3.zero,
                    impactNormalWorld = Vector3.zero,
                });

                ValidateCapturedEvents(capturedEvents);

                var toolEvents = capturedEvents
                    .Where(record => TryReadString(record, "eventType").StartsWith("TOOL_", StringComparison.Ordinal))
                    .ToArray();
                var eventTypes = toolEvents
                    .Select(record => TryReadString(record, "eventType"))
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
                    "events={0}; toolEvents={1}; maxSequence={2}; eventTypes={3}",
                    capturedEvents.Count,
                    toolEvents.Length,
                    maxSequence,
                    string.Join(",", eventTypes));
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
                throw new InvalidOperationException("No canonical events were captured.");
            }

            var previousSequence = 0L;
            var observedToolEventIds = new HashSet<string>(StringComparer.Ordinal);
            var observedToolEventTypes = new HashSet<string>(StringComparer.Ordinal);
            var observedValidTarget = false;
            var observedInvalidTarget = false;

            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];

                EnsureRequiredString(record, "eventId");
                EnsureRequiredString(record, "sequenceNumber");
                EnsureRequiredString(record, "ownerKey");
                EnsureRequiredString(record, "sessionKey");
                EnsureRequiredString(record, "sourceOfTruth");
                EnsureRequiredString(record, "interactionType");
                EnsureRequiredString(record, "eventType");

                var sourceOfTruth = TryReadString(record, "sourceOfTruth");
                if (!string.Equals(sourceOfTruth, "MOBILE_CONTROLLER", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Unexpected sourceOfTruth at index {i}: {sourceOfTruth}");
                }

                var sequence = TryReadLong(record, "sequenceNumber");
                if (sequence <= previousSequence)
                {
                    throw new InvalidOperationException(
                        $"Non-monotonic sequenceNumber at index {i}: previous={previousSequence}, current={sequence}");
                }

                previousSequence = sequence;

                var eventType = TryReadString(record, "eventType");
                if (!eventType.StartsWith("TOOL_", StringComparison.Ordinal))
                {
                    continue;
                }

                var interactionType = TryReadString(record, "interactionType");
                if (!string.Equals(interactionType, "TOOL", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Tool event emitted with unexpected interactionType at index {i}: {interactionType}");
                }

                var eventId = TryReadString(record, "eventId");
                if (!observedToolEventIds.Add(eventId))
                {
                    throw new InvalidOperationException($"Duplicate tool eventId detected: {eventId}");
                }

                observedToolEventTypes.Add(eventType);

                if (eventType.StartsWith("TOOL_IMPACT_", StringComparison.Ordinal))
                {
                    var targetValid = TryReadBool(record, "targetValid");
                    observedValidTarget |= targetValid;
                    observedInvalidTarget |= !targetValid;
                }
            }

            var requiredEventTypes = new[]
            {
                "TOOL_GRIP_START",
                "TOOL_GRIP_HOLD",
                "TOOL_GRIP_END",
                "TOOL_IMPACT_HIT",
                "TOOL_IMPACT_INVALID",
                "TOOL_IMPACT_MISS",
            };

            for (var i = 0; i < requiredEventTypes.Length; i++)
            {
                var required = requiredEventTypes[i];
                if (!observedToolEventTypes.Contains(required))
                {
                    throw new InvalidOperationException($"Missing required tool event type: {required}");
                }
            }

            if (!observedValidTarget || !observedInvalidTarget)
            {
                throw new InvalidOperationException(
                    "Tool impact validation did not capture both valid and invalid target outcomes.");
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

        private static bool TryReadBool(
            IReadOnlyDictionary<string, object> record,
            string key)
        {
            if (record == null || string.IsNullOrWhiteSpace(key) || !record.TryGetValue(key, out var value) || value == null)
            {
                return false;
            }

            switch (value)
            {
                case bool boolValue:
                    return boolValue;
                case int intValue:
                    return intValue != 0;
                case long longValue:
                    return longValue != 0L;
                case float floatValue:
                    return Math.Abs(floatValue) > float.Epsilon;
                case double doubleValue:
                    return Math.Abs(doubleValue) > double.Epsilon;
            }

            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (bool.TryParse(text, out var parsedBool))
            {
                return parsedBool;
            }

            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedFloat))
            {
                return Math.Abs(parsedFloat) > float.Epsilon;
            }

            return false;
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
                "tool_telemetry_stack_result.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            var lines = new[]
            {
                $"status={status}",
                $"timestampUtc={DateTime.UtcNow:O}",
                $"details={details ?? string.Empty}",
            };

            File.WriteAllLines(outputPath, lines);
            Debug.Log("[ToolTelemetryStackValidation] Result file: " + outputPath);
        }
    }
}
