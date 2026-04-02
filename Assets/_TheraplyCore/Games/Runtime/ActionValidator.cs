using System;
using System.Globalization;
using GameContracts = TheraplyCore.Games.Contracts;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Validates action intent against allowed-action constraints.
    /// </summary>
    public sealed class ActionValidator
    {
        public GameContracts.ActionValidationResult Validate(
            GameContracts.ActionIntent intent,
            GameContracts.ActionContext context,
            GameContracts.AllowedActionDefinition allowedAction)
        {
            if (intent == null)
            {
                return GameContracts.ActionValidationResult.Rejected("ACTION_INTENT_REQUIRED");
            }

            if (allowedAction == null)
            {
                return GameContracts.ActionValidationResult.Rejected("ALLOWED_ACTION_REQUIRED");
            }

            var incomingActionId = Normalize(intent.actionId);
            var expectedActionId = Normalize(allowedAction.actionId);
            if (string.IsNullOrWhiteSpace(incomingActionId))
            {
                return GameContracts.ActionValidationResult.Rejected("ACTION_ID_REQUIRED");
            }

            if (!string.Equals(incomingActionId, expectedActionId, StringComparison.OrdinalIgnoreCase))
            {
                return GameContracts.ActionValidationResult.Rejected("ACTION_ID_MISMATCH");
            }

            if (allowedAction.constraints == null || allowedAction.constraints.Count == 0)
            {
                return GameContracts.ActionValidationResult.Accepted(intent.targetId);
            }

            for (var i = 0; i < allowedAction.constraints.Count; i++)
            {
                var constraint = allowedAction.constraints[i];
                if (constraint == null || string.IsNullOrWhiteSpace(constraint.key))
                {
                    continue;
                }

                var key = Normalize(constraint.key);
                var value = Normalize(constraint.value);

                if (string.Equals(key, "requiredChannelId", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(Normalize(intent.channelId), value, StringComparison.OrdinalIgnoreCase))
                    {
                        return GameContracts.ActionValidationResult.Rejected("CHANNEL_ID_MISMATCH");
                    }
                }

                if (string.Equals(key, "requiredTargetId", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(Normalize(intent.targetId), value, StringComparison.OrdinalIgnoreCase))
                    {
                        return GameContracts.ActionValidationResult.Rejected("TARGET_ID_MISMATCH");
                    }
                }

                if (string.Equals(key, "requiredControlMode", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(Normalize(context == null ? string.Empty : context.controlMode), value, StringComparison.OrdinalIgnoreCase))
                    {
                        return GameContracts.ActionValidationResult.Rejected("CONTROL_MODE_MISMATCH");
                    }
                }

                if (string.Equals(key, "minInputValue", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseFloat(value, out var minValue))
                    {
                        return GameContracts.ActionValidationResult.Rejected("CONSTRAINT_PARSE_FAILED");
                    }

                    if (intent.inputValue < minValue)
                    {
                        return GameContracts.ActionValidationResult.Rejected("INPUT_VALUE_BELOW_MIN");
                    }
                }

                if (string.Equals(key, "maxInputValue", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseFloat(value, out var maxValue))
                    {
                        return GameContracts.ActionValidationResult.Rejected("CONSTRAINT_PARSE_FAILED");
                    }

                    if (intent.inputValue > maxValue)
                    {
                        return GameContracts.ActionValidationResult.Rejected("INPUT_VALUE_ABOVE_MAX");
                    }
                }
            }

            return GameContracts.ActionValidationResult.Accepted(intent.targetId);
        }

        private static bool TryParseFloat(string value, out float parsed)
        {
            return float.TryParse(
                value,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out parsed);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
