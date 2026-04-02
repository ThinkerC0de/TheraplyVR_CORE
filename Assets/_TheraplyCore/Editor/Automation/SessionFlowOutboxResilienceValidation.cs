using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TheraplyCore.Firebase;
using UnityEditor;
using UnityEngine;

namespace TheraplyCore.Editor.Automation
{
    public static class SessionFlowOutboxResilienceValidation
    {
        public static void RunSessionFlowOutboxResilienceValidation()
        {
            try
            {
                var summary = ExecuteValidation();
                Debug.Log("[SessionFlowOutboxResilienceValidation] PASS: " + summary);
                PersistValidationResult("PASS", summary);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[SessionFlowOutboxResilienceValidation] FAIL: " + exception);
                PersistValidationResult("FAIL", exception.ToString());
                EditorApplication.Exit(1);
            }
        }

        private static string ExecuteValidation()
        {
            var runtimeAssembly = typeof(FirebaseDataService).Assembly;
            var durableOutboxType = ResolveType(runtimeAssembly, "TheraplyCore.Firebase.DurableEventOutbox");
            var recordType = ResolveType(runtimeAssembly, "TheraplyCore.Firebase.DurableSessionEventRecord");
            var retryRecordType = ResolveType(runtimeAssembly, "TheraplyCore.Firebase.SessionOutboxRetryRecord");

            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var projectRoot = Directory.GetCurrentDirectory();
            var tempFolder = Path.Combine(projectRoot, "Temp", "CliValidation", "outbox_resilience_" + timestamp);
            Directory.CreateDirectory(tempFolder);

            var sqlitePath = Path.Combine(tempFolder, "events.db");
            var jsonLinePath = Path.Combine(tempFolder, "events.ndjson");

            object outbox = null;
            try
            {
                outbox = Activator.CreateInstance(
                    durableOutboxType,
                    true,
                    sqlitePath,
                    jsonLinePath,
                    false,
                    false,
                    256,
                    false);
                AssertTrue(outbox != null, "Failed to create DurableEventOutbox instance.");

                var sessionId = "flow-resilience-" + Guid.NewGuid().ToString("N");
                var baseEventIds = new[]
                {
                    "evt-" + Guid.NewGuid().ToString("N"),
                    "evt-" + Guid.NewGuid().ToString("N"),
                    "evt-" + Guid.NewGuid().ToString("N"),
                };

                for (var i = 0; i < baseEventIds.Length; i++)
                {
                    var record = CreateDurableRecord(
                        recordType,
                        baseEventIds[i],
                        sessionId,
                        "FLOW_EVENT_" + (i + 1));
                    AssertTrue(
                        TryEnqueue(durableOutboxType, outbox, record, out var enqueueError),
                        "Failed to enqueue event " + baseEventIds[i] + ". error=" + enqueueError);
                }

                var duplicateRecord = CreateDurableRecord(
                    recordType,
                    baseEventIds[1],
                    sessionId,
                    "FLOW_EVENT_DUPLICATE");
                AssertTrue(
                    TryEnqueue(durableOutboxType, outbox, duplicateRecord, out var duplicateError),
                    "Failed to enqueue duplicate event. error=" + duplicateError);

                FlushOutbox(durableOutboxType, outbox, TimeSpan.FromSeconds(2));

                AssertTrue(
                    TryGetSessionSequenceIndex(
                        durableOutboxType,
                        outbox,
                        sessionId,
                        out var sequenceIndex,
                        out var sequenceError),
                    "Failed to fetch sequence index. error=" + sequenceError);

                AssertTrue(sequenceIndex != null, "Sequence index should not be null.");
                AssertTrue(sequenceIndex.Count == baseEventIds.Length, "Duplicate event should not create extra rows.");
                ValidateSequenceContainsExactEventIds(sequenceIndex, baseEventIds);

                AssertTrue(
                    TryClaimOutboxBatch(
                        durableOutboxType,
                        outbox,
                        32,
                        "offline_worker",
                        out var firstBatch,
                        out var firstClaimError),
                    "Failed to claim offline batch. error=" + firstClaimError);
                AssertTrue(firstBatch != null, "First claim batch should not be null.");

                var firstBatchIds = ExtractBatchEventIds(firstBatch, out var firstAttemptCounts);
                AssertTrue(firstBatchIds.Count == baseEventIds.Length, "Offline claim should include all base events.");
                AssertAllAttemptsEqual(firstAttemptCounts, 1, "Initial claim attempt count mismatch.");

                var retryList = CreateRetryRecordList(retryRecordType, firstBatchIds, "NETWORK_OFFLINE");
                AssertTrue(
                    RescheduleOutboxBatch(
                        durableOutboxType,
                        outbox,
                        retryList,
                        out var rescheduleError),
                    "Failed to reschedule offline batch. error=" + rescheduleError);

                var offlineStats = ReadOutboxStats(durableOutboxType, outbox);
                AssertTrue(offlineStats.failed >= baseEventIds.Length, "Offline stats should show failed backlog.");

                AssertTrue(
                    TryClaimOutboxBatch(
                        durableOutboxType,
                        outbox,
                        32,
                        "reconnect_worker",
                        out var replayBatch,
                        out var replayClaimError),
                    "Failed to claim replay batch. error=" + replayClaimError);
                AssertTrue(replayBatch != null, "Replay batch should not be null.");

                var replayIds = ExtractBatchEventIds(replayBatch, out var replayAttemptCounts);
                AssertTrue(replayIds.Count == baseEventIds.Length, "Replay claim should include all events.");
                AssertAllAttemptsAtLeast(replayAttemptCounts, 2, "Replay claim should bump attempt count.");

                AssertTrue(
                    MarkOutboxBatchSynced(
                        durableOutboxType,
                        outbox,
                        replayIds,
                        out var syncError),
                    "Failed to mark replay batch synced. error=" + syncError);

                var finalStats = ReadOutboxStats(durableOutboxType, outbox);
                AssertTrue(finalStats.pending == 0, "Pending outbox rows should be zero after sync.");
                AssertTrue(finalStats.inFlight == 0, "In-flight outbox rows should be zero after sync.");
                AssertTrue(finalStats.failed == 0, "Failed outbox rows should be zero after sync.");
                AssertTrue(finalStats.synced == baseEventIds.Length, "All events should be synced.");
                AssertTrue(finalStats.replayed >= baseEventIds.Length, "Replay counter should include all retried events.");

                AssertTrue(
                    TryClaimOutboxBatch(
                        durableOutboxType,
                        outbox,
                        32,
                        "post_sync_worker",
                        out var emptyBatch,
                        out var emptyClaimError),
                    "Failed to claim post-sync batch. error=" + emptyClaimError);
                AssertTrue(emptyBatch == null || emptyBatch.Count == 0, "No events should remain after sync.");

                return
                    "dedupe=OK; " +
                    "offlineFailed=" + offlineStats.failed + "; " +
                    "replayAttempts=" + JoinIntValues(replayAttemptCounts) + "; " +
                    "finalSynced=" + finalStats.synced + "; " +
                    "finalReplayed=" + finalStats.replayed + "; " +
                    "storePath=" + jsonLinePath;
            }
            finally
            {
                if (outbox is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        private static Type ResolveType(Assembly runtimeAssembly, string fullName)
        {
            if (runtimeAssembly == null)
            {
                throw new ArgumentNullException(nameof(runtimeAssembly));
            }

            var type = runtimeAssembly.GetType(fullName, throwOnError: false);
            if (type == null)
            {
                throw new InvalidOperationException("Runtime type not found: " + fullName);
            }

            return type;
        }

        private static object CreateDurableRecord(
            Type recordType,
            string eventId,
            string sessionId,
            string eventType)
        {
            var record = Activator.CreateInstance(recordType);
            SetField(record, "eventId", eventId);
            SetField(record, "sessionId", sessionId);
            SetField(record, "patientId", "validation_patient");
            SetField(record, "therapistId", "validation_therapist");
            SetField(record, "deviceId", "validation_device");
            SetField(record, "sequence", 0L);
            SetField(record, "eventType", eventType);
            SetField(record, "eventVersion", 1);
            SetField(record, "createdAtUtc", DateTime.UtcNow.ToString("O"));
            SetField(
                record,
                "payloadJson",
                "{\"eventType\":\"" + eventType + "\",\"sessionId\":\"" + sessionId + "\"}");
            SetField(record, "checksum", string.Empty);
            return record;
        }

        private static IList CreateRetryRecordList(Type retryRecordType, IReadOnlyList<string> eventIds, string errorCode)
        {
            var listType = typeof(List<>).MakeGenericType(retryRecordType);
            var list = (IList)Activator.CreateInstance(listType);

            for (var i = 0; i < eventIds.Count; i++)
            {
                var retryRecord = Activator.CreateInstance(retryRecordType);
                SetField(retryRecord, "eventId", eventIds[i]);
                SetField(retryRecord, "nextAttemptUtc", DateTime.UtcNow.AddMilliseconds(-1));
                SetField(retryRecord, "errorCode", errorCode);
                list.Add(retryRecord);
            }

            return list;
        }

        private static bool TryEnqueue(Type durableOutboxType, object outbox, object record, out string error)
        {
            var method = ResolveMethod(durableOutboxType, "TryEnqueue");
            var args = new[] { record, null };
            var ok = Convert.ToBoolean(method.Invoke(outbox, args));
            error = args[1] as string ?? string.Empty;
            return ok;
        }

        private static void FlushOutbox(Type durableOutboxType, object outbox, TimeSpan timeout)
        {
            var method = ResolveMethod(durableOutboxType, "Flush");
            method.Invoke(outbox, new object[] { timeout });
        }

        private static bool TryGetSessionSequenceIndex(
            Type durableOutboxType,
            object outbox,
            string sessionId,
            out IList indexRecords,
            out string error)
        {
            var method = ResolveMethod(durableOutboxType, "TryGetSessionSequenceIndex");
            var args = new object[] { sessionId, null, null };
            var ok = Convert.ToBoolean(method.Invoke(outbox, args));
            indexRecords = args[1] as IList;
            error = args[2] as string ?? string.Empty;
            return ok;
        }

        private static bool TryClaimOutboxBatch(
            Type durableOutboxType,
            object outbox,
            int maxBatchSize,
            string workerId,
            out IList batch,
            out string error)
        {
            var method = ResolveMethod(durableOutboxType, "TryClaimOutboxBatch");
            var args = new object[] { maxBatchSize, workerId, null, null };
            var ok = Convert.ToBoolean(method.Invoke(outbox, args));
            batch = args[2] as IList;
            error = args[3] as string ?? string.Empty;
            return ok;
        }

        private static bool RescheduleOutboxBatch(
            Type durableOutboxType,
            object outbox,
            IList retryRecords,
            out string error)
        {
            var method = ResolveMethod(durableOutboxType, "RescheduleOutboxBatch");
            var args = new object[] { retryRecords, null };
            var ok = Convert.ToBoolean(method.Invoke(outbox, args));
            error = args[1] as string ?? string.Empty;
            return ok;
        }

        private static bool MarkOutboxBatchSynced(
            Type durableOutboxType,
            object outbox,
            IReadOnlyList<string> eventIds,
            out string error)
        {
            var method = ResolveMethod(durableOutboxType, "MarkOutboxBatchSynced");
            var args = new object[] { eventIds, null };
            var ok = Convert.ToBoolean(method.Invoke(outbox, args));
            error = args[1] as string ?? string.Empty;
            return ok;
        }

        private static OutboxStats ReadOutboxStats(Type durableOutboxType, object outbox)
        {
            var method = ResolveMethod(durableOutboxType, "GetStatistics");
            var statsObject = method.Invoke(outbox, null);
            if (statsObject == null)
            {
                throw new InvalidOperationException("Outbox statistics object is null.");
            }

            return new OutboxStats
            {
                pending = ReadIntField(statsObject, "outboxPending"),
                inFlight = ReadIntField(statsObject, "outboxInFlight"),
                failed = ReadIntField(statsObject, "outboxFailed"),
                synced = ReadIntField(statsObject, "outboxSynced"),
                replayed = ReadIntField(statsObject, "outboxReplayed"),
                retryCount = ReadIntField(statsObject, "outboxRetryCount"),
            };
        }

        private static List<string> ExtractBatchEventIds(IList batch, out List<int> attemptCounts)
        {
            attemptCounts = new List<int>();
            var eventIds = new List<string>();

            if (batch == null)
            {
                return eventIds;
            }

            for (var i = 0; i < batch.Count; i++)
            {
                var item = batch[i];
                var attempt = ReadIntField(item, "attemptCount");
                attemptCounts.Add(attempt);

                var record = ReadField(item, "record");
                var eventId = ReadStringField(record, "eventId");
                if (!string.IsNullOrWhiteSpace(eventId))
                {
                    eventIds.Add(eventId);
                }
            }

            return eventIds;
        }

        private static void ValidateSequenceContainsExactEventIds(IList sequenceIndex, IReadOnlyList<string> expectedEventIds)
        {
            var observedIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < sequenceIndex.Count; i++)
            {
                var item = sequenceIndex[i];
                var eventId = ReadStringField(item, "eventId");
                var status = ReadStringField(item, "outboxStatus");
                AssertTrue(!string.IsNullOrWhiteSpace(eventId), "Sequence record has empty eventId.");
                AssertTrue(
                    string.Equals(status, "PENDING", StringComparison.OrdinalIgnoreCase),
                    "Expected PENDING outbox status before claim; observed " + status + ".");
                observedIds.Add(eventId);
            }

            var expected = new HashSet<string>(expectedEventIds, StringComparer.Ordinal);
            AssertTrue(expected.SetEquals(observedIds), "Sequence index event id set mismatch.");
        }

        private static void AssertAllAttemptsEqual(IReadOnlyList<int> attempts, int expected, string message)
        {
            for (var i = 0; i < attempts.Count; i++)
            {
                if (attempts[i] != expected)
                {
                    throw new InvalidOperationException(message + " expected=" + expected + " actual=" + attempts[i]);
                }
            }
        }

        private static void AssertAllAttemptsAtLeast(IReadOnlyList<int> attempts, int minimum, string message)
        {
            for (var i = 0; i < attempts.Count; i++)
            {
                if (attempts[i] < minimum)
                {
                    throw new InvalidOperationException(message + " minimum=" + minimum + " actual=" + attempts[i]);
                }
            }
        }

        private static string JoinIntValues(IReadOnlyList<int> values)
        {
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            var parts = new string[values.Count];
            for (var i = 0; i < values.Count; i++)
            {
                parts[i] = values[i].ToString();
            }

            return string.Join(",", parts);
        }

        private static MethodInfo ResolveMethod(Type targetType, string methodName)
        {
            var method = targetType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
            if (method == null)
            {
                throw new MissingMethodException(targetType.FullName, methodName);
            }

            return method;
        }

        private static object ReadField(object target, string fieldName)
        {
            if (target == null)
            {
                return null;
            }

            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(target.GetType().FullName, fieldName);
            }

            return field.GetValue(target);
        }

        private static string ReadStringField(object target, string fieldName)
        {
            var value = ReadField(target, fieldName);
            return value as string ?? string.Empty;
        }

        private static int ReadIntField(object target, string fieldName)
        {
            var value = ReadField(target, fieldName);
            if (value is int intValue)
            {
                return intValue;
            }

            if (value is long longValue)
            {
                if (longValue > int.MaxValue)
                {
                    return int.MaxValue;
                }

                if (longValue < int.MinValue)
                {
                    return int.MinValue;
                }

                return (int)longValue;
            }

            return int.TryParse(value?.ToString() ?? "0", out var parsed) ? parsed : 0;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(target.GetType().FullName, fieldName);
            }

            field.SetValue(target, value);
        }

        private static void PersistValidationResult(string status, string details)
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var outputPath = Path.Combine(
                projectRoot,
                "Temp",
                "CliValidation",
                "session_flow_outbox_resilience_validation_result.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            var lines = new[]
            {
                $"status={status}",
                $"timestampUtc={DateTime.UtcNow:O}",
                $"details={details ?? string.Empty}",
            };

            File.WriteAllLines(outputPath, lines);
            Debug.Log("[SessionFlowOutboxResilienceValidation] Result file: " + outputPath);
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private struct OutboxStats
        {
            public int pending;
            public int inFlight;
            public int failed;
            public int synced;
            public int replayed;
            public int retryCount;
        }
    }
}
