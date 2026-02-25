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

            return "definitionValidation=OK; conditionContracts=OK; localizationPolicy=OK; actionValidator=OK";
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
