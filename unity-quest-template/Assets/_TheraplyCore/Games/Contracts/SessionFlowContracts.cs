using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheraplyCore.Games.Contracts
{
    /// <summary>
    /// Stable control mode ids for session execution.
    /// </summary>
    public static class SessionFlowControlModes
    {
        public const string RemoteOnly = "remote_only";
        public const string LocalOnly = "local_only";
        public const string Hybrid = "hybrid";

        private static readonly HashSet<string> SupportedModes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                RemoteOnly,
                LocalOnly,
                Hybrid,
            };

        public static bool IsSupported(string mode)
        {
            return !string.IsNullOrWhiteSpace(mode) && SupportedModes.Contains(mode.Trim());
        }

        public static string NormalizeOrDefault(string mode)
        {
            return IsSupported(mode) ? mode.Trim().ToLowerInvariant() : Hybrid;
        }
    }

    /// <summary>
    /// Stable channel ids for action input.
    /// </summary>
    public static class SessionFlowChannelIds
    {
        public const string Pointer = "pointer";
        public const string ToolImpact = "tool_impact";
        public const string HandContact = "hand_contact";
        public const string HandGrab = "hand_grab";
        public const string Gaze = "gaze";
        public const string Breath = "breath";
        public const string AudioSource = "audio_source";
        public const string DualHand = "dual_hand";
        public const string PosePath = "pose_path";
        public const string Sequence = "sequence";
        public const string Timeline = "timeline";

        private static readonly HashSet<string> SupportedChannels =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Pointer,
                ToolImpact,
                HandContact,
                HandGrab,
                Gaze,
                Breath,
                AudioSource,
                DualHand,
                PosePath,
                Sequence,
                Timeline,
            };

        public static bool IsSupported(string channelId)
        {
            return !string.IsNullOrWhiteSpace(channelId) && SupportedChannels.Contains(channelId.Trim());
        }

        public static string NormalizeOrEmpty(string channelId)
        {
            return IsSupported(channelId) ? channelId.Trim().ToLowerInvariant() : string.Empty;
        }
    }

    /// <summary>
    /// Stable condition ids for deterministic branch routing.
    /// </summary>
    public static class SessionFlowConditionIds
    {
        public const string ScoreThreshold = "score_threshold";
        public const string StateFlagEquals = "state_flag_equals";
        public const string ControlModeEquals = "control_mode_equals";
        public const string ChannelEnabled = "channel_enabled";
        public const string ElapsedTimeWindow = "elapsed_time_window";

        private static readonly HashSet<string> SupportedConditionIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ScoreThreshold,
                StateFlagEquals,
                ControlModeEquals,
                ChannelEnabled,
                ElapsedTimeWindow,
            };

        public static bool IsSupported(string conditionId)
        {
            return !string.IsNullOrWhiteSpace(conditionId) && SupportedConditionIds.Contains(conditionId.Trim());
        }

        public static string NormalizeOrEmpty(string conditionId)
        {
            return IsSupported(conditionId) ? conditionId.Trim().ToLowerInvariant() : string.Empty;
        }
    }

    /// <summary>
    /// Canonical reason codes for definition validation.
    /// </summary>
    public static class SessionFlowDefinitionReasonCodes
    {
        public const string DefinitionNull = "DEFINITION_NULL";
        public const string DefinitionSourceMissing = "DEFINITION_SOURCE_MISSING";
        public const string DefinitionParseFailed = "DEFINITION_PARSE_FAILED";
        public const string GameIdRequired = "GAME_ID_REQUIRED";
        public const string ControlModeUnsupported = "CONTROL_MODE_UNSUPPORTED";
        public const string ChannelsRequired = "CHANNELS_REQUIRED";
        public const string ChannelIdRequired = "CHANNEL_ID_REQUIRED";
        public const string ChannelIdUnsupported = "CHANNEL_ID_UNSUPPORTED";
        public const string ChannelIdDuplicate = "CHANNEL_ID_DUPLICATE";
        public const string TaskGraphRequired = "TASK_GRAPH_REQUIRED";
        public const string EntryNodeIdRequired = "ENTRY_NODE_ID_REQUIRED";
        public const string EntryNodeNotFound = "ENTRY_NODE_NOT_FOUND";
        public const string NodeIdRequired = "NODE_ID_REQUIRED";
        public const string NodeIdDuplicate = "NODE_ID_DUPLICATE";
        public const string NodeTypeUnsupported = "NODE_TYPE_UNSUPPORTED";
    }

    public interface IGameDefinitionProvider
    {
        bool TryGetGameDefinition(out GameDefinition definition, out string reasonCode);
    }

    /// <summary>
    /// Top-level definition for session-driven gameplay.
    /// </summary>
    [Serializable]
    public sealed class GameDefinition
    {
        public string gameId = string.Empty;
        public string displayName = string.Empty;
        public string commentVersion = string.Empty;
        public string controlMode = SessionFlowControlModes.Hybrid;
        public GameDefinitionConfig config = new GameDefinitionConfig();
        public GameDefinitionBindings bindings = new GameDefinitionBindings();
        public List<SessionFlowChannelConfig> channels = SessionFlowDefaults.CreateDefaultChannels();
        public SessionFlowPolicies policies = new SessionFlowPolicies();
        public TaskGraphDefinition taskGraph = TaskGraphDefinition.CreateEmpty();

        public bool TryValidate(out string reasonCode)
        {
            return SessionFlowDefinitionValidator.TryValidate(this, out reasonCode);
        }

        public static GameDefinition CreateSample()
        {
            return new GameDefinition
            {
                gameId = "sample_training_session",
                displayName = "Sample Training Session",
                commentVersion = "2026-02-24: sample fixture",
                controlMode = SessionFlowControlModes.Hybrid,
                config = new GameDefinitionConfig
                {
                    difficulty = "normal",
                    timeLimitSec = 180,
                    targetCount = 20,
                },
                bindings = GameDefinitionBindings.CreateEmpty(),
                channels = SessionFlowDefaults.CreateDefaultChannels(),
                policies = SessionFlowPolicies.CreateDefault(),
                taskGraph = TaskGraphDefinition.CreateEmpty(),
            };
        }
    }

    [Serializable]
    public sealed class GameDefinitionConfig
    {
        public string difficulty = "normal";
        public int timeLimitSec = 180;
        public int targetCount = 20;
        public List<KeyValuePairString> extras = new List<KeyValuePairString>();
    }

    [Serializable]
    public sealed class GameDefinitionBindings
    {
        public List<BindingReference> objects = new List<BindingReference>();
        public List<BindingReference> zones = new List<BindingReference>();
        public List<BindingReference> audio = new List<BindingReference>();
        public List<BindingReference> timeline = new List<BindingReference>();

        public static GameDefinitionBindings CreateEmpty()
        {
            return new GameDefinitionBindings();
        }
    }

    [Serializable]
    public sealed class BindingReference
    {
        public string key = string.Empty;
        public string value = string.Empty;
    }

    [Serializable]
    public sealed class KeyValuePairString
    {
        public string key = string.Empty;
        public string value = string.Empty;
    }

    [Serializable]
    public sealed class SessionFlowChannelConfig
    {
        public string channelId = string.Empty;
        public bool enabled = true;
    }

    [Serializable]
    public sealed class SessionFlowPolicies
    {
        public string wrongActionPolicy = "warn";
        public RetryPolicy retryPolicy = new RetryPolicy();
        public TimeoutPolicy timeoutPolicy = new TimeoutPolicy();
        public BranchPolicy branchPolicy = new BranchPolicy();
        public ScoringPolicy scoringPolicy = new ScoringPolicy();
        public DifficultyPolicy difficultyPolicy = new DifficultyPolicy();
        public SafetyPolicy safetyPolicy = new SafetyPolicy();
        public ControlPolicy controlPolicy = new ControlPolicy();
        public TelemetryPolicy telemetryPolicy = new TelemetryPolicy();

        public static SessionFlowPolicies CreateDefault()
        {
            return new SessionFlowPolicies();
        }
    }

    [Serializable]
    public sealed class RetryPolicy
    {
        public int maxRetries = 3;
        public float cooldownSec = 0.5f;
    }

    [Serializable]
    public sealed class TimeoutPolicy
    {
        public float defaultTimeoutSec = 15f;
    }

    [Serializable]
    public sealed class ScoringPolicy
    {
        public int lives = 3;
        public int pointsPerCorrect = 1;
        public int pointsPerWrong = -1;
    }

    [Serializable]
    public sealed class BranchPolicy
    {
        public string precedence = BranchPrecedenceModes.FirstMatch;
    }

    public static class BranchPrecedenceModes
    {
        public const string FirstMatch = "first_match";
        public const string LastMatch = "last_match";

        private static readonly HashSet<string> SupportedModes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                FirstMatch,
                LastMatch,
            };

        public static bool IsSupported(string precedence)
        {
            return !string.IsNullOrWhiteSpace(precedence) && SupportedModes.Contains(precedence.Trim());
        }

        public static string NormalizeOrDefault(string precedence)
        {
            return IsSupported(precedence) ? precedence.Trim().ToLowerInvariant() : FirstMatch;
        }
    }

    [Serializable]
    public sealed class DifficultyPolicy
    {
        public bool adaptiveEnabled = true;
    }

    [Serializable]
    public sealed class SafetyPolicy
    {
        public bool emergencyStopEnabled = true;
        public bool rejectDisabledChannels = true;
    }

    [Serializable]
    public sealed class ControlPolicy
    {
        public string mode = SessionFlowControlModes.Hybrid;
    }

    [Serializable]
    public sealed class TelemetryPolicy
    {
        public bool requireDecisionForEveryAction = true;
    }

    [Serializable]
    public sealed class TaskGraphDefinition
    {
        public string entryNodeId = "n1";
        public List<TaskGraphNodeDefinition> nodes = new List<TaskGraphNodeDefinition>();

        public static TaskGraphDefinition CreateEmpty()
        {
            return new TaskGraphDefinition
            {
                entryNodeId = "n1",
                nodes = new List<TaskGraphNodeDefinition>(),
            };
        }
    }

    public static class TaskGraphNodeTypes
    {
        public const string Action = "Action";
        public const string Condition = "Condition";
        public const string Branch = "Branch";
        public const string Timer = "Timer";
        public const string Complete = "Complete";
        public const string Fail = "Fail";

        private static readonly HashSet<string> SupportedTypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Action,
                Condition,
                Branch,
                Timer,
                Complete,
                Fail,
            };

        public static bool IsSupported(string nodeType)
        {
            return !string.IsNullOrWhiteSpace(nodeType) && SupportedTypes.Contains(nodeType.Trim());
        }
    }

    [Serializable]
    public sealed class TaskGraphNodeDefinition
    {
        public string nodeId = string.Empty;
        public string nodeType = TaskGraphNodeTypes.Action;
        public List<AllowedActionDefinition> allowedActions = new List<AllowedActionDefinition>();
        public List<ConditionDefinition> conditions = new List<ConditionDefinition>();
        public float timeoutSec = 0f;
        public List<EffectDefinition> onEnterEffects = new List<EffectDefinition>();
        public List<EffectDefinition> onExitEffects = new List<EffectDefinition>();
        public List<EffectDefinition> onAcceptedEffects = new List<EffectDefinition>();
        public List<EffectDefinition> onRejectedEffects = new List<EffectDefinition>();
        public List<EffectDefinition> onTimeoutEffects = new List<EffectDefinition>();
        public string nextOnSuccess = string.Empty;
        public string nextOnFail = string.Empty;
        public string nextOnTimeout = string.Empty;
    }

    [Serializable]
    public sealed class AllowedActionDefinition
    {
        public string actionId = string.Empty;
        public string targetGroup = string.Empty;
        public List<KeyValuePairString> constraints = new List<KeyValuePairString>();
    }

    [Serializable]
    public sealed class ActionIntent
    {
        public string actionId = string.Empty;
        public string channelId = string.Empty;
        public string targetId = string.Empty;
        public string inputSource = string.Empty;
        public string inputHand = string.Empty;
        public float inputValue = 0f;
        public float occurredAtElapsedSec = 0f;
        public List<KeyValuePairString> details = new List<KeyValuePairString>();
    }

    [Serializable]
    public sealed class ActionContext
    {
        public string gameId = string.Empty;
        public string flowId = string.Empty;
        public string sessionId = string.Empty;
        public string stepId = string.Empty;
        public string nodeId = string.Empty;
        public string controlMode = SessionFlowControlModes.Hybrid;
        public List<KeyValuePairString> state = new List<KeyValuePairString>();
    }

    [Serializable]
    public sealed class ActionValidationResult
    {
        public bool accepted;
        public string reasonCode = string.Empty;
        public string normalizedTargetId = string.Empty;

        public static ActionValidationResult Accepted(string normalizedTargetId = "")
        {
            return new ActionValidationResult
            {
                accepted = true,
                reasonCode = "ACTION_ACCEPTED",
                normalizedTargetId = string.IsNullOrWhiteSpace(normalizedTargetId) ? string.Empty : normalizedTargetId.Trim(),
            };
        }

        public static ActionValidationResult Rejected(string reasonCode)
        {
            return new ActionValidationResult
            {
                accepted = false,
                reasonCode = string.IsNullOrWhiteSpace(reasonCode) ? "ACTION_REJECTED" : reasonCode.Trim(),
                normalizedTargetId = string.Empty,
            };
        }
    }

    [Serializable]
    public sealed class ActionApplyResult
    {
        public bool stepCompleted;
        public bool stepFailed;
        public bool stepTimedOut;
        public string nextNodeId = string.Empty;
        public string reasonCode = string.Empty;
        public List<KeyValuePairString> metrics = new List<KeyValuePairString>();
    }

    [Serializable]
    public sealed class ConditionDefinition
    {
        public string conditionId = string.Empty;
        public string subject = string.Empty;
        public string op = string.Empty;
        public string value = string.Empty;
        public string nextNodeId = string.Empty;
    }

    [Serializable]
    public sealed class EffectDefinition
    {
        public string effectId = string.Empty;
        public string binding = string.Empty;
        public List<KeyValuePairString> parameters = new List<KeyValuePairString>();
    }

    public static class SessionFlowDefaults
    {
        public static List<SessionFlowChannelConfig> CreateDefaultChannels()
        {
            return new List<SessionFlowChannelConfig>
            {
                new SessionFlowChannelConfig { channelId = SessionFlowChannelIds.Pointer, enabled = true },
                new SessionFlowChannelConfig { channelId = SessionFlowChannelIds.ToolImpact, enabled = true },
                new SessionFlowChannelConfig { channelId = SessionFlowChannelIds.HandContact, enabled = false },
                new SessionFlowChannelConfig { channelId = SessionFlowChannelIds.HandGrab, enabled = false },
                new SessionFlowChannelConfig { channelId = SessionFlowChannelIds.Gaze, enabled = false },
                new SessionFlowChannelConfig { channelId = SessionFlowChannelIds.Breath, enabled = false },
                new SessionFlowChannelConfig { channelId = SessionFlowChannelIds.AudioSource, enabled = false },
                new SessionFlowChannelConfig { channelId = SessionFlowChannelIds.DualHand, enabled = false },
                new SessionFlowChannelConfig { channelId = SessionFlowChannelIds.PosePath, enabled = false },
                new SessionFlowChannelConfig { channelId = SessionFlowChannelIds.Sequence, enabled = false },
                new SessionFlowChannelConfig { channelId = SessionFlowChannelIds.Timeline, enabled = false },
            };
        }
    }

    public static class SessionFlowDefinitionValidator
    {
        public static bool TryValidate(GameDefinition definition, out string reasonCode)
        {
            reasonCode = string.Empty;

            if (definition == null)
            {
                reasonCode = SessionFlowDefinitionReasonCodes.DefinitionNull;
                return false;
            }

            if (string.IsNullOrWhiteSpace(definition.gameId))
            {
                reasonCode = SessionFlowDefinitionReasonCodes.GameIdRequired;
                return false;
            }

            if (!SessionFlowControlModes.IsSupported(definition.controlMode))
            {
                reasonCode = SessionFlowDefinitionReasonCodes.ControlModeUnsupported;
                return false;
            }

            if (definition.policies != null &&
                definition.policies.controlPolicy != null &&
                !SessionFlowControlModes.IsSupported(definition.policies.controlPolicy.mode))
            {
                reasonCode = SessionFlowDefinitionReasonCodes.ControlModeUnsupported;
                return false;
            }

            if (definition.channels == null || definition.channels.Count == 0)
            {
                reasonCode = SessionFlowDefinitionReasonCodes.ChannelsRequired;
                return false;
            }

            var seenChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < definition.channels.Count; i++)
            {
                var channel = definition.channels[i];
                if (channel == null || string.IsNullOrWhiteSpace(channel.channelId))
                {
                    reasonCode = SessionFlowDefinitionReasonCodes.ChannelIdRequired;
                    return false;
                }

                var normalizedChannel = channel.channelId.Trim();
                if (!SessionFlowChannelIds.IsSupported(normalizedChannel))
                {
                    reasonCode = SessionFlowDefinitionReasonCodes.ChannelIdUnsupported;
                    return false;
                }

                if (!seenChannels.Add(normalizedChannel))
                {
                    reasonCode = SessionFlowDefinitionReasonCodes.ChannelIdDuplicate;
                    return false;
                }
            }

            if (definition.taskGraph == null)
            {
                reasonCode = SessionFlowDefinitionReasonCodes.TaskGraphRequired;
                return false;
            }

            if (string.IsNullOrWhiteSpace(definition.taskGraph.entryNodeId))
            {
                reasonCode = SessionFlowDefinitionReasonCodes.EntryNodeIdRequired;
                return false;
            }

            var nodes = definition.taskGraph.nodes;
            if (nodes != null && nodes.Count > 0)
            {
                var seenNodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var hasEntryNode = false;

                for (var i = 0; i < nodes.Count; i++)
                {
                    var node = nodes[i];
                    if (node == null || string.IsNullOrWhiteSpace(node.nodeId))
                    {
                        reasonCode = SessionFlowDefinitionReasonCodes.NodeIdRequired;
                        return false;
                    }

                    var normalizedNodeId = node.nodeId.Trim();
                    if (!seenNodeIds.Add(normalizedNodeId))
                    {
                        reasonCode = SessionFlowDefinitionReasonCodes.NodeIdDuplicate;
                        return false;
                    }

                    if (!TaskGraphNodeTypes.IsSupported(node.nodeType))
                    {
                        reasonCode = SessionFlowDefinitionReasonCodes.NodeTypeUnsupported;
                        return false;
                    }

                    if (string.Equals(normalizedNodeId, definition.taskGraph.entryNodeId.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        hasEntryNode = true;
                    }
                }

                if (!hasEntryNode)
                {
                    reasonCode = SessionFlowDefinitionReasonCodes.EntryNodeNotFound;
                    return false;
                }
            }

            return true;
        }
    }

    [CreateAssetMenu(
        fileName = "GameDefinition",
        menuName = "Theraply/Session Flow/Game Definition",
        order = 420)]
    public sealed class GameDefinitionAsset : ScriptableObject
    {
        public GameDefinition definition = GameDefinition.CreateSample();
    }
}
