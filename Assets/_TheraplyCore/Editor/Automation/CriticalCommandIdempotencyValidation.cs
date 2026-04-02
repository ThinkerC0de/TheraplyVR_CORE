using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using TheraplyCore.Network.Connection;
using UnityEditor;
using UnityEngine;

namespace TheraplyCore.Editor.Automation
{
    public static class CriticalCommandIdempotencyValidation
    {
        private const string JournalFileName = "critical_commands.ndjson";

        public static void RunCriticalCommandIdempotencyValidation()
        {
            try
            {
                var summary = ExecuteValidation();
                Debug.Log($"[CriticalCommandIdempotencyValidation] PASS: {summary}");
                PersistValidationResult("PASS", summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[CriticalCommandIdempotencyValidation] FAIL: {exception}");
                PersistValidationResult("FAIL", exception.ToString());
                EditorApplication.Exit(1);
            }
        }

        private static string ExecuteValidation()
        {
            var folderName = "critical_command_validation_" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var journalDirectory = Path.Combine(Application.persistentDataPath, folderName);
            var journalPath = Path.Combine(journalDirectory, JournalFileName);
            if (Directory.Exists(journalDirectory))
            {
                Directory.Delete(journalDirectory, true);
            }

            var firstMessageId = "validation-msg-" + Guid.NewGuid().ToString("N");
            var secondMessageId = "validation-msg-" + Guid.NewGuid().ToString("N");
            var initialHandlerCalls = 0;
            var restartHandlerCalls = 0;

            var firstHost = CreateValidationHost(folderName);
            try
            {
                firstHost.commandBus.Subscribe<StartGameCommand>(_ => initialHandlerCalls++);
                firstHost.Dispatch(CreateCriticalStartGameMessage(firstHost.sessionContext, firstMessageId));
                firstHost.Dispatch(CreateCriticalStartGameMessage(firstHost.sessionContext, firstMessageId));

                if (initialHandlerCalls != 1)
                {
                    throw new InvalidOperationException(
                        $"Expected exactly one handler invocation for duplicate message in first runtime; observed {initialHandlerCalls}.");
                }
            }
            finally
            {
                firstHost.Dispose();
            }

            if (!File.Exists(journalPath))
            {
                throw new InvalidOperationException($"Command journal file was not created: {journalPath}");
            }

            var secondHost = CreateValidationHost(folderName);
            try
            {
                secondHost.commandBus.Subscribe<StartGameCommand>(_ => restartHandlerCalls++);
                secondHost.Dispatch(CreateCriticalStartGameMessage(secondHost.sessionContext, firstMessageId));
                secondHost.Dispatch(CreateCriticalStartGameMessage(secondHost.sessionContext, secondMessageId));

                if (restartHandlerCalls != 1)
                {
                    throw new InvalidOperationException(
                        $"Expected one handler invocation after restart (duplicate suppressed + new message executed); observed {restartHandlerCalls}.");
                }
            }
            finally
            {
                secondHost.Dispose();
            }

            var lines = File.ReadAllLines(journalPath);
            var firstMessageRecords = lines
                .Select(line => ParseJournalLine(line))
                .Where(record => record != null && string.Equals(record.messageId, firstMessageId, StringComparison.Ordinal))
                .ToArray();
            var secondMessageRecords = lines
                .Select(line => ParseJournalLine(line))
                .Where(record => record != null && string.Equals(record.messageId, secondMessageId, StringComparison.Ordinal))
                .ToArray();

            if (firstMessageRecords.Length < 3)
            {
                throw new InvalidOperationException(
                    $"Expected journal to persist duplicate history for first message (>=3 records), observed {firstMessageRecords.Length}.");
            }

            if (!firstMessageRecords.Any(record =>
                    string.Equals(record.status, "APPLIED", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Journal does not contain APPLIED status for first message.");
            }

            if (secondMessageRecords.Length < 2)
            {
                throw new InvalidOperationException(
                    $"Expected journal to persist received+applied for second message (>=2 records), observed {secondMessageRecords.Length}.");
            }

            return
                $"firstRuntimeHandlerCalls={initialHandlerCalls}; " +
                $"restartRuntimeHandlerCalls={restartHandlerCalls}; " +
                $"firstMessageJournalRecords={firstMessageRecords.Length}; " +
                $"secondMessageJournalRecords={secondMessageRecords.Length}; " +
                $"journalPath={journalPath}";
        }

        private static ValidationHost CreateValidationHost(string journalFolder)
        {
            var hostObject = new GameObject("CriticalCommandIdempotencyValidationHost");
            var sessionContext = hostObject.AddComponent<GameSessionContext>();
            InvokeNonPublic(sessionContext, "Awake");

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

            return new ValidationHost(hostObject, sessionContext, commandBus);
        }

        private static NetworkMessage CreateCriticalStartGameMessage(GameSessionContext sessionContext, string messageId)
        {
            var sessionId = sessionContext == null ? string.Empty : sessionContext.SessionId ?? string.Empty;
            var patientId = sessionContext == null ? string.Empty : sessionContext.PatientId ?? string.Empty;
            var therapistId = sessionContext == null ? string.Empty : sessionContext.TherapistId ?? string.Empty;
            var ownerKey = string.IsNullOrWhiteSpace(therapistId) || string.IsNullOrWhiteSpace(patientId)
                ? string.Empty
                : therapistId + "|" + patientId;
            var sessionKey = string.IsNullOrWhiteSpace(ownerKey) || string.IsNullOrWhiteSpace(sessionId)
                ? string.Empty
                : ownerKey + "|" + sessionId;

            var payload = new StartGameOwnershipPayload
            {
                correlationId = messageId,
                gameId = "validation_game",
                resumeFromSaved = false,
                therapistId = therapistId,
                patientId = patientId,
                studentId = patientId,
                ownerKey = ownerKey,
                sessionKey = sessionKey,
            };

            var envelope = new CriticalCommandEnvelope
            {
                messageId = messageId,
                sessionId = sessionId,
                commandId = GameCommandIds.StartGame,
                issuedAtUtc = DateTime.UtcNow.ToString("O"),
                payloadJson = JsonUtility.ToJson(payload),
            };

            return new NetworkMessage
            {
                messageId = messageId,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                commandId = GameCommandIds.StartGame,
                payloadString = JsonUtility.ToJson(envelope),
            };
        }

        private static void PersistValidationResult(string status, string details)
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var outputPath = Path.Combine(projectRoot, "Temp", "CliValidation", "critical_command_idempotency_result.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            var lines = new[]
            {
                $"status={status}",
                $"timestampUtc={DateTime.UtcNow:O}",
                $"details={details ?? string.Empty}",
            };

            File.WriteAllLines(outputPath, lines);
            Debug.Log($"[CriticalCommandIdempotencyValidation] Result file: {outputPath}");
        }

        private static JournalRecord ParseJournalLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<JournalRecord>(line);
            }
            catch
            {
                return null;
            }
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
        private sealed class StartGameOwnershipPayload
        {
            public string correlationId;
            public string gameId;
            public bool resumeFromSaved;
            public string therapistId;
            public string patientId;
            public string studentId;
            public string ownerKey;
            public string sessionKey;
        }

        [Serializable]
        private sealed class JournalRecord
        {
            public string messageId;
            public string status;
            public string reasonCode;
        }

        private sealed class ValidationHost : IDisposable
        {
            public readonly GameObject hostObject;
            public readonly GameSessionContext sessionContext;
            public readonly GameCommandBus commandBus;
            private bool _disposed;

            public ValidationHost(GameObject hostObject, GameSessionContext sessionContext, GameCommandBus commandBus)
            {
                this.hostObject = hostObject;
                this.sessionContext = sessionContext;
                this.commandBus = commandBus;
            }

            public void Dispatch(NetworkMessage message)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(ValidationHost));
                }

                InvokeNonPublic(commandBus, "HandleIncomingMessage", message);
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                try
                {
                    InvokeNonPublic(commandBus, "OnDestroy");
                }
                catch (Exception)
                {
                    // Ignore teardown failures in validation cleanup.
                }

                if (hostObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(hostObject);
                }
            }
        }
    }
}
