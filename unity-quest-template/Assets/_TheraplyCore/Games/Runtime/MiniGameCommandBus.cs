using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Network.Connection;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Typed command bus implementation backed by TCP network messaging.
    /// Supports both TCPServerService (Quest host) and TCPConnectionService (client mode).
    /// </summary>
    [DisallowMultipleComponent]
    public class MiniGameCommandBus : MonoBehaviour, ICommandBus
    {
        [Header("Dependencies")]
        [SerializeField] private TCPServerService _tcpServerService;
        [SerializeField] private TCPConnectionService _tcpConnectionService;

        [Header("Debug")]
        [SerializeField] private bool _logInbound = true;
        [SerializeField] private bool _logOutbound = true;
        [SerializeField] private bool _logUnmappedIncoming = false;

        private readonly Dictionary<Type, string> _commandIdByType = new Dictionary<Type, string>();
        private readonly Dictionary<string, List<Delegate>> _handlersByCommandId =
            new Dictionary<string, List<Delegate>>(StringComparer.OrdinalIgnoreCase);

        private void Awake()
        {
            if (_tcpServerService == null)
            {
                _tcpServerService = FindFirstObjectByType<TCPServerService>();
            }

            if (_tcpConnectionService == null)
            {
                _tcpConnectionService = FindFirstObjectByType<TCPConnectionService>();
            }

            RegisterBuiltInCommandMappings();
        }

        private void OnEnable()
        {
            if (_tcpServerService != null)
            {
                _tcpServerService.OnMessageReceived += HandleIncomingMessage;
            }

            if (_tcpConnectionService != null)
            {
                _tcpConnectionService.OnMessageReceived += HandleIncomingMessage;
            }
        }

        private void OnDisable()
        {
            if (_tcpServerService != null)
            {
                _tcpServerService.OnMessageReceived -= HandleIncomingMessage;
            }

            if (_tcpConnectionService != null)
            {
                _tcpConnectionService.OnMessageReceived -= HandleIncomingMessage;
            }
        }

        public void Subscribe<TCommand>(Action<TCommand> handler) where TCommand : IGameCommand
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var commandType = typeof(TCommand);
            var commandId = EnsureCommandIdMapping(commandType);

            if (!_handlersByCommandId.TryGetValue(commandId, out var handlers))
            {
                handlers = new List<Delegate>();
                _handlersByCommandId[commandId] = handlers;
            }

            if (!handlers.Contains(handler))
            {
                handlers.Add(handler);
            }

            if (_logInbound)
            {
                Logger.Debug($"[MiniGameCommandBus] Subscribed {commandType.Name} -> {commandId}");
            }
        }

        public void Unsubscribe<TCommand>(Action<TCommand> handler) where TCommand : IGameCommand
        {
            if (handler == null) return;

            var commandType = typeof(TCommand);
            var commandId = EnsureCommandIdMapping(commandType);

            if (_handlersByCommandId.TryGetValue(commandId, out var handlers))
            {
                handlers.Remove(handler);

                if (handlers.Count == 0)
                {
                    _handlersByCommandId.Remove(commandId);
                }
            }
        }

        public async Task PublishAsync<TCommand>(TCommand command) where TCommand : IGameCommand
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            var commandType = typeof(TCommand);
            var commandId = EnsureCommandIdMapping(commandType);

            EnsureCorrelationId(command);

            var payloadJson = JsonUtility.ToJson(command);
            var message = new NetworkMessage
            {
                messageId = string.IsNullOrWhiteSpace(command.CorrelationId) ? Guid.NewGuid().ToString() : command.CorrelationId,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                commandId = commandId,
                payloadString = payloadJson,
            };

            var sent = await SendMessageInternalAsync(message);
            if (!sent)
            {
                throw new InvalidOperationException($"[MiniGameCommandBus] Failed to send command: {commandId}");
            }

            if (_logOutbound)
            {
                Logger.Debug($"[MiniGameCommandBus] Sent {commandId} ({commandType.Name})");
            }
        }

        public void RegisterCommandId<TCommand>(string commandId) where TCommand : IGameCommand
        {
            SetCommandIdMapping(typeof(TCommand), commandId);
        }

        private async Task<bool> SendMessageInternalAsync(NetworkMessage message)
        {
            if (_tcpServerService != null && _tcpServerService.HasClient)
            {
                return await _tcpServerService.SendMessageAsync(message);
            }

            if (_tcpConnectionService != null && _tcpConnectionService.IsConnected)
            {
                return await _tcpConnectionService.SendMessageAsync(message);
            }

            Logger.Warning("[MiniGameCommandBus] Cannot send command. No active TCP route.");
            return false;
        }

        private void HandleIncomingMessage(NetworkMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.commandId))
            {
                return;
            }

            if (!_handlersByCommandId.TryGetValue(message.commandId, out var handlers) || handlers.Count == 0)
            {
                if (_logUnmappedIncoming)
                {
                    Logger.Debug($"[MiniGameCommandBus] Unmapped incoming command: {message.commandId}");
                }
                return;
            }

            var snapshot = handlers.ToArray();
            foreach (var handler in snapshot)
            {
                var parameterInfo = handler.Method.GetParameters();
                if (parameterInfo.Length != 1)
                {
                    continue;
                }

                var commandType = parameterInfo[0].ParameterType;
                var typedCommand = DeserializeCommand(commandType, message);
                if (typedCommand == null)
                {
                    continue;
                }

                try
                {
                    handler.DynamicInvoke(typedCommand);
                }
                catch (Exception e)
                {
                    Logger.Error($"[MiniGameCommandBus] Handler failed for {message.commandId}: {e.Message}", e);
                }
            }

            if (_logInbound)
            {
                Logger.Debug($"[MiniGameCommandBus] Received {message.commandId} (handlers={snapshot.Length})");
            }
        }

        private object DeserializeCommand(Type commandType, NetworkMessage message)
        {
            try
            {
                object instance;
                var payloadJson = message.payloadString;

                if (string.IsNullOrWhiteSpace(payloadJson))
                {
                    instance = Activator.CreateInstance(commandType);
                }
                else
                {
                    instance = JsonUtility.FromJson(payloadJson, commandType) ?? Activator.CreateInstance(commandType);
                }

                TryPopulateCorrelationId(instance, message.messageId);
                return instance;
            }
            catch (Exception e)
            {
                Logger.Error($"[MiniGameCommandBus] Deserialize failed for {commandType.Name}: {e.Message}", e);
                return null;
            }
        }

        private static void TryPopulateCorrelationId(object commandInstance, string fallbackMessageId)
        {
            if (commandInstance == null) return;

            var correlationField = commandInstance.GetType().GetField("correlationId");
            if (correlationField == null || correlationField.FieldType != typeof(string))
            {
                return;
            }

            var current = correlationField.GetValue(commandInstance) as string;
            if (!string.IsNullOrWhiteSpace(current))
            {
                return;
            }

            correlationField.SetValue(commandInstance,
                string.IsNullOrWhiteSpace(fallbackMessageId) ? Guid.NewGuid().ToString() : fallbackMessageId);
        }

        private static void EnsureCorrelationId(IGameCommand command)
        {
            if (command == null) return;
            if (!string.IsNullOrWhiteSpace(command.CorrelationId)) return;

            var correlationField = command.GetType().GetField("correlationId");
            if (correlationField == null || correlationField.FieldType != typeof(string))
            {
                return;
            }

            correlationField.SetValue(command, Guid.NewGuid().ToString());
        }

        private void RegisterBuiltInCommandMappings()
        {
            SetCommandIdMapping(typeof(StartGameCommand), MiniGameCommandIds.StartGame);
            SetCommandIdMapping(typeof(PauseGameCommand), MiniGameCommandIds.PauseGame);
            SetCommandIdMapping(typeof(ResumeGameCommand), MiniGameCommandIds.ResumeGame);
            SetCommandIdMapping(typeof(StopGameCommand), MiniGameCommandIds.StopGame);
        }

        private string EnsureCommandIdMapping(Type commandType)
        {
            if (_commandIdByType.TryGetValue(commandType, out var existing))
            {
                return existing;
            }

            if (commandType.IsGenericType &&
                commandType.GetGenericTypeDefinition() == typeof(UpdateConfigCommand<>))
            {
                SetCommandIdMapping(commandType, MiniGameCommandIds.UpdateConfig);
                return MiniGameCommandIds.UpdateConfig;
            }

            var generated = ToWireCommandId(commandType.Name);
            SetCommandIdMapping(commandType, generated);
            return generated;
        }

        private void SetCommandIdMapping(Type commandType, string commandId)
        {
            if (commandType == null || string.IsNullOrWhiteSpace(commandId))
            {
                return;
            }

            _commandIdByType[commandType] = commandId.Trim();
        }

        private static string ToWireCommandId(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return "UNKNOWN_COMMAND";
            }

            var trimmed = typeName.EndsWith("Command", true, CultureInfo.InvariantCulture)
                ? typeName.Substring(0, typeName.Length - "Command".Length)
                : typeName;

            var builder = new StringBuilder(trimmed.Length + 8);
            for (var i = 0; i < trimmed.Length; i++)
            {
                var ch = trimmed[i];
                if (char.IsUpper(ch) && i > 0)
                {
                    builder.Append('_');
                }
                builder.Append(char.ToUpperInvariant(ch));
            }

            return builder.ToString();
        }
    }
}
