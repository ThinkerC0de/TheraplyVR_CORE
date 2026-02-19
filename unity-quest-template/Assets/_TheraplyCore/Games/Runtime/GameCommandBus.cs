using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Network.Connection;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Typed command bus implementation backed by TCP network messaging.
    /// Uses TCPServerService (Quest acts as TCP server).
    /// </summary>
    [DisallowMultipleComponent]
    public class GameCommandBus : MonoBehaviour, GameContracts.ICommandBus
    {
        [Header("Dependencies")]
        [SerializeField] private TCPServerService _tcpServerService;
        [SerializeField] private GameSessionContext _sessionContext;

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

            if (_sessionContext == null)
            {
                _sessionContext = FindFirstObjectByType<GameSessionContext>();
            }

            RegisterBuiltInCommandMappings();
        }

        private void OnEnable()
        {
            if (_tcpServerService != null)
            {
                _tcpServerService.OnMessageReceived += HandleIncomingMessage;
            }
        }

        private void OnDisable()
        {
            if (_tcpServerService != null)
            {
                _tcpServerService.OnMessageReceived -= HandleIncomingMessage;
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
                Logger.Debug($"[GameCommandBus] Subscribed {commandType.Name} -> {commandId}");
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
                throw new InvalidOperationException($"[GameCommandBus] Failed to send command: {commandId}");
            }

            if (_logOutbound)
            {
                Logger.Debug($"[GameCommandBus] Sent {commandId} ({commandType.Name})");
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

            Logger.Warning("[GameCommandBus] Cannot send command. No active TCP route.");
            return false;
        }

        private void HandleIncomingMessage(NetworkMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.commandId))
            {
                return;
            }

            var isCritical = CriticalCommandIds.IsCritical(message.commandId);

            if (!_handlersByCommandId.TryGetValue(message.commandId, out var handlers) || handlers.Count == 0)
            {
                if (_logUnmappedIncoming)
                {
                    Logger.Debug($"[GameCommandBus] Unmapped incoming command: {message.commandId}");
                }

                if (isCritical)
                {
                    _ = SendCriticalAckAsync(message, CommandAckStatus.Nack, AckReasonCodes.NoHandler, string.Empty);
                }
                return;
            }

            var snapshot = handlers.ToArray();
            string envelopeSessionId = string.Empty;
            foreach (var handler in snapshot)
            {
                var parameterInfo = handler.Method.GetParameters();
                if (parameterInfo.Length != 1)
                {
                    continue;
                }

                if (!TryDeserializeCommand(
                        parameterInfo[0].ParameterType,
                        message,
                        out var typedCommand,
                        out var parsedSessionId,
                        out var rejectReasonCode))
                {
                    if (isCritical)
                    {
                        _ = SendCriticalAckAsync(
                            message,
                            CommandAckStatus.Nack,
                            string.IsNullOrWhiteSpace(rejectReasonCode) ? AckReasonCodes.DeserializeFailed : rejectReasonCode,
                            parsedSessionId);
                        return;
                    }

                    continue;
                }

                if (!string.IsNullOrWhiteSpace(parsedSessionId))
                {
                    envelopeSessionId = parsedSessionId;
                }

                try
                {
                    handler.DynamicInvoke(typedCommand);
                }
                catch (Exception e)
                {
                    Logger.Error($"[GameCommandBus] Handler failed for {message.commandId}: {e.Message}", e);
                    if (isCritical)
                    {
                        _ = SendCriticalAckAsync(
                            message,
                            CommandAckStatus.Nack,
                            AckReasonCodes.HandlerException,
                            envelopeSessionId);
                    }
                    return;
                }
            }

            if (_logInbound)
            {
                Logger.Debug($"[GameCommandBus] Received {message.commandId} (handlers={snapshot.Length})");
            }

            if (isCritical)
            {
                _ = SendCriticalAckAsync(message, CommandAckStatus.Ack, AckReasonCodes.Ok, envelopeSessionId);
            }
        }

        private bool TryDeserializeCommand(
            Type commandType,
            NetworkMessage message,
            out object typedCommand,
            out string sessionId,
            out string rejectReasonCode)
        {
            typedCommand = null;
            sessionId = string.Empty;
            rejectReasonCode = string.Empty;

            try
            {
                if (!TryResolvePayloadJson(message, out var payloadJson, out sessionId, out rejectReasonCode))
                {
                    return false;
                }

                object instance;
                if (string.IsNullOrWhiteSpace(payloadJson))
                {
                    instance = Activator.CreateInstance(commandType);
                }
                else
                {
                    instance = JsonUtility.FromJson(payloadJson, commandType) ?? Activator.CreateInstance(commandType);
                }

                TryPopulateCorrelationId(instance, message.messageId);
                typedCommand = instance;
                return true;
            }
            catch (Exception e)
            {
                Logger.Error($"[GameCommandBus] Deserialize failed for {commandType.Name}: {e.Message}", e);
                rejectReasonCode = AckReasonCodes.DeserializeFailed;
                return false;
            }
        }

        private bool TryResolvePayloadJson(
            NetworkMessage message,
            out string payloadJson,
            out string sessionId,
            out string rejectReasonCode)
        {
            payloadJson = message.payloadString;
            sessionId = string.Empty;
            rejectReasonCode = string.Empty;

            if (!CriticalCommandIds.IsCritical(message.commandId))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                Logger.Warning(
                    $"[GameCommandBus] Critical command without envelope payload: {message.commandId} (msgId={message.messageId})");
                return true;
            }

            CriticalCommandEnvelope envelope;
            try
            {
                envelope = JsonUtility.FromJson<CriticalCommandEnvelope>(payloadJson);
            }
            catch (Exception e)
            {
                Logger.Warning($"[GameCommandBus] Critical envelope parse failed for {message.commandId}: {e.Message}");
                return true; // allow legacy payload format
            }

            if (!LooksLikeCriticalEnvelope(envelope))
            {
                return true; // allow legacy payload format
            }

            if (!ValidateCriticalEnvelope(message, envelope, out rejectReasonCode))
            {
                return false;
            }

            sessionId = envelope.sessionId ?? string.Empty;
            payloadJson = envelope.payloadJson;
            return true;
        }

        private static bool LooksLikeCriticalEnvelope(CriticalCommandEnvelope envelope)
        {
            return envelope != null &&
                   !string.IsNullOrWhiteSpace(envelope.messageId) &&
                   !string.IsNullOrWhiteSpace(envelope.sessionId) &&
                   !string.IsNullOrWhiteSpace(envelope.commandId) &&
                   !string.IsNullOrWhiteSpace(envelope.issuedAtUtc);
        }

        private bool ValidateCriticalEnvelope(
            NetworkMessage message,
            CriticalCommandEnvelope envelope,
            out string rejectReasonCode)
        {
            rejectReasonCode = string.Empty;

            if (!string.Equals(message.commandId, envelope.commandId, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warning(
                    $"[GameCommandBus] Rejecting critical command. commandId mismatch wire={message.commandId} envelope={envelope.commandId}");
                rejectReasonCode = AckReasonCodes.EnvelopeCommandIdMismatch;
                return false;
            }

            if (!string.Equals(message.messageId, envelope.messageId, StringComparison.Ordinal))
            {
                Logger.Warning(
                    $"[GameCommandBus] Rejecting critical command. messageId mismatch wire={message.messageId} envelope={envelope.messageId}");
                rejectReasonCode = AckReasonCodes.EnvelopeMessageIdMismatch;
                return false;
            }

            if (!TryParseUtc(envelope.issuedAtUtc, out _))
            {
                Logger.Warning($"[GameCommandBus] Rejecting critical command. Invalid issuedAtUtc: {envelope.issuedAtUtc}");
                rejectReasonCode = AckReasonCodes.EnvelopeInvalidIssuedAt;
                return false;
            }

            if (!string.IsNullOrWhiteSpace(envelope.expiresAtUtc))
            {
                if (!TryParseUtc(envelope.expiresAtUtc, out var expiresAtUtc))
                {
                    Logger.Warning($"[GameCommandBus] Rejecting critical command. Invalid expiresAtUtc: {envelope.expiresAtUtc}");
                    rejectReasonCode = AckReasonCodes.EnvelopeInvalidExpiresAt;
                    return false;
                }

                if (DateTime.UtcNow > expiresAtUtc)
                {
                    Logger.Warning(
                        $"[GameCommandBus] Rejecting expired critical command: {message.commandId} (expiredAt={expiresAtUtc:O})");
                    rejectReasonCode = AckReasonCodes.EnvelopeExpired;
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(envelope.payloadJson))
            {
                envelope.payloadJson = "{}";
            }

            if (!ValidateSessionLock(message.commandId, envelope.sessionId, out rejectReasonCode))
            {
                return false;
            }

            return true;
        }

        private bool ValidateSessionLock(
            string commandId,
            string envelopeSessionId,
            out string rejectReasonCode)
        {
            rejectReasonCode = string.Empty;

            if (_sessionContext == null)
            {
                _sessionContext = FindFirstObjectByType<GameSessionContext>();
            }

            if (_sessionContext == null)
            {
                return true;
            }

            var activeSessionId = _sessionContext.SessionId;
            if (string.IsNullOrWhiteSpace(activeSessionId) ||
                string.IsNullOrWhiteSpace(envelopeSessionId) ||
                string.Equals(activeSessionId, envelopeSessionId, StringComparison.Ordinal))
            {
                return true;
            }

            var activeState = _sessionContext.SessionState;

            if (IsTerminalState(activeState))
            {
                return true;
            }

            // Bootstrap case: runtime already has CREATED session; allow START_GAME to attach.
            if (activeState == GameContracts.SessionLifecycleState.CREATED &&
                string.Equals(commandId, GameCommandIds.StartGame, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            Logger.Warning(
                $"[GameCommandBus] Rejecting critical command due active session lock. activeSession={activeSessionId}, state={activeState}, incomingSession={envelopeSessionId}, command={commandId}");
            rejectReasonCode = AckReasonCodes.SessionLockConflict;
            return false;
        }

        private static bool IsTerminalState(GameContracts.SessionLifecycleState state)
        {
            return state == GameContracts.SessionLifecycleState.COMPLETED ||
                   state == GameContracts.SessionLifecycleState.ABORTED_BY_THERAPIST ||
                   state == GameContracts.SessionLifecycleState.FAILED_TECHNICAL;
        }

        private async Task SendCriticalAckAsync(
            NetworkMessage requestMessage,
            string status,
            string reasonCode,
            string sessionId)
        {
            var ackPayload = new CriticalCommandAckPayload
            {
                messageId = requestMessage.messageId ?? string.Empty,
                commandId = requestMessage.commandId ?? string.Empty,
                sessionId = sessionId ?? string.Empty,
                status = string.IsNullOrWhiteSpace(status) ? CommandAckStatus.Nack : status,
                reasonCode = string.IsNullOrWhiteSpace(reasonCode) ? AckReasonCodes.Unspecified : reasonCode,
                processedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            };

            var ackMessage = new NetworkMessage
            {
                messageId = Guid.NewGuid().ToString(),
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                commandId = GameCommandIds.CommandAck,
                payloadString = JsonUtility.ToJson(ackPayload),
            };

            var sent = await SendMessageInternalAsync(ackMessage);
            if (!sent)
            {
                Logger.Warning(
                    $"[GameCommandBus] Failed to send {GameCommandIds.CommandAck} for {requestMessage.commandId} ({requestMessage.messageId})");
            }
        }

        private static bool TryParseUtc(string value, out DateTime parsedUtc)
        {
            var ok = DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsedUtc);

            if (ok && parsedUtc.Kind != DateTimeKind.Utc)
            {
                parsedUtc = parsedUtc.ToUniversalTime();
            }

            return ok;
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
            SetCommandIdMapping(typeof(StartGameCommand), GameCommandIds.StartGame);
            SetCommandIdMapping(typeof(PauseGameCommand), GameCommandIds.PauseGame);
            SetCommandIdMapping(typeof(ResumeGameCommand), GameCommandIds.ResumeGame);
            SetCommandIdMapping(typeof(StopGameCommand), GameCommandIds.StopGame);
            SetCommandIdMapping(typeof(EndSessionCommand), GameCommandIds.EndSession);
            SetCommandIdMapping(typeof(CriticalCommandAckPayload), GameCommandIds.CommandAck);
            SetCommandIdMapping(typeof(SessionStateUpdateCommand), GameCommandIds.SessionStateUpdate);
            SetCommandIdMapping(typeof(RuntimeStatusUpdateCommand), GameCommandIds.RuntimeStatusUpdate);
            SetCommandIdMapping(typeof(SessionWatchdogHeartbeatCommand), GameCommandIds.SessionWatchdogHeartbeat);
            SetCommandIdMapping(typeof(ManualResyncCommand), GameCommandIds.ManualResync);
            SetCommandIdMapping(typeof(ManualResyncReportCommand), GameCommandIds.ManualResyncReport);
            SetCommandIdMapping(typeof(SyncCatalogCommand), GameCommandIds.SyncCatalog);
            SetCommandIdMapping(typeof(InstallGameCommand), GameCommandIds.InstallGame);
            SetCommandIdMapping(typeof(UninstallGameCommand), GameCommandIds.UninstallGame);
            SetCommandIdMapping(typeof(GameInstallStatusCommand), GameCommandIds.GameInstallStatus);
        }

        private static class AckReasonCodes
        {
            public const string Ok = "OK";
            public const string NoHandler = "NO_HANDLER";
            public const string DeserializeFailed = "DESERIALIZE_FAILED";
            public const string HandlerException = "HANDLER_EXCEPTION";
            public const string EnvelopeCommandIdMismatch = "ENVELOPE_COMMAND_ID_MISMATCH";
            public const string EnvelopeMessageIdMismatch = "ENVELOPE_MESSAGE_ID_MISMATCH";
            public const string EnvelopeInvalidIssuedAt = "ENVELOPE_INVALID_ISSUED_AT";
            public const string EnvelopeInvalidExpiresAt = "ENVELOPE_INVALID_EXPIRES_AT";
            public const string EnvelopeExpired = "ENVELOPE_EXPIRED";
            public const string SessionLockConflict = "SESSION_LOCK_CONFLICT";
            public const string Unspecified = "UNSPECIFIED";
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
                SetCommandIdMapping(commandType, GameCommandIds.UpdateConfig);
                return GameCommandIds.UpdateConfig;
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
