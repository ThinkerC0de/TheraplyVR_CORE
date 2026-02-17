using System;
using System.Collections.Generic;
using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    [Serializable]
    public class GameRegistryEntry
    {
        public string gameId;
        public MonoBehaviour moduleBehaviour;
    }

    /// <summary>
    /// Concrete registry mapping game IDs to game modules.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameRegistryService : MonoBehaviour, IGameRegistry
    {
        [SerializeField] private List<GameRegistryEntry> _entries = new List<GameRegistryEntry>();
        [SerializeField] private bool _logMappings = true;

        private readonly Dictionary<string, GameContracts.IGameModule> _modules =
            new Dictionary<string, GameContracts.IGameModule>(StringComparer.OrdinalIgnoreCase);

        private void Awake()
        {
            RebuildRegistry();
        }

        public bool TryResolve(string gameId, out GameContracts.IGameModule module)
        {
            if (string.IsNullOrWhiteSpace(gameId))
            {
                module = null;
                return false;
            }

            return _modules.TryGetValue(gameId, out module);
        }

        public void RebuildRegistry()
        {
            _modules.Clear();

            foreach (var entry in _entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.gameId) || entry.moduleBehaviour == null)
                {
                    continue;
                }

                if (!(entry.moduleBehaviour is GameContracts.IGameModule module))
                {
                    Logger.Warning($"[GameRegistry] {entry.moduleBehaviour.name} does not implement GameContracts.IGameModule.");
                    continue;
                }

                if (_modules.ContainsKey(entry.gameId))
                {
                    Logger.Warning($"[GameRegistry] Duplicate gameId ignored: {entry.gameId}");
                    continue;
                }

                _modules.Add(entry.gameId, module);
            }

            if (_logMappings)
            {
                Logger.Info($"[GameRegistry] Loaded {_modules.Count} game mappings.");
            }
        }

        public bool RegisterRuntime(string gameId, GameContracts.IGameModule module)
        {
            if (string.IsNullOrWhiteSpace(gameId) || module == null)
            {
                return false;
            }

            _modules[gameId] = module;
            return true;
        }

#if UNITY_EDITOR
        [ContextMenu("Log Registered Games")]
        private void LogRegisteredGames()
        {
            foreach (var pair in _modules)
            {
                Logger.Info($"[GameRegistry] {pair.Key} -> {pair.Value.GetType().Name}");
            }
        }
#endif
    }
}
