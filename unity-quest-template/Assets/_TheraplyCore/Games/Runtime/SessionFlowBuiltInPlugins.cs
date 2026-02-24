using System;
using System.Collections.Generic;
using System.Globalization;
using GameContracts = TheraplyCore.Games.Contracts;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Built-in action plugins for core channels.
    /// </summary>
    public static class SessionFlowBuiltInPlugins
    {
        public static void RegisterBuiltIns(ActionPluginRegistry registry, bool replaceExisting = true)
        {
            if (registry == null)
            {
                return;
            }

            registry.Register(new PointerSelectActionPlugin("point_and_select_target", allowImplicitActionId: true), replaceExisting);
            registry.Register(new PointerSelectActionPlugin("confirm_choice", allowImplicitActionId: false), replaceExisting);
            registry.Register(new PointerSelectActionPlugin("choose_reward", allowImplicitActionId: false), replaceExisting);

            registry.Register(new ToolImpactActionPlugin("touch_target_with_tool", allowImplicitActionId: true), replaceExisting);
            registry.Register(new ToolImpactActionPlugin("intercept_moving_target", allowImplicitActionId: false), replaceExisting);
            registry.Register(new ToolImpactActionPlugin("avoid_hazard_contact", allowImplicitActionId: false), replaceExisting);

            registry.Register(new HandContactActionPlugin("touch_target_with_hand", allowImplicitActionId: true), replaceExisting);

            registry.Register(new GrabPlaceActionPlugin("grab_object"), replaceExisting);
            registry.Register(new GrabPlaceActionPlugin("release_object"), replaceExisting);
            registry.Register(new GrabPlaceActionPlugin("place_object_in_zone"), replaceExisting);
            registry.Register(new GrabPlaceActionPlugin("remove_object_from_zone"), replaceExisting);
            registry.Register(new GrabPlaceActionPlugin("collect_item_to_container"), replaceExisting);

            registry.Register(new GazeActionPlugin("hold_gaze_on_target"), replaceExisting);
            registry.Register(new GazeActionPlugin("select_target_with_gaze_and_tool"), replaceExisting);
        }
    }

    public sealed class PointerSelectActionPlugin : GameContracts.IActionPlugin
    {
        private readonly ActionValidator _validator = new ActionValidator();
        private readonly string _actionId;
        private readonly bool _allowImplicitActionId;

        public PointerSelectActionPlugin(string actionId, bool allowImplicitActionId)
        {
            _actionId = string.IsNullOrWhiteSpace(actionId)
                ? "point_and_select_target"
                : actionId.Trim();
            _allowImplicitActionId = allowImplicitActionId;
        }

        public string ActionId => _actionId;
        public string ChannelId => GameContracts.SessionFlowChannelIds.Pointer;

        public bool TryCreateIntent(
            IReadOnlyDictionary<string, object> rawInput,
            out GameContracts.ActionIntent intent)
        {
            intent = null;
            if (!SessionFlowPluginPayload.TryReadString(rawInput, "eventType", out var eventType))
            {
                return false;
            }

            if (!string.Equals(eventType, "POINTER_SELECT", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(eventType, "POINTER_SELECT_INVALID", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var requestedActionId = SessionFlowPluginPayload.ReadRequestedActionId(rawInput);
            if (!IsRequestedActionSupported(requestedActionId))
            {
                return false;
            }

            intent = new GameContracts.ActionIntent
            {
                actionId = string.IsNullOrWhiteSpace(requestedActionId) ? ActionId : requestedActionId,
                channelId = ChannelId,
                targetId = SessionFlowPluginPayload.ReadString(rawInput, "targetId"),
                inputSource = SessionFlowPluginPayload.ReadString(rawInput, "inputSource"),
                inputHand = SessionFlowPluginPayload.ReadString(rawInput, "inputHand"),
                inputValue = SessionFlowPluginPayload.ReadFloat(rawInput, "inputValue"),
                occurredAtElapsedSec = SessionFlowPluginPayload.ReadFloat(rawInput, "occurredAtElapsedSec"),
                details = new List<GameContracts.KeyValuePairString>
                {
                    new GameContracts.KeyValuePairString { key = "eventType", value = eventType },
                    new GameContracts.KeyValuePairString { key = "reasonCode", value = SessionFlowPluginPayload.ReadString(rawInput, "reasonCode") },
                },
            };
            return true;
        }

        public GameContracts.ActionValidationResult Validate(
            GameContracts.ActionIntent intent,
            GameContracts.ActionContext context,
            GameContracts.AllowedActionDefinition allowedAction)
        {
            if (SessionFlowPluginPayload.IsEventType(intent, "POINTER_SELECT_INVALID"))
            {
                return GameContracts.ActionValidationResult.Rejected("TARGET_INVALID");
            }

            return _validator.Validate(intent, context, allowedAction);
        }

        public GameContracts.ActionApplyResult Apply(
            GameContracts.ActionIntent intent,
            GameContracts.ActionContext context,
            GameContracts.AllowedActionDefinition allowedAction)
        {
            return SessionFlowPluginPayload.BuildAppliedResult(ActionId, ChannelId, "POINTER_ACTION_APPLIED");
        }

        private bool IsRequestedActionSupported(string requestedActionId)
        {
            if (string.IsNullOrWhiteSpace(requestedActionId))
            {
                return _allowImplicitActionId;
            }

            return string.Equals(requestedActionId.Trim(), ActionId, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class ToolImpactActionPlugin : GameContracts.IActionPlugin
    {
        private readonly ActionValidator _validator = new ActionValidator();
        private readonly string _actionId;
        private readonly bool _allowImplicitActionId;

        public ToolImpactActionPlugin(string actionId, bool allowImplicitActionId)
        {
            _actionId = string.IsNullOrWhiteSpace(actionId)
                ? "touch_target_with_tool"
                : actionId.Trim();
            _allowImplicitActionId = allowImplicitActionId;
        }

        public string ActionId => _actionId;
        public string ChannelId => GameContracts.SessionFlowChannelIds.ToolImpact;

        public bool TryCreateIntent(
            IReadOnlyDictionary<string, object> rawInput,
            out GameContracts.ActionIntent intent)
        {
            intent = null;
            if (!SessionFlowPluginPayload.TryReadString(rawInput, "eventType", out var eventType))
            {
                return false;
            }

            if (!eventType.StartsWith("TOOL_IMPACT_", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var requestedActionId = SessionFlowPluginPayload.ReadRequestedActionId(rawInput);
            if (!IsRequestedActionSupported(requestedActionId))
            {
                return false;
            }

            intent = new GameContracts.ActionIntent
            {
                actionId = string.IsNullOrWhiteSpace(requestedActionId) ? ActionId : requestedActionId,
                channelId = ChannelId,
                targetId = SessionFlowPluginPayload.ReadString(rawInput, "targetId"),
                inputSource = SessionFlowPluginPayload.ReadString(rawInput, "inputSource"),
                inputHand = SessionFlowPluginPayload.ReadString(rawInput, "inputHand"),
                inputValue = SessionFlowPluginPayload.ReadFloat(rawInput, "inputValue"),
                occurredAtElapsedSec = SessionFlowPluginPayload.ReadFloat(rawInput, "occurredAtElapsedSec"),
                details = new List<GameContracts.KeyValuePairString>
                {
                    new GameContracts.KeyValuePairString { key = "eventType", value = eventType },
                    new GameContracts.KeyValuePairString { key = "reasonCode", value = SessionFlowPluginPayload.ReadString(rawInput, "reasonCode") },
                },
            };
            return true;
        }

        public GameContracts.ActionValidationResult Validate(
            GameContracts.ActionIntent intent,
            GameContracts.ActionContext context,
            GameContracts.AllowedActionDefinition allowedAction)
        {
            if (SessionFlowPluginPayload.IsEventType(intent, "TOOL_IMPACT_INVALID"))
            {
                return GameContracts.ActionValidationResult.Rejected("TARGET_INVALID");
            }

            if (SessionFlowPluginPayload.IsEventType(intent, "TOOL_IMPACT_MISS"))
            {
                return GameContracts.ActionValidationResult.Rejected("TARGET_NOT_FOUND");
            }

            return _validator.Validate(intent, context, allowedAction);
        }

        public GameContracts.ActionApplyResult Apply(
            GameContracts.ActionIntent intent,
            GameContracts.ActionContext context,
            GameContracts.AllowedActionDefinition allowedAction)
        {
            return SessionFlowPluginPayload.BuildAppliedResult(ActionId, ChannelId, "TOOL_ACTION_APPLIED");
        }

        private bool IsRequestedActionSupported(string requestedActionId)
        {
            if (string.IsNullOrWhiteSpace(requestedActionId))
            {
                return _allowImplicitActionId;
            }

            return string.Equals(requestedActionId.Trim(), ActionId, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class HandContactActionPlugin : GameContracts.IActionPlugin
    {
        private readonly ActionValidator _validator = new ActionValidator();
        private readonly string _actionId;
        private readonly bool _allowImplicitActionId;

        public HandContactActionPlugin(string actionId, bool allowImplicitActionId)
        {
            _actionId = string.IsNullOrWhiteSpace(actionId)
                ? "touch_target_with_hand"
                : actionId.Trim();
            _allowImplicitActionId = allowImplicitActionId;
        }

        public string ActionId => _actionId;
        public string ChannelId => GameContracts.SessionFlowChannelIds.HandContact;

        public bool TryCreateIntent(
            IReadOnlyDictionary<string, object> rawInput,
            out GameContracts.ActionIntent intent)
        {
            intent = null;
            if (!SessionFlowPluginPayload.TryReadString(rawInput, "eventType", out var eventType))
            {
                return false;
            }

            if (!eventType.StartsWith("HAND_CONTACT_", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var requestedActionId = SessionFlowPluginPayload.ReadRequestedActionId(rawInput);
            if (!IsRequestedActionSupported(requestedActionId))
            {
                return false;
            }

            intent = new GameContracts.ActionIntent
            {
                actionId = string.IsNullOrWhiteSpace(requestedActionId) ? ActionId : requestedActionId,
                channelId = ChannelId,
                targetId = SessionFlowPluginPayload.ReadString(rawInput, "targetId"),
                inputSource = SessionFlowPluginPayload.ReadString(rawInput, "inputSource"),
                inputHand = SessionFlowPluginPayload.ReadString(rawInput, "inputHand"),
                inputValue = SessionFlowPluginPayload.ReadFloat(rawInput, "inputValue"),
                occurredAtElapsedSec = SessionFlowPluginPayload.ReadFloat(rawInput, "occurredAtElapsedSec"),
                details = new List<GameContracts.KeyValuePairString>
                {
                    new GameContracts.KeyValuePairString { key = "eventType", value = eventType },
                    new GameContracts.KeyValuePairString { key = "reasonCode", value = SessionFlowPluginPayload.ReadString(rawInput, "reasonCode") },
                },
            };
            return true;
        }

        public GameContracts.ActionValidationResult Validate(
            GameContracts.ActionIntent intent,
            GameContracts.ActionContext context,
            GameContracts.AllowedActionDefinition allowedAction)
        {
            if (SessionFlowPluginPayload.IsEventType(intent, "HAND_CONTACT_INVALID"))
            {
                return GameContracts.ActionValidationResult.Rejected("TARGET_INVALID");
            }

            if (SessionFlowPluginPayload.IsEventType(intent, "HAND_CONTACT_MISS"))
            {
                return GameContracts.ActionValidationResult.Rejected("TARGET_NOT_FOUND");
            }

            return _validator.Validate(intent, context, allowedAction);
        }

        public GameContracts.ActionApplyResult Apply(
            GameContracts.ActionIntent intent,
            GameContracts.ActionContext context,
            GameContracts.AllowedActionDefinition allowedAction)
        {
            return SessionFlowPluginPayload.BuildAppliedResult(ActionId, ChannelId, "HAND_ACTION_APPLIED");
        }

        private bool IsRequestedActionSupported(string requestedActionId)
        {
            if (string.IsNullOrWhiteSpace(requestedActionId))
            {
                return _allowImplicitActionId;
            }

            return string.Equals(requestedActionId.Trim(), ActionId, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class GrabPlaceActionPlugin : GameContracts.IActionPlugin
    {
        private readonly ActionValidator _validator = new ActionValidator();
        private readonly string _actionId;

        public GrabPlaceActionPlugin(string actionId)
        {
            _actionId = string.IsNullOrWhiteSpace(actionId)
                ? "grab_object"
                : actionId.Trim();
        }

        public string ActionId => _actionId;
        public string ChannelId => GameContracts.SessionFlowChannelIds.HandGrab;

        public bool TryCreateIntent(
            IReadOnlyDictionary<string, object> rawInput,
            out GameContracts.ActionIntent intent)
        {
            intent = null;
            if (!SessionFlowPluginPayload.TryReadString(rawInput, "eventType", out var eventType))
            {
                return false;
            }

            if (!SessionFlowPluginPayload.IsGrabEventType(eventType))
            {
                return false;
            }

            var requestedActionId = SessionFlowPluginPayload.ReadRequestedActionId(rawInput);
            var suggestedActionId = SessionFlowPluginPayload.ResolveGrabActionIdFromEventType(eventType);
            var effectiveActionId = !string.IsNullOrWhiteSpace(requestedActionId)
                ? requestedActionId
                : suggestedActionId;
            if (string.IsNullOrWhiteSpace(effectiveActionId) ||
                !string.Equals(effectiveActionId, ActionId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            intent = new GameContracts.ActionIntent
            {
                actionId = effectiveActionId.Trim(),
                channelId = ChannelId,
                targetId = SessionFlowPluginPayload.ResolveGrabTargetId(rawInput, effectiveActionId),
                inputSource = SessionFlowPluginPayload.ReadString(rawInput, "inputSource"),
                inputHand = SessionFlowPluginPayload.ReadString(rawInput, "inputHand"),
                inputValue = SessionFlowPluginPayload.ReadFloat(rawInput, "inputValue"),
                occurredAtElapsedSec = SessionFlowPluginPayload.ReadFloat(rawInput, "occurredAtElapsedSec"),
                details = new List<GameContracts.KeyValuePairString>
                {
                    new GameContracts.KeyValuePairString { key = "eventType", value = eventType },
                    new GameContracts.KeyValuePairString { key = "reasonCode", value = SessionFlowPluginPayload.ReadString(rawInput, "reasonCode") },
                    new GameContracts.KeyValuePairString { key = "objectId", value = SessionFlowPluginPayload.ReadString(rawInput, "objectId") },
                    new GameContracts.KeyValuePairString { key = "zoneId", value = SessionFlowPluginPayload.ReadString(rawInput, "zoneId") },
                },
            };
            return true;
        }

        public GameContracts.ActionValidationResult Validate(
            GameContracts.ActionIntent intent,
            GameContracts.ActionContext context,
            GameContracts.AllowedActionDefinition allowedAction)
        {
            var eventType = SessionFlowPluginPayload.ReadEventTypeFromIntent(intent);
            if (eventType.IndexOf("INVALID", StringComparison.OrdinalIgnoreCase) >= 0 ||
                eventType.IndexOf("FAILED", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return GameContracts.ActionValidationResult.Rejected("TARGET_INVALID");
            }

            if (eventType.IndexOf("MISS", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return GameContracts.ActionValidationResult.Rejected("TARGET_NOT_FOUND");
            }

            return _validator.Validate(intent, context, allowedAction);
        }

        public GameContracts.ActionApplyResult Apply(
            GameContracts.ActionIntent intent,
            GameContracts.ActionContext context,
            GameContracts.AllowedActionDefinition allowedAction)
        {
            return SessionFlowPluginPayload.BuildAppliedResult(ActionId, ChannelId, "GRAB_ACTION_APPLIED");
        }
    }

    public sealed class GazeActionPlugin : GameContracts.IActionPlugin
    {
        private readonly ActionValidator _validator = new ActionValidator();
        private readonly string _actionId;

        public GazeActionPlugin(string actionId)
        {
            _actionId = string.IsNullOrWhiteSpace(actionId)
                ? "hold_gaze_on_target"
                : actionId.Trim();
        }

        public string ActionId => _actionId;
        public string ChannelId => GameContracts.SessionFlowChannelIds.Gaze;

        public bool TryCreateIntent(
            IReadOnlyDictionary<string, object> rawInput,
            out GameContracts.ActionIntent intent)
        {
            intent = null;
            if (!SessionFlowPluginPayload.TryReadString(rawInput, "eventType", out var eventType))
            {
                return false;
            }

            if (!SessionFlowPluginPayload.IsGazeEventType(eventType))
            {
                return false;
            }

            var requestedActionId = SessionFlowPluginPayload.ReadRequestedActionId(rawInput);
            var suggestedActionId = SessionFlowPluginPayload.ResolveGazeActionIdFromEventType(eventType);
            var effectiveActionId = !string.IsNullOrWhiteSpace(requestedActionId)
                ? requestedActionId
                : suggestedActionId;
            if (string.IsNullOrWhiteSpace(effectiveActionId) ||
                !string.Equals(effectiveActionId, ActionId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            intent = new GameContracts.ActionIntent
            {
                actionId = effectiveActionId.Trim(),
                channelId = ChannelId,
                targetId = SessionFlowPluginPayload.ReadString(rawInput, "targetId"),
                inputSource = SessionFlowPluginPayload.ReadString(rawInput, "inputSource"),
                inputHand = SessionFlowPluginPayload.ReadString(rawInput, "inputHand"),
                inputValue = SessionFlowPluginPayload.ReadFloat(rawInput, "inputValue"),
                occurredAtElapsedSec = SessionFlowPluginPayload.ReadFloat(rawInput, "occurredAtElapsedSec"),
                details = new List<GameContracts.KeyValuePairString>
                {
                    new GameContracts.KeyValuePairString { key = "eventType", value = eventType },
                    new GameContracts.KeyValuePairString { key = "reasonCode", value = SessionFlowPluginPayload.ReadString(rawInput, "reasonCode") },
                    new GameContracts.KeyValuePairString { key = "dwellSec", value = SessionFlowPluginPayload.ReadString(rawInput, "dwellSec") },
                    new GameContracts.KeyValuePairString { key = "requiredDwellSec", value = SessionFlowPluginPayload.ReadString(rawInput, "requiredDwellSec") },
                },
            };
            return true;
        }

        public GameContracts.ActionValidationResult Validate(
            GameContracts.ActionIntent intent,
            GameContracts.ActionContext context,
            GameContracts.AllowedActionDefinition allowedAction)
        {
            var eventType = SessionFlowPluginPayload.ReadEventTypeFromIntent(intent);

            if (string.Equals(ActionId, "hold_gaze_on_target", StringComparison.OrdinalIgnoreCase))
            {
                if (eventType.IndexOf("GAZE_HOLD_INVALID", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return GameContracts.ActionValidationResult.Rejected("GAZE_TARGET_INVALID");
                }

                if (eventType.IndexOf("GAZE_HOLD_TICK", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return GameContracts.ActionValidationResult.Rejected("GAZE_DWELL_NOT_REACHED");
                }

                if (eventType.IndexOf("GAZE_HOLD_COMPLETED", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return GameContracts.ActionValidationResult.Rejected("ACTION_EVENT_TYPE_MISMATCH");
                }
            }

            if (string.Equals(ActionId, "select_target_with_gaze_and_tool", StringComparison.OrdinalIgnoreCase))
            {
                if (eventType.IndexOf("GAZE_TOOL_SELECT_INVALID", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return GameContracts.ActionValidationResult.Rejected("GAZE_TOOL_INVALID");
                }

                if (eventType.IndexOf("GAZE_TOOL_SELECT", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return GameContracts.ActionValidationResult.Rejected("ACTION_EVENT_TYPE_MISMATCH");
                }
            }

            return _validator.Validate(intent, context, allowedAction);
        }

        public GameContracts.ActionApplyResult Apply(
            GameContracts.ActionIntent intent,
            GameContracts.ActionContext context,
            GameContracts.AllowedActionDefinition allowedAction)
        {
            return SessionFlowPluginPayload.BuildAppliedResult(ActionId, ChannelId, "GAZE_ACTION_APPLIED");
        }
    }

    internal static class SessionFlowPluginPayload
    {
        public static bool IsGrabEventType(string eventType)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                return false;
            }

            var normalized = eventType.Trim();
            return normalized.StartsWith("GRAB_OBJECT_", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("HAND_GRAB_", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("TOOL_GRIP_", StringComparison.OrdinalIgnoreCase);
        }

        public static string ResolveGrabActionIdFromEventType(string eventType)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                return string.Empty;
            }

            var normalized = eventType.Trim().ToUpperInvariant();
            switch (normalized)
            {
                case "GRAB_OBJECT_START":
                case "HAND_GRAB_START":
                case "TOOL_GRIP_START":
                    return "grab_object";
                case "GRAB_OBJECT_RELEASE":
                case "HAND_GRAB_RELEASE":
                case "TOOL_GRIP_END":
                    return "release_object";
                case "GRAB_OBJECT_PLACED":
                case "HAND_GRAB_PLACE":
                    return "place_object_in_zone";
                case "GRAB_OBJECT_REMOVED":
                case "HAND_GRAB_REMOVE":
                    return "remove_object_from_zone";
                case "GRAB_OBJECT_COLLECTED":
                case "HAND_GRAB_COLLECT":
                    return "collect_item_to_container";
                default:
                    return string.Empty;
            }
        }

        public static bool IsGazeEventType(string eventType)
        {
            return !string.IsNullOrWhiteSpace(eventType) &&
                   eventType.Trim().StartsWith("GAZE_", StringComparison.OrdinalIgnoreCase);
        }

        public static string ResolveGazeActionIdFromEventType(string eventType)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                return string.Empty;
            }

            var normalized = eventType.Trim().ToUpperInvariant();
            switch (normalized)
            {
                case "GAZE_HOLD_TICK":
                case "GAZE_HOLD_COMPLETED":
                case "GAZE_HOLD_INVALID":
                    return "hold_gaze_on_target";
                case "GAZE_TOOL_SELECT":
                case "GAZE_TOOL_SELECT_INVALID":
                    return "select_target_with_gaze_and_tool";
                default:
                    return string.Empty;
            }
        }

        public static string ResolveGrabTargetId(
            IReadOnlyDictionary<string, object> payload,
            string actionId)
        {
            var targetId = ReadString(payload, "targetId");
            if (!string.IsNullOrWhiteSpace(targetId))
            {
                return targetId;
            }

            if (string.Equals(actionId, "grab_object", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(actionId, "release_object", StringComparison.OrdinalIgnoreCase))
            {
                return ReadString(payload, "objectId");
            }

            var zoneId = ReadString(payload, "zoneId");
            if (!string.IsNullOrWhiteSpace(zoneId))
            {
                return zoneId;
            }

            return ReadString(payload, "objectId");
        }

        public static string ReadEventTypeFromIntent(GameContracts.ActionIntent intent)
        {
            if (intent == null || intent.details == null)
            {
                return string.Empty;
            }

            for (var i = 0; i < intent.details.Count; i++)
            {
                var detail = intent.details[i];
                if (detail == null || !string.Equals(detail.key, "eventType", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return detail.value ?? string.Empty;
            }

            return string.Empty;
        }

        public static string ReadRequestedActionId(IReadOnlyDictionary<string, object> payload)
        {
            var explicitActionId = ReadString(payload, "actionId");
            if (!string.IsNullOrWhiteSpace(explicitActionId))
            {
                return explicitActionId;
            }

            return ReadString(payload, "flowActionId");
        }

        public static bool IsEventType(GameContracts.ActionIntent intent, string expected)
        {
            if (intent == null || intent.details == null)
            {
                return false;
            }

            for (var i = 0; i < intent.details.Count; i++)
            {
                var detail = intent.details[i];
                if (detail == null || !string.Equals(detail.key, "eventType", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return string.Equals(detail.value, expected, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        public static bool TryReadString(IReadOnlyDictionary<string, object> payload, string key, out string value)
        {
            value = ReadString(payload, key);
            return !string.IsNullOrWhiteSpace(value);
        }

        public static string ReadString(IReadOnlyDictionary<string, object> payload, string key)
        {
            if (payload == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            if (!payload.TryGetValue(key, out var value) || value == null)
            {
                return string.Empty;
            }

            var converted = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(converted) ? string.Empty : converted.Trim();
        }

        public static float ReadFloat(IReadOnlyDictionary<string, object> payload, string key)
        {
            if (payload == null || string.IsNullOrWhiteSpace(key))
            {
                return 0f;
            }

            if (!payload.TryGetValue(key, out var value) || value == null)
            {
                return 0f;
            }

            if (value is float floatValue)
            {
                return floatValue;
            }

            if (value is double doubleValue)
            {
                return (float)doubleValue;
            }

            if (value is int intValue)
            {
                return intValue;
            }

            var converted = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(converted))
            {
                return 0f;
            }

            return float.TryParse(
                converted,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : 0f;
        }

        public static GameContracts.ActionApplyResult BuildAppliedResult(
            string actionId,
            string channelId,
            string reasonCode)
        {
            return new GameContracts.ActionApplyResult
            {
                stepCompleted = true,
                stepFailed = false,
                stepTimedOut = false,
                nextNodeId = string.Empty,
                reasonCode = string.IsNullOrWhiteSpace(reasonCode) ? "ACTION_APPLIED" : reasonCode.Trim(),
                metrics = new List<GameContracts.KeyValuePairString>
                {
                    new GameContracts.KeyValuePairString { key = "channelId", value = channelId ?? string.Empty },
                    new GameContracts.KeyValuePairString { key = "actionId", value = actionId ?? string.Empty },
                },
            };
        }
    }
}
