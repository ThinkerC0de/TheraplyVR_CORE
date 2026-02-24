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

            registry.Register(new BreathCycleActionPlugin("perform_breath_cycle"), replaceExisting);

            registry.Register(new AudioSourceActionPlugin("identify_sound_source"), replaceExisting);

            registry.Register(new DualHandActionPlugin("mark_left_and_right_targets"), replaceExisting);
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

    public sealed class BreathCycleActionPlugin : GameContracts.IActionPlugin
    {
        private readonly ActionValidator _validator = new ActionValidator();
        private readonly string _actionId;

        public BreathCycleActionPlugin(string actionId)
        {
            _actionId = string.IsNullOrWhiteSpace(actionId)
                ? "perform_breath_cycle"
                : actionId.Trim();
        }

        public string ActionId => _actionId;
        public string ChannelId => GameContracts.SessionFlowChannelIds.Breath;

        public bool TryCreateIntent(
            IReadOnlyDictionary<string, object> rawInput,
            out GameContracts.ActionIntent intent)
        {
            intent = null;
            if (!SessionFlowPluginPayload.TryReadString(rawInput, "eventType", out var eventType))
            {
                return false;
            }

            if (!SessionFlowPluginPayload.IsBreathEventType(eventType))
            {
                return false;
            }

            var requestedActionId = SessionFlowPluginPayload.ReadRequestedActionId(rawInput);
            var suggestedActionId = SessionFlowPluginPayload.ResolveBreathActionIdFromEventType(eventType);
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
                    new GameContracts.KeyValuePairString { key = "cycleIndex", value = SessionFlowPluginPayload.ReadString(rawInput, "cycleIndex") },
                    new GameContracts.KeyValuePairString { key = "targetCycleCount", value = SessionFlowPluginPayload.ReadString(rawInput, "targetCycleCount") },
                    new GameContracts.KeyValuePairString { key = "phaseDurationSec", value = SessionFlowPluginPayload.ReadString(rawInput, "phaseDurationSec") },
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
            if (eventType.IndexOf("BREATH_CYCLE_INVALID", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return GameContracts.ActionValidationResult.Rejected("BREATH_CYCLE_INVALID");
            }

            if (eventType.IndexOf("BREATH_PHASE_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return GameContracts.ActionValidationResult.Rejected("BREATH_CYCLE_NOT_COMPLETED");
            }

            if (eventType.IndexOf("BREATH_CYCLE_COMPLETED", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return GameContracts.ActionValidationResult.Rejected("ACTION_EVENT_TYPE_MISMATCH");
            }

            return _validator.Validate(intent, context, allowedAction);
        }

        public GameContracts.ActionApplyResult Apply(
            GameContracts.ActionIntent intent,
            GameContracts.ActionContext context,
            GameContracts.AllowedActionDefinition allowedAction)
        {
            return SessionFlowPluginPayload.BuildAppliedResult(ActionId, ChannelId, "BREATH_ACTION_APPLIED");
        }
    }

    public sealed class AudioSourceActionPlugin : GameContracts.IActionPlugin
    {
        private readonly ActionValidator _validator = new ActionValidator();
        private readonly string _actionId;

        public AudioSourceActionPlugin(string actionId)
        {
            _actionId = string.IsNullOrWhiteSpace(actionId)
                ? "identify_sound_source"
                : actionId.Trim();
        }

        public string ActionId => _actionId;
        public string ChannelId => GameContracts.SessionFlowChannelIds.AudioSource;

        public bool TryCreateIntent(
            IReadOnlyDictionary<string, object> rawInput,
            out GameContracts.ActionIntent intent)
        {
            intent = null;
            if (!SessionFlowPluginPayload.TryReadString(rawInput, "eventType", out var eventType))
            {
                return false;
            }

            if (!SessionFlowPluginPayload.IsAudioSourceEventType(eventType))
            {
                return false;
            }

            var requestedActionId = SessionFlowPluginPayload.ReadRequestedActionId(rawInput);
            var suggestedActionId = SessionFlowPluginPayload.ResolveAudioSourceActionIdFromEventType(eventType);
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
                targetId = SessionFlowPluginPayload.ResolveAudioSourceTargetId(rawInput),
                inputSource = SessionFlowPluginPayload.ReadString(rawInput, "inputSource"),
                inputHand = SessionFlowPluginPayload.ReadString(rawInput, "inputHand"),
                inputValue = SessionFlowPluginPayload.ReadFloat(rawInput, "inputValue"),
                occurredAtElapsedSec = SessionFlowPluginPayload.ReadFloat(rawInput, "occurredAtElapsedSec"),
                details = new List<GameContracts.KeyValuePairString>
                {
                    new GameContracts.KeyValuePairString { key = "eventType", value = eventType },
                    new GameContracts.KeyValuePairString { key = "reasonCode", value = SessionFlowPluginPayload.ReadString(rawInput, "reasonCode") },
                    new GameContracts.KeyValuePairString { key = "cueId", value = SessionFlowPluginPayload.ReadString(rawInput, "cueId") },
                    new GameContracts.KeyValuePairString { key = "activeSourceId", value = SessionFlowPluginPayload.ReadString(rawInput, "activeSourceId") },
                    new GameContracts.KeyValuePairString { key = "selectedSourceId", value = SessionFlowPluginPayload.ReadString(rawInput, "selectedSourceId") },
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
            if (eventType.IndexOf("AUDIO_SOURCE_SELECTED_INVALID", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return GameContracts.ActionValidationResult.Rejected("AUDIO_SOURCE_MISMATCH");
            }

            if (eventType.IndexOf("AUDIO_SOURCE_SELECTED", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return GameContracts.ActionValidationResult.Rejected("SOURCE_SELECTION_NOT_MADE");
            }

            var activeSourceId = SessionFlowPluginPayload.ReadDetailValueFromIntent(intent, "activeSourceId");
            var selectedSourceId = SessionFlowPluginPayload.ReadDetailValueFromIntent(intent, "selectedSourceId");
            if (string.IsNullOrWhiteSpace(activeSourceId))
            {
                return GameContracts.ActionValidationResult.Rejected("AUDIO_CUE_MISSING");
            }

            if (string.IsNullOrWhiteSpace(selectedSourceId))
            {
                return GameContracts.ActionValidationResult.Rejected("SOURCE_SELECTION_MISSING");
            }

            if (!string.Equals(activeSourceId.Trim(), selectedSourceId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return GameContracts.ActionValidationResult.Rejected("AUDIO_SOURCE_MISMATCH");
            }

            return _validator.Validate(intent, context, allowedAction);
        }

        public GameContracts.ActionApplyResult Apply(
            GameContracts.ActionIntent intent,
            GameContracts.ActionContext context,
            GameContracts.AllowedActionDefinition allowedAction)
        {
            return SessionFlowPluginPayload.BuildAppliedResult(ActionId, ChannelId, "AUDIO_SOURCE_ACTION_APPLIED");
        }
    }

    public sealed class DualHandActionPlugin : GameContracts.IActionPlugin
    {
        private readonly ActionValidator _validator = new ActionValidator();
        private readonly string _actionId;

        public DualHandActionPlugin(string actionId)
        {
            _actionId = string.IsNullOrWhiteSpace(actionId)
                ? "mark_left_and_right_targets"
                : actionId.Trim();
        }

        public string ActionId => _actionId;
        public string ChannelId => GameContracts.SessionFlowChannelIds.DualHand;

        public bool TryCreateIntent(
            IReadOnlyDictionary<string, object> rawInput,
            out GameContracts.ActionIntent intent)
        {
            intent = null;
            if (!SessionFlowPluginPayload.TryReadString(rawInput, "eventType", out var eventType))
            {
                return false;
            }

            if (!SessionFlowPluginPayload.IsDualHandEventType(eventType))
            {
                return false;
            }

            var requestedActionId = SessionFlowPluginPayload.ReadRequestedActionId(rawInput);
            var suggestedActionId = SessionFlowPluginPayload.ResolveDualHandActionIdFromEventType(eventType);
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
                targetId = SessionFlowPluginPayload.ResolveDualHandTargetId(rawInput),
                inputSource = SessionFlowPluginPayload.ReadString(rawInput, "inputSource"),
                inputHand = SessionFlowPluginPayload.ReadString(rawInput, "inputHand"),
                inputValue = SessionFlowPluginPayload.ReadFloat(rawInput, "inputValue"),
                occurredAtElapsedSec = SessionFlowPluginPayload.ReadFloat(rawInput, "occurredAtElapsedSec"),
                details = new List<GameContracts.KeyValuePairString>
                {
                    new GameContracts.KeyValuePairString { key = "eventType", value = eventType },
                    new GameContracts.KeyValuePairString { key = "reasonCode", value = SessionFlowPluginPayload.ReadString(rawInput, "reasonCode") },
                    new GameContracts.KeyValuePairString { key = "leftTargetId", value = SessionFlowPluginPayload.ReadString(rawInput, "leftTargetId") },
                    new GameContracts.KeyValuePairString { key = "rightTargetId", value = SessionFlowPluginPayload.ReadString(rawInput, "rightTargetId") },
                    new GameContracts.KeyValuePairString { key = "leftMatched", value = SessionFlowPluginPayload.ReadString(rawInput, "leftMatched") },
                    new GameContracts.KeyValuePairString { key = "rightMatched", value = SessionFlowPluginPayload.ReadString(rawInput, "rightMatched") },
                    new GameContracts.KeyValuePairString { key = "leftMarked", value = SessionFlowPluginPayload.ReadString(rawInput, "leftMarked") },
                    new GameContracts.KeyValuePairString { key = "rightMarked", value = SessionFlowPluginPayload.ReadString(rawInput, "rightMarked") },
                    new GameContracts.KeyValuePairString { key = "syncDeltaMs", value = SessionFlowPluginPayload.ReadString(rawInput, "syncDeltaMs") },
                    new GameContracts.KeyValuePairString { key = "syncWindowMs", value = SessionFlowPluginPayload.ReadString(rawInput, "syncWindowMs") },
                    new GameContracts.KeyValuePairString { key = "syncSatisfied", value = SessionFlowPluginPayload.ReadString(rawInput, "syncSatisfied") },
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
            if (eventType.IndexOf("DUAL_HAND_MARK_INVALID", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return GameContracts.ActionValidationResult.Rejected("DUAL_HAND_TARGET_INVALID");
            }

            if (eventType.IndexOf("DUAL_HAND_MARK_OUT_OF_SYNC", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return GameContracts.ActionValidationResult.Rejected("DUAL_HAND_SYNC_WINDOW_EXCEEDED");
            }

            if (eventType.IndexOf("DUAL_HAND_MARK_LEFT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                eventType.IndexOf("DUAL_HAND_MARK_RIGHT", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return GameContracts.ActionValidationResult.Rejected("DUAL_HAND_MARK_INCOMPLETE");
            }

            if (eventType.IndexOf("DUAL_HAND_MARK_SYNC", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return GameContracts.ActionValidationResult.Rejected("ACTION_EVENT_TYPE_MISMATCH");
            }

            if (TryReadDetailBool(intent, "leftMatched", out var leftMatched) && !leftMatched)
            {
                return GameContracts.ActionValidationResult.Rejected("DUAL_HAND_LEFT_TARGET_MISMATCH");
            }

            if (TryReadDetailBool(intent, "rightMatched", out var rightMatched) && !rightMatched)
            {
                return GameContracts.ActionValidationResult.Rejected("DUAL_HAND_RIGHT_TARGET_MISMATCH");
            }

            if (TryReadDetailBool(intent, "syncSatisfied", out var syncSatisfied) && !syncSatisfied)
            {
                return GameContracts.ActionValidationResult.Rejected("DUAL_HAND_SYNC_NOT_CONFIRMED");
            }

            if (TryReadDetailFloat(intent, "syncDeltaMs", out var syncDeltaMs))
            {
                var syncWindowMs = ResolveSyncWindowMs(allowedAction, intent);
                if (syncWindowMs > 0f && syncDeltaMs > syncWindowMs)
                {
                    return GameContracts.ActionValidationResult.Rejected("DUAL_HAND_SYNC_WINDOW_EXCEEDED");
                }
            }

            return _validator.Validate(intent, context, allowedAction);
        }

        public GameContracts.ActionApplyResult Apply(
            GameContracts.ActionIntent intent,
            GameContracts.ActionContext context,
            GameContracts.AllowedActionDefinition allowedAction)
        {
            return SessionFlowPluginPayload.BuildAppliedResult(ActionId, ChannelId, "DUAL_HAND_ACTION_APPLIED");
        }

        private static float ResolveSyncWindowMs(
            GameContracts.AllowedActionDefinition allowedAction,
            GameContracts.ActionIntent intent)
        {
            var constraintWindow = ReadConstraintFloat(allowedAction, "syncWindowMs");
            if (constraintWindow <= 0f)
            {
                constraintWindow = ReadConstraintFloat(allowedAction, "maxSyncDeltaMs");
            }

            if (constraintWindow > 0f)
            {
                return constraintWindow;
            }

            if (TryReadDetailFloat(intent, "syncWindowMs", out var detailWindow) && detailWindow > 0f)
            {
                return detailWindow;
            }

            return 0f;
        }

        private static float ReadConstraintFloat(GameContracts.AllowedActionDefinition allowedAction, string key)
        {
            if (allowedAction == null || allowedAction.constraints == null || string.IsNullOrWhiteSpace(key))
            {
                return 0f;
            }

            for (var i = 0; i < allowedAction.constraints.Count; i++)
            {
                var constraint = allowedAction.constraints[i];
                if (constraint == null ||
                    !string.Equals(constraint.key, key, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(constraint.value))
                {
                    continue;
                }

                if (float.TryParse(
                        constraint.value,
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    return parsed;
                }
            }

            return 0f;
        }

        private static bool TryReadDetailBool(GameContracts.ActionIntent intent, string key, out bool value)
        {
            value = false;
            var raw = SessionFlowPluginPayload.ReadDetailValueFromIntent(intent, key);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            if (bool.TryParse(raw, out value))
            {
                return true;
            }

            if (float.TryParse(
                    raw,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out var numeric))
            {
                value = Math.Abs(numeric) > float.Epsilon;
                return true;
            }

            return false;
        }

        private static bool TryReadDetailFloat(GameContracts.ActionIntent intent, string key, out float value)
        {
            value = 0f;
            var raw = SessionFlowPluginPayload.ReadDetailValueFromIntent(intent, key);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            return float.TryParse(
                raw,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out value);
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

        public static bool IsBreathEventType(string eventType)
        {
            return !string.IsNullOrWhiteSpace(eventType) &&
                   eventType.Trim().StartsWith("BREATH_", StringComparison.OrdinalIgnoreCase);
        }

        public static string ResolveBreathActionIdFromEventType(string eventType)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                return string.Empty;
            }

            var normalized = eventType.Trim().ToUpperInvariant();
            switch (normalized)
            {
                case "BREATH_PHASE_INHALE":
                case "BREATH_PHASE_HOLD":
                case "BREATH_PHASE_EXHALE":
                case "BREATH_CYCLE_COMPLETED":
                case "BREATH_CYCLE_INVALID":
                    return "perform_breath_cycle";
                default:
                    return string.Empty;
            }
        }

        public static bool IsAudioSourceEventType(string eventType)
        {
            return !string.IsNullOrWhiteSpace(eventType) &&
                   eventType.Trim().StartsWith("AUDIO_SOURCE_", StringComparison.OrdinalIgnoreCase);
        }

        public static string ResolveAudioSourceActionIdFromEventType(string eventType)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                return string.Empty;
            }

            var normalized = eventType.Trim().ToUpperInvariant();
            switch (normalized)
            {
                case "AUDIO_SOURCE_SELECTED":
                case "AUDIO_SOURCE_SELECTED_INVALID":
                    return "identify_sound_source";
                default:
                    return string.Empty;
            }
        }

        public static bool IsDualHandEventType(string eventType)
        {
            return !string.IsNullOrWhiteSpace(eventType) &&
                   eventType.Trim().StartsWith("DUAL_HAND_", StringComparison.OrdinalIgnoreCase);
        }

        public static string ResolveDualHandActionIdFromEventType(string eventType)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                return string.Empty;
            }

            var normalized = eventType.Trim().ToUpperInvariant();
            switch (normalized)
            {
                case "DUAL_HAND_MARK_LEFT":
                case "DUAL_HAND_MARK_RIGHT":
                case "DUAL_HAND_MARK_SYNC":
                case "DUAL_HAND_MARK_OUT_OF_SYNC":
                case "DUAL_HAND_MARK_INVALID":
                    return "mark_left_and_right_targets";
                default:
                    return string.Empty;
            }
        }

        public static string ResolveDualHandTargetId(IReadOnlyDictionary<string, object> payload)
        {
            var targetId = ReadString(payload, "targetId");
            if (!string.IsNullOrWhiteSpace(targetId))
            {
                return targetId;
            }

            var leftTargetId = ReadString(payload, "leftTargetId");
            var rightTargetId = ReadString(payload, "rightTargetId");
            if (string.IsNullOrWhiteSpace(leftTargetId))
            {
                return rightTargetId;
            }

            if (string.IsNullOrWhiteSpace(rightTargetId))
            {
                return leftTargetId;
            }

            return leftTargetId + "|" + rightTargetId;
        }

        public static string ResolveAudioSourceTargetId(IReadOnlyDictionary<string, object> payload)
        {
            var targetId = ReadString(payload, "targetId");
            if (!string.IsNullOrWhiteSpace(targetId))
            {
                return targetId;
            }

            var selectedSourceId = ReadString(payload, "selectedSourceId");
            if (!string.IsNullOrWhiteSpace(selectedSourceId))
            {
                return selectedSourceId;
            }

            return ReadString(payload, "activeSourceId");
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

        public static string ReadDetailValueFromIntent(GameContracts.ActionIntent intent, string key)
        {
            if (intent == null || intent.details == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            for (var i = 0; i < intent.details.Count; i++)
            {
                var detail = intent.details[i];
                if (detail == null || !string.Equals(detail.key, key, StringComparison.OrdinalIgnoreCase))
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
