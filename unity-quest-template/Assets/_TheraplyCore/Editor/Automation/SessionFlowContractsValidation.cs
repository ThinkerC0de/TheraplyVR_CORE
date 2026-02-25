using System;
using System.Collections.Generic;
using System.IO;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using UnityEditor;
using UnityEngine;

namespace TheraplyCore.Editor.Automation
{
    public static class SessionFlowContractsValidation
    {
        public static void RunSessionFlowContractsValidation()
        {
            try
            {
                var summary = ExecuteValidation();
                Debug.Log("[SessionFlowContractsValidation] PASS: " + summary);
                PersistValidationResult("PASS", summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[SessionFlowContractsValidation] FAIL: " + exception);
                PersistValidationResult("FAIL", exception.ToString());
                EditorApplication.Exit(1);
            }
        }

        private static string ExecuteValidation()
        {
            var sampleDefinition = GameDefinition.CreateSample();
            AssertTrue(
                sampleDefinition.TryValidate(out var sampleReason),
                "Sample definition should validate. reason=" + sampleReason);

            var invalidControlPolicyMode = GameDefinition.CreateSample();
            invalidControlPolicyMode.policies.controlPolicy.mode = "invalid_mode";
            AssertFalse(
                invalidControlPolicyMode.TryValidate(out var invalidPolicyReason),
                "Definition with invalid control policy mode should fail.");
            AssertEqual(
                SessionFlowDefinitionReasonCodes.ControlModeUnsupported,
                invalidPolicyReason,
                "Unexpected reason code for invalid control policy mode.");

            var invalidControlMode = GameDefinition.CreateSample();
            invalidControlMode.controlMode = "unsupported_control_mode";
            AssertFalse(
                invalidControlMode.TryValidate(out var invalidModeReason),
                "Definition with invalid control mode should fail.");
            AssertEqual(
                SessionFlowDefinitionReasonCodes.ControlModeUnsupported,
                invalidModeReason,
                "Unexpected reason code for invalid top-level control mode.");

            AssertTrue(
                SessionFlowConditionIds.IsSupported(SessionFlowConditionIds.ScoreThreshold),
                "Built-in score threshold condition id should be supported.");
            AssertTrue(
                SessionFlowConditionIds.IsSupported(SessionFlowConditionIds.ElapsedTimeWindow),
                "Built-in elapsed time window condition id should be supported.");
            AssertTrue(
                SessionFlowConditionIds.IsSupported(SessionFlowConditionIds.IsEventActive),
                "Built-in calendar event condition id should be supported.");
            AssertTrue(
                SessionFlowConditionIds.IsSupported(SessionFlowConditionIds.IsWithinDateWindow),
                "Built-in date window condition id should be supported.");
            AssertTrue(
                SessionFlowConditionIds.IsSupported(SessionFlowConditionIds.IsProfileBirthday),
                "Built-in profile birthday condition id should be supported.");
            AssertEqual(
                BranchPrecedenceModes.FirstMatch,
                BranchPrecedenceModes.NormalizeOrDefault("unsupported"),
                "Branch precedence should normalize to first_match fallback.");
            AssertEqual(
                BranchPrecedenceModes.LastMatch,
                BranchPrecedenceModes.NormalizeOrDefault(BranchPrecedenceModes.LastMatch),
                "Branch precedence should keep last_match mode.");

            var localizationPolicy = sampleDefinition.policies.localizationPolicy;
            AssertTrue(localizationPolicy != null, "Localization policy should exist in default policies.");
            AssertTrue(
                !string.IsNullOrWhiteSpace(localizationPolicy.defaultLocale),
                "Localization policy default locale should not be empty.");

            var sampleMobileSchema = MobileControlSchema.CreateDefault(sampleDefinition.gameId);
            sampleMobileSchema.title = "Sample Mobile Schema";
            sampleMobileSchema.payload.gameConfigType = "sample_control_config";
            sampleMobileSchema.controls.Add(new MobileControlDefinition
            {
                controlId = "target_count",
                type = MobileControlTypes.Slider,
                label = "Target Count",
                defaultValue = "8",
                binding = new MobileControlBindingDefinition
                {
                    target = MobileControlPayloadTargets.GameConfig,
                    path = "targetCount",
                    valueType = MobileControlValueTypes.Integer,
                    emitOnStartGame = true,
                    emitOnUpdateConfig = true,
                },
                validation = new MobileControlValidationDefinition
                {
                    required = true,
                    minValue = "3",
                    maxValue = "32",
                    step = "1",
                },
            });
            AssertTrue(
                sampleMobileSchema.TryValidate(out var mobileSchemaReason),
                "Sample mobile control schema should validate. reason=" + mobileSchemaReason);

            var invalidMobileSchema = MobileControlSchema.CreateDefault(sampleDefinition.gameId);
            invalidMobileSchema.controls.Add(new MobileControlDefinition
            {
                controlId = "invalid_control",
                type = "unsupported_type",
                binding = new MobileControlBindingDefinition
                {
                    path = "value",
                },
            });
            AssertFalse(
                invalidMobileSchema.TryValidate(out var invalidMobileReason),
                "Unsupported control type should fail schema validation.");
            AssertEqual(
                MobileControlSchemaReasonCodes.ControlTypeUnsupported,
                invalidMobileReason,
                "Unexpected reason code for unsupported mobile control type.");

            var catalogEntry = new GameCatalogContractEntry
            {
                gameId = "demo_cube_clicker",
                title = "Demo Cube Clicker",
                targetContentVersion = "1.2.0",
                deliveryMode = string.Empty,
                sceneKey = string.Empty,
                contentVersion = string.Empty,
                entitlementKey = string.Empty,
            };
            AssertTrue(
                catalogEntry.TryValidate(out var catalogReason),
                "Catalog entry should validate with backward-compatible defaults. reason=" + catalogReason);
            AssertEqual(
                "demo_cube_clicker",
                catalogEntry.sceneKey,
                "Catalog sceneKey should fallback to gameId when missing.");
            AssertEqual(
                "1.2.0",
                catalogEntry.contentVersion,
                "Catalog contentVersion should fallback to targetContentVersion when missing.");
            AssertEqual(
                "game:demo_cube_clicker",
                catalogEntry.entitlementKey,
                "Catalog entitlementKey should fallback to game-scoped key when missing.");
            AssertEqual(
                GameCatalogDeliveryModes.Bundled,
                catalogEntry.deliveryMode,
                "Catalog deliveryMode should fallback to bundled when missing.");

            var invalidCatalogEntry = GameCatalogContractEntry.CreateDefault("demo_cube_clicker", "Demo");
            invalidCatalogEntry.deliveryMode = "unsupported_mode";
            AssertFalse(
                invalidCatalogEntry.TryValidate(out var invalidCatalogReason),
                "Catalog entry with unsupported delivery mode should fail.");
            AssertEqual(
                GameCatalogContractReasonCodes.DeliveryModeUnsupported,
                invalidCatalogReason,
                "Unexpected reason code for unsupported catalog delivery mode.");

            var calendarPolicy = sampleDefinition.policies.calendarPolicy;
            AssertTrue(calendarPolicy != null, "Calendar policy should exist in default policies.");
            AssertTrue(
                !string.IsNullOrWhiteSpace(calendarPolicy.timezoneId),
                "Calendar policy timezone should not be empty.");
            AssertEqual(
                CalendarConflictPolicies.HighestPriority,
                CalendarConflictPolicies.NormalizeOrDefault("unsupported"),
                "Calendar conflict policy should normalize to highest_priority fallback.");

            var actionValidator = new ActionValidator();
            var allowedAction = new AllowedActionDefinition
            {
                actionId = "touch_target_with_tool",
                constraints = new List<KeyValuePairString>
                {
                    new KeyValuePairString { key = "requiredChannelId", value = SessionFlowChannelIds.Pointer },
                    new KeyValuePairString { key = "requiredControlMode", value = SessionFlowControlModes.RemoteOnly },
                    new KeyValuePairString { key = "minInputValue", value = "0.5" },
                    new KeyValuePairString { key = "maxInputValue", value = "1.0" },
                },
            };

            var actionContext = new ActionContext
            {
                controlMode = SessionFlowControlModes.LocalOnly,
            };
            var actionIntent = new ActionIntent
            {
                actionId = "touch_target_with_tool",
                channelId = SessionFlowChannelIds.Pointer,
                inputValue = 0.8f,
            };

            var controlMismatch = actionValidator.Validate(actionIntent, actionContext, allowedAction);
            AssertFalse(controlMismatch.accepted, "Expected control mode mismatch rejection.");
            AssertEqual("CONTROL_MODE_MISMATCH", controlMismatch.reasonCode, "Unexpected control mode mismatch reason.");

            actionContext.controlMode = SessionFlowControlModes.RemoteOnly;
            actionIntent.inputValue = 0.2f;
            var minThresholdMismatch = actionValidator.Validate(actionIntent, actionContext, allowedAction);
            AssertFalse(minThresholdMismatch.accepted, "Expected min threshold rejection.");
            AssertEqual("INPUT_VALUE_BELOW_MIN", minThresholdMismatch.reasonCode, "Unexpected min threshold reason.");

            actionIntent.inputValue = 0.75f;
            var validResult = actionValidator.Validate(actionIntent, actionContext, allowedAction);
            AssertTrue(validResult.accepted, "Expected action acceptance for valid control mode and input range.");
            AssertEqual("ACTION_ACCEPTED", validResult.reasonCode, "Unexpected reason code for valid action.");

            return "definitionValidation=OK; conditionContracts=OK; localizationPolicy=OK; mobileControlSchema=OK; catalogContract=OK; calendarPolicy=OK; actionValidator=OK";
        }

        private static void PersistValidationResult(string status, string details)
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var outputPath = Path.Combine(
                projectRoot,
                "Temp",
                "CliValidation",
                "session_flow_contracts_validation_result.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            var lines = new[]
            {
                $"status={status}",
                $"timestampUtc={DateTime.UtcNow:O}",
                $"details={details ?? string.Empty}",
            };

            File.WriteAllLines(outputPath, lines);
            Debug.Log("[SessionFlowContractsValidation] Result file: " + outputPath);
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
            var normalizedExpected = expected ?? string.Empty;
            var normalizedActual = actual ?? string.Empty;
            if (!string.Equals(normalizedExpected, normalizedActual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{message} expected={normalizedExpected} actual={normalizedActual}");
            }
        }
    }
}
