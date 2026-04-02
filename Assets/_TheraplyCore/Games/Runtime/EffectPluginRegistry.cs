using System;
using System.Collections.Generic;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Runtime registry for effect plugins keyed by effect id.
    /// </summary>
    public sealed class EffectPluginRegistry
    {
        private readonly Dictionary<string, IEffectPlugin> _pluginsByEffectId =
            new Dictionary<string, IEffectPlugin>(StringComparer.OrdinalIgnoreCase);
        private readonly List<IEffectPlugin> _plugins = new List<IEffectPlugin>();

        public int Count => _plugins.Count;

        public IReadOnlyList<IEffectPlugin> GetPlugins()
        {
            return _plugins;
        }

        public bool Register(IEffectPlugin plugin, bool replaceExisting = true)
        {
            if (plugin == null || string.IsNullOrWhiteSpace(plugin.EffectId))
            {
                return false;
            }

            var effectId = plugin.EffectId.Trim();
            if (_pluginsByEffectId.TryGetValue(effectId, out var existing))
            {
                if (!replaceExisting)
                {
                    return false;
                }

                _pluginsByEffectId[effectId] = plugin;
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

            _pluginsByEffectId[effectId] = plugin;
            _plugins.Add(plugin);
            return true;
        }

        public bool TryResolve(string effectId, out IEffectPlugin plugin)
        {
            plugin = null;
            if (string.IsNullOrWhiteSpace(effectId))
            {
                return false;
            }

            return _pluginsByEffectId.TryGetValue(effectId.Trim(), out plugin) && plugin != null;
        }

        public void Clear()
        {
            _pluginsByEffectId.Clear();
            _plugins.Clear();
        }
    }
}
