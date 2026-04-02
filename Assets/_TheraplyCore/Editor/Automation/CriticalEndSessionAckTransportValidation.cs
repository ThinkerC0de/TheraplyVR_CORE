using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using TheraplyCore.Network.Connection;
using UnityEditor;
using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;

namespace TheraplyCore.Editor.Automation
{
    public static class CriticalEndSessionAckTransportValidation
    {
        private static Task<string> _activeValidationTask;

        public static void RunCriticalEndSessionAckTransportValidation()
        {
            if (_activeValidationTask != null)
            {
                Debug.LogWarning("[CriticalEndSessionAckTransportValidation] Validation already running.");
                return;
            }

            _activeValidationTask = ExecuteValidationAsync();
            EditorApplication.update += PumpValidationTask;
        }

        private static void PumpValidationTask()
        {
            if (_activeValidationTask == null)
            {
                EditorApplication.update -= PumpValidationTask;
                return;
            }

            if (!_activeValidationTask.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= PumpValidationTask;
            try
            {
                var summary = _activeValidationTask.GetAwaiter().GetResult();
                Debug.Log($"[CriticalEndSessionAckTransportValidation] PASS: {summary}");
                PersistValidationResult("PASS", summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                var unwrapped = exception is AggregateException aggregate
                    ? aggregate.GetBaseException()
                    : exception;
                Debug.LogError($"[CriticalEndSessionAckTransportValidation] FAIL: {unwrapped}");
                PersistValidationResult("FAIL", unwrapped.ToString());
                EditorApplication.Exit(1);
            }
            finally
            {
                _activeValidationTask = null;
            }
        }

        private static async Task<string> ExecuteValidationAsync()
        {
            var port = ReserveTcpPort();
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var journalFolder = "critical_end_session_ack_validation_" + timestamp;
            var firstSessionId = "session-a-" + Guid.NewGuid().ToString("N");
            var secondSessionId = "session-b-" + Guid.NewGuid().ToString("N");
            var firstMessageId = "end-msg-" + Guid.NewGuid().ToString("N");
            var conflictMessageId = "end-msg-" + Guid.NewGuid().ToString("N");
            var duplicateMessageId = firstMessageId;
            var finalMessageId = "end-msg-" + Guid.NewGuid().ToString("N");

            GameObject hostObject = null;
            GameSessionContext sessionContext = null;
            GameRuntimeService runtime = null;
            GameCommandBus commandBus = null;
            TCPServerService tcpServer = null;
            TcpClient client = null;
            Action<SessionAttachCommand> sessionAttachHandler = null;
            Action<EndSessionCommand> endSessionHandler = null;

            try
            {
                hostObject = new GameObject("CriticalEndSessionAckTransportValidationHost");

                sessionContext = hostObject.AddComponent<GameSessionContext>();
                SetNonPublicField(sessionContext, "_autoStartSessionOnAwake", false);
                SetNonPublicField(sessionContext, "_deferAutoStartWhenRecoverySnapshotExists", false);
                InvokeNonPublic(sessionContext, "Awake");

                runtime = hostObject.AddComponent<GameRuntimeService>();
                SetNonPublicField(runtime, "_sessionContext", sessionContext);
                sessionAttachHandler = CreateRuntimeHandler<SessionAttachCommand>(runtime, "HandleSessionAttachCommand");
                endSessionHandler = CreateRuntimeHandler<EndSessionCommand>(runtime, "HandleEndSessionCommand");

                tcpServer = hostObject.AddComponent<TCPServerService>();
                SetNonPublicField(tcpServer, "_serverPort", port);
                SetNonPublicField(tcpServer, "_logConnections", false);
                SetNonPublicField(tcpServer, "_logMessages", false);
                tcpServer.StartServer();

                commandBus = CreateConfiguredCommandBus(hostObject, tcpServer, sessionContext, journalFolder);
                commandBus.Subscribe(sessionAttachHandler);
                commandBus.Subscribe(endSessionHandler);

                client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                await WaitForConditionAsync(
                    () => tcpServer != null && tcpServer.HasClient,
                    TimeSpan.FromSeconds(5),
                    "tcp client connect");

                sessionContext.BeginSession("validation_patient", "validation_therapist", firstSessionId);

                var firstMessage = CreateEndSessionMessage(
                    firstMessageId,
                    firstSessionId,
                    "validation_patient",
                    "validation_therapist",
                    "THERAPIST_END");
                DispatchMessage(commandBus, firstMessage);
                var firstAck = await ReadAckPayloadAsync(client, firstMessageId, TimeSpan.FromSeconds(5), "first END_SESSION");
                AssertAck(firstAck, CommandAckStatus.Ack, "OK", firstSessionId, "first END_SESSION");

                if (sessionContext.SessionState != GameContracts.SessionLifecycleState.ABORTED_BY_THERAPIST)
                {
                    throw new InvalidOperationException(
                        $"Expected ABORTED_BY_THERAPIST after first END_SESSION, observed {sessionContext.SessionState}.");
                }

                sessionContext.BeginSession("validation_patient", "validation_therapist", secondSessionId);

                var conflictMessage = CreateEndSessionMessage(
                    conflictMessageId,
                    secondSessionId,
                    "validation_patient",
                    "intruder_therapist",
                    "THERAPIST_END");
                DispatchMessage(commandBus, conflictMessage);
                var conflictAck = await ReadAckPayloadAsync(client, conflictMessageId, TimeSpan.FromSeconds(5), "ownership conflict END_SESSION");
                AssertAck(conflictAck, CommandAckStatus.Nack, "SESSION_OWNERSHIP_CONFLICT", secondSessionId, "ownership conflict END_SESSION");

                if (sessionContext.SessionState != GameContracts.SessionLifecycleState.CREATED)
                {
                    throw new InvalidOperationException(
                        $"Expected CREATED after ownership conflict, observed {sessionContext.SessionState}.");
                }

                DisposeCommandBus(commandBus);
                commandBus = CreateConfiguredCommandBus(hostObject, tcpServer, sessionContext, journalFolder);
                commandBus.Subscribe(sessionAttachHandler);
                commandBus.Subscribe(endSessionHandler);

                var reconnectAttachMessageId = "attach-msg-" + Guid.NewGuid().ToString("N");
                var reconnectAttach = CreateSessionAttachMessage(
                    reconnectAttachMessageId,
                    secondSessionId,
                    "validation_patient",
                    "validation_therapist",
                    "RECONNECT_ATTACH");
                DispatchMessage(commandBus, reconnectAttach);
                var reconnectAck = await ReadAckPayloadAsync(client, reconnectAttachMessageId, TimeSpan.FromSeconds(5), "reconnect attach");
                AssertAck(reconnectAck, CommandAckStatus.Ack, "OK", secondSessionId, "reconnect attach");

                var duplicateMessage = CreateEndSessionMessage(
                    duplicateMessageId,
                    firstSessionId,
                    "validation_patient",
                    "validation_therapist",
                    "THERAPIST_END_DUPLICATE");
                DispatchMessage(commandBus, duplicateMessage);
                var duplicateAck = await ReadAckPayloadAsync(client, duplicateMessageId, TimeSpan.FromSeconds(5), "duplicate END_SESSION");
                AssertAck(duplicateAck, CommandAckStatus.Ack, "DUPLICATE_COMMAND", firstSessionId, "duplicate END_SESSION");

                var finalMessage = CreateEndSessionMessage(
                    finalMessageId,
                    secondSessionId,
                    "validation_patient",
                    "validation_therapist",
                    "THERAPIST_END_FINAL");
                DispatchMessage(commandBus, finalMessage);
                var finalAck = await ReadAckPayloadAsync(client, finalMessageId, TimeSpan.FromSeconds(5), "final END_SESSION");
                AssertAck(finalAck, CommandAckStatus.Ack, "OK", secondSessionId, "final END_SESSION");

                if (sessionContext.SessionState != GameContracts.SessionLifecycleState.ABORTED_BY_THERAPIST)
                {
                    throw new InvalidOperationException(
                        $"Expected ABORTED_BY_THERAPIST after final END_SESSION, observed {sessionContext.SessionState}.");
                }

                return
                    $"first={firstAck.status}:{firstAck.reasonCode}; " +
                    $"conflict={conflictAck.status}:{conflictAck.reasonCode}; " +
                    $"reconnect={reconnectAck.status}:{reconnectAck.reasonCode}; " +
                    $"duplicateAfterRestart={duplicateAck.status}:{duplicateAck.reasonCode}; " +
                    $"final={finalAck.status}:{finalAck.reasonCode}; " +
                    $"transport=tcp; mode=non-batch";
            }
            finally
            {
                if (client != null)
                {
                    client.Close();
                }

                DisposeCommandBus(commandBus);

                if (tcpServer != null)
                {
                    try
                    {
                        tcpServer.StopServer();
                    }
                    catch (Exception)
                    {
                        // Best-effort shutdown.
                    }
                }

                if (hostObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(hostObject);
                }
            }
        }

        private static async Task WaitForConditionAsync(
            Func<bool> predicate,
            TimeSpan timeout,
            string phase)
        {
            var startedAt = DateTime.UtcNow;
            while (DateTime.UtcNow - startedAt < timeout)
            {
                if (predicate != null && predicate())
                {
                    return;
                }

                await Task.Delay(25);
            }

            throw new TimeoutException(
                $"Condition timeout in phase '{phase}' after {timeout.TotalSeconds:F1}s.");
        }

        private static async Task<CriticalCommandAckPayload> ReadAckPayloadAsync(
            TcpClient client,
            string expectedMessageId,
            TimeSpan timeout,
            string phase)
        {
            if (client == null || !client.Connected)
            {
                throw new InvalidOperationException($"TCP client disconnected before phase '{phase}'.");
            }

            var stream = client.GetStream();
            using (var timeoutSource = new CancellationTokenSource(timeout))
            {
                while (!timeoutSource.IsCancellationRequested)
                {
                    NetworkMessage message;
                    try
                    {
                        message = await ReadWireMessageAsync(stream, timeoutSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (!string.Equals(message.commandId, GameCommandIds.CommandAck, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(message.payloadString))
                    {
                        continue;
                    }

                    var payload = JsonUtility.FromJson<CriticalCommandAckPayload>(message.payloadString);
                    if (payload == null)
                    {
                        continue;
                    }

                    if (!string.Equals(payload.messageId ?? string.Empty, expectedMessageId ?? string.Empty, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    return payload;
                }
            }

            throw new TimeoutException(
                $"Timed out waiting for COMMAND_ACK for messageId '{expectedMessageId}' in phase '{phase}'.");
        }

        private static async Task<NetworkMessage> ReadWireMessageAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var lengthBuffer = new byte[4];
            await ReadExactAsync(stream, lengthBuffer, 4, cancellationToken);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(lengthBuffer);
            }

            var length = BitConverter.ToInt32(lengthBuffer, 0);
            if (length <= 0 || length > 10 * 1024 * 1024)
            {
                throw new InvalidOperationException($"Invalid wire message length: {length}");
            }

            var payloadBuffer = new byte[length];
            await ReadExactAsync(stream, payloadBuffer, length, cancellationToken);
            var json = Encoding.UTF8.GetString(payloadBuffer);
            var wrapper = JsonUtility.FromJson<NetworkMessageJson>(json);
            if (wrapper == null)
            {
                throw new InvalidOperationException("Failed to deserialize wire message wrapper.");
            }

            return new NetworkMessage
            {
                messageId = wrapper.messageId,
                timestamp = wrapper.timestamp,
                commandId = wrapper.commandId,
                payload = string.IsNullOrWhiteSpace(wrapper.payload) ? null : Convert.FromBase64String(wrapper.payload),
            };
        }

        private static async Task ReadExactAsync(
            NetworkStream stream,
            byte[] buffer,
            int count,
            CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < count)
            {
                var read = await stream.ReadAsync(buffer, offset, count - offset, cancellationToken);
                if (read <= 0)
                {
                    throw new IOException("Socket closed while reading wire message.");
                }

                offset += read;
            }
        }

        private static void AssertAck(
            CriticalCommandAckPayload payload,
            string expectedStatus,
            string expectedReasonCode,
            string expectedSessionId,
            string phase)
        {
            if (payload == null)
            {
                throw new InvalidOperationException($"Missing ACK payload in phase '{phase}'.");
            }

            if (!string.Equals(payload.status, expectedStatus, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Unexpected ACK status in phase '{phase}'. Expected '{expectedStatus}', observed '{payload.status}'.");
            }

            if (!string.Equals(payload.reasonCode ?? string.Empty, expectedReasonCode ?? string.Empty, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unexpected ACK reasonCode in phase '{phase}'. Expected '{expectedReasonCode}', observed '{payload.reasonCode}'.");
            }

            if (!string.Equals(payload.sessionId ?? string.Empty, expectedSessionId ?? string.Empty, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unexpected ACK sessionId in phase '{phase}'. Expected '{expectedSessionId}', observed '{payload.sessionId}'.");
            }
        }

        private static GameCommandBus CreateConfiguredCommandBus(
            GameObject hostObject,
            TCPServerService tcpServer,
            GameSessionContext sessionContext,
            string journalFolder)
        {
            var commandBus = hostObject.AddComponent<GameCommandBus>();
            SetNonPublicField(commandBus, "_tcpServerService", tcpServer);
            SetNonPublicField(commandBus, "_sessionContext", sessionContext);
            SetNonPublicField(commandBus, "_logInbound", false);
            SetNonPublicField(commandBus, "_logOutbound", false);
            SetNonPublicField(commandBus, "_logUnmappedIncoming", false);
            SetNonPublicField(commandBus, "_enableCommandJournal", true);
            SetNonPublicField(commandBus, "_commandJournalFolder", journalFolder);
            SetNonPublicField(commandBus, "_commandJournalFileName", "critical_commands.ndjson");
            SetNonPublicField(commandBus, "_commandJournalMaxEntriesInMemory", 2048);
            SetNonPublicField(commandBus, "_commandJournalMaxPendingWrites", 512);
            SetNonPublicField(commandBus, "_logCommandJournalVerbose", true);
            InvokeNonPublic(commandBus, "Awake");
            return commandBus;
        }

        private static void DisposeCommandBus(GameCommandBus commandBus)
        {
            if (commandBus == null)
            {
                return;
            }

            try
            {
                InvokeNonPublic(commandBus, "OnDestroy");
            }
            catch (Exception)
            {
                // Ignore validation teardown failures.
            }

            UnityEngine.Object.DestroyImmediate(commandBus);
        }

        private static Action<TCommand> CreateRuntimeHandler<TCommand>(
            GameRuntimeService runtime,
            string methodName)
            where TCommand : IGameCommand
        {
            var method = runtime.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(runtime.GetType().FullName, methodName);
            }

            return (Action<TCommand>)Delegate.CreateDelegate(typeof(Action<TCommand>), runtime, method);
        }

        private static void DispatchMessage(GameCommandBus commandBus, NetworkMessage message)
        {
            InvokeNonPublic(commandBus, "HandleIncomingMessage", message);
        }

        private static NetworkMessage CreateSessionAttachMessage(
            string messageId,
            string sessionId,
            string patientId,
            string therapistId,
            string reasonCode)
        {
            var normalizedMessageId = NormalizeOrGuid(messageId);
            var normalizedSessionId = Normalize(sessionId);
            var normalizedPatientId = Normalize(patientId);
            var normalizedTherapistId = Normalize(therapistId);
            var ownerKey = BuildOwnerKey(normalizedTherapistId, normalizedPatientId);
            var sessionKey = BuildSessionKey(ownerKey, normalizedSessionId);

            var payload = new SessionAttachOwnershipPayload
            {
                correlationId = normalizedMessageId,
                sessionId = normalizedSessionId,
                patientId = normalizedPatientId,
                therapistId = normalizedTherapistId,
                reasonCode = Normalize(reasonCode),
                studentId = normalizedPatientId,
                ownerKey = ownerKey,
                sessionKey = sessionKey,
            };

            return CreateCriticalMessage(
                normalizedMessageId,
                GameCommandIds.SessionAttach,
                normalizedSessionId,
                JsonUtility.ToJson(payload));
        }

        private static NetworkMessage CreateEndSessionMessage(
            string messageId,
            string sessionId,
            string patientId,
            string therapistId,
            string reason)
        {
            var normalizedMessageId = NormalizeOrGuid(messageId);
            var normalizedSessionId = Normalize(sessionId);
            var normalizedPatientId = Normalize(patientId);
            var normalizedTherapistId = Normalize(therapistId);
            var ownerKey = BuildOwnerKey(normalizedTherapistId, normalizedPatientId);
            var sessionKey = BuildSessionKey(ownerKey, normalizedSessionId);

            var payload = new EndSessionOwnershipPayload
            {
                correlationId = normalizedMessageId,
                reason = Normalize(reason),
                patientId = normalizedPatientId,
                studentId = normalizedPatientId,
                therapistId = normalizedTherapistId,
                ownerKey = ownerKey,
                sessionKey = sessionKey,
            };

            return CreateCriticalMessage(
                normalizedMessageId,
                GameCommandIds.EndSession,
                normalizedSessionId,
                JsonUtility.ToJson(payload));
        }

        private static NetworkMessage CreateCriticalMessage(
            string messageId,
            string commandId,
            string sessionId,
            string payloadJson)
        {
            var envelope = new CriticalCommandEnvelope
            {
                messageId = NormalizeOrGuid(messageId),
                sessionId = Normalize(sessionId),
                commandId = Normalize(commandId),
                issuedAtUtc = DateTime.UtcNow.ToString("O"),
                payloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson,
            };

            return new NetworkMessage
            {
                messageId = envelope.messageId,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                commandId = envelope.commandId,
                payloadString = JsonUtility.ToJson(envelope),
            };
        }

        private static string BuildOwnerKey(string therapistId, string patientId)
        {
            var normalizedTherapistId = Normalize(therapistId);
            var normalizedPatientId = Normalize(patientId);
            if (string.IsNullOrWhiteSpace(normalizedTherapistId) || string.IsNullOrWhiteSpace(normalizedPatientId))
            {
                return string.Empty;
            }

            return normalizedTherapistId + "|" + normalizedPatientId;
        }

        private static string BuildSessionKey(string ownerKey, string sessionId)
        {
            var normalizedOwnerKey = Normalize(ownerKey);
            var normalizedSessionId = Normalize(sessionId);
            if (string.IsNullOrWhiteSpace(normalizedOwnerKey) || string.IsNullOrWhiteSpace(normalizedSessionId))
            {
                return string.Empty;
            }

            return normalizedOwnerKey + "|" + normalizedSessionId;
        }

        private static int ReserveTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string NormalizeOrGuid(string value)
        {
            var normalized = Normalize(value);
            return string.IsNullOrWhiteSpace(normalized) ? Guid.NewGuid().ToString() : normalized;
        }

        private static void PersistValidationResult(string status, string details)
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var outputPath = Path.Combine(projectRoot, "Temp", "CliValidation", "critical_end_session_ack_transport_result.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            var lines = new[]
            {
                $"status={status}",
                $"timestampUtc={DateTime.UtcNow:O}",
                $"details={details ?? string.Empty}",
            };

            File.WriteAllLines(outputPath, lines);
            Debug.Log($"[CriticalEndSessionAckTransportValidation] Result file: {outputPath}");
        }

        private static void InvokeNonPublic(object target, string methodName, params object[] arguments)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(target.GetType().FullName, methodName);
            }

            method.Invoke(target, arguments);
        }

        private static void SetNonPublicField(object target, string fieldName, object value)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(target.GetType().FullName, fieldName);
            }

            field.SetValue(target, value);
        }

        [Serializable]
        private sealed class SessionAttachOwnershipPayload
        {
            public string correlationId;
            public string sessionId;
            public string patientId;
            public string therapistId;
            public string reasonCode;
            public string studentId;
            public string ownerKey;
            public string sessionKey;
        }

        [Serializable]
        private sealed class EndSessionOwnershipPayload
        {
            public string correlationId;
            public string reason;
            public string patientId;
            public string studentId;
            public string therapistId;
            public string ownerKey;
            public string sessionKey;
        }
    }
}
