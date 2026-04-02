using System;
using System.IO;
using System.Reflection;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using TheraplyCore.Network.Connection;
using UnityEditor;
using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;

namespace TheraplyCore.Editor.Automation
{
    public static class CriticalEndSessionResilienceValidation
    {
        private const string JournalFileName = "critical_commands.ndjson";

        public static void RunCriticalEndSessionResilienceValidation()
        {
            try
            {
                var summary = ExecuteValidation();
                Debug.Log($"[CriticalEndSessionResilienceValidation] PASS: {summary}");
                PersistValidationResult("PASS", summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[CriticalEndSessionResilienceValidation] FAIL: {exception}");
                PersistValidationResult("FAIL", exception.ToString());
                EditorApplication.Exit(1);
            }
        }

        private static string ExecuteValidation()
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var journalFolder = "critical_end_session_validation_" + timestamp;
            var firstSessionId = "session-a-" + Guid.NewGuid().ToString("N");
            var secondSessionId = "session-b-" + Guid.NewGuid().ToString("N");
            var firstMessageId = "end-msg-" + Guid.NewGuid().ToString("N");
            var conflictMessageId = "end-msg-" + Guid.NewGuid().ToString("N");
            var reconnectAttachMessageId = "attach-msg-" + Guid.NewGuid().ToString("N");
            var finalMessageId = "end-msg-" + Guid.NewGuid().ToString("N");

            GameObject hostObject = null;
            GameSessionContext sessionContext = null;
            GameRuntimeService runtime = null;
            GameCommandBus commandBus = null;
            Action<SessionAttachCommand> sessionAttachHandler = null;
            Action<EndSessionCommand> endSessionHandler = null;

            try
            {
                hostObject = new GameObject("CriticalEndSessionResilienceValidationHost");

                sessionContext = hostObject.AddComponent<GameSessionContext>();
                SetNonPublicField(sessionContext, "_autoStartSessionOnAwake", false);
                SetNonPublicField(sessionContext, "_deferAutoStartWhenRecoverySnapshotExists", false);
                InvokeNonPublic(sessionContext, "Awake");

                runtime = hostObject.AddComponent<GameRuntimeService>();
                SetNonPublicField(runtime, "_sessionContext", sessionContext);
                sessionAttachHandler = CreateRuntimeHandler<SessionAttachCommand>(runtime, "HandleSessionAttachCommand");
                endSessionHandler = CreateRuntimeHandler<EndSessionCommand>(runtime, "HandleEndSessionCommand");

                commandBus = CreateConfiguredCommandBus(hostObject, sessionContext, journalFolder);
                commandBus.Subscribe(sessionAttachHandler);
                commandBus.Subscribe(endSessionHandler);

                sessionContext.BeginSession("validation_patient", "validation_therapist", firstSessionId);

                DispatchMessage(
                    commandBus,
                    CreateEndSessionMessage(
                        firstMessageId,
                        firstSessionId,
                        "validation_patient",
                        "validation_therapist",
                        "THERAPIST_END"));

                var firstRecord = GetLatestJournalRecord(commandBus, firstMessageId, "first END_SESSION");
                AssertJournalRecord(firstRecord, JournalStatusApplied, "OK", GameCommandIds.EndSession, "first END_SESSION");

                if (sessionContext.SessionState != GameContracts.SessionLifecycleState.ABORTED_BY_THERAPIST)
                {
                    throw new InvalidOperationException(
                        $"Expected session state ABORTED_BY_THERAPIST after first END_SESSION, observed {sessionContext.SessionState}.");
                }

                sessionContext.BeginSession("validation_patient", "validation_therapist", secondSessionId);

                DispatchMessage(
                    commandBus,
                    CreateEndSessionMessage(
                        conflictMessageId,
                        secondSessionId,
                        "validation_patient",
                        "intruder_therapist",
                        "THERAPIST_END"));

                var conflictRecord = GetLatestJournalRecord(commandBus, conflictMessageId, "ownership conflict END_SESSION");
                AssertJournalRecord(
                    conflictRecord,
                    JournalStatusRejected,
                    "SESSION_OWNERSHIP_CONFLICT",
                    GameCommandIds.EndSession,
                    "ownership conflict END_SESSION");

                if (sessionContext.SessionState != GameContracts.SessionLifecycleState.CREATED)
                {
                    throw new InvalidOperationException(
                        $"Expected session state to remain CREATED after ownership conflict, observed {sessionContext.SessionState}.");
                }

                DisposeCommandBus(commandBus);
                commandBus = CreateConfiguredCommandBus(hostObject, sessionContext, journalFolder);
                commandBus.Subscribe(sessionAttachHandler);
                commandBus.Subscribe(endSessionHandler);

                DispatchMessage(
                    commandBus,
                    CreateSessionAttachMessage(
                        reconnectAttachMessageId,
                        secondSessionId,
                        "validation_patient",
                        "validation_therapist",
                        "RECONNECT_ATTACH"));

                var reconnectRecord = GetLatestJournalRecord(commandBus, reconnectAttachMessageId, "reconnect attach after restart");
                AssertJournalRecord(
                    reconnectRecord,
                    JournalStatusApplied,
                    "OK",
                    GameCommandIds.SessionAttach,
                    "reconnect attach after restart");

                DispatchMessage(
                    commandBus,
                    CreateEndSessionMessage(
                        firstMessageId,
                        firstSessionId,
                        "validation_patient",
                        "validation_therapist",
                        "THERAPIST_END"));

                var duplicateRecord = GetLatestJournalRecord(commandBus, firstMessageId, "duplicate END_SESSION after restart");
                AssertJournalRecord(
                    duplicateRecord,
                    JournalStatusApplied,
                    "DUPLICATE_COMMAND",
                    GameCommandIds.EndSession,
                    "duplicate END_SESSION after restart");

                DispatchMessage(
                    commandBus,
                    CreateEndSessionMessage(
                        finalMessageId,
                        secondSessionId,
                        "validation_patient",
                        "validation_therapist",
                        "THERAPIST_END_FINAL"));

                var finalRecord = GetLatestJournalRecord(commandBus, finalMessageId, "post-restart END_SESSION");
                AssertJournalRecord(
                    finalRecord,
                    JournalStatusApplied,
                    "OK",
                    GameCommandIds.EndSession,
                    "post-restart END_SESSION");

                if (sessionContext.SessionState != GameContracts.SessionLifecycleState.ABORTED_BY_THERAPIST)
                {
                    throw new InvalidOperationException(
                        $"Expected session state ABORTED_BY_THERAPIST after post-restart END_SESSION, observed {sessionContext.SessionState}.");
                }

                return
                    $"first={firstRecord.status}:{firstRecord.reasonCode}; " +
                    $"conflict={conflictRecord.status}:{conflictRecord.reasonCode}; " +
                    $"reconnect={reconnectRecord.status}:{reconnectRecord.reasonCode}; " +
                    $"duplicateAfterRestart={duplicateRecord.status}:{duplicateRecord.reasonCode}; " +
                    $"final={finalRecord.status}:{finalRecord.reasonCode}; " +
                    $"journalFolder={journalFolder}";
            }
            finally
            {
                DisposeCommandBus(commandBus);

                if (hostObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(hostObject);
                }
            }
        }

        private static GameCommandBus CreateConfiguredCommandBus(
            GameObject hostObject,
            GameSessionContext sessionContext,
            string journalFolder)
        {
            var commandBus = hostObject.AddComponent<GameCommandBus>();
            SetNonPublicField(commandBus, "_tcpServerService", null);
            SetNonPublicField(commandBus, "_sessionContext", sessionContext);
            SetNonPublicField(commandBus, "_logInbound", false);
            SetNonPublicField(commandBus, "_logOutbound", false);
            SetNonPublicField(commandBus, "_logUnmappedIncoming", false);
            SetNonPublicField(commandBus, "_enableCommandJournal", true);
            SetNonPublicField(commandBus, "_commandJournalFolder", journalFolder);
            SetNonPublicField(commandBus, "_commandJournalFileName", JournalFileName);
            SetNonPublicField(commandBus, "_commandJournalMaxEntriesInMemory", 2048);
            SetNonPublicField(commandBus, "_commandJournalMaxPendingWrites", 512);
            SetNonPublicField(commandBus, "_logCommandJournalVerbose", true);
            InvokeNonPublic(commandBus, "Awake");
            return commandBus;
        }

        private static Action<TCommand> CreateRuntimeHandler<TCommand>(
            GameRuntimeService runtime,
            string methodName)
            where TCommand : IGameCommand
        {
            var method = runtime.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(runtime.GetType().FullName, methodName);
            }

            return (Action<TCommand>)Delegate.CreateDelegate(typeof(Action<TCommand>), runtime, method);
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
                // Best-effort cleanup.
            }

            UnityEngine.Object.DestroyImmediate(commandBus);
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

        private static JournalRecordView GetLatestJournalRecord(
            GameCommandBus commandBus,
            string messageId,
            string phase)
        {
            var journalField = commandBus.GetType().GetField("_commandJournal", BindingFlags.Instance | BindingFlags.NonPublic);
            var journal = journalField?.GetValue(commandBus);
            if (journal == null)
            {
                throw new InvalidOperationException($"Command journal is unavailable while validating phase '{phase}'.");
            }

            var tryGetLatestMethod = journal.GetType().GetMethod("TryGetLatest", BindingFlags.Instance | BindingFlags.Public);
            if (tryGetLatestMethod == null)
            {
                throw new MissingMethodException(journal.GetType().FullName, "TryGetLatest");
            }

            var invokeArgs = new object[] { messageId, null };
            var found = (bool)tryGetLatestMethod.Invoke(journal, invokeArgs);
            if (!found || invokeArgs[1] == null)
            {
                throw new InvalidOperationException(
                    $"Command journal did not contain latest record for messageId '{messageId}' in phase '{phase}'.");
            }

            var record = invokeArgs[1];
            var recordType = record.GetType();
            return new JournalRecordView
            {
                status = recordType.GetField("status", BindingFlags.Instance | BindingFlags.Public)?.GetValue(record) as string ?? string.Empty,
                reasonCode = recordType.GetField("reasonCode", BindingFlags.Instance | BindingFlags.Public)?.GetValue(record) as string ?? string.Empty,
                commandId = recordType.GetField("commandId", BindingFlags.Instance | BindingFlags.Public)?.GetValue(record) as string ?? string.Empty,
            };
        }

        private static void AssertJournalRecord(
            JournalRecordView record,
            string expectedStatus,
            string expectedReasonCode,
            string expectedCommandId,
            string phase)
        {
            if (!string.Equals(record.status, expectedStatus, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Unexpected journal status in phase '{phase}'. Expected '{expectedStatus}', observed '{record.status}'.");
            }

            if (!string.Equals(record.reasonCode ?? string.Empty, expectedReasonCode ?? string.Empty, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unexpected journal reasonCode in phase '{phase}'. Expected '{expectedReasonCode}', observed '{record.reasonCode}'.");
            }

            if (!string.Equals(record.commandId ?? string.Empty, expectedCommandId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Unexpected journal commandId in phase '{phase}'. Expected '{expectedCommandId}', observed '{record.commandId}'.");
            }
        }

        private static void DispatchMessage(GameCommandBus commandBus, NetworkMessage message)
        {
            InvokeNonPublic(commandBus, "HandleIncomingMessage", message);
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
            var outputPath = Path.Combine(projectRoot, "Temp", "CliValidation", "critical_end_session_resilience_result.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            var lines = new[]
            {
                $"status={status}",
                $"timestampUtc={DateTime.UtcNow:O}",
                $"details={details ?? string.Empty}",
            };

            File.WriteAllLines(outputPath, lines);
            Debug.Log($"[CriticalEndSessionResilienceValidation] Result file: {outputPath}");
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

        private struct JournalRecordView
        {
            public string status;
            public string reasonCode;
            public string commandId;
        }

        private const string JournalStatusApplied = "APPLIED";
        private const string JournalStatusRejected = "REJECTED";
    }
}
