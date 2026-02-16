using System;
using System.Collections.Generic;
using UnityEngine;
using TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    [Serializable]
    public class MiniGameRegistryEntry
    {
        public string gameId;
        public MonoBehaviour moduleBehaviour;
    }

    /// <summary>
    /// Concrete registry mapping game IDs to mini-game modules.
    /// </summary>
    [DisallowMultipleComponent]
    public class MiniGameRegistryService : MonoBehaviour, IGameRegistry
    {
        [SerializeField] private List<MiniGameRegistryEntry> _entries = new List<MiniGameRegistryEntry>();
        [SerializeField] private bool _logMappings = true;

        private readonly Dictionary<string, IMiniGameModule> _modules =
            new Dictionary<string, IMiniGameModule>(StringComparer.OrdinalIgnoreCase);

        private void Awake()
        {
            RebuildRegistry();
        }

        public bool TryResolve(string gameId, out IMiniGameModule module)
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

                if (!(entry.moduleBehaviour is IMiniGameModule module))
                {
                    Logger.Warning($"[MiniGameRegistry] {entry.moduleBehaviour.name} does not implement IMiniGameModule.");
                    continue;
                }

                if (_modules.ContainsKey(entry.gameId))
                {
                    Logger.Warning($"[MiniGameRegistry] Duplicate gameId ignored: {entry.gameId}");
                    continue;
                }

                _modules.Add(entry.gameId, module);
            }

            if (_logMappings)
            {
                Logger.Info($"[MiniGameRegistry] Loaded {_modules.Count} game mappings.");
            }
        }

        public bool RegisterRuntime(string gameId, IMiniGameModule module)
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
                Logger.Info($"[MiniGameRegistry] {pair.Key} -> {pair.Value.GetType().Name}");
            }
        }
#endif
    }
}
