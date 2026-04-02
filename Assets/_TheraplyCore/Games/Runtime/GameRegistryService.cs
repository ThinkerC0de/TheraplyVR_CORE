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

    [Serializable]
    public class GameRuntimeProvisionEntry
    {
        public string gameId;
        public string moduleTypeName;
        public string hostObjectName;
    }

    /// <summary>
    /// Concrete registry mapping game IDs to game modules.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameRegistryService : MonoBehaviour, IGameRegistry
    {
        [SerializeField] private List<GameRegistryEntry> _entries = new List<GameRegistryEntry>();
        [SerializeField] private bool _logMappings = true;
        [SerializeField] private bool _autoDiscoverSceneModules = true;
        [SerializeField] private bool _autoProvisionFallbackModuleWhenEmpty = true;
        [SerializeField] private string _autoProvisionFallbackGameId = string.Empty;
        [SerializeField] private string _autoProvisionFallbackModuleTypeName = string.Empty;
        [SerializeField] private string _autoProvisionHostObjectName = "GameModuleRuntime";
        [SerializeField] private bool _enableOnDemandProvisioning = true;
        [SerializeField] private List<GameRuntimeProvisionEntry> _runtimeProvisionEntries =
            new List<GameRuntimeProvisionEntry>();

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

            if (_modules.TryGetValue(gameId, out module))
            {
                return true;
            }

            if (_enableOnDemandProvisioning && TryAutoProvisionByGameId(gameId, out module))
            {
                return true;
            }

            module = null;
            return false;
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

                if (!string.IsNullOrWhiteSpace(module.GameId) &&
                    !string.Equals(entry.gameId, module.GameId, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Warning(
                        $"[GameRegistry] Entry gameId '{entry.gameId}' does not match module gameId '{module.GameId}'. Entry value will be used.");
                }

                TryRegisterModule(entry.gameId, module, "serialized-entry");
            }

            if (_autoDiscoverSceneModules)
            {
                RegisterSceneModules();
            }

            if (_modules.Count == 0 && _autoProvisionFallbackModuleWhenEmpty)
            {
                TryAutoProvisionFallbackModule();
            }

            if (_logMappings)
            {
                Logger.Info($"[GameRegistry] Loaded {_modules.Count} game mappings.");
            }
        }

        public bool RegisterRuntime(string gameId, GameContracts.IGameModule module)
        {
            return TryRegisterModule(gameId, module, "runtime");
        }

        private void RegisterSceneModules()
        {
            var sceneBehaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if (sceneBehaviours == null || sceneBehaviours.Length == 0)
            {
                return;
            }

            foreach (var behaviour in sceneBehaviours)
            {
                if (!(behaviour is GameContracts.IGameModule module))
                {
                    continue;
                }

                if (ContainsReference(module))
                {
                    continue;
                }

                TryRegisterModule(module.GameId, module, $"scene:{behaviour.name}");
            }
        }

        private void TryAutoProvisionFallbackModule()
        {
            if (string.IsNullOrWhiteSpace(_autoProvisionFallbackGameId))
            {
                return;
            }

            TryAutoProvisionByGameId(_autoProvisionFallbackGameId, out _);
        }

        private bool TryAutoProvisionByGameId(string gameId, out GameContracts.IGameModule module)
        {
            module = null;

            if (string.IsNullOrWhiteSpace(gameId))
            {
                return false;
            }

            if (_modules.TryGetValue(gameId, out module))
            {
                return true;
            }

            var typeName = ResolveProvisionTypeName(gameId);
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return false;
            }

            var moduleType = ResolveType(typeName);
            if (moduleType == null)
            {
                Logger.Warning($"[GameRegistry] Auto-provision skipped: type '{typeName}' not found.");
                return false;
            }

            if (!typeof(MonoBehaviour).IsAssignableFrom(moduleType) ||
                !typeof(GameContracts.IGameModule).IsAssignableFrom(moduleType))
            {
                Logger.Warning(
                    $"[GameRegistry] Auto-provision skipped: type '{moduleType.FullName}' must inherit MonoBehaviour and implement IGameModule.");
                return false;
            }

            var hostBehaviour = FindSceneModuleBehaviour(moduleType);
            if (hostBehaviour == null)
            {
                var hostName = ResolveProvisionHostName(gameId);
                var host = new GameObject(hostName);
                hostBehaviour = (MonoBehaviour)host.AddComponent(moduleType);
            }

            if (!(hostBehaviour is GameContracts.IGameModule runtimeModule))
            {
                Logger.Warning(
                    $"[GameRegistry] Auto-provision failed: component '{hostBehaviour.GetType().Name}' does not implement IGameModule.");
                return false;
            }

            if (!TryRegisterModule(gameId, runtimeModule, "auto-provision"))
            {
                return _modules.TryGetValue(gameId, out module);
            }

            module = runtimeModule;
            Logger.Info(
                $"[GameRegistry] Auto-provisioned module '{moduleType.Name}' for gameId '{gameId}' on '{hostBehaviour.gameObject.name}'.");
            return true;
        }

        private string ResolveProvisionTypeName(string gameId)
        {
            if (!string.IsNullOrWhiteSpace(_autoProvisionFallbackGameId) &&
                string.Equals(gameId, _autoProvisionFallbackGameId, StringComparison.OrdinalIgnoreCase))
            {
                return _autoProvisionFallbackModuleTypeName;
            }

            if (_runtimeProvisionEntries == null)
            {
                return null;
            }

            for (var i = 0; i < _runtimeProvisionEntries.Count; i++)
            {
                var entry = _runtimeProvisionEntries[i];
                if (entry == null ||
                    string.IsNullOrWhiteSpace(entry.gameId) ||
                    string.IsNullOrWhiteSpace(entry.moduleTypeName))
                {
                    continue;
                }

                if (string.Equals(entry.gameId.Trim(), gameId, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.moduleTypeName.Trim();
                }
            }

            return null;
        }

        private string ResolveProvisionHostName(string gameId)
        {
            if (!string.IsNullOrWhiteSpace(_autoProvisionFallbackGameId) &&
                string.Equals(gameId, _autoProvisionFallbackGameId, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(_autoProvisionHostObjectName)
                    ? "GameModuleRuntime"
                    : _autoProvisionHostObjectName.Trim();
            }

            if (_runtimeProvisionEntries != null)
            {
                for (var i = 0; i < _runtimeProvisionEntries.Count; i++)
                {
                    var entry = _runtimeProvisionEntries[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.gameId))
                    {
                        continue;
                    }

                    if (!string.Equals(entry.gameId.Trim(), gameId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(entry.hostObjectName))
                    {
                        return entry.hostObjectName.Trim();
                    }
                }
            }

            return $"{gameId.Trim()}_Runtime";
        }

        private static Type ResolveType(string fullTypeName)
        {
            if (string.IsNullOrWhiteSpace(fullTypeName))
            {
                return null;
            }

            var resolved = Type.GetType(fullTypeName, false);
            if (resolved != null)
            {
                return resolved;
            }

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                resolved = assemblies[i].GetType(fullTypeName, false);
                if (resolved != null)
                {
                    return resolved;
                }
            }

            return null;
        }

        private static MonoBehaviour FindSceneModuleBehaviour(Type moduleType)
        {
            var sceneBehaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if (sceneBehaviours == null || sceneBehaviours.Length == 0)
            {
                return null;
            }

            for (var i = 0; i < sceneBehaviours.Length; i++)
            {
                var behaviour = sceneBehaviours[i];
                if (behaviour != null && moduleType.IsInstanceOfType(behaviour))
                {
                    return behaviour;
                }
            }

            return null;
        }

        private bool ContainsReference(GameContracts.IGameModule candidate)
        {
            if (candidate == null || _modules.Count == 0)
            {
                return false;
            }

            foreach (var pair in _modules)
            {
                if (ReferenceEquals(pair.Value, candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryRegisterModule(string gameId, GameContracts.IGameModule module, string sourceLabel)
        {
            if (module == null)
            {
                return false;
            }

            var resolvedGameId = string.IsNullOrWhiteSpace(gameId) ? module.GameId : gameId.Trim();
            if (string.IsNullOrWhiteSpace(resolvedGameId))
            {
                Logger.Warning(
                    $"[GameRegistry] Ignored module {module.GetType().Name} from {sourceLabel}: empty gameId.");
                return false;
            }

            if (_modules.ContainsKey(resolvedGameId))
            {
                Logger.Warning($"[GameRegistry] Duplicate gameId ignored ({sourceLabel}): {resolvedGameId}");
                return false;
            }

            _modules[resolvedGameId] = module;
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
