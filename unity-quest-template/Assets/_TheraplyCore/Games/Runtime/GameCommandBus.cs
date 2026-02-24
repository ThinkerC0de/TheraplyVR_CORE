using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        private const string OwnershipLockHotfixRevision = "ownership-lock-hotfix-2026-02-23-r2";

        [Header("Dependencies")]
        [SerializeField] private TCPServerService _tcpServerService;
        [SerializeField] private GameSessionContext _sessionContext;

        [Header("Debug")]
        [SerializeField] private bool _logInbound = true;
        [SerializeField] private bool _logOutbound = true;
        [SerializeField] private bool _logUnmappedIncoming = false;

        [Header("Critical Command Durability")]
        [SerializeField] private bool _enableCommandJournal = true;
        [SerializeField] private string _commandJournalFolder = "TheraplyRuntime";
        [SerializeField] private string _commandJournalFileName = "critical_commands.ndjson";
        [SerializeField] private int _commandJournalMaxEntriesInMemory = 4096;
        [SerializeField] private int _commandJournalMaxPendingWrites = 1024;
        [SerializeField] private bool _logCommandJournalVerbose = false;

        private readonly Dictionary<Type, string> _commandIdByType = new Dictionary<Type, string>();
        private readonly Dictionary<string, List<Delegate>> _handlersByCommandId =
            new Dictionary<string, List<Delegate>>(StringComparer.OrdinalIgnoreCase);
        private CommandJournal _commandJournal;
        private IdempotencyGuard _idempotencyGuard;

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
            InitializeCommandJournal();
            Logger.Info($"[GameCommandBus] Ownership lock revision: {OwnershipLockHotfixRevision}");
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

        private void OnDestroy()
        {
            DisposeCommandJournal();
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
            var initialSessionId = isCritical ? ResolveCriticalSessionIdHint(message.payloadString) : string.Empty;
            if (isCritical &&
                _idempotencyGuard != null &&
                !_idempotencyGuard.TryBeginCriticalCommand(
                    message.messageId,
                    message.commandId,
                    initialSessionId,
                    out var idempotencyDecision))
            {
                if (idempotencyDecision.shouldAck)
                {
                    _ = SendCriticalAckAsync(
                        message,
                        idempotencyDecision.ackStatus,
                        idempotencyDecision.reasonCode,
                        idempotencyDecision.sessionId);
                }

                return;
            }

            if (!_handlersByCommandId.TryGetValue(message.commandId, out var handlers) || handlers.Count == 0)
            {
                if (_logUnmappedIncoming)
                {
                    Logger.Debug($"[GameCommandBus] Unmapped incoming command: {message.commandId}");
                }

                if (isCritical)
                {
                    CompleteCriticalCommand(
                        message,
                        initialSessionId,
                        CommandJournalStatus.Rejected,
                        AckReasonCodes.NoHandler);
                    _ = SendCriticalAckAsync(message, CommandAckStatus.Nack, AckReasonCodes.NoHandler, initialSessionId);
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
                        var rejectCode = string.IsNullOrWhiteSpace(rejectReasonCode)
                            ? AckReasonCodes.DeserializeFailed
                            : rejectReasonCode;
                        var rejectSessionId = string.IsNullOrWhiteSpace(parsedSessionId) ? initialSessionId : parsedSessionId;
                        CompleteCriticalCommand(
                            message,
                            rejectSessionId,
                            CommandJournalStatus.Rejected,
                            rejectCode);
                        _ = SendCriticalAckAsync(
                            message,
                            CommandAckStatus.Nack,
                            rejectCode,
                            rejectSessionId);
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
                        var failedSessionId = string.IsNullOrWhiteSpace(envelopeSessionId) ? initialSessionId : envelopeSessionId;
                        CompleteCriticalCommand(
                            message,
                            failedSessionId,
                            CommandJournalStatus.Failed,
                            AckReasonCodes.HandlerException);
                        _ = SendCriticalAckAsync(
                            message,
                            CommandAckStatus.Nack,
                            AckReasonCodes.HandlerException,
                            failedSessionId);
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
                var finalSessionId = string.IsNullOrWhiteSpace(envelopeSessionId) ? initialSessionId : envelopeSessionId;
                CompleteCriticalCommand(
                    message,
                    finalSessionId,
                    CommandJournalStatus.Applied,
                    AckReasonCodes.Ok);
                _ = SendCriticalAckAsync(message, CommandAckStatus.Ack, AckReasonCodes.Ok, finalSessionId);
            }
        }

        private void InitializeCommandJournal()
        {
            if (!_enableCommandJournal)
            {
                _idempotencyGuard = null;
                _commandJournal = null;
                return;
            }

            if (_commandJournal != null)
            {
                return;
            }

            try
            {
                var folder = string.IsNullOrWhiteSpace(_commandJournalFolder)
                    ? "TheraplyRuntime"
                    : _commandJournalFolder.Trim();
                var fileName = string.IsNullOrWhiteSpace(_commandJournalFileName)
                    ? "critical_commands.ndjson"
                    : _commandJournalFileName.Trim();
                var directory = Path.Combine(Application.persistentDataPath, folder);
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, fileName);

                _commandJournal = new CommandJournal(
                    path,
                    _commandJournalMaxEntriesInMemory,
                    _commandJournalMaxPendingWrites,
                    _logCommandJournalVerbose);
                _idempotencyGuard = new IdempotencyGuard(_commandJournal, _logCommandJournalVerbose);

                if (_logCommandJournalVerbose)
                {
                    Logger.Info($"[GameCommandBus] Command journal enabled at {path}");
                }
            }
            catch (Exception exception)
            {
                _commandJournal = null;
                _idempotencyGuard = null;
                Logger.Warning($"[GameCommandBus] Command journal unavailable: {exception.Message}");
            }
        }

        private void DisposeCommandJournal()
        {
            var journal = _commandJournal;
            _commandJournal = null;
            _idempotencyGuard = null;

            if (journal == null)
            {
                return;
            }

            try
            {
                journal.Flush(TimeSpan.FromMilliseconds(250));
            }
            catch (Exception)
            {
                // Best-effort flush; dispose still proceeds.
            }

            try
            {
                journal.Dispose();
            }
            catch (Exception exception)
            {
                Logger.Warning($"[GameCommandBus] Command journal dispose failed: {exception.Message}");
            }
        }

        private void CompleteCriticalCommand(
            NetworkMessage message,
            string sessionId,
            string status,
            string reasonCode)
        {
            if (_idempotencyGuard == null ||
                !CriticalCommandIds.IsCritical(message.commandId))
            {
                return;
            }

            _idempotencyGuard.CompleteCriticalCommand(
                message.messageId,
                message.commandId,
                sessionId,
                status,
                reasonCode);
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

        private static string ResolveCriticalSessionIdHint(string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return string.Empty;
            }

            try
            {
                var envelope = JsonUtility.FromJson<CriticalCommandEnvelope>(payloadJson);
                return envelope == null || string.IsNullOrWhiteSpace(envelope.sessionId)
                    ? string.Empty
                    : envelope.sessionId.Trim();
            }
            catch (Exception)
            {
                return string.Empty;
            }
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

            if (!ValidateSessionLock(
                    message.commandId,
                    envelope.sessionId,
                    envelope.payloadJson,
                    out rejectReasonCode))
            {
                return false;
            }

            return true;
        }

        private bool ValidateSessionLock(
            string commandId,
            string envelopeSessionId,
            string payloadJson,
            out string rejectReasonCode)
        {
            rejectReasonCode = string.Empty;

            if (!ValidateOwnershipLock(commandId, envelopeSessionId, payloadJson, out rejectReasonCode))
            {
                return false;
            }

            if (string.Equals(commandId, GameCommandIds.SessionAttach, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(commandId, GameCommandIds.EndSession, StringComparison.OrdinalIgnoreCase))
            {
                if (_sessionContext == null)
                {
                    _sessionContext = FindFirstObjectByType<GameSessionContext>();
                }

                var activeSessionIdForEnd = _sessionContext == null ? string.Empty : _sessionContext.SessionId ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(activeSessionIdForEnd) &&
                    !string.IsNullOrWhiteSpace(envelopeSessionId) &&
                    !string.Equals(activeSessionIdForEnd, envelopeSessionId, StringComparison.Ordinal))
                {
                    Logger.Warning(
                        $"[GameCommandBus] Allowing END_SESSION despite session mismatch. activeSession={activeSessionIdForEnd}, incomingSession={envelopeSessionId}");
                }

                return true;
            }

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

        private bool ValidateOwnershipLock(
            string commandId,
            string envelopeSessionId,
            string payloadJson,
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

            var activeSessionId = (_sessionContext.SessionId ?? string.Empty).Trim();
            var activeState = _sessionContext.SessionState;
            if (string.IsNullOrWhiteSpace(activeSessionId) || IsTerminalState(activeState))
            {
                return true;
            }

            ResolveIncomingOwnership(
                payloadJson,
                out var incomingTherapistId,
                out var incomingStudentId,
                out var incomingOwnerKeyRaw,
                out var incomingSessionKeyRaw,
                out var incomingReasonCode);

            if (string.IsNullOrWhiteSpace(incomingTherapistId) || string.IsNullOrWhiteSpace(incomingStudentId))
            {
                Logger.Warning(
                    $"[GameCommandBus] Rejecting critical command due missing ownership metadata. command={commandId}, session={envelopeSessionId}");
                rejectReasonCode = AckReasonCodes.SessionOwnershipMissing;
                return false;
            }

            var activeTherapistId = (_sessionContext.TherapistId ?? string.Empty).Trim();
            var activeStudentId = (_sessionContext.PatientId ?? string.Empty).Trim();
            var activeOwnerKey = BuildOwnerKey(activeTherapistId, activeStudentId);
            var activeOwnershipIsBootstrapPlaceholder = IsBootstrapOwnershipPlaceholder(activeTherapistId, activeStudentId, activeState);
            if (string.IsNullOrWhiteSpace(activeOwnerKey))
            {
                return true;
            }

            var incomingOwnerKey = string.IsNullOrWhiteSpace(incomingOwnerKeyRaw)
                ? BuildOwnerKey(incomingTherapistId, incomingStudentId)
                : incomingOwnerKeyRaw.Trim();
            if (string.IsNullOrWhiteSpace(incomingOwnerKey))
            {
                Logger.Warning(
                    $"[GameCommandBus] Rejecting critical command due empty ownership key. command={commandId}, session={envelopeSessionId}");
                rejectReasonCode = AckReasonCodes.SessionOwnershipMissing;
                return false;
            }

            if (!string.Equals(activeOwnerKey, incomingOwnerKey, StringComparison.Ordinal))
            {
                if (activeOwnershipIsBootstrapPlaceholder &&
                    (string.Equals(commandId, GameCommandIds.SessionAttach, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(commandId, GameCommandIds.EndSession, StringComparison.OrdinalIgnoreCase)))
                {
                    Logger.Warning(
                        $"[GameCommandBus] Allowing {commandId} to replace bootstrap ownership placeholder. activeOwner={activeOwnerKey}, incomingOwner={incomingOwnerKey}, state={activeState}");
                    return true;
                }

                if (string.IsNullOrWhiteSpace(incomingTherapistId))
                {
                    incomingTherapistId = ExtractTherapistIdFromOwnerKey(incomingOwnerKey);
                }

                if (string.Equals(commandId, GameCommandIds.EndSession, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(activeTherapistId) &&
                    !string.IsNullOrWhiteSpace(incomingTherapistId) &&
                    string.Equals(activeTherapistId, incomingTherapistId, StringComparison.Ordinal))
                {
                    Logger.Warning(
                        $"[GameCommandBus] Allowing END_SESSION despite ownership mismatch for same therapist. activeOwner={activeOwnerKey}, incomingOwner={incomingOwnerKey}, reason={incomingReasonCode}");
                    return true;
                }

                Logger.Warning(
                    $"[GameCommandBus] Rejecting critical command due ownership mismatch. command={commandId}, session={envelopeSessionId}, activeOwner={activeOwnerKey}, incomingOwner={incomingOwnerKey}");
                rejectReasonCode = AckReasonCodes.SessionOwnershipConflict;
                return false;
            }

            var activeSessionKey = BuildSessionKey(activeOwnerKey, activeSessionId);
            var incomingSessionKey = string.IsNullOrWhiteSpace(incomingSessionKeyRaw)
                ? BuildSessionKey(incomingOwnerKey, envelopeSessionId)
                : incomingSessionKeyRaw.Trim();

            if (string.IsNullOrWhiteSpace(activeSessionKey) || string.IsNullOrWhiteSpace(incomingSessionKey))
            {
                return true;
            }

            if (string.Equals(activeSessionKey, incomingSessionKey, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(commandId, GameCommandIds.SessionAttach, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warning(
                    $"[GameCommandBus] Allowing SESSION_ATTACH with ownership match despite session key mismatch. activeSessionKey={activeSessionKey}, incomingSessionKey={incomingSessionKey}");
                return true;
            }

            if (string.Equals(commandId, GameCommandIds.EndSession, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warning(
                    $"[GameCommandBus] Allowing END_SESSION with ownership match despite session key mismatch. activeSessionKey={activeSessionKey}, incomingSessionKey={incomingSessionKey}");
                return true;
            }

            if (activeState == GameContracts.SessionLifecycleState.CREATED &&
                string.Equals(commandId, GameCommandIds.StartGame, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            Logger.Warning(
                $"[GameCommandBus] Rejecting critical command due session key mismatch. command={commandId}, activeSessionKey={activeSessionKey}, incomingSessionKey={incomingSessionKey}");
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

        private static void ResolveIncomingOwnership(
            string payloadJson,
            out string therapistId,
            out string studentId,
            out string ownerKey,
            out string sessionKey,
            out string reasonCode)
        {
            therapistId = string.Empty;
            studentId = string.Empty;
            ownerKey = string.Empty;
            sessionKey = string.Empty;
            reasonCode = string.Empty;

            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return;
            }

            try
            {
                var probe = JsonUtility.FromJson<CriticalCommandOwnershipProbe>(payloadJson);
                if (probe == null)
                {
                    return;
                }

                therapistId = (probe.therapistId ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(probe.studentId))
                {
                    studentId = probe.studentId.Trim();
                }
                else
                {
                    studentId = (probe.patientId ?? string.Empty).Trim();
                }

                ownerKey = (probe.ownerKey ?? string.Empty).Trim();
                sessionKey = (probe.sessionKey ?? string.Empty).Trim();
                reasonCode = (probe.reasonCode ?? string.Empty).Trim();
            }
            catch (Exception)
            {
                // Keep empty fields so caller can decide whether to reject or fallback.
            }
        }

        private static string BuildOwnerKey(string therapistId, string studentId)
        {
            var normalizedTherapistId = string.IsNullOrWhiteSpace(therapistId) ? string.Empty : therapistId.Trim();
            var normalizedStudentId = string.IsNullOrWhiteSpace(studentId) ? string.Empty : studentId.Trim();
            if (string.IsNullOrWhiteSpace(normalizedTherapistId) || string.IsNullOrWhiteSpace(normalizedStudentId))
            {
                return string.Empty;
            }

            return $"{normalizedTherapistId}|{normalizedStudentId}";
        }

        private static bool IsBootstrapOwnershipPlaceholder(
            string therapistId,
            string studentId,
            GameContracts.SessionLifecycleState activeState)
        {
            if (activeState != GameContracts.SessionLifecycleState.CREATED)
            {
                return false;
            }

            return IsPlaceholderIdentityToken(therapistId) && IsPlaceholderIdentityToken(studentId);
        }

        private static bool IsPlaceholderIdentityToken(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return true;
            }

            if (string.Equals(normalized, "unknown_therapist", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "unknown_patient", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "unknown_student", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return normalized.StartsWith("unknown_", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractTherapistIdFromOwnerKey(string ownerKey)
        {
            var normalizedOwnerKey = string.IsNullOrWhiteSpace(ownerKey) ? string.Empty : ownerKey.Trim();
            if (string.IsNullOrWhiteSpace(normalizedOwnerKey))
            {
                return string.Empty;
            }

            var separatorIndex = normalizedOwnerKey.IndexOf('|');
            if (separatorIndex <= 0)
            {
                return string.Empty;
            }

            return normalizedOwnerKey.Substring(0, separatorIndex).Trim();
        }

        private static string BuildSessionKey(string ownerKey, string sessionId)
        {
            var normalizedOwnerKey = string.IsNullOrWhiteSpace(ownerKey) ? string.Empty : ownerKey.Trim();
            var normalizedSessionId = string.IsNullOrWhiteSpace(sessionId) ? string.Empty : sessionId.Trim();
            if (string.IsNullOrWhiteSpace(normalizedOwnerKey) || string.IsNullOrWhiteSpace(normalizedSessionId))
            {
                return string.Empty;
            }

            return $"{normalizedOwnerKey}|{normalizedSessionId}";
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
            SetCommandIdMapping(typeof(SessionAttachCommand), GameCommandIds.SessionAttach);
            SetCommandIdMapping(typeof(StartGameCommand), GameCommandIds.StartGame);
            SetCommandIdMapping(typeof(PauseGameCommand), GameCommandIds.PauseGame);
            SetCommandIdMapping(typeof(ResumeGameCommand), GameCommandIds.ResumeGame);
            SetCommandIdMapping(typeof(StopGameCommand), GameCommandIds.StopGame);
            SetCommandIdMapping(typeof(EndSessionCommand), GameCommandIds.EndSession);
            SetCommandIdMapping(typeof(CriticalCommandAckPayload), GameCommandIds.CommandAck);
            SetCommandIdMapping(typeof(SessionStateUpdateCommand), GameCommandIds.SessionStateUpdate);
            SetCommandIdMapping(typeof(RuntimeStatusUpdateCommand), GameCommandIds.RuntimeStatusUpdate);
            SetCommandIdMapping(typeof(SessionWatchdogHeartbeatCommand), GameCommandIds.SessionWatchdogHeartbeat);
            SetCommandIdMapping(typeof(DevicePresenceUpdateCommand), GameCommandIds.DevicePresenceUpdate);
            SetCommandIdMapping(typeof(ManualResyncCommand), GameCommandIds.ManualResync);
            SetCommandIdMapping(typeof(ManualResyncReportCommand), GameCommandIds.ManualResyncReport);
            SetCommandIdMapping(typeof(SyncCatalogCommand), GameCommandIds.SyncCatalog);
            SetCommandIdMapping(typeof(InstallGameCommand), GameCommandIds.InstallGame);
            SetCommandIdMapping(typeof(UninstallGameCommand), GameCommandIds.UninstallGame);
            SetCommandIdMapping(typeof(GameInstallStatusCommand), GameCommandIds.GameInstallStatus);
        }

        [Serializable]
        private sealed class CriticalCommandOwnershipProbe
        {
            public string studentId;
            public string patientId;
            public string therapistId;
            public string ownerKey;
            public string sessionKey;
            public string reasonCode;
        }

        private static class AckReasonCodes
        {
            public const string Ok = "OK";
            public const string DuplicateCommand = "DUPLICATE_COMMAND";
            public const string CommandInProgress = "COMMAND_IN_PROGRESS";
            public const string NoHandler = "NO_HANDLER";
            public const string DeserializeFailed = "DESERIALIZE_FAILED";
            public const string HandlerException = "HANDLER_EXCEPTION";
            public const string EnvelopeCommandIdMismatch = "ENVELOPE_COMMAND_ID_MISMATCH";
            public const string EnvelopeMessageIdMismatch = "ENVELOPE_MESSAGE_ID_MISMATCH";
            public const string EnvelopeInvalidIssuedAt = "ENVELOPE_INVALID_ISSUED_AT";
            public const string EnvelopeInvalidExpiresAt = "ENVELOPE_INVALID_EXPIRES_AT";
            public const string EnvelopeExpired = "ENVELOPE_EXPIRED";
            public const string SessionLockConflict = "SESSION_LOCK_CONFLICT";
            public const string SessionOwnershipConflict = "SESSION_OWNERSHIP_CONFLICT";
            public const string SessionOwnershipMissing = "SESSION_OWNERSHIP_MISSING";
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
