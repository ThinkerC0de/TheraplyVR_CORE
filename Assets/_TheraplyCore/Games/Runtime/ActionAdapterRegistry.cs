using System;
using System.Collections.Generic;
using UnityEngine;
using TheraplyCore.Interactions;
using GameContracts = TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Bridges canonical interaction events into normalized action intents using registered plugins.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActionAdapterRegistry : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private InteractionEventBridge _interactionEventBridge;

        [Header("Registration")]
        [SerializeField] private bool _registerBuiltInPluginsOnAwake = true;

        [Header("Behavior")]
        [SerializeField] private bool _emitIntentEvents = true;
        [SerializeField] private bool _logIntentEvents;

        private readonly ActionPluginRegistry _pluginRegistry = new ActionPluginRegistry();

        public event Action<GameContracts.ActionIntent> IntentReceived;
        public ActionPluginRegistry PluginRegistry => _pluginRegistry;

        private void Awake()
        {
            ResolveDependencies();

            if (_registerBuiltInPluginsOnAwake)
            {
                SessionFlowBuiltInPlugins.RegisterBuiltIns(_pluginRegistry, replaceExisting: true);
            }
        }

        private void OnEnable()
        {
            ResolveDependencies();
            if (_interactionEventBridge != null)
            {
                _interactionEventBridge.EventPublished += HandleCanonicalEvent;
            }
        }

        private void OnDisable()
        {
            if (_interactionEventBridge != null)
            {
                _interactionEventBridge.EventPublished -= HandleCanonicalEvent;
            }
        }

        public bool RegisterPlugin(GameContracts.IActionPlugin plugin, bool replaceExisting = true)
        {
            return _pluginRegistry.Register(plugin, replaceExisting);
        }

        public bool TryResolvePlugin(string actionId, out GameContracts.IActionPlugin plugin)
        {
            return _pluginRegistry.TryResolve(actionId, out plugin);
        }

        private void HandleCanonicalEvent(IReadOnlyDictionary<string, object> payload)
        {
            if (!_emitIntentEvents || payload == null)
            {
                return;
            }

            var plugins = _pluginRegistry.GetPlugins();
            if (plugins == null || plugins.Count == 0)
            {
                return;
            }

            for (var i = 0; i < plugins.Count; i++)
            {
                var plugin = plugins[i];
                if (plugin == null)
                {
                    continue;
                }

                if (!plugin.TryCreateIntent(payload, out var intent) || intent == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(intent.actionId))
                {
                    intent.actionId = plugin.ActionId;
                }

                if (string.IsNullOrWhiteSpace(intent.channelId))
                {
                    intent.channelId = plugin.ChannelId;
                }

                if (_logIntentEvents)
                {
                    Logger.Info(
                        "[ActionAdapterRegistry] intent actionId=" + intent.actionId +
                        " channelId=" + intent.channelId +
                        " targetId=" + intent.targetId);
                }

                try
                {
                    IntentReceived?.Invoke(intent);
                }
                catch (Exception e)
                {
                    Logger.Warning("[ActionAdapterRegistry] Intent callback failed: " + e.Message);
                }
            }
        }

        private void ResolveDependencies()
        {
            if (_interactionEventBridge == null)
            {
                _interactionEventBridge = InteractionEventBridge.Instance;
                if (_interactionEventBridge == null)
                {
                    _interactionEventBridge = FindFirstObjectByType<InteractionEventBridge>();
                }
            }
        }
    }
}
