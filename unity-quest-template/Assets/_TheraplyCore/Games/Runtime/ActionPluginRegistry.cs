using System;
using System.Collections.Generic;
using GameContracts = TheraplyCore.Games.Contracts;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Runtime registry for action plugins keyed by action id.
    /// </summary>
    public sealed class ActionPluginRegistry
    {
        private readonly Dictionary<string, GameContracts.IActionPlugin> _pluginsByActionId =
            new Dictionary<string, GameContracts.IActionPlugin>(StringComparer.OrdinalIgnoreCase);
        private readonly List<GameContracts.IActionPlugin> _plugins = new List<GameContracts.IActionPlugin>();

        public int Count => _plugins.Count;

        public IReadOnlyList<GameContracts.IActionPlugin> GetPlugins()
        {
            return _plugins;
        }

        public bool Register(GameContracts.IActionPlugin plugin, bool replaceExisting = true)
        {
            if (plugin == null || string.IsNullOrWhiteSpace(plugin.ActionId))
            {
                return false;
            }

            var actionId = plugin.ActionId.Trim();
            if (_pluginsByActionId.TryGetValue(actionId, out var existing))
            {
                if (!replaceExisting)
                {
                    return false;
                }

                _pluginsByActionId[actionId] = plugin;
                for (var i = 0; i < _plugins.Count; i++)
                {
                    if (ReferenceEquals(_plugins[i], existing))
                    {
                        _plugins[i] = plugin;
                        return true;
                    }
                }

                _plugins.Add(plugin);
                return true;
            }

            _pluginsByActionId[actionId] = plugin;
            _plugins.Add(plugin);
            return true;
        }

        public bool Unregister(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return false;
            }

            var normalized = actionId.Trim();
            if (!_pluginsByActionId.TryGetValue(normalized, out var existing))
            {
                return false;
            }

            _pluginsByActionId.Remove(normalized);
            for (var i = _plugins.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_plugins[i], existing))
                {
                    _plugins.RemoveAt(i);
                }
            }

            return true;
        }

        public bool TryResolve(string actionId, out GameContracts.IActionPlugin plugin)
        {
            plugin = null;
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return false;
            }

            return _pluginsByActionId.TryGetValue(actionId.Trim(), out plugin) && plugin != null;
        }

        public void Clear()
        {
            _pluginsByActionId.Clear();
            _plugins.Clear();
        }
    }
}
