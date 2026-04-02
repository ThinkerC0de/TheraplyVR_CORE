using System;
using System.Collections.Generic;
using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Contracts;

namespace TheraplyCore.Games.Runtime
{
    public static class SceneGameControllerReasonCodes
    {
        public const string GameIdMismatch = "GAME_ID_MISMATCH";
        public const string SceneConfigNull = "SCENE_CONFIG_NULL";
        public const string SceneConfigRejected = "SCENE_CONFIG_REJECTED";
        public const string SceneConfigGameIdRequired = "SCENE_CONFIG_GAME_ID_REQUIRED";
        public const string SceneConfigVersionInvalid = "SCENE_CONFIG_VERSION_INVALID";
        public const string SceneConfigJsonInvalid = "SCENE_CONFIG_JSON_INVALID";
    }

    [Serializable]
    public sealed class SceneGameConfig : GameContracts.IGameConfig
    {
        [SerializeField] private string _gameId = string.Empty;
        [SerializeField] private int _version = 1;
        [SerializeField] private string _configType = string.Empty;
        [SerializeField] private int _configVersion = 1;
        [SerializeField] private string _configJson = "{}";
        [SerializeField] private bool _resumeFromSaved;

        public string GameId => string.IsNullOrWhiteSpace(_gameId) ? string.Empty : _gameId.Trim();
        public int Version => Mathf.Max(1, _version);
        public string ConfigType => string.IsNullOrWhiteSpace(_configType) ? string.Empty : _configType.Trim();
        public int ConfigVersion => Mathf.Max(1, _configVersion);
        public string ConfigJson => string.IsNullOrWhiteSpace(_configJson) ? "{}" : _configJson.Trim();
        public bool ResumeFromSaved => _resumeFromSaved;

        public SceneGameConfig Clone()
        {
            return new SceneGameConfig
            {
                _gameId = GameId,
                _version = Version,
                _configType = ConfigType,
                _configVersion = ConfigVersion,
                _configJson = ConfigJson,
                _resumeFromSaved = ResumeFromSaved,
            };
        }

        public static SceneGameConfig CreateDefault(
            string gameId,
            string configType,
            int configVersion,
            string configJson)
        {
            var normalizedGameId = string.IsNullOrWhiteSpace(gameId) ? string.Empty : gameId.Trim();
            var resolvedConfigType = ResolveConfigType(configType, normalizedGameId);
            var resolvedConfigVersion = Mathf.Max(1, configVersion);
            var resolvedConfigJson = ResolveConfigJson(configJson);

            return new SceneGameConfig
            {
                _gameId = normalizedGameId,
                _version = resolvedConfigVersion,
                _configType = resolvedConfigType,
                _configVersion = resolvedConfigVersion,
                _configJson = resolvedConfigJson,
                _resumeFromSaved = false,
            };
        }

        public static SceneGameConfig CreateFromStartCommand(
            StartGameCommand command,
            SceneGameConfig previous,
            string fallbackGameId,
            string fallbackConfigType,
            int fallbackConfigVersion,
            string fallbackConfigJson)
        {
            var baseline = previous ?? CreateDefault(
                fallbackGameId,
                fallbackConfigType,
                fallbackConfigVersion,
                fallbackConfigJson);

            if (command == null)
            {
                return baseline.Clone();
            }

            var resolvedGameId = string.IsNullOrWhiteSpace(command.gameId)
                ? baseline.GameId
                : command.gameId.Trim();
            if (string.IsNullOrWhiteSpace(resolvedGameId))
            {
                resolvedGameId = string.IsNullOrWhiteSpace(fallbackGameId)
                    ? baseline.GameId
                    : fallbackGameId.Trim();
            }

            var resolvedConfigType = string.IsNullOrWhiteSpace(command.gameConfigType)
                ? baseline.ConfigType
                : command.gameConfigType.Trim();
            resolvedConfigType = ResolveConfigType(resolvedConfigType, resolvedGameId);

            var resolvedConfigVersion = command.gameConfigVersion > 0
                ? command.gameConfigVersion
                : baseline.ConfigVersion;
            if (resolvedConfigVersion <= 0)
            {
                resolvedConfigVersion = Mathf.Max(1, fallbackConfigVersion);
            }

            var resolvedConfigJson = string.IsNullOrWhiteSpace(command.gameConfigJson)
                ? baseline.ConfigJson
                : command.gameConfigJson.Trim();
            resolvedConfigJson = ResolveConfigJson(resolvedConfigJson);

            return new SceneGameConfig
            {
                _gameId = resolvedGameId,
                _version = resolvedConfigVersion,
                _configType = resolvedConfigType,
                _configVersion = resolvedConfigVersion,
                _configJson = resolvedConfigJson,
                _resumeFromSaved = command.resumeFromSaved,
            };
        }

        private static string ResolveConfigType(string configType, string gameId)
        {
            if (!string.IsNullOrWhiteSpace(configType))
            {
                return configType.Trim();
            }

            return string.IsNullOrWhiteSpace(gameId)
                ? "scene_config"
                : gameId.Trim() + "_config";
        }

        private static string ResolveConfigJson(string configJson)
        {
            return string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson.Trim();
        }
    }

    [DisallowMultipleComponent]
    public abstract class SceneGameController : GameModuleBase,
        GameContracts.IDefaultGameConfigProvider,
        GameContracts.IStartCommandConfigProvider
    {
        [Header("Scene Config Defaults")]
        [SerializeField] private string _defaultConfigType = "scene_config";
        [SerializeField] private int _defaultConfigVersion = 1;
        [SerializeField] [TextArea(2, 12)] private string _defaultConfigJson = "{}";

        protected SceneGameConfig ActiveSceneConfig { get; private set; }

        public virtual GameContracts.IGameConfig CreateDefaultConfig()
        {
            return SceneGameConfig.CreateDefault(
                GameId,
                ResolveDefaultConfigType(),
                ResolveDefaultConfigVersion(),
                ResolveDefaultConfigJson());
        }

        public bool TryCreateConfigFromStartCommand(
            StartGameCommand command,
            GameContracts.IGameConfig previousConfig,
            out GameContracts.IGameConfig resolvedConfig,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            resolvedConfig = null;

            if (command != null &&
                !string.IsNullOrWhiteSpace(command.gameId) &&
                !string.Equals(command.gameId.Trim(), GameId, StringComparison.OrdinalIgnoreCase))
            {
                reasonCode = SceneGameControllerReasonCodes.GameIdMismatch;
                return false;
            }

            var previousSceneConfig = previousConfig as SceneGameConfig;
            var candidate = SceneGameConfig.CreateFromStartCommand(
                command,
                previousSceneConfig,
                GameId,
                ResolveDefaultConfigType(),
                ResolveDefaultConfigVersion(),
                ResolveDefaultConfigJson());

            if (!ValidateResolvedConfig(candidate, out reasonCode))
            {
                return false;
            }

            if (!TryResolveConfig(
                    candidate,
                    previousSceneConfig,
                    command,
                    out var finalConfig,
                    out reasonCode))
            {
                reasonCode = string.IsNullOrWhiteSpace(reasonCode)
                    ? SceneGameControllerReasonCodes.SceneConfigRejected
                    : reasonCode;
                return false;
            }

            if (finalConfig == null)
            {
                reasonCode = SceneGameControllerReasonCodes.SceneConfigNull;
                return false;
            }

            resolvedConfig = finalConfig;
            reasonCode = string.Empty;
            return true;
        }

        public sealed override void Initialize(
            GameContracts.IGameConfig config,
            GameContracts.IGameContext context)
        {
            var normalizedConfig = NormalizeRuntimeConfig(config);
            ActiveSceneConfig = normalizedConfig;

            base.Initialize(normalizedConfig, context);
            ApplyConfig(normalizedConfig);
            OnInitialized(normalizedConfig, context);
        }

        public sealed override void StartGame()
        {
            var previousState = State;
            base.StartGame();
            if (State != GameContracts.GameState.Playing)
            {
                return;
            }

            if (previousState == GameContracts.GameState.Paused)
            {
                OnResumed();
                return;
            }

            if (previousState == GameContracts.GameState.Initialized)
            {
                OnStarted();
            }
        }

        public sealed override void PauseGame()
        {
            var wasPlaying = State == GameContracts.GameState.Playing;
            base.PauseGame();
            if (wasPlaying && State == GameContracts.GameState.Paused)
            {
                OnPaused();
            }
        }

        public sealed override void ResumeGame()
        {
            StartGame();
        }

        public sealed override void StopGame(GameContracts.GameStopReason reason)
        {
            var alreadyTerminal = State == GameContracts.GameState.Completed ||
                                  State == GameContracts.GameState.Failed;
            if (alreadyTerminal)
            {
                return;
            }

            base.StopGame(reason);
            OnStopped(reason);
        }

        public sealed override void UpdateConfig(GameContracts.IGameConfig newConfig)
        {
            var normalizedConfig = NormalizeRuntimeConfig(newConfig);
            ActiveSceneConfig = normalizedConfig;

            base.UpdateConfig(normalizedConfig);
            ApplyConfig(normalizedConfig);
            OnConfigUpdated(normalizedConfig);
        }

        protected virtual bool ValidateResolvedConfig(
            SceneGameConfig config,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            if (config == null)
            {
                reasonCode = SceneGameControllerReasonCodes.SceneConfigNull;
                return false;
            }

            if (string.IsNullOrWhiteSpace(config.GameId))
            {
                reasonCode = SceneGameControllerReasonCodes.SceneConfigGameIdRequired;
                return false;
            }

            if (!string.Equals(config.GameId, GameId, StringComparison.OrdinalIgnoreCase))
            {
                reasonCode = SceneGameControllerReasonCodes.GameIdMismatch;
                return false;
            }

            if (config.Version <= 0 || config.ConfigVersion <= 0)
            {
                reasonCode = SceneGameControllerReasonCodes.SceneConfigVersionInvalid;
                return false;
            }

            reasonCode = string.Empty;
            return true;
        }

        protected virtual bool TryResolveConfig(
            SceneGameConfig candidate,
            SceneGameConfig previous,
            StartGameCommand command,
            out SceneGameConfig resolved,
            out string reasonCode)
        {
            resolved = candidate;
            reasonCode = string.Empty;
            return true;
        }

        public virtual void ApplyConfig(SceneGameConfig config)
        {
        }

        protected virtual void OnInitialized(
            SceneGameConfig config,
            GameContracts.IGameContext context)
        {
        }

        protected virtual void OnStarted()
        {
        }

        protected virtual void OnPaused()
        {
        }

        protected virtual void OnResumed()
        {
        }

        protected virtual void OnStopped(GameContracts.GameStopReason reason)
        {
        }

        protected virtual void OnConfigUpdated(SceneGameConfig config)
        {
        }

        protected bool TryReadConfigPayload<TPayload>(out TPayload payload, out string reasonCode)
            where TPayload : class, new()
        {
            payload = new TPayload();
            reasonCode = string.Empty;

            var json = ActiveSceneConfig == null ? "{}" : ActiveSceneConfig.ConfigJson;
            if (string.IsNullOrWhiteSpace(json) ||
                string.Equals(json.Trim(), "{}", StringComparison.Ordinal))
            {
                return true;
            }

            try
            {
                var parsed = JsonUtility.FromJson<TPayload>(json);
                if (parsed != null)
                {
                    payload = parsed;
                }

                return true;
            }
            catch
            {
                reasonCode = SceneGameControllerReasonCodes.SceneConfigJsonInvalid;
                return false;
            }
        }

        protected void TrackSceneLifecycle(
            string eventName,
            string reasonCode,
            IReadOnlyDictionary<string, object> extras = null)
        {
            var payload = extras != null
                ? new Dictionary<string, object>(extras)
                : new Dictionary<string, object>();
            payload["reasonCode"] = string.IsNullOrWhiteSpace(reasonCode)
                ? string.Empty
                : reasonCode.Trim();
            payload["configType"] = ActiveSceneConfig == null ? string.Empty : ActiveSceneConfig.ConfigType;
            payload["configVersion"] = ActiveSceneConfig == null ? 0 : ActiveSceneConfig.ConfigVersion;
            payload["resumeFromSaved"] = ActiveSceneConfig != null && ActiveSceneConfig.ResumeFromSaved;
            TrackEvent(eventName, payload);
        }

        private SceneGameConfig NormalizeRuntimeConfig(GameContracts.IGameConfig config)
        {
            if (config is SceneGameConfig sceneConfig)
            {
                return sceneConfig.Clone();
            }

            return SceneGameConfig.CreateDefault(
                string.IsNullOrWhiteSpace(config == null ? null : config.GameId)
                    ? GameId
                    : config.GameId,
                ResolveDefaultConfigType(),
                config == null ? ResolveDefaultConfigVersion() : Mathf.Max(1, config.Version),
                ResolveDefaultConfigJson());
        }

        private string ResolveDefaultConfigType()
        {
            if (!string.IsNullOrWhiteSpace(_defaultConfigType))
            {
                return _defaultConfigType.Trim();
            }

            return string.IsNullOrWhiteSpace(GameId)
                ? "scene_config"
                : GameId.Trim() + "_config";
        }

        private int ResolveDefaultConfigVersion()
        {
            return Mathf.Max(1, _defaultConfigVersion);
        }

        private string ResolveDefaultConfigJson()
        {
            return string.IsNullOrWhiteSpace(_defaultConfigJson) ? "{}" : _defaultConfigJson.Trim();
        }
    }
}
