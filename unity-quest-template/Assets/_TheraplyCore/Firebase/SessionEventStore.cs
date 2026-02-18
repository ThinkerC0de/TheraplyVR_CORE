using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Firebase
{
    [Serializable]
    internal sealed class DurableSessionEventRecord
    {
        public string eventId;
        public string sessionId;
        public string patientId;
        public string therapistId;
        public string deviceId;
        public long sequence;
        public string eventType;
        public int eventVersion;
        public string createdAtUtc;
        public string payloadJson;
        public string checksum;
    }

    internal struct SessionEventStoreStatistics
    {
        public int pendingWrites;
        public int eventsPersisted;
        public int eventsFailed;
        public int eventsDropped;
        public bool outboxSupported;
        public int outboxPending;
        public int outboxInFlight;
        public int outboxSynced;
        public int outboxRetryCount;
        public string mode;
    }

    internal struct SessionOutboxBatchItem
    {
        public DurableSessionEventRecord record;
        public int attemptCount;
    }

    internal struct SessionOutboxRetryRecord
    {
        public string eventId;
        public DateTime nextAttemptUtc;
        public string errorCode;
    }

    internal struct SessionOutboxStatistics
    {
        public int pending;
        public int inFlight;
        public int synced;
        public int retryCount;
    }

    internal struct SessionSequenceIndexRecord
    {
        public string eventId;
        public long sequence;
        public string outboxStatus;
    }

    internal sealed class SessionEventStore : IDisposable
    {
        private readonly bool _enabled;
        private readonly BlockingCollection<DurableSessionEventRecord> _pendingWrites;
        private readonly CancellationTokenSource _cancelSource;
        private readonly Task _writerTask;
        private readonly IEventStoreBackend _backend;
        private readonly string _mode;
        private bool _disposed;

        private int _eventsPersisted;
        private int _eventsFailed;
        private int _eventsDropped;

        public SessionEventStore(
            bool enabled,
            string sqlitePath,
            string jsonLinePath,
            bool preferSqliteWal,
            bool mirrorToJsonLine,
            int maxPendingWrites,
            bool logVerbose)
        {
            _enabled = enabled;
            _pendingWrites = new BlockingCollection<DurableSessionEventRecord>(
                new ConcurrentQueue<DurableSessionEventRecord>(),
                Math.Max(64, maxPendingWrites));
            _cancelSource = new CancellationTokenSource();

            if (!_enabled)
            {
                _mode = "disabled";
                _backend = new NullEventStoreBackend();
                _writerTask = Task.CompletedTask;
                return;
            }

            _backend = BuildBackend(sqlitePath, jsonLinePath, preferSqliteWal, mirrorToJsonLine, logVerbose);
            _mode = _backend.Mode;
            _writerTask = Task.Run(() => WriterLoop(_cancelSource.Token), _cancelSource.Token);
        }

        public bool TryEnqueue(DurableSessionEventRecord record, out string error)
        {
            error = string.Empty;

            if (!_enabled)
            {
                return true;
            }

            if (record == null)
            {
                error = "Durable record is null.";
                return false;
            }

            EnsureRecordDefaults(record);

            try
            {
                if (_pendingWrites.IsAddingCompleted)
                {
                    error = "Durable event store is completing; cannot enqueue.";
                    Interlocked.Increment(ref _eventsDropped);
                    return false;
                }

                if (!_pendingWrites.TryAdd(record))
                {
                    error = "Durable event store queue is full.";
                    Interlocked.Increment(ref _eventsDropped);
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Interlocked.Increment(ref _eventsDropped);
                return false;
            }
        }

        public SessionEventStoreStatistics GetStatistics()
        {
            var outbox = _backend.GetOutboxStatistics();
            return new SessionEventStoreStatistics
            {
                pendingWrites = _enabled ? _pendingWrites.Count : 0,
                eventsPersisted = _eventsPersisted,
                eventsFailed = _eventsFailed,
                eventsDropped = _eventsDropped,
                outboxSupported = _backend.SupportsOutbox,
                outboxPending = outbox.pending,
                outboxInFlight = outbox.inFlight,
                outboxSynced = outbox.synced,
                outboxRetryCount = outbox.retryCount,
                mode = _mode ?? "unknown",
            };
        }

        public bool SupportsOutboxSync => _enabled && _backend.SupportsOutbox;

        public bool TryClaimOutboxBatch(
            int maxBatchSize,
            string workerId,
            out List<SessionOutboxBatchItem> batch,
            out string error)
        {
            batch = new List<SessionOutboxBatchItem>();
            error = string.Empty;

            if (!_enabled || !_backend.SupportsOutbox)
            {
                return false;
            }

            return _backend.TryClaimOutboxBatch(
                Math.Max(1, maxBatchSize),
                string.IsNullOrWhiteSpace(workerId) ? "unknown_worker" : workerId,
                out batch,
                out error);
        }

        public bool MarkOutboxBatchSynced(IReadOnlyList<string> eventIds, out string error)
        {
            error = string.Empty;
            if (!_enabled || !_backend.SupportsOutbox)
            {
                return false;
            }

            if (eventIds == null || eventIds.Count == 0)
            {
                return true;
            }

            return _backend.MarkOutboxBatchSynced(eventIds, out error);
        }

        public bool RescheduleOutboxBatch(IReadOnlyList<SessionOutboxRetryRecord> retryRecords, out string error)
        {
            error = string.Empty;
            if (!_enabled || !_backend.SupportsOutbox)
            {
                return false;
            }

            if (retryRecords == null || retryRecords.Count == 0)
            {
                return true;
            }

            return _backend.RescheduleOutboxBatch(retryRecords, out error);
        }

        public bool ForceOutboxPending(
            IReadOnlyList<string> eventIds,
            DateTime nextAttemptUtc,
            string reasonCode,
            out int rowsUpdated,
            out string error)
        {
            rowsUpdated = 0;
            error = string.Empty;
            if (!_enabled || !_backend.SupportsOutbox)
            {
                error = "Outbox is not supported by current backend.";
                return false;
            }

            if (eventIds == null || eventIds.Count == 0)
            {
                return true;
            }

            var normalizedNextAttemptUtc = nextAttemptUtc == default
                ? DateTime.UtcNow
                : nextAttemptUtc;
            if (normalizedNextAttemptUtc.Kind != DateTimeKind.Utc)
            {
                normalizedNextAttemptUtc = normalizedNextAttemptUtc.ToUniversalTime();
            }

            return _backend.ForceOutboxPending(
                eventIds,
                normalizedNextAttemptUtc,
                string.IsNullOrWhiteSpace(reasonCode) ? "MANUAL_RESYNC" : reasonCode,
                out rowsUpdated,
                out error);
        }

        public bool TryGetSessionSequenceIndex(
            string sessionId,
            out List<SessionSequenceIndexRecord> records,
            out string error)
        {
            records = new List<SessionSequenceIndexRecord>();
            error = string.Empty;

            if (!_enabled)
            {
                error = "Durable event store disabled.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                error = "Session id is required.";
                return false;
            }

            return _backend.TryGetSessionSequenceIndex(sessionId.Trim(), out records, out error);
        }

        public void Flush(TimeSpan timeout)
        {
            if (!_enabled)
            {
                return;
            }

            var deadline = DateTime.UtcNow.Add(timeout);
            while (_pendingWrites.Count > 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(5);
            }
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
                _pendingWrites.CompleteAdding();
                _cancelSource.Cancel();

                try
                {
                    _writerTask.Wait(2000);
                }
                catch (Exception)
                {
                    // Ignore worker wait failures during shutdown.
                }
            }
            finally
            {
                _backend.Dispose();
                _cancelSource.Dispose();
                _pendingWrites.Dispose();
            }
        }

        internal static string ComputeChecksum(DurableSessionEventRecord record)
        {
            var canonical = string.Concat(
                record?.eventId ?? string.Empty, "|",
                record?.sessionId ?? string.Empty, "|",
                record?.patientId ?? string.Empty, "|",
                record?.therapistId ?? string.Empty, "|",
                record?.deviceId ?? string.Empty, "|",
                record?.sequence.ToString(CultureInfo.InvariantCulture) ?? "0", "|",
                record?.eventType ?? string.Empty, "|",
                record?.eventVersion.ToString(CultureInfo.InvariantCulture) ?? "1", "|",
                record?.createdAtUtc ?? string.Empty, "|",
                record?.payloadJson ?? "{}");

            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                var builder = new StringBuilder(bytes.Length * 2);
                for (var i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static IEventStoreBackend BuildBackend(
            string sqlitePath,
            string jsonLinePath,
            bool preferSqliteWal,
            bool mirrorToJsonLine,
            bool logVerbose)
        {
            if (preferSqliteWal)
            {
                try
                {
                    var sqliteBackend = new SqliteWalEventStoreBackend(sqlitePath, mirrorToJsonLine, jsonLinePath, logVerbose);
                    Logger.Info("[SessionEventStore] SQLite WAL backend enabled.");
                    return sqliteBackend;
                }
                catch (Exception e)
                {
                    Logger.Warning($"[SessionEventStore] SQLite WAL backend unavailable: {e.Message}. Falling back to NDJSON.");
                }
            }

            return new JsonLineEventStoreBackend(jsonLinePath, logVerbose);
        }

        private static void EnsureRecordDefaults(DurableSessionEventRecord record)
        {
            if (string.IsNullOrWhiteSpace(record.eventId))
            {
                record.eventId = Guid.NewGuid().ToString();
            }

            if (string.IsNullOrWhiteSpace(record.sessionId))
            {
                record.sessionId = "unknown_session";
            }

            if (string.IsNullOrWhiteSpace(record.patientId))
            {
                record.patientId = "unknown_patient";
            }

            if (string.IsNullOrWhiteSpace(record.therapistId))
            {
                record.therapistId = "unknown_therapist";
            }

            if (string.IsNullOrWhiteSpace(record.deviceId))
            {
                record.deviceId = "unknown_device";
            }

            if (string.IsNullOrWhiteSpace(record.eventType))
            {
                record.eventType = "unknown_event";
            }

            if (record.eventVersion <= 0)
            {
                record.eventVersion = 1;
            }

            if (string.IsNullOrWhiteSpace(record.createdAtUtc))
            {
                record.createdAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            }

            if (string.IsNullOrWhiteSpace(record.payloadJson))
            {
                record.payloadJson = "{}";
            }
        }

        private void WriterLoop(CancellationToken cancellationToken)
        {
            try
            {
                foreach (var record in _pendingWrites.GetConsumingEnumerable(cancellationToken))
                {
                    if (record == null)
                    {
                        continue;
                    }

                    try
                    {
                        _backend.Persist(record);
                        Interlocked.Increment(ref _eventsPersisted);
                    }
                    catch (Exception e)
                    {
                        Interlocked.Increment(ref _eventsFailed);
                        Logger.Warning($"[SessionEventStore] Persist failed for {record.eventType}: {e.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown path.
            }
            catch (Exception e)
            {
                Interlocked.Increment(ref _eventsFailed);
                Logger.Error($"[SessionEventStore] Writer loop crashed: {e.Message}", e);
            }
        }

        private interface IEventStoreBackend : IDisposable
        {
            string Mode { get; }
            bool SupportsOutbox { get; }
            void Persist(DurableSessionEventRecord record);
            bool TryClaimOutboxBatch(
                int maxBatchSize,
                string workerId,
                out List<SessionOutboxBatchItem> batch,
                out string error);
            bool MarkOutboxBatchSynced(IReadOnlyList<string> eventIds, out string error);
            bool RescheduleOutboxBatch(IReadOnlyList<SessionOutboxRetryRecord> retryRecords, out string error);
            bool ForceOutboxPending(
                IReadOnlyList<string> eventIds,
                DateTime nextAttemptUtc,
                string reasonCode,
                out int rowsUpdated,
                out string error);
            bool TryGetSessionSequenceIndex(
                string sessionId,
                out List<SessionSequenceIndexRecord> records,
                out string error);
            SessionOutboxStatistics GetOutboxStatistics();
        }

        private sealed class NullEventStoreBackend : IEventStoreBackend
        {
            public string Mode => "disabled";
            public bool SupportsOutbox => false;

            public void Persist(DurableSessionEventRecord record)
            {
                // No-op
            }

            public bool TryClaimOutboxBatch(
                int maxBatchSize,
                string workerId,
                out List<SessionOutboxBatchItem> batch,
                out string error)
            {
                batch = new List<SessionOutboxBatchItem>();
                error = "Outbox is not supported by disabled backend.";
                return false;
            }

            public bool MarkOutboxBatchSynced(IReadOnlyList<string> eventIds, out string error)
            {
                error = "Outbox is not supported by disabled backend.";
                return false;
            }

            public bool RescheduleOutboxBatch(IReadOnlyList<SessionOutboxRetryRecord> retryRecords, out string error)
            {
                error = "Outbox is not supported by disabled backend.";
                return false;
            }

            public bool ForceOutboxPending(
                IReadOnlyList<string> eventIds,
                DateTime nextAttemptUtc,
                string reasonCode,
                out int rowsUpdated,
                out string error)
            {
                rowsUpdated = 0;
                error = "Outbox is not supported by disabled backend.";
                return false;
            }

            public bool TryGetSessionSequenceIndex(
                string sessionId,
                out List<SessionSequenceIndexRecord> records,
                out string error)
            {
                records = new List<SessionSequenceIndexRecord>();
                error = "Sequence index is not supported by disabled backend.";
                return false;
            }

            public SessionOutboxStatistics GetOutboxStatistics()
            {
                return default;
            }

            public void Dispose()
            {
                // No-op
            }
        }

        private sealed class JsonLineEventStoreBackend : IEventStoreBackend
        {
            private readonly string _jsonLinePath;
            private readonly bool _logVerbose;
            private readonly object _writeLock = new object();
            private readonly Dictionary<string, DurableSessionEventRecord> _recordsByEventId =
                new Dictionary<string, DurableSessionEventRecord>(StringComparer.Ordinal);
            private readonly Dictionary<string, JsonLineOutboxRow> _outboxByEventId =
                new Dictionary<string, JsonLineOutboxRow>(StringComparer.Ordinal);
            private readonly Dictionary<string, long> _lastSequenceBySession =
                new Dictionary<string, long>(StringComparer.Ordinal);

            public JsonLineEventStoreBackend(string jsonLinePath, bool logVerbose)
            {
                _jsonLinePath = jsonLinePath;
                _logVerbose = logVerbose;

                if (string.IsNullOrWhiteSpace(_jsonLinePath))
                {
                    throw new InvalidOperationException("NDJSON path is not configured.");
                }

                var directory = Path.GetDirectoryName(_jsonLinePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                LoadExistingRecords();
            }

            public string Mode => "ndjson";
            public bool SupportsOutbox => true;

            public void Persist(DurableSessionEventRecord record)
            {
                lock (_writeLock)
                {
                    if (record == null)
                    {
                        return;
                    }

                    if (record.sequence <= 0)
                    {
                        record.sequence = ResolveNextSequence(record.sessionId);
                    }

                    if (string.IsNullOrWhiteSpace(record.checksum))
                    {
                        record.checksum = ComputeChecksum(record);
                    }

                    if (_recordsByEventId.ContainsKey(record.eventId))
                    {
                        return;
                    }

                    _recordsByEventId[record.eventId] = record;
                    TrackSessionSequence(record.sessionId, record.sequence);
                    EnsureOutboxRow(record.eventId, record.createdAtUtc);

                    var line = JsonUtility.ToJson(record);
                    File.AppendAllText(_jsonLinePath, line + Environment.NewLine, Encoding.UTF8);
                }

                if (_logVerbose)
                {
                    Logger.Debug($"[SessionEventStore] NDJSON persisted {record.eventType} ({record.sessionId})");
                }
            }

            public bool TryClaimOutboxBatch(
                int maxBatchSize,
                string workerId,
                out List<SessionOutboxBatchItem> batch,
                out string error)
            {
                batch = new List<SessionOutboxBatchItem>();
                error = string.Empty;

                var safeBatchSize = Math.Max(1, maxBatchSize);
                var safeWorkerId = string.IsNullOrWhiteSpace(workerId) ? "default_worker" : workerId.Trim();
                var nowUtc = DateTime.UtcNow;

                lock (_writeLock)
                {
                    try
                    {
                        var candidates = new List<KeyValuePair<string, JsonLineOutboxRow>>();
                        foreach (var pair in _outboxByEventId)
                        {
                            var row = pair.Value;
                            if (!string.Equals(row.status, "PENDING", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (row.nextAttemptUtc > nowUtc)
                            {
                                continue;
                            }

                            candidates.Add(pair);
                        }

                        candidates.Sort((a, b) =>
                        {
                            var nextAttemptCompare = a.Value.nextAttemptUtc.CompareTo(b.Value.nextAttemptUtc);
                            if (nextAttemptCompare != 0)
                            {
                                return nextAttemptCompare;
                            }

                            var createdCompare = a.Value.createdAtUtc.CompareTo(b.Value.createdAtUtc);
                            if (createdCompare != 0)
                            {
                                return createdCompare;
                            }

                            return string.CompareOrdinal(a.Key, b.Key);
                        });

                        var count = 0;
                        for (var i = 0; i < candidates.Count && count < safeBatchSize; i++)
                        {
                            var eventId = candidates[i].Key;
                            var row = candidates[i].Value;

                            if (!_recordsByEventId.TryGetValue(eventId, out var record) || record == null)
                            {
                                row.status = "SYNCED";
                                row.lastError = "MISSING_SESSION_EVENT";
                                row.syncedAtUtc = nowUtc;
                                row.lockedBy = string.Empty;
                                row.lockedAtUtc = default;
                                continue;
                            }

                            row.status = "IN_FLIGHT";
                            row.attemptCount += 1;
                            row.lastAttemptUtc = nowUtc;
                            row.lockedBy = safeWorkerId;
                            row.lockedAtUtc = nowUtc;
                            row.syncedAtUtc = default;

                            batch.Add(new SessionOutboxBatchItem
                            {
                                record = record,
                                attemptCount = row.attemptCount,
                            });

                            count++;
                        }

                        return true;
                    }
                    catch (Exception e)
                    {
                        error = e.Message;
                        return false;
                    }
                }
            }

            public bool MarkOutboxBatchSynced(IReadOnlyList<string> eventIds, out string error)
            {
                error = string.Empty;
                if (eventIds == null || eventIds.Count == 0)
                {
                    return true;
                }

                var nowUtc = DateTime.UtcNow;
                lock (_writeLock)
                {
                    try
                    {
                        for (var i = 0; i < eventIds.Count; i++)
                        {
                            var eventId = eventIds[i];
                            if (string.IsNullOrWhiteSpace(eventId))
                            {
                                continue;
                            }

                            if (!_outboxByEventId.TryGetValue(eventId.Trim(), out var row))
                            {
                                continue;
                            }

                            row.status = "SYNCED";
                            row.syncedAtUtc = nowUtc;
                            row.lockedBy = string.Empty;
                            row.lockedAtUtc = default;
                            row.lastError = string.Empty;
                        }

                        return true;
                    }
                    catch (Exception e)
                    {
                        error = e.Message;
                        return false;
                    }
                }
            }

            public bool RescheduleOutboxBatch(IReadOnlyList<SessionOutboxRetryRecord> retryRecords, out string error)
            {
                error = string.Empty;
                if (retryRecords == null || retryRecords.Count == 0)
                {
                    return true;
                }

                lock (_writeLock)
                {
                    try
                    {
                        for (var i = 0; i < retryRecords.Count; i++)
                        {
                            var retryRecord = retryRecords[i];
                            if (string.IsNullOrWhiteSpace(retryRecord.eventId))
                            {
                                continue;
                            }

                            var eventId = retryRecord.eventId.Trim();
                            if (!_outboxByEventId.TryGetValue(eventId, out var row))
                            {
                                continue;
                            }

                            if (string.Equals(row.status, "SYNCED", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            var nextAttemptUtc = retryRecord.nextAttemptUtc == default
                                ? DateTime.UtcNow
                                : retryRecord.nextAttemptUtc;
                            if (nextAttemptUtc.Kind != DateTimeKind.Utc)
                            {
                                nextAttemptUtc = nextAttemptUtc.ToUniversalTime();
                            }

                            row.status = "PENDING";
                            row.nextAttemptUtc = nextAttemptUtc;
                            row.lastError = string.IsNullOrWhiteSpace(retryRecord.errorCode)
                                ? "OUTBOX_RETRY"
                                : retryRecord.errorCode.Trim();
                            row.lockedBy = string.Empty;
                            row.lockedAtUtc = default;
                            row.syncedAtUtc = default;
                        }

                        return true;
                    }
                    catch (Exception e)
                    {
                        error = e.Message;
                        return false;
                    }
                }
            }

            public bool ForceOutboxPending(
                IReadOnlyList<string> eventIds,
                DateTime nextAttemptUtc,
                string reasonCode,
                out int rowsUpdated,
                out string error)
            {
                rowsUpdated = 0;
                error = string.Empty;
                if (eventIds == null || eventIds.Count == 0)
                {
                    return true;
                }

                var nextAttempt = nextAttemptUtc == default
                    ? DateTime.UtcNow
                    : nextAttemptUtc;
                if (nextAttempt.Kind != DateTimeKind.Utc)
                {
                    nextAttempt = nextAttempt.ToUniversalTime();
                }

                var safeReasonCode = string.IsNullOrWhiteSpace(reasonCode)
                    ? "MANUAL_RESYNC"
                    : reasonCode.Trim();

                lock (_writeLock)
                {
                    try
                    {
                        for (var i = 0; i < eventIds.Count; i++)
                        {
                            var eventId = eventIds[i];
                            if (string.IsNullOrWhiteSpace(eventId))
                            {
                                continue;
                            }

                            var safeEventId = eventId.Trim();
                            if (!_recordsByEventId.TryGetValue(safeEventId, out var record) || record == null)
                            {
                                continue;
                            }

                            var row = EnsureOutboxRow(safeEventId, record.createdAtUtc);
                            row.status = "PENDING";
                            row.attemptCount = 0;
                            row.nextAttemptUtc = nextAttempt;
                            row.lastError = safeReasonCode;
                            row.lockedBy = string.Empty;
                            row.lockedAtUtc = default;
                            row.syncedAtUtc = default;
                            rowsUpdated++;
                        }

                        return true;
                    }
                    catch (Exception e)
                    {
                        error = e.Message;
                        return false;
                    }
                }
            }

            public bool TryGetSessionSequenceIndex(
                string sessionId,
                out List<SessionSequenceIndexRecord> records,
                out string error)
            {
                records = new List<SessionSequenceIndexRecord>();
                error = string.Empty;
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    error = "Session id is required.";
                    return false;
                }

                var normalizedSessionId = sessionId.Trim();

                lock (_writeLock)
                {
                    try
                    {
                        foreach (var pair in _recordsByEventId)
                        {
                            var record = pair.Value;
                            if (record == null)
                            {
                                continue;
                            }

                            if (!string.Equals(record.sessionId, normalizedSessionId, StringComparison.Ordinal))
                            {
                                continue;
                            }

                            var status = "UNKNOWN";
                            if (_outboxByEventId.TryGetValue(record.eventId, out var row))
                            {
                                status = string.IsNullOrWhiteSpace(row.status) ? "UNKNOWN" : row.status;
                            }

                            records.Add(new SessionSequenceIndexRecord
                            {
                                eventId = record.eventId,
                                sequence = record.sequence,
                                outboxStatus = status,
                            });
                        }

                        records.Sort((a, b) =>
                        {
                            var sequenceCompare = a.sequence.CompareTo(b.sequence);
                            if (sequenceCompare != 0)
                            {
                                return sequenceCompare;
                            }

                            return string.CompareOrdinal(a.eventId, b.eventId);
                        });

                        return true;
                    }
                    catch (Exception e)
                    {
                        error = e.Message;
                        return false;
                    }
                }
            }

            public SessionOutboxStatistics GetOutboxStatistics()
            {
                lock (_writeLock)
                {
                    var stats = default(SessionOutboxStatistics);
                    foreach (var pair in _outboxByEventId)
                    {
                        var row = pair.Value;
                        if (row == null)
                        {
                            continue;
                        }

                        if (string.Equals(row.status, "PENDING", StringComparison.OrdinalIgnoreCase))
                        {
                            stats.pending++;
                        }
                        else if (string.Equals(row.status, "IN_FLIGHT", StringComparison.OrdinalIgnoreCase))
                        {
                            stats.inFlight++;
                        }
                        else if (string.Equals(row.status, "SYNCED", StringComparison.OrdinalIgnoreCase))
                        {
                            stats.synced++;
                        }

                        if (row.attemptCount > 1)
                        {
                            stats.retryCount += row.attemptCount - 1;
                        }
                    }

                    return stats;
                }
            }

            public void Dispose()
            {
                // No-op
            }

            private void LoadExistingRecords()
            {
                if (!File.Exists(_jsonLinePath))
                {
                    return;
                }

                var loaded = 0;
                var lines = File.ReadAllLines(_jsonLinePath);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    DurableSessionEventRecord record;
                    try
                    {
                        record = JsonUtility.FromJson<DurableSessionEventRecord>(line);
                    }
                    catch
                    {
                        continue;
                    }

                    if (record == null || string.IsNullOrWhiteSpace(record.eventId))
                    {
                        continue;
                    }

                    if (record.sequence <= 0)
                    {
                        record.sequence = ResolveNextSequence(record.sessionId);
                    }
                    else
                    {
                        TrackSessionSequence(record.sessionId, record.sequence);
                    }

                    if (string.IsNullOrWhiteSpace(record.checksum))
                    {
                        record.checksum = ComputeChecksum(record);
                    }

                    if (_recordsByEventId.ContainsKey(record.eventId))
                    {
                        continue;
                    }

                    _recordsByEventId[record.eventId] = record;
                    EnsureOutboxRow(record.eventId, record.createdAtUtc);
                    loaded++;
                }

                if (_logVerbose && loaded > 0)
                {
                    Logger.Debug($"[SessionEventStore] NDJSON loaded {loaded} persisted events.");
                }
            }

            private long ResolveNextSequence(string sessionId)
            {
                var normalizedSessionId = string.IsNullOrWhiteSpace(sessionId)
                    ? "unknown_session"
                    : sessionId.Trim();

                if (_lastSequenceBySession.TryGetValue(normalizedSessionId, out var current))
                {
                    current++;
                    _lastSequenceBySession[normalizedSessionId] = current;
                    return current;
                }

                _lastSequenceBySession[normalizedSessionId] = 1;
                return 1;
            }

            private void TrackSessionSequence(string sessionId, long sequence)
            {
                var normalizedSessionId = string.IsNullOrWhiteSpace(sessionId)
                    ? "unknown_session"
                    : sessionId.Trim();

                if (_lastSequenceBySession.TryGetValue(normalizedSessionId, out var current))
                {
                    if (sequence > current)
                    {
                        _lastSequenceBySession[normalizedSessionId] = sequence;
                    }

                    return;
                }

                _lastSequenceBySession[normalizedSessionId] = Math.Max(0, sequence);
            }

            private JsonLineOutboxRow EnsureOutboxRow(string eventId, string createdAtUtcText)
            {
                if (_outboxByEventId.TryGetValue(eventId, out var existing) && existing != null)
                {
                    return existing;
                }

                var createdAtUtc = ParseUtcOrDefault(createdAtUtcText, DateTime.UtcNow);
                var row = new JsonLineOutboxRow
                {
                    status = "PENDING",
                    attemptCount = 0,
                    nextAttemptUtc = createdAtUtc,
                    lastAttemptUtc = default,
                    lastError = string.Empty,
                    lockedBy = string.Empty,
                    lockedAtUtc = default,
                    syncedAtUtc = default,
                    createdAtUtc = createdAtUtc,
                };

                _outboxByEventId[eventId] = row;
                return row;
            }

            private static DateTime ParseUtcOrDefault(string text, DateTime fallbackUtc)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return fallbackUtc;
                }

                if (DateTime.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var parsed))
                {
                    if (parsed.Kind != DateTimeKind.Utc)
                    {
                        parsed = parsed.ToUniversalTime();
                    }

                    return parsed;
                }

                return fallbackUtc;
            }

            private sealed class JsonLineOutboxRow
            {
                public string status;
                public int attemptCount;
                public DateTime nextAttemptUtc;
                public DateTime lastAttemptUtc;
                public string lastError;
                public string lockedBy;
                public DateTime lockedAtUtc;
                public DateTime syncedAtUtc;
                public DateTime createdAtUtc;
            }
        }

        private sealed class SqliteWalEventStoreBackend : IEventStoreBackend
        {
            private readonly DbConnection _connection;
            private readonly object _writeLock = new object();
            private readonly Dictionary<string, long> _lastSequenceBySession =
                new Dictionary<string, long>(StringComparer.Ordinal);
            private readonly bool _mirrorToJsonLine;
            private readonly string _jsonLinePath;
            private readonly bool _logVerbose;
            private readonly string _nowFormat = "O";

            public SqliteWalEventStoreBackend(
                string sqlitePath,
                bool mirrorToJsonLine,
                string jsonLinePath,
                bool logVerbose)
            {
                if (string.IsNullOrWhiteSpace(sqlitePath))
                {
                    throw new InvalidOperationException("SQLite path is not configured.");
                }

                var connectionType = Type.GetType("Mono.Data.Sqlite.SqliteConnection, Mono.Data.Sqlite");
                if (connectionType == null)
                {
                    throw new InvalidOperationException("Mono.Data.Sqlite provider is not available.");
                }

                var directory = Path.GetDirectoryName(sqlitePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var connectionString = string.Format(CultureInfo.InvariantCulture, "URI=file:{0}", sqlitePath);
                _connection = Activator.CreateInstance(connectionType, connectionString) as DbConnection;
                if (_connection == null)
                {
                    throw new InvalidOperationException("Failed to create SQLite connection.");
                }

                _connection.Open();
                _mirrorToJsonLine = mirrorToJsonLine;
                _jsonLinePath = jsonLinePath;
                _logVerbose = logVerbose;

                if (_mirrorToJsonLine && !string.IsNullOrWhiteSpace(_jsonLinePath))
                {
                    var mirrorDirectory = Path.GetDirectoryName(_jsonLinePath);
                    if (!string.IsNullOrWhiteSpace(mirrorDirectory))
                    {
                        Directory.CreateDirectory(mirrorDirectory);
                    }
                }

                ExecuteNonQuery("PRAGMA journal_mode=WAL;");
                ExecuteNonQuery("PRAGMA synchronous=NORMAL;");
                ExecuteNonQuery("PRAGMA temp_store=MEMORY;");
                ExecuteNonQuery("PRAGMA wal_autocheckpoint=100;");

                ExecuteNonQuery(
                    "CREATE TABLE IF NOT EXISTS session_events (" +
                    "id INTEGER PRIMARY KEY AUTOINCREMENT," +
                    "event_id TEXT NOT NULL UNIQUE," +
                    "session_id TEXT NOT NULL," +
                    "patient_id TEXT NOT NULL," +
                    "therapist_id TEXT NOT NULL," +
                    "device_id TEXT NOT NULL," +
                    "sequence INTEGER NOT NULL," +
                    "event_type TEXT NOT NULL," +
                    "event_version INTEGER NOT NULL," +
                    "created_at_utc TEXT NOT NULL," +
                    "payload_json TEXT NOT NULL," +
                    "checksum TEXT NOT NULL" +
                    ");");

                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_session_events_session_sequence " +
                    "ON session_events(session_id, sequence);");

                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_session_events_created_at " +
                    "ON session_events(created_at_utc);");

                ExecuteNonQuery(
                    "CREATE TABLE IF NOT EXISTS sync_outbox (" +
                    "event_id TEXT PRIMARY KEY," +
                    "status TEXT NOT NULL," +
                    "attempt_count INTEGER NOT NULL DEFAULT 0," +
                    "next_attempt_utc TEXT NOT NULL," +
                    "last_attempt_utc TEXT," +
                    "last_error TEXT," +
                    "locked_by TEXT," +
                    "locked_at_utc TEXT," +
                    "synced_at_utc TEXT," +
                    "created_at_utc TEXT NOT NULL" +
                    ");");

                ExecuteNonQuery(
                    "CREATE INDEX IF NOT EXISTS idx_sync_outbox_status_next_attempt " +
                    "ON sync_outbox(status, next_attempt_utc);");

                ExecuteNonQuery(
                    "UPDATE sync_outbox " +
                    "SET status='PENDING', locked_by=NULL, locked_at_utc=NULL " +
                    "WHERE status='IN_FLIGHT';");
            }

            public string Mode => _mirrorToJsonLine ? "sqlite_wal+ndjson_mirror" : "sqlite_wal";
            public bool SupportsOutbox => true;

            public void Persist(DurableSessionEventRecord record)
            {
                lock (_writeLock)
                {
                    if (record.sequence <= 0)
                    {
                        record.sequence = ResolveNextSequence(record.sessionId);
                    }

                    if (string.IsNullOrWhiteSpace(record.checksum))
                    {
                        record.checksum = ComputeChecksum(record);
                    }

                    using (var transaction = _connection.BeginTransaction())
                    {
                        using (var command = _connection.CreateCommand())
                        {
                            command.Transaction = transaction;
                            command.CommandText =
                                "INSERT OR IGNORE INTO session_events(" +
                                "event_id, session_id, patient_id, therapist_id, device_id, sequence, event_type, event_version, created_at_utc, payload_json, checksum) " +
                                "VALUES (@event_id, @session_id, @patient_id, @therapist_id, @device_id, @sequence, @event_type, @event_version, @created_at_utc, @payload_json, @checksum);";

                            AddParameter(command, "@event_id", record.eventId);
                            AddParameter(command, "@session_id", record.sessionId);
                            AddParameter(command, "@patient_id", record.patientId);
                            AddParameter(command, "@therapist_id", record.therapistId);
                            AddParameter(command, "@device_id", record.deviceId);
                            AddParameter(command, "@sequence", record.sequence);
                            AddParameter(command, "@event_type", record.eventType);
                            AddParameter(command, "@event_version", record.eventVersion);
                            AddParameter(command, "@created_at_utc", record.createdAtUtc);
                            AddParameter(command, "@payload_json", record.payloadJson);
                            AddParameter(command, "@checksum", record.checksum);

                            command.ExecuteNonQuery();
                        }

                        using (var outboxCommand = _connection.CreateCommand())
                        {
                            outboxCommand.Transaction = transaction;
                            outboxCommand.CommandText =
                                "INSERT OR IGNORE INTO sync_outbox(" +
                                "event_id, status, attempt_count, next_attempt_utc, created_at_utc) " +
                                "VALUES (@event_id, 'PENDING', 0, @next_attempt_utc, @created_at_utc);";

                            AddParameter(outboxCommand, "@event_id", record.eventId);
                            AddParameter(outboxCommand, "@next_attempt_utc", record.createdAtUtc);
                            AddParameter(outboxCommand, "@created_at_utc", record.createdAtUtc);

                            outboxCommand.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }

                    TrackSessionSequence(record.sessionId, record.sequence);

                    if (_mirrorToJsonLine && !string.IsNullOrWhiteSpace(_jsonLinePath))
                    {
                        var line = JsonUtility.ToJson(record);
                        File.AppendAllText(_jsonLinePath, line + Environment.NewLine, Encoding.UTF8);
                    }

                    if (_logVerbose)
                    {
                        Logger.Debug(
                            $"[SessionEventStore] SQLite persisted {record.eventType} seq={record.sequence} session={record.sessionId}");
                    }
                }
            }

            public bool TryClaimOutboxBatch(
                int maxBatchSize,
                string workerId,
                out List<SessionOutboxBatchItem> batch,
                out string error)
            {
                batch = new List<SessionOutboxBatchItem>();
                error = string.Empty;

                var safeBatchSize = Math.Max(1, maxBatchSize);
                var safeWorkerId = string.IsNullOrWhiteSpace(workerId) ? "default_worker" : workerId.Trim();
                var nowUtcText = DateTime.UtcNow.ToString(_nowFormat, CultureInfo.InvariantCulture);

                lock (_writeLock)
                {
                    try
                    {
                        var eventIdsToClaim = new List<string>(safeBatchSize);
                        using (var transaction = _connection.BeginTransaction())
                        {
                            using (var selectCommand = _connection.CreateCommand())
                            {
                                selectCommand.Transaction = transaction;
                                selectCommand.CommandText =
                                    "SELECT event_id " +
                                    "FROM sync_outbox " +
                                    "WHERE status='PENDING' AND next_attempt_utc <= @now_utc " +
                                    "ORDER BY next_attempt_utc ASC, created_at_utc ASC " +
                                    "LIMIT @limit;";
                                AddParameter(selectCommand, "@now_utc", nowUtcText);
                                AddParameter(selectCommand, "@limit", safeBatchSize);

                                using (var reader = selectCommand.ExecuteReader())
                                {
                                    while (reader.Read())
                                    {
                                        var eventId = Convert.ToString(reader["event_id"], CultureInfo.InvariantCulture);
                                        if (!string.IsNullOrWhiteSpace(eventId))
                                        {
                                            eventIdsToClaim.Add(eventId);
                                        }
                                    }
                                }
                            }

                            if (eventIdsToClaim.Count == 0)
                            {
                                transaction.Commit();
                                return true;
                            }

                            foreach (var eventId in eventIdsToClaim)
                            {
                                using (var claimCommand = _connection.CreateCommand())
                                {
                                    claimCommand.Transaction = transaction;
                                    claimCommand.CommandText =
                                        "UPDATE sync_outbox " +
                                        "SET status='IN_FLIGHT', " +
                                        "attempt_count=attempt_count + 1, " +
                                        "last_attempt_utc=@now_utc, " +
                                        "locked_by=@worker_id, " +
                                        "locked_at_utc=@now_utc " +
                                        "WHERE event_id=@event_id AND status='PENDING';";
                                    AddParameter(claimCommand, "@now_utc", nowUtcText);
                                    AddParameter(claimCommand, "@worker_id", safeWorkerId);
                                    AddParameter(claimCommand, "@event_id", eventId);

                                    if (claimCommand.ExecuteNonQuery() <= 0)
                                    {
                                        continue;
                                    }
                                }

                                if (TryReadOutboxBatchItem(eventId, transaction, out var outboxItem))
                                {
                                    batch.Add(outboxItem);
                                }
                                else
                                {
                                    // If event payload is missing, stop retrying this row forever.
                                    MarkOutboxRowSyncedInternal(
                                        eventId,
                                        nowUtcText,
                                        "MISSING_SESSION_EVENT",
                                        transaction);
                                }
                            }

                            transaction.Commit();
                        }

                        return true;
                    }
                    catch (Exception e)
                    {
                        error = e.Message;
                        return false;
                    }
                }
            }

            public bool MarkOutboxBatchSynced(IReadOnlyList<string> eventIds, out string error)
            {
                error = string.Empty;
                if (eventIds == null || eventIds.Count == 0)
                {
                    return true;
                }

                var nowUtcText = DateTime.UtcNow.ToString(_nowFormat, CultureInfo.InvariantCulture);
                lock (_writeLock)
                {
                    try
                    {
                        using (var transaction = _connection.BeginTransaction())
                        {
                            for (var i = 0; i < eventIds.Count; i++)
                            {
                                var eventId = eventIds[i];
                                if (string.IsNullOrWhiteSpace(eventId))
                                {
                                    continue;
                                }

                                MarkOutboxRowSyncedInternal(eventId.Trim(), nowUtcText, null, transaction);
                            }

                            transaction.Commit();
                        }

                        return true;
                    }
                    catch (Exception e)
                    {
                        error = e.Message;
                        return false;
                    }
                }
            }

            public bool RescheduleOutboxBatch(IReadOnlyList<SessionOutboxRetryRecord> retryRecords, out string error)
            {
                error = string.Empty;
                if (retryRecords == null || retryRecords.Count == 0)
                {
                    return true;
                }

                lock (_writeLock)
                {
                    try
                    {
                        using (var transaction = _connection.BeginTransaction())
                        {
                            for (var i = 0; i < retryRecords.Count; i++)
                            {
                                var retryRecord = retryRecords[i];
                                if (string.IsNullOrWhiteSpace(retryRecord.eventId))
                                {
                                    continue;
                                }

                                var nextAttemptUtc = retryRecord.nextAttemptUtc == default
                                    ? DateTime.UtcNow
                                    : retryRecord.nextAttemptUtc;
                                if (nextAttemptUtc.Kind != DateTimeKind.Utc)
                                {
                                    nextAttemptUtc = nextAttemptUtc.ToUniversalTime();
                                }

                                using (var command = _connection.CreateCommand())
                                {
                                    command.Transaction = transaction;
                                    command.CommandText =
                                        "UPDATE sync_outbox " +
                                        "SET status='PENDING', " +
                                        "next_attempt_utc=@next_attempt_utc, " +
                                        "last_error=@last_error, " +
                                        "locked_by=NULL, " +
                                        "locked_at_utc=NULL, " +
                                        "synced_at_utc=NULL " +
                                        "WHERE event_id=@event_id AND status <> 'SYNCED';";
                                    AddParameter(
                                        command,
                                        "@next_attempt_utc",
                                        nextAttemptUtc.ToString(_nowFormat, CultureInfo.InvariantCulture));
                                    AddParameter(command, "@last_error", retryRecord.errorCode ?? "OUTBOX_RETRY");
                                    AddParameter(command, "@event_id", retryRecord.eventId.Trim());
                                    command.ExecuteNonQuery();
                                }
                            }

                            transaction.Commit();
                        }

                        return true;
                    }
                    catch (Exception e)
                    {
                        error = e.Message;
                        return false;
                    }
                }
            }

            public bool ForceOutboxPending(
                IReadOnlyList<string> eventIds,
                DateTime nextAttemptUtc,
                string reasonCode,
                out int rowsUpdated,
                out string error)
            {
                rowsUpdated = 0;
                error = string.Empty;
                if (eventIds == null || eventIds.Count == 0)
                {
                    return true;
                }

                var nextAttempt = nextAttemptUtc == default
                    ? DateTime.UtcNow
                    : nextAttemptUtc;
                if (nextAttempt.Kind != DateTimeKind.Utc)
                {
                    nextAttempt = nextAttempt.ToUniversalTime();
                }

                var safeReasonCode = string.IsNullOrWhiteSpace(reasonCode)
                    ? "MANUAL_RESYNC"
                    : reasonCode.Trim();
                var nextAttemptUtcText = nextAttempt.ToString(_nowFormat, CultureInfo.InvariantCulture);

                lock (_writeLock)
                {
                    try
                    {
                        using (var transaction = _connection.BeginTransaction())
                        {
                            for (var i = 0; i < eventIds.Count; i++)
                            {
                                var eventId = eventIds[i];
                                if (string.IsNullOrWhiteSpace(eventId))
                                {
                                    continue;
                                }

                                var safeEventId = eventId.Trim();
                                using (var insertCommand = _connection.CreateCommand())
                                {
                                    insertCommand.Transaction = transaction;
                                    insertCommand.CommandText =
                                        "INSERT OR IGNORE INTO sync_outbox(" +
                                        "event_id, status, attempt_count, next_attempt_utc, created_at_utc, last_error) " +
                                        "SELECT se.event_id, 'PENDING', 0, @next_attempt_utc, se.created_at_utc, @last_error " +
                                        "FROM session_events se " +
                                        "WHERE se.event_id = @event_id;";
                                    AddParameter(insertCommand, "@next_attempt_utc", nextAttemptUtcText);
                                    AddParameter(insertCommand, "@last_error", safeReasonCode);
                                    AddParameter(insertCommand, "@event_id", safeEventId);
                                    insertCommand.ExecuteNonQuery();
                                }

                                using (var updateCommand = _connection.CreateCommand())
                                {
                                    updateCommand.Transaction = transaction;
                                    updateCommand.CommandText =
                                        "UPDATE sync_outbox " +
                                        "SET status='PENDING', " +
                                        "attempt_count=0, " +
                                        "next_attempt_utc=@next_attempt_utc, " +
                                        "last_error=@last_error, " +
                                        "locked_by=NULL, " +
                                        "locked_at_utc=NULL, " +
                                        "synced_at_utc=NULL " +
                                        "WHERE event_id=@event_id;";
                                    AddParameter(updateCommand, "@next_attempt_utc", nextAttemptUtcText);
                                    AddParameter(updateCommand, "@last_error", safeReasonCode);
                                    AddParameter(updateCommand, "@event_id", safeEventId);
                                    rowsUpdated += Math.Max(0, updateCommand.ExecuteNonQuery());
                                }
                            }

                            transaction.Commit();
                        }

                        return true;
                    }
                    catch (Exception e)
                    {
                        error = e.Message;
                        return false;
                    }
                }
            }

            public bool TryGetSessionSequenceIndex(
                string sessionId,
                out List<SessionSequenceIndexRecord> records,
                out string error)
            {
                records = new List<SessionSequenceIndexRecord>();
                error = string.Empty;

                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    error = "Session id is required.";
                    return false;
                }

                lock (_writeLock)
                {
                    try
                    {
                        using (var command = _connection.CreateCommand())
                        {
                            command.CommandText =
                                "SELECT se.event_id, se.sequence, COALESCE(so.status, 'UNKNOWN') AS outbox_status " +
                                "FROM session_events se " +
                                "LEFT JOIN sync_outbox so ON so.event_id = se.event_id " +
                                "WHERE se.session_id = @session_id " +
                                "ORDER BY se.sequence ASC, se.event_id ASC;";
                            AddParameter(command, "@session_id", sessionId.Trim());

                            using (var reader = command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    records.Add(new SessionSequenceIndexRecord
                                    {
                                        eventId = ReadString(reader, "event_id"),
                                        sequence = ReadInt64(reader, "sequence", 0L),
                                        outboxStatus = ReadString(reader, "outbox_status"),
                                    });
                                }
                            }
                        }

                        return true;
                    }
                    catch (Exception e)
                    {
                        error = e.Message;
                        return false;
                    }
                }
            }

            public SessionOutboxStatistics GetOutboxStatistics()
            {
                lock (_writeLock)
                {
                    try
                    {
                        return new SessionOutboxStatistics
                        {
                            pending = ExecuteScalarInt(
                                "SELECT COUNT(1) FROM sync_outbox WHERE status='PENDING';",
                                0),
                            inFlight = ExecuteScalarInt(
                                "SELECT COUNT(1) FROM sync_outbox WHERE status='IN_FLIGHT';",
                                0),
                            synced = ExecuteScalarInt(
                                "SELECT COUNT(1) FROM sync_outbox WHERE status='SYNCED';",
                                0),
                            retryCount = ExecuteScalarInt(
                                "SELECT COALESCE(SUM(CASE WHEN attempt_count > 1 THEN attempt_count - 1 ELSE 0 END), 0) " +
                                "FROM sync_outbox;",
                                0),
                        };
                    }
                    catch (Exception e)
                    {
                        Logger.Warning($"[SessionEventStore] Failed to read outbox statistics: {e.Message}");
                        return default;
                    }
                }
            }

            public void Dispose()
            {
                try
                {
                    _connection.Close();
                }
                catch (Exception)
                {
                    // Ignore close errors on shutdown.
                }

                _connection.Dispose();
            }

            private void ExecuteNonQuery(string sql)
            {
                using (var command = _connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.ExecuteNonQuery();
                }
            }

            private long ResolveNextSequence(string sessionId)
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    return 1;
                }

                if (_lastSequenceBySession.TryGetValue(sessionId, out var lastSequence))
                {
                    return lastSequence + 1;
                }

                using (var command = _connection.CreateCommand())
                {
                    command.CommandText =
                        "SELECT COALESCE(MAX(sequence), 0) FROM session_events WHERE session_id = @session_id;";
                    AddParameter(command, "@session_id", sessionId);
                    var scalar = command.ExecuteScalar();

                    long maxSequence = 0;
                    if (scalar != null && scalar != DBNull.Value)
                    {
                        long.TryParse(
                            Convert.ToString(scalar, CultureInfo.InvariantCulture),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out maxSequence);
                    }

                    _lastSequenceBySession[sessionId] = maxSequence;
                    return maxSequence + 1;
                }
            }

            private void TrackSessionSequence(string sessionId, long sequence)
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    return;
                }

                if (_lastSequenceBySession.TryGetValue(sessionId, out var current))
                {
                    if (sequence > current)
                    {
                        _lastSequenceBySession[sessionId] = sequence;
                    }
                    return;
                }

                _lastSequenceBySession[sessionId] = sequence;
            }

            private static void AddParameter(DbCommand command, string name, object value)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }

            private bool TryReadOutboxBatchItem(
                string eventId,
                DbTransaction transaction,
                out SessionOutboxBatchItem item)
            {
                item = default;
                using (var command = _connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText =
                        "SELECT " +
                        "se.event_id, se.session_id, se.patient_id, se.therapist_id, se.device_id, se.sequence, " +
                        "se.event_type, se.event_version, se.created_at_utc, se.payload_json, se.checksum, " +
                        "so.attempt_count " +
                        "FROM session_events se " +
                        "INNER JOIN sync_outbox so ON so.event_id = se.event_id " +
                        "WHERE se.event_id = @event_id " +
                        "LIMIT 1;";
                    AddParameter(command, "@event_id", eventId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            return false;
                        }

                        var record = new DurableSessionEventRecord
                        {
                            eventId = ReadString(reader, "event_id"),
                            sessionId = ReadString(reader, "session_id"),
                            patientId = ReadString(reader, "patient_id"),
                            therapistId = ReadString(reader, "therapist_id"),
                            deviceId = ReadString(reader, "device_id"),
                            sequence = ReadInt64(reader, "sequence", 0L),
                            eventType = ReadString(reader, "event_type"),
                            eventVersion = ReadInt32(reader, "event_version", 1),
                            createdAtUtc = ReadString(reader, "created_at_utc"),
                            payloadJson = ReadString(reader, "payload_json"),
                            checksum = ReadString(reader, "checksum"),
                        };

                        item = new SessionOutboxBatchItem
                        {
                            record = record,
                            attemptCount = ReadInt32(reader, "attempt_count", 0),
                        };

                        return true;
                    }
                }
            }

            private void MarkOutboxRowSyncedInternal(
                string eventId,
                string syncedAtUtc,
                string lastError,
                DbTransaction transaction)
            {
                using (var command = _connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText =
                        "UPDATE sync_outbox " +
                        "SET status='SYNCED', " +
                        "next_attempt_utc=@synced_at_utc, " +
                        "locked_by=NULL, " +
                        "locked_at_utc=NULL, " +
                        "synced_at_utc=@synced_at_utc, " +
                        "last_error=@last_error " +
                        "WHERE event_id=@event_id;";
                    AddParameter(command, "@synced_at_utc", syncedAtUtc);
                    AddParameter(command, "@last_error", lastError);
                    AddParameter(command, "@event_id", eventId);
                    command.ExecuteNonQuery();
                }
            }

            private int ExecuteScalarInt(string sql, int fallback)
            {
                using (var command = _connection.CreateCommand())
                {
                    command.CommandText = sql;
                    var scalar = command.ExecuteScalar();
                    if (scalar == null || scalar == DBNull.Value)
                    {
                        return fallback;
                    }

                    if (int.TryParse(
                            Convert.ToString(scalar, CultureInfo.InvariantCulture),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var parsed))
                    {
                        return parsed;
                    }

                    return fallback;
                }
            }

            private static string ReadString(DbDataReader reader, string columnName)
            {
                var value = reader[columnName];
                if (value == null || value == DBNull.Value)
                {
                    return string.Empty;
                }

                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }

            private static int ReadInt32(DbDataReader reader, string columnName, int fallback)
            {
                var value = reader[columnName];
                if (value == null || value == DBNull.Value)
                {
                    return fallback;
                }

                if (value is int intValue)
                {
                    return intValue;
                }

                if (int.TryParse(
                        Convert.ToString(value, CultureInfo.InvariantCulture),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    return parsed;
                }

                return fallback;
            }

            private static long ReadInt64(DbDataReader reader, string columnName, long fallback)
            {
                var value = reader[columnName];
                if (value == null || value == DBNull.Value)
                {
                    return fallback;
                }

                if (value is long longValue)
                {
                    return longValue;
                }

                if (long.TryParse(
                        Convert.ToString(value, CultureInfo.InvariantCulture),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    return parsed;
                }

                return fallback;
            }
        }
    }
}
