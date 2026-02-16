using System;
using System.Collections.Generic;
using UnityEngine;
using TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Runtime controller that coordinates active game lifecycle through contract services.
    /// </summary>
    [DisallowMultipleComponent]
    public class MiniGameRuntimeService : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private MiniGameRegistryService _registryService;
        [SerializeField] private MiniGameContextService _contextService;
        [SerializeField] private MiniGameCommandBus _commandBus;

        [Header("Runtime")]
        [SerializeField] private string _defaultGameId = "";
        [SerializeField] private bool _subscribeToStandardCommands = true;

        private readonly Dictionary<string, IMiniGameConfig> _knownConfigs =
            new Dictionary<string, IMiniGameConfig>(StringComparer.OrdinalIgnoreCase);

        private IMiniGameModule _activeGame;
        private string _activeGameId;

        public IMiniGameModule ActiveGame => _activeGame;
        public string ActiveGameId => _activeGameId;

        private void Awake()
        {
            if (_registryService == null) _registryService = FindFirstObjectByType<MiniGameRegistryService>();
            if (_contextService == null) _contextService = FindFirstObjectByType<MiniGameContextService>();
            if (_commandBus == null) _commandBus = FindFirstObjectByType<MiniGameCommandBus>();

            if (!string.IsNullOrWhiteSpace(_defaultGameId))
            {
                SetActiveGame(_defaultGameId);
            }
        }

        private void OnEnable()
        {
            if (!_subscribeToStandardCommands || _commandBus == null) return;

            _commandBus.Subscribe<StartGameCommand>(HandleStartCommand);
            _commandBus.Subscribe<PauseGameCommand>(HandlePauseCommand);
            _commandBus.Subscribe<ResumeGameCommand>(HandleResumeCommand);
            _commandBus.Subscribe<StopGameCommand>(HandleStopCommand);
        }

        private void OnDisable()
        {
            if (!_subscribeToStandardCommands || _commandBus == null) return;

            _commandBus.Unsubscribe<StartGameCommand>(HandleStartCommand);
            _commandBus.Unsubscribe<PauseGameCommand>(HandlePauseCommand);
            _commandBus.Unsubscribe<ResumeGameCommand>(HandleResumeCommand);
            _commandBus.Unsubscribe<StopGameCommand>(HandleStopCommand);
        }

        public bool SetActiveGame(string gameId)
        {
            if (_registryService == null)
            {
                Logger.Warning("[MiniGameRuntime] Registry service is missing.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(gameId))
            {
                Logger.Warning("[MiniGameRuntime] Cannot activate game with empty gameId.");
                return false;
            }

            if (!_registryService.TryResolve(gameId, out var module))
            {
                Logger.Warning($"[MiniGameRuntime] Game not found in registry: {gameId}");
                return false;
            }

            _activeGame = module;
            _activeGameId = gameId;
            Logger.Info($"[MiniGameRuntime] Active game set: {gameId}");
            return true;
        }

        public bool InitializeGame(string gameId, IMiniGameConfig config)
        {
            if (config == null)
            {
                Logger.Warning("[MiniGameRuntime] Initialize failed: config is null.");
                return false;
            }

            if (!SetActiveGame(gameId))
            {
                return false;
            }

            if (_contextService == null)
            {
                Logger.Warning("[MiniGameRuntime] Initialize failed: context service is missing.");
                return false;
            }

            _knownConfigs[gameId] = config;
            _activeGame.Initialize(config, _contextService);
            return true;
        }

        public bool UpdateGameConfig(string gameId, IMiniGameConfig config)
        {
            if (config == null)
            {
                Logger.Warning("[MiniGameRuntime] Update config failed: config is null.");
                return false;
            }

            if (!SetActiveGame(gameId))
            {
                return false;
            }

            _knownConfigs[gameId] = config;

            if (_activeGame.State == MiniGameState.NotInitialized)
            {
                if (_contextService == null)
                {
                    Logger.Warning("[MiniGameRuntime] Update config failed: context service is missing.");
                    return false;
                }

                _activeGame.Initialize(config, _contextService);
            }
            else
            {
                _activeGame.UpdateConfig(config);
            }

            return true;
        }

        public bool StartActiveGame()
        {
            if (!EnsureActiveGame())
            {
                return false;
            }

            if (_activeGame.State == MiniGameState.NotInitialized)
            {
                if (!_knownConfigs.TryGetValue(_activeGameId, out var cachedConfig))
                {
                    Logger.Warning($"[MiniGameRuntime] Start failed: {_activeGameId} has no config. Call InitializeGame first.");
                    return false;
                }

                _activeGame.Initialize(cachedConfig, _contextService);
            }

            _activeGame.StartGame();
            return true;
        }

        public void PauseActiveGame()
        {
            if (!EnsureActiveGame()) return;
            _activeGame.PauseGame();
        }

        public void ResumeActiveGame()
        {
            if (!EnsureActiveGame()) return;
            _activeGame.ResumeGame();
        }

        public void StopActiveGame(MiniGameStopReason reason)
        {
            if (!EnsureActiveGame()) return;
            _activeGame.StopGame(reason);
        }

        private void HandleStartCommand(StartGameCommand command)
        {
            if (!TryResolveCommandGame(command?.gameId)) return;
            StartActiveGame();
        }

        private void HandlePauseCommand(PauseGameCommand command)
        {
            if (!TryResolveCommandGame(command?.gameId)) return;
            PauseActiveGame();
        }

        private void HandleResumeCommand(ResumeGameCommand command)
        {
            if (!TryResolveCommandGame(command?.gameId)) return;
            ResumeActiveGame();
        }

        private void HandleStopCommand(StopGameCommand command)
        {
            if (!TryResolveCommandGame(command?.gameId)) return;

            var reason = MiniGameStopReason.TherapistStop;
            if (!string.IsNullOrWhiteSpace(command?.reason) &&
                Enum.TryParse(command.reason, true, out MiniGameStopReason parsedReason))
            {
                reason = parsedReason;
            }

            StopActiveGame(reason);
        }

        private bool EnsureActiveGame()
        {
            if (_activeGame != null)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(_defaultGameId))
            {
                return SetActiveGame(_defaultGameId);
            }

            Logger.Warning("[MiniGameRuntime] No active game selected.");
            return false;
        }

        private bool TryResolveCommandGame(string gameId)
        {
            if (!string.IsNullOrWhiteSpace(gameId))
            {
                return SetActiveGame(gameId);
            }

            return EnsureActiveGame();
        }
    }
}
