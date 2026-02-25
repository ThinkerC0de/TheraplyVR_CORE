using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using TheraplyCore.Network.Connection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheraplyCore.Editor.Automation
{
    public static class MobileLocaleSyncValidation
    {
        public static void RunMobileLocaleSyncValidation()
        {
            try
            {
                var summary = ExecuteValidation();
                Debug.Log("[MobileLocaleSyncValidation] PASS: " + summary);
                PersistValidationResult("PASS", summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[MobileLocaleSyncValidation] FAIL: " + exception);
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
                runtimeHost = new GameObject("MobileLocaleSyncValidationHost");
                runtimeHost.SetActive(false);

                var sessionContext = runtimeHost.AddComponent<GameSessionContext>();
                var telemetryService = runtimeHost.AddComponent<GameTelemetryService>();
                var interactionBridge = runtimeHost.AddComponent<TheraplyCore.Interactions.InteractionEventBridge>();
                var commandBus = runtimeHost.AddComponent<GameCommandBus>();
                var flowConfigProvider = runtimeHost.AddComponent<FlowConfigProvider>();
                var sessionBridge = runtimeHost.AddComponent<SessionRuntimeBridge>();
                var localizationRuntime = runtimeHost.AddComponent<LocalizationRuntime>();
                var controlGateway = runtimeHost.AddComponent<ControlRuntimeGateway>();

                SetNonPublicField(commandBus, "_enableCommandJournal", false);
                SetNonPublicField(commandBus, "_logInbound", false);
                SetNonPublicField(commandBus, "_logOutbound", false);
                SetNonPublicField(commandBus, "_logUnmappedIncoming", false);
                SetNonPublicField(controlGateway, "_logRouting", false);
                SetNonPublicField(controlGateway, "_emitControlTelemetry", true);
                SetNonPublicField(controlGateway, "_subscribeToRemoteCommands", true);
                SetNonPublicField(controlGateway, "_commandBus", commandBus);
                SetNonPublicField(controlGateway, "_flowConfigProvider", flowConfigProvider);
                SetNonPublicField(controlGateway, "_sessionRuntimeBridge", sessionBridge);
                SetNonPublicField(controlGateway, "_localizationRuntime", localizationRuntime);
                SetNonPublicField(controlGateway, "_interactionEventBridge", interactionBridge);
                SetNonPublicField(localizationRuntime, "_emitLocalizationTelemetry", false);
                SetNonPublicField(localizationRuntime, "_logLocalization", false);
                SetNonPublicField(flowConfigProvider, "_validateOnAwake", false);
                ConfigureFlowDefinition(flowConfigProvider, SessionFlowControlModes.Hybrid);

                runtimeHost.SetActive(true);

                commandBus.RegisterCommandId<SetLocaleRequestCommand>(GameCommandIds.SetLocaleRequest);
                InvokeNonPublicMethod(controlGateway, "RefreshControlMode");
                InvokeNonPublicMethod(controlGateway, "SubscribeRemoteCommands");

                AssertTrue(sessionContext != null, "SessionContext should be initialized.");
                AssertTrue(telemetryService != null, "GameTelemetryService should be initialized.");
                AssertTrue(sessionBridge != null, "SessionRuntimeBridge should be initialized.");
                AssertTrue(controlGateway != null, "ControlRuntimeGateway should be initialized.");

                localizationRuntime.ApplyPolicy(
                    new LocalizationPolicy
                    {
                        defaultLocale = "en-US",
                        fallbackToLanguageCode = true,
                    });
                localizationRuntime.SetValue("en-US", "ui.welcome.text", "Hello");
                localizationRuntime.SetValue("pl", "ui.welcome.text", "Czesc");
                AssertTrue(
                    localizationRuntime.TrySetLocale("en-US", out var initialLocaleReason),
                    "Initial locale should be set. reason=" + initialLocaleReason);

                var publishedEvents = new List<IReadOnlyDictionary<string, object>>();
                interactionBridge.EventPublished += payload =>
                {
                    if (payload == null)
                    {
                        return;
                    }

                    publishedEvents.Add(new Dictionary<string, object>(payload));
                };

                AssertTrue(
                    HasCommandHandler(commandBus, GameCommandIds.SetLocaleRequest),
                    "SET_LOCALE_REQUEST handler should be registered in GameCommandBus. registered=" +
                    DescribeRegisteredCommands(commandBus));

                ValidateRemoteLocaleSwitch(commandBus, localizationRuntime, publishedEvents);
                ValidateUnsupportedLocaleRejection(commandBus, localizationRuntime, publishedEvents);
                ValidateLocalOnlyRemoteRejection(commandBus, flowConfigProvider, localizationRuntime, publishedEvents);

                return "remoteLocaleSwitch=OK; unsupportedLocaleRejected=OK; localOnlyRemoteRejected=OK";
            }
            finally
            {
                if (runtimeHost != null)
                {
                    UnityEngine.Object.DestroyImmediate(runtimeHost);
                }
            }
        }

        private static void ValidateRemoteLocaleSwitch(
            GameCommandBus commandBus,
            LocalizationRuntime localizationRuntime,
            IReadOnlyList<IReadOnlyDictionary<string, object>> publishedEvents)
        {
            AssertTrue(commandBus != null, "GameCommandBus is required.");
            AssertTrue(localizationRuntime != null, "Localization runtime is required.");
            AssertTrue(publishedEvents != null, "Published events list is required.");

            var beforeEventCount = publishedEvents.Count;
            DispatchLocaleChangeCommand(commandBus, "pl-PL", "locale_req_success");

            var requestedEvent = FindEventSince(publishedEvents, beforeEventCount, "LOCALE_CHANGE_REQUESTED");
            AssertTrue(requestedEvent != null, "Expected locale_change_requested event.");
            AssertEqual(
                "pl-PL",
                ReadDetailsValue(requestedEvent, "locale"),
                "Requested event should include requested locale.");

            var appliedEvent = FindEventSince(publishedEvents, beforeEventCount, "LOCALE_CHANGE_APPLIED");
            if (appliedEvent == null)
            {
                var rejectedEvent = FindEventSince(publishedEvents, beforeEventCount, "LOCALE_CHANGE_REJECTED");
                var rejectedReason = rejectedEvent == null ? "NONE" : ReadValue(rejectedEvent, "reasonCode");
                throw new InvalidOperationException(
                    "Expected locale_change_applied event, but locale change was not applied. rejectedReason=" + rejectedReason);
            }

            AssertEqual(
                "pl-PL",
                localizationRuntime.CurrentLocale,
                "Locale should switch from remote locale command.");
            AssertEqual(
                "LOCALE_CHANGE_APPLIED",
                ReadValue(appliedEvent, "reasonCode"),
                "Applied event should provide explicit reason code.");
            AssertEqual(
                "pl-PL",
                ReadDetailsValue(appliedEvent, "locale"),
                "Applied event should include active locale.");
            AssertEqual(
                "en-US",
                ReadDetailsValue(appliedEvent, "previousLocale"),
                "Applied event should include previous locale.");
        }

        private static void ValidateUnsupportedLocaleRejection(
            GameCommandBus commandBus,
            LocalizationRuntime localizationRuntime,
            IReadOnlyList<IReadOnlyDictionary<string, object>> publishedEvents)
        {
            AssertTrue(commandBus != null, "GameCommandBus is required.");
            AssertTrue(localizationRuntime != null, "Localization runtime is required.");
            AssertTrue(publishedEvents != null, "Published events list is required.");

            localizationRuntime.ApplyPolicy(
                new LocalizationPolicy
                {
                    defaultLocale = "locale_without_entries",
                    fallbackToLanguageCode = false,
                });

            var beforeEventCount = publishedEvents.Count;
            var localeBeforeRequest = localizationRuntime.CurrentLocale;
            DispatchLocaleChangeCommand(commandBus, "unsupported-locale", "locale_req_reject");

            AssertEqual(
                localeBeforeRequest,
                localizationRuntime.CurrentLocale,
                "Unsupported locale request should not change active locale.");

            var rejectedEvent = FindEventSince(publishedEvents, beforeEventCount, "LOCALE_CHANGE_REJECTED");
            AssertTrue(rejectedEvent != null, "Expected locale_change_rejected event for unsupported locale.");
            AssertEqual(
                "LOCALIZATION_LOCALE_NOT_AVAILABLE",
                ReadValue(rejectedEvent, "reasonCode"),
                "Unsupported locale should emit expected reason code.");
        }

        private static void ValidateLocalOnlyRemoteRejection(
            GameCommandBus commandBus,
            FlowConfigProvider flowConfigProvider,
            LocalizationRuntime localizationRuntime,
            IReadOnlyList<IReadOnlyDictionary<string, object>> publishedEvents)
        {
            AssertTrue(commandBus != null, "GameCommandBus is required.");
            AssertTrue(flowConfigProvider != null, "FlowConfigProvider is required.");
            AssertTrue(localizationRuntime != null, "Localization runtime is required.");
            AssertTrue(publishedEvents != null, "Published events list is required.");

            ConfigureFlowDefinition(flowConfigProvider, SessionFlowControlModes.LocalOnly);

            var beforeEventCount = publishedEvents.Count;
            var localeBeforeRequest = localizationRuntime.CurrentLocale;
            DispatchLocaleChangeCommand(commandBus, "en-US", "locale_req_local_only");

            AssertEqual(
                localeBeforeRequest,
                localizationRuntime.CurrentLocale,
                "Remote locale request should be rejected in local_only mode.");

            var rejectedEvent = FindEventSince(publishedEvents, beforeEventCount, "LOCALE_CHANGE_REJECTED");
            AssertTrue(rejectedEvent != null, "Expected locale_change_rejected event in local_only mode.");
            AssertEqual(
                "CONTROL_SOURCE_REMOTE_NOT_ALLOWED",
                ReadValue(rejectedEvent, "reasonCode"),
                "Expected explicit reason code for local_only mode rejection.");
        }

        private static void ConfigureFlowDefinition(FlowConfigProvider flowConfigProvider, string controlMode)
        {
            AssertTrue(flowConfigProvider != null, "FlowConfigProvider is required.");

            var definition = GameDefinition.CreateSample();
            definition.controlMode = SessionFlowControlModes.NormalizeOrDefault(controlMode);
            if (definition.policies == null)
            {
                definition.policies = new SessionFlowPolicies();
            }

            if (definition.policies.controlPolicy == null)
            {
                definition.policies.controlPolicy = new ControlPolicy();
            }

            definition.policies.controlPolicy.mode = definition.controlMode;

            var json = JsonUtility.ToJson(definition);
            SetNonPublicField(flowConfigProvider, "_definitionJson", new TextAsset(json));
            SetNonPublicField(flowConfigProvider, "_preferJsonSource", true);
            SetNonPublicField(flowConfigProvider, "_fallbackToSampleWhenMissing", false);
            flowConfigProvider.InvalidateCache();
        }

        private static void DispatchLocaleChangeCommand(GameCommandBus commandBus, string locale, string requestId)
        {
            AssertTrue(commandBus != null, "GameCommandBus is required.");

            var localeCommand = new SetLocaleRequestCommand
            {
                correlationId = Guid.NewGuid().ToString("N"),
                gameId = "sample_game",
                locale = locale ?? string.Empty,
                requestId = requestId ?? string.Empty,
                source = "mobile_settings",
            };

            var message = new NetworkMessage
            {
                messageId = Guid.NewGuid().ToString("N"),
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                commandId = GameCommandIds.SetLocaleRequest,
                payloadString = JsonUtility.ToJson(localeCommand),
            };

            var handleIncomingMethod = typeof(GameCommandBus).GetMethod(
                "HandleIncomingMessage",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (handleIncomingMethod == null)
            {
                throw new MissingMethodException(typeof(GameCommandBus).FullName, "HandleIncomingMessage");
            }

            handleIncomingMethod.Invoke(commandBus, new object[] { message });
        }

        private static IReadOnlyDictionary<string, object> FindEventSince(
            IReadOnlyList<IReadOnlyDictionary<string, object>> events,
            int startIndex,
            string eventType)
        {
            if (events == null || events.Count <= 0)
            {
                return null;
            }

            var normalizedType = Normalize(eventType);
            var safeStart = Math.Max(0, startIndex);
            for (var i = events.Count - 1; i >= safeStart; i--)
            {
                var item = events[i];
                if (item == null)
                {
                    continue;
                }

                var itemType = Normalize(ReadValue(item, "eventType"));
                if (string.Equals(itemType, normalizedType, StringComparison.Ordinal))
                {
                    return item;
                }
            }

            return null;
        }

        private static bool HasCommandHandler(GameCommandBus commandBus, string commandId)
        {
            if (commandBus == null || string.IsNullOrWhiteSpace(commandId))
            {
                return false;
            }

            var field = typeof(GameCommandBus).GetField(
                "_handlersByCommandId",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                return false;
            }

            if (!(field.GetValue(commandBus) is Dictionary<string, List<Delegate>> handlersByCommandId))
            {
                return false;
            }

            return handlersByCommandId.TryGetValue(commandId, out var handlers) &&
                   handlers != null &&
                   handlers.Count > 0;
        }

        private static string DescribeRegisteredCommands(GameCommandBus commandBus)
        {
            if (commandBus == null)
            {
                return "NONE";
            }

            var field = typeof(GameCommandBus).GetField(
                "_handlersByCommandId",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                return "NO_FIELD";
            }

            if (!(field.GetValue(commandBus) is Dictionary<string, List<Delegate>> handlersByCommandId) ||
                handlersByCommandId.Count <= 0)
            {
                return "EMPTY";
            }

            var keys = new List<string>(handlersByCommandId.Keys);
            keys.Sort(StringComparer.Ordinal);
            return string.Join(",", keys);
        }

        private static string ReadValue(IReadOnlyDictionary<string, object> payload, string key)
        {
            if (payload == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            if (!payload.TryGetValue(key, out var value) || value == null)
            {
                return string.Empty;
            }

            return value.ToString();
        }

        private static string ReadDetailsValue(IReadOnlyDictionary<string, object> payload, string key)
        {
            if (payload == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            if (!payload.TryGetValue("details", out var detailsObject) || detailsObject == null)
            {
                return string.Empty;
            }

            if (detailsObject is IReadOnlyDictionary<string, object> readOnlyDetails &&
                readOnlyDetails.TryGetValue(key, out var readOnlyValue) &&
                readOnlyValue != null)
            {
                return readOnlyValue.ToString();
            }

            if (detailsObject is Dictionary<string, object> mutableDetails &&
                mutableDetails.TryGetValue(key, out var mutableValue) &&
                mutableValue != null)
            {
                return mutableValue.ToString();
            }

            return string.Empty;
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

        private static void InvokeNonPublicMethod(object target, string methodName)
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

            method.Invoke(target, null);
        }

        private static void PersistValidationResult(string status, string details)
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var outputPath = Path.Combine(
                projectRoot,
                "Temp",
                "CliValidation",
                "mobile_locale_sync_validation_result.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);
            File.WriteAllLines(
                outputPath,
                new[]
                {
                    $"status={status}",
                    $"timestampUtc={DateTime.UtcNow:O}",
                    $"details={details ?? string.Empty}",
                });

            Debug.Log("[MobileLocaleSyncValidation] Result file: " + outputPath);
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
            var safeExpected = Normalize(expected);
            var safeActual = Normalize(actual);
            if (!string.Equals(safeExpected, safeActual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{message} expected={safeExpected} actual={safeActual}");
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
