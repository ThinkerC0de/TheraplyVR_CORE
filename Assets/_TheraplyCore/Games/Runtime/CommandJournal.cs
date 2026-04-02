using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TheraplyCore.Games.Contracts;
using UnityEngine;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    internal static class CommandJournalStatus
    {
        public const string Received = "RECEIVED";
        public const string Applied = "APPLIED";
        public const string Rejected = "REJECTED";
        public const string Failed = "FAILED";
        public const string Duplicate = "DUPLICATE";
    }

    internal static class IdempotencyReasonCodes
    {
        public const string DuplicateCommand = "DUPLICATE_COMMAND";
        public const string CommandInProgress = "COMMAND_IN_PROGRESS";
    }

    internal struct IdempotencyDecision
    {
        public bool shouldAck;
        public string ackStatus;
        public string reasonCode;
        public string sessionId;
    }

    [Serializable]
    internal sealed class CommandJournalRecord
    {
        public int journalVersion;
        public long sequence;
        public string messageId;
        public string commandId;
        public string sessionId;
        public string status;
        public string reasonCode;
        public string recordedAtUtc;
    }

    internal sealed class CommandJournal : IDisposable
    {
        private readonly string _journalPath;
        private readonly bool _logVerbose;
        private readonly int _maxEntriesInMemory;
        private readonly int _maxPendingWrites;
        private readonly object _indexLock = new object();
        private readonly object _fileWriteLock = new object();
        private readonly Dictionary<string, CommandJournalRecord> _latestByMessageId =
            new Dictionary<string, CommandJournalRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _revisionByMessageId =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Queue<CommandJournalIndexMarker> _indexOrder =
            new Queue<CommandJournalIndexMarker>();
        private readonly BlockingCollection<CommandJournalRecord> _pendingWrites;
        private readonly CancellationTokenSource _cancelSource;
        private readonly Task _writerTask;

        private long _nextSequence = 1;
        private int _writesDropped;
        private int _writeFailures;
        private bool _disposed;

        public CommandJournal(
            string journalPath,
            int maxEntriesInMemory,
            int maxPendingWrites,
            bool logVerbose)
        {
            if (string.IsNullOrWhiteSpace(journalPath))
            {
                throw new InvalidOperationException("Command journal path is required.");
            }

            _journalPath = journalPath;
            _logVerbose = logVerbose;
            _maxEntriesInMemory = Math.Max(256, maxEntriesInMemory);
            _maxPendingWrites = Math.Max(64, maxPendingWrites);

            var directory = Path.GetDirectoryName(_journalPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            LoadExistingJournal();

            _pendingWrites = new BlockingCollection<CommandJournalRecord>(
                new ConcurrentQueue<CommandJournalRecord>(),
                _maxPendingWrites);
            _cancelSource = new CancellationTokenSource();
            _writerTask = Task.Run(() => WriterLoop(_cancelSource.Token), _cancelSource.Token);
        }

        public bool TryGetLatest(string messageId, out CommandJournalRecord record)
        {
            record = null;
            if (string.IsNullOrWhiteSpace(messageId))
            {
                return false;
            }

            lock (_indexLock)
            {
                return _latestByMessageId.TryGetValue(messageId.Trim(), out record) && record != null;
            }
        }

        public void Record(
            string messageId,
            string commandId,
            string sessionId,
            string status,
            string reasonCode)
        {
            if (_disposed || string.IsNullOrWhiteSpace(messageId))
            {
                return;
            }

            var normalizedMessageId = messageId.Trim();
            var nowUtc = DateTime.UtcNow;
            long sequence;
            long revision;

            lock (_indexLock)
            {
                sequence = _nextSequence++;
                revision = _revisionByMessageId.TryGetValue(normalizedMessageId, out var existing)
                    ? existing + 1
                    : 1;

                _revisionByMessageId[normalizedMessageId] = revision;

                var normalizedRecord = new CommandJournalRecord
                {
                    journalVersion = 1,
                    sequence = sequence,
                    messageId = normalizedMessageId,
                    commandId = string.IsNullOrWhiteSpace(commandId) ? string.Empty : commandId.Trim(),
                    sessionId = string.IsNullOrWhiteSpace(sessionId) ? string.Empty : sessionId.Trim(),
                    status = string.IsNullOrWhiteSpace(status) ? CommandJournalStatus.Received : status.Trim(),
                    reasonCode = string.IsNullOrWhiteSpace(reasonCode) ? string.Empty : reasonCode.Trim(),
                    recordedAtUtc = nowUtc.ToString("O"),
                };

                _latestByMessageId[normalizedMessageId] = normalizedRecord;
                _indexOrder.Enqueue(new CommandJournalIndexMarker
                {
                    messageId = normalizedMessageId,
                    revision = revision,
                });
                TrimIndexIfNeeded();

                if (!_pendingWrites.TryAdd(normalizedRecord))
                {
                    _writesDropped++;
                    var fallbackPersisted = TryWriteRecord(normalizedRecord);
                    if (!fallbackPersisted)
                    {
                        _writeFailures++;
                    }

                    if (_logVerbose)
                    {
                        var mode = fallbackPersisted ? "synchronously persisted fallback" : "write failed";
                        Logger.Warning(
                            $"[CommandJournal] Pending queue full. {mode} for {normalizedRecord.commandId} ({normalizedRecord.messageId}).");
                    }
                }
            }
        }

        public void Flush(TimeSpan timeout)
        {
            if (_disposed)
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
                    // Ignore teardown exceptions.
                }
            }
            finally
            {
                _cancelSource.Dispose();
                _pendingWrites.Dispose();
            }
        }

        private void LoadExistingJournal()
        {
            if (!File.Exists(_journalPath))
            {
                return;
            }

            var loaded = 0;
            try
            {
                var lines = File.ReadAllLines(_journalPath, Encoding.UTF8);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    CommandJournalRecord parsed;
                    try
                    {
                        parsed = JsonUtility.FromJson<CommandJournalRecord>(line);
                    }
                    catch
                    {
                        continue;
                    }

                    if (parsed == null || string.IsNullOrWhiteSpace(parsed.messageId))
                    {
                        continue;
                    }

                    var normalizedMessageId = parsed.messageId.Trim();
                    var sequence = parsed.sequence > 0 ? parsed.sequence : _nextSequence++;
                    if (sequence >= _nextSequence)
                    {
                        _nextSequence = sequence + 1;
                    }

                    var revision = _revisionByMessageId.TryGetValue(normalizedMessageId, out var existingRevision)
                        ? existingRevision + 1
                        : 1;
                    _revisionByMessageId[normalizedMessageId] = revision;

                    parsed.sequence = sequence;
                    parsed.messageId = normalizedMessageId;
                    parsed.commandId = string.IsNullOrWhiteSpace(parsed.commandId) ? string.Empty : parsed.commandId.Trim();
                    parsed.sessionId = string.IsNullOrWhiteSpace(parsed.sessionId) ? string.Empty : parsed.sessionId.Trim();
                    parsed.status = string.IsNullOrWhiteSpace(parsed.status) ? CommandJournalStatus.Received : parsed.status.Trim();
                    parsed.reasonCode = string.IsNullOrWhiteSpace(parsed.reasonCode) ? string.Empty : parsed.reasonCode.Trim();

                    _latestByMessageId[normalizedMessageId] = parsed;
                    _indexOrder.Enqueue(new CommandJournalIndexMarker
                    {
                        messageId = normalizedMessageId,
                        revision = revision,
                    });
                    TrimIndexIfNeeded();
                    loaded++;
                }
            }
            catch (Exception e)
            {
                Logger.Warning($"[CommandJournal] Failed to load existing journal: {e.Message}");
            }

            if (_logVerbose && loaded > 0)
            {
                Logger.Info($"[CommandJournal] Loaded {loaded} historical records.");
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
                        if (!TryWriteRecord(record))
                        {
                            _writeFailures++;
                        }
                    }
                    catch (Exception e)
                    {
                        _writeFailures++;
                        Logger.Warning($"[CommandJournal] Persist failed: {e.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (ThreadAbortException)
            {
                // Unity editor teardown may abort worker threads.
            }
            catch (Exception e)
            {
                _writeFailures++;
                Logger.Warning($"[CommandJournal] Writer loop crashed: {e.Message}");
            }
        }

        private void TrimIndexIfNeeded()
        {
            while (_latestByMessageId.Count > _maxEntriesInMemory && _indexOrder.Count > 0)
            {
                var marker = _indexOrder.Dequeue();
                if (!_revisionByMessageId.TryGetValue(marker.messageId, out var latestRevision) ||
                    latestRevision != marker.revision)
                {
                    continue;
                }

                _revisionByMessageId.Remove(marker.messageId);
                _latestByMessageId.Remove(marker.messageId);
            }
        }

        private bool TryWriteRecord(CommandJournalRecord record)
        {
            if (record == null)
            {
                return true;
            }

            try
            {
                var line = JsonUtility.ToJson(record) + Environment.NewLine;
                lock (_fileWriteLock)
                {
                    File.AppendAllText(_journalPath, line, Encoding.UTF8);
                }

                return true;
            }
            catch (Exception e)
            {
                Logger.Warning($"[CommandJournal] Persist failed: {e.Message}");
                return false;
            }
        }

        private struct CommandJournalIndexMarker
        {
            public string messageId;
            public long revision;
        }
    }

    internal sealed class IdempotencyGuard
    {
        private readonly CommandJournal _journal;
        private readonly bool _logVerbose;
        private readonly object _lock = new object();
        private readonly HashSet<string> _inFlightMessageIds =
            new HashSet<string>(StringComparer.Ordinal);

        public IdempotencyGuard(CommandJournal journal, bool logVerbose)
        {
            _journal = journal;
            _logVerbose = logVerbose;
        }

        public bool TryBeginCriticalCommand(
            string messageId,
            string commandId,
            string sessionId,
            out IdempotencyDecision decision)
        {
            decision = default;
            if (_journal == null || string.IsNullOrWhiteSpace(messageId))
            {
                return true;
            }

            var normalizedMessageId = messageId.Trim();
            var normalizedCommandId = string.IsNullOrWhiteSpace(commandId) ? string.Empty : commandId.Trim();
            var normalizedSessionId = string.IsNullOrWhiteSpace(sessionId) ? string.Empty : sessionId.Trim();

            lock (_lock)
            {
                if (_inFlightMessageIds.Contains(normalizedMessageId))
                {
                    decision = new IdempotencyDecision
                    {
                        shouldAck = true,
                        ackStatus = CommandAckStatus.Nack,
                        reasonCode = IdempotencyReasonCodes.CommandInProgress,
                        sessionId = normalizedSessionId,
                    };
                    return false;
                }

                if (_journal.TryGetLatest(normalizedMessageId, out var latest) && latest != null)
                {
                    if (IsTerminal(latest.status))
                    {
                        decision = ResolveDuplicateDecision(latest, normalizedSessionId);
                        var duplicateStatus = string.IsNullOrWhiteSpace(latest.status)
                            ? CommandJournalStatus.Applied
                            : latest.status;
                        _journal.Record(
                            normalizedMessageId,
                            string.IsNullOrWhiteSpace(latest.commandId) ? normalizedCommandId : latest.commandId,
                            string.IsNullOrWhiteSpace(latest.sessionId) ? normalizedSessionId : latest.sessionId,
                            duplicateStatus,
                            decision.reasonCode);

                        if (_logVerbose)
                        {
                            Logger.Info(
                                $"[IdempotencyGuard] Duplicate command suppressed: command={normalizedCommandId}, messageId={normalizedMessageId}, priorStatus={latest.status}.");
                        }

                        return false;
                    }
                }

                _inFlightMessageIds.Add(normalizedMessageId);
            }

            _journal.Record(
                normalizedMessageId,
                normalizedCommandId,
                normalizedSessionId,
                CommandJournalStatus.Received,
                string.Empty);
            return true;
        }

        public void CompleteCriticalCommand(
            string messageId,
            string commandId,
            string sessionId,
            string status,
            string reasonCode)
        {
            if (_journal == null || string.IsNullOrWhiteSpace(messageId))
            {
                return;
            }

            var normalizedMessageId = messageId.Trim();
            lock (_lock)
            {
                _inFlightMessageIds.Remove(normalizedMessageId);
            }

            _journal.Record(
                normalizedMessageId,
                commandId,
                sessionId,
                status,
                reasonCode);
        }

        private static bool IsTerminal(string status)
        {
            return string.Equals(status, CommandJournalStatus.Applied, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, CommandJournalStatus.Rejected, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, CommandJournalStatus.Failed, StringComparison.OrdinalIgnoreCase);
        }

        private static IdempotencyDecision ResolveDuplicateDecision(
            CommandJournalRecord latest,
            string fallbackSessionId)
        {
            var normalizedStatus = latest.status ?? string.Empty;
            var sessionId = string.IsNullOrWhiteSpace(latest.sessionId) ? fallbackSessionId : latest.sessionId;
            var reasonCode = string.IsNullOrWhiteSpace(latest.reasonCode)
                ? IdempotencyReasonCodes.DuplicateCommand
                : latest.reasonCode;

            if (string.Equals(normalizedStatus, CommandJournalStatus.Applied, StringComparison.OrdinalIgnoreCase))
            {
                return new IdempotencyDecision
                {
                    shouldAck = true,
                    ackStatus = CommandAckStatus.Ack,
                    reasonCode = IdempotencyReasonCodes.DuplicateCommand,
                    sessionId = sessionId,
                };
            }

            if (string.Equals(normalizedStatus, CommandJournalStatus.Rejected, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedStatus, CommandJournalStatus.Failed, StringComparison.OrdinalIgnoreCase))
            {
                return new IdempotencyDecision
                {
                    shouldAck = true,
                    ackStatus = CommandAckStatus.Nack,
                    reasonCode = reasonCode,
                    sessionId = sessionId,
                };
            }

            return new IdempotencyDecision
            {
                shouldAck = true,
                ackStatus = CommandAckStatus.Ack,
                reasonCode = IdempotencyReasonCodes.DuplicateCommand,
                sessionId = sessionId,
            };
        }
    }
}
