using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using TheraplyCore.Games;
using TheraplyCore.Games.Runtime;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Firebase
{
    /// <summary>
    /// Non-blocking Firebase data collection service
    /// NEVER blocks main thread - all writes happen in background
    /// </summary>
    public class FirebaseDataService : MonoBehaviour
    {
        [Header("Batch Settings")]
        [SerializeField] private int _batchSize = 10;
        [SerializeField] private float _batchInterval = 5f;
        
        [Header("Queue Settings")]
        [SerializeField] private int _maxQueueSize = 1000;
        [SerializeField] private bool _dropOldestOnFull = true;
        
        [Header("Debug")]
        [SerializeField] private bool _logWrites = true;
        [SerializeField] private bool _simulateFirebase = false;

        [Header("Firebase Backend")]
        [SerializeField] private string _sessionIngestEndpointUrl = string.Empty;
        [SerializeField] private string _sessionReconciliationEndpointUrl = string.Empty;
        [SerializeField] private string _firebaseAuthBearerToken = string.Empty;
        [SerializeField] private string _firebaseApiKey = string.Empty;
        [SerializeField] [Range(3, 120)] private int _firebaseRequestTimeoutSeconds = 20;
        [SerializeField] private bool _logFirebaseBackendPayloads = false;
        [SerializeField] private bool _logFirebaseBackendDiagnostics = true;

        [Header("Local Durability")]
        [SerializeField] private bool _persistCriticalSessionEventsLocally = true;
        [SerializeField] private bool _persistAllEventsToDurableStore = false;
        [SerializeField] private bool _preferSqliteWalStore = true;
        [SerializeField] private bool _mirrorDurableEventsToNdjson = true;
        [SerializeField] private string _localDurableFolder = "session_resilience";
        [SerializeField] private string _localDurableFileName = "events.ndjson";
        [SerializeField] private string _sqliteStoreFileName = "session_events.db";
        [SerializeField] private int _maxDurableStoreQueueSize = 2048;
        [SerializeField] private bool _logLocalPersistence = false;
        [SerializeField] private bool _allowVolatileQueueFallbackWhenDurableWriteFails = true;
        [SerializeField] private GameSessionContext _sessionContext;

        [Header("Outbox Sync")]
        [SerializeField] private bool _enableOutboxSync = true;
        [SerializeField] private float _outboxSyncIntervalSeconds = 2f;
        [SerializeField] private int _outboxBatchSize = 32;
        [SerializeField] private float _outboxBackoffBaseSeconds = 1f;
        [SerializeField] private float _outboxBackoffMaxSeconds = 60f;
        [SerializeField] [Range(0f, 1f)] private float _outboxBackoffJitter = 0.2f;
        [SerializeField] private bool _logOutboxSync = false;
        [SerializeField] private string _outboxWorkerId = string.Empty;
        
        private Queue<GameDataPoint> _writeQueue = new Queue<GameDataPoint>();
        private bool _isProcessing = false;
        private DateTime _lastBatchWrite;

        private string _sessionId;
        private string _patientId;
        private string _therapistId;
        private string _localDurablePath;
        private string _sqliteStorePath;
        private SessionEventStore _sessionEventStore;

        private int _pointsQueued = 0;
        private int _pointsWritten = 0;
        private int _pointsFailed = 0;
        private int _batchesWritten = 0;
        private int _criticalEventsPersisted = 0;
        private int _criticalEventPersistFailures = 0;
        private bool _isOutboxSyncRunning = false;
        private DateTime _lastOutboxSyncAtUtc = DateTime.MinValue;
        private int _outboxBatchesSynced = 0;
        private int _outboxEventsSynced = 0;
        private int _outboxSyncFailures = 0;
        private int _outboxRetriesScheduled = 0;
        private int _outboxDuplicatesAcknowledged = 0;

        private static readonly HashSet<string> CriticalDurableEventTypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "session_start",
                "game_start",
                "game_end",
                "session_stop",
                "error",
                "controller_connected",
                "controller_reconnected",
                "controller_disconnected",
                // Compatibility with existing event names in framework modules.
                "game_started",
                "game_completed",
                "game_failed",
                "session_error",
            };

        private struct OutboxUploadResult
        {
            public bool success;
            public string errorCode;
            public int duplicateCount;
            public List<string> syncedEventIds;
            public List<SessionOutboxRetryRecord> retryRecords;
        }

        private struct OutboxClaimResult
        {
            public bool success;
            public List<SessionOutboxBatchItem> batch;
            public string error;
        }

        private struct OutboxStoreResult
        {
            public bool success;
            public string error;
        }

        private struct OutboxForceResult
        {
            public bool success;
            public int rowsUpdated;
            public string error;
        }

        private struct SessionSequenceIndexFetchResult
        {
            public bool success;
            public string error;
            public List<SessionSequenceIndexRecord> records;
        }

        private struct SessionReconciliationFetchResult
        {
            public bool success;
            public string errorCode;
            public List<SessionReconciliationEventReference> events;
        }

        private struct BackendPostResult
        {
            public bool success;
            public string errorCode;
            public long statusCode;
            public string responseBody;
        }

        private static class SessionIngestStatus
        {
            public const string Accepted = "ACCEPTED";
            public const string Duplicate = "DUPLICATE";
            public const string Retry = "RETRY";
        }

        [Serializable]
        private sealed class SessionIngestBatchRequest
        {
            public int contractVersion;
            public string ingestBatchId;
            public string sourceDeviceId;
            public string submittedAtUtc;
            public List<SessionIngestEventEnvelope> events;
        }

        [Serializable]
        private sealed class SessionIngestEventEnvelope
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

        [Serializable]
        private sealed class SessionIngestBatchResponse
        {
            public bool success;
            public string errorCode;
            public List<SessionIngestEventResult> eventResults;
        }

        [Serializable]
        private sealed class SessionIngestBatchResponseEnvelope
        {
            public SessionIngestBatchResponse result;
            public SessionIngestBatchResponse data;
        }

        [Serializable]
        private sealed class SessionIngestEventResult
        {
            public string eventId;
            public string status;
            public string reasonCode;
        }

        [Serializable]
        private sealed class SessionReconciliationRequest
        {
            public int contractVersion;
            public string sessionId;
            public string requestedAtUtc;
            public string sourceDeviceId;
        }

        [Serializable]
        private sealed class SessionReconciliationResponse
        {
            public bool success;
            public string errorCode;
            public List<SessionReconciliationEventReference> events;
        }

        [Serializable]
        private sealed class SessionReconciliationResponseEnvelope
        {
            public SessionReconciliationResponse result;
            public SessionReconciliationResponse data;
        }

        [Serializable]
        private sealed class SessionReconciliationEventReference
        {
            public string eventId;
            public long sequence;
        }

        private static readonly object SimulatedIngestLock = new object();
        private static readonly HashSet<string> SimulatedIngestedEventIds =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Dictionary<long, string>> SimulatedServerSessionEvents =
            new Dictionary<string, Dictionary<long, string>>(StringComparer.Ordinal);

        private void Awake()
        {
            if (_sessionContext == null)
            {
                _sessionContext = FindFirstObjectByType<GameSessionContext>();
            }

            if (_persistCriticalSessionEventsLocally)
            {
                var folder = string.IsNullOrWhiteSpace(_localDurableFolder) ? "session_resilience" : _localDurableFolder.Trim();
                var fileName = string.IsNullOrWhiteSpace(_localDurableFileName) ? "events.ndjson" : _localDurableFileName.Trim();
                var sqliteFileName = string.IsNullOrWhiteSpace(_sqliteStoreFileName) ? "session_events.db" : _sqliteStoreFileName.Trim();
                _localDurablePath = Path.Combine(Application.persistentDataPath, folder, fileName);
                _sqliteStorePath = Path.Combine(Application.persistentDataPath, folder, sqliteFileName);
            }
        }

        void Start()
        {
            _lastBatchWrite = DateTime.UtcNow;
            _lastOutboxSyncAtUtc = DateTime.UtcNow;
            RefreshSessionMetadataFromContext();
            if (_sessionContext != null)
            {
                _sessionContext.OnSessionChanged += HandleSessionChanged;
            }

            InitializeDurableStore();
            StartCoroutine(BackgroundWorker());
            Logger.Info("[FirebaseData] Service started");
        }
        
        void OnApplicationPause(bool pause)
        {
            if (pause)
            {
                Logger.Info("[FirebaseData] App pausing - flushing data");
                FlushAllData();
            }
        }
        
        void OnApplicationQuit()
        {
            Logger.Info("[FirebaseData] App quitting - flushing data");
            FlushAllData();
        }

        void OnDestroy()
        {
            if (_sessionContext != null)
            {
                _sessionContext.OnSessionChanged -= HandleSessionChanged;
            }

            DisposeDurableStore();
        }
        
        public void QueueDataPoint(GameDataPoint dataPoint)
        {
            if (dataPoint == null)
            {
                _pointsFailed++;
                Logger.Warning("[FirebaseData] QueueDataPoint ignored: data point is null.");
                return;
            }

            if (ShouldPersistLocallyBeforeNetwork(dataPoint))
            {
                if (!TryPersistCriticalEventLocally(dataPoint, out var persistError))
                {
                    _criticalEventPersistFailures++;
                    _pointsFailed++;
                    Logger.Error($"[FirebaseData] Failed local durability write for {dataPoint.dataType}: {persistError}");

                    if (!_allowVolatileQueueFallbackWhenDurableWriteFails)
                    {
                        return;
                    }

                    Logger.Warning(
                        $"[FirebaseData] Falling back to volatile queue for {dataPoint.dataType} after durable write failure.");
                }
                else
                {
                    _criticalEventsPersisted++;
                }
            }

            if (_writeQueue.Count >= _maxQueueSize)
            {
                if (_dropOldestOnFull)
                {
                    var dropped = _writeQueue.Dequeue();
                    var droppedType = dropped != null ? dropped.dataType : "<null>";
                    Logger.Warning($"[FirebaseData] Queue full - dropped oldest point ({droppedType})");
                }
                else
                {
                    Logger.Warning("[FirebaseData] Queue full - dropping new point");
                    _pointsFailed++;
                    return;
                }
            }
            
            _writeQueue.Enqueue(dataPoint);
            _pointsQueued++;
            
            Logger.Debug($"[FirebaseData] Queued: {dataPoint.dataType ?? "<unknown>"} (queue: {_writeQueue.Count})");
            
            if (_writeQueue.Count >= _batchSize)
            {
                TriggerBatchWrite();
            }
        }
        
        public void SetSessionId(string sessionId)
        {
            _sessionId = sessionId;
            Logger.Info($"[FirebaseData] Session ID set: {sessionId}");
        }
        
        public void FlushAllData()
        {
            Logger.Info($"[FirebaseData] Flushing all data ({_writeQueue.Count} points)");
            
            while (_writeQueue.Count > 0)
            {
                var batch = DequeueBatch();
                if (!TryFlushBatchWithTimeout(batch, TimeSpan.FromSeconds(2)))
                {
                    Logger.Warning("[FirebaseData] Flush aborted early after batch timeout/failure.");
                    break;
                }
            }

            if (_sessionEventStore != null)
            {
                _sessionEventStore.Flush(TimeSpan.FromSeconds(2));
            }
            
            Logger.Info("[FirebaseData] All data flushed");
        }

        private bool TryFlushBatchWithTimeout(List<GameDataPoint> batch, TimeSpan timeout)
        {
            if (batch == null || batch.Count == 0)
            {
                return true;
            }

            try
            {
                var flushTask = WriteBatchToFirebase(batch);
                if (!flushTask.Wait(timeout))
                {
                    _pointsFailed += batch.Count;
                    Logger.Warning(
                        $"[FirebaseData] Batch flush timed out after {timeout.TotalSeconds:F1}s ({batch.Count} points).");
                    return false;
                }

                _pointsWritten += batch.Count;
                _batchesWritten++;
                return true;
            }
            catch (AggregateException e)
            {
                _pointsFailed += batch.Count;
                var inner = e.GetBaseException();
                Logger.Error(
                    $"[FirebaseData] Batch flush failed: {inner.Message}",
                    inner);
                return false;
            }
            catch (Exception e)
            {
                _pointsFailed += batch.Count;
                Logger.Error($"[FirebaseData] Batch flush failed: {e.Message}", e);
                return false;
            }
        }
        
        public QueueStatistics GetStatistics()
        {
            var durableStats = _sessionEventStore != null
                ? _sessionEventStore.GetStatistics()
                : new SessionEventStoreStatistics { mode = "disabled" };

            return new QueueStatistics
            {
                queueSize = _writeQueue.Count,
                pointsQueued = _pointsQueued,
                pointsWritten = _pointsWritten,
                pointsFailed = _pointsFailed,
                batchesWritten = _batchesWritten,
                criticalEventsPersisted = _criticalEventsPersisted,
                criticalEventPersistFailures = _criticalEventPersistFailures,
                durablePendingWrites = durableStats.pendingWrites,
                durableEventsPersisted = durableStats.eventsPersisted,
                durableEventsFailed = durableStats.eventsFailed,
                durableEventsDropped = durableStats.eventsDropped,
                durableOutboxSupported = durableStats.outboxSupported,
                durableOutboxPending = durableStats.outboxPending,
                durableOutboxInFlight = durableStats.outboxInFlight,
                durableOutboxSynced = durableStats.outboxSynced,
                durableOutboxRetryCount = durableStats.outboxRetryCount,
                outboxBatchesSynced = _outboxBatchesSynced,
                outboxEventsSynced = _outboxEventsSynced,
                outboxSyncFailures = _outboxSyncFailures,
                outboxRetriesScheduled = _outboxRetriesScheduled,
                outboxDuplicatesAcknowledged = _outboxDuplicatesAcknowledged,
                durableStoreMode = durableStats.mode ?? "unknown",
                successRate = _pointsQueued > 0 ? (float)_pointsWritten / _pointsQueued * 100 : 0
            };
        }

        public async Task<SessionReconciliationReport> BuildSessionReconciliationReportAsync(string sessionId)
        {
            var normalizedSessionId = string.IsNullOrWhiteSpace(sessionId) ? string.Empty : sessionId.Trim();
            var report = new SessionReconciliationReport
            {
                success = false,
                reasonCode = "UNINITIALIZED",
                sessionId = normalizedSessionId,
                generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                localEvents = 0,
                localSequenceCount = 0,
                localMinSequence = 0,
                localMaxSequence = 0,
                localOutboxPending = 0,
                localOutboxInFlight = 0,
                localOutboxSynced = 0,
                serverEvents = 0,
                serverSequenceCount = 0,
                serverMinSequence = 0,
                serverMaxSequence = 0,
                missingOnServerCount = 0,
                missingOnDeviceCount = 0,
                missingOnServerSequences = new List<long>(),
                missingOnDeviceSequences = new List<long>(),
            };

            if (string.IsNullOrWhiteSpace(normalizedSessionId))
            {
                report.reasonCode = "SESSION_ID_REQUIRED";
                return report;
            }

            if (_sessionEventStore == null)
            {
                report.reasonCode = "DURABLE_STORE_UNAVAILABLE";
                return report;
            }

            var localResult = await Task.Run(() =>
            {
                var result = new SessionSequenceIndexFetchResult
                {
                    success = false,
                    error = string.Empty,
                    records = new List<SessionSequenceIndexRecord>(),
                };

                if (_sessionEventStore == null)
                {
                    result.error = "Session event store unavailable.";
                    return result;
                }

                result.success = _sessionEventStore.TryGetSessionSequenceIndex(
                    normalizedSessionId,
                    out var records,
                    out var error);
                result.records = records ?? new List<SessionSequenceIndexRecord>();
                result.error = error;
                return result;
            });

            if (!localResult.success)
            {
                report.reasonCode = "LOCAL_RECONCILIATION_READ_FAILED";
                if (_logOutboxSync)
                {
                    Logger.Warning($"[FirebaseData] Reconciliation local read failed: {localResult.error}");
                }

                return report;
            }

            var localSequences = new SortedSet<long>();
            report.localEvents = localResult.records.Count;

            for (var i = 0; i < localResult.records.Count; i++)
            {
                var localRecord = localResult.records[i];
                if (localRecord.sequence > 0)
                {
                    localSequences.Add(localRecord.sequence);
                }

                var status = localRecord.outboxStatus ?? string.Empty;
                if (string.Equals(status, "PENDING", StringComparison.OrdinalIgnoreCase))
                {
                    report.localOutboxPending++;
                }
                else if (string.Equals(status, "IN_FLIGHT", StringComparison.OrdinalIgnoreCase))
                {
                    report.localOutboxInFlight++;
                }
                else if (string.Equals(status, "SYNCED", StringComparison.OrdinalIgnoreCase))
                {
                    report.localOutboxSynced++;
                }
            }

            report.localSequenceCount = localSequences.Count;
            ResolveSequenceRange(localSequences, out report.localMinSequence, out report.localMaxSequence);

            var serverResult = await FetchServerReconciliationIndexAsync(normalizedSessionId);
            if (!serverResult.success)
            {
                report.reasonCode = string.IsNullOrWhiteSpace(serverResult.errorCode)
                    ? "SERVER_RECONCILIATION_UNAVAILABLE"
                    : serverResult.errorCode;
                return report;
            }

            var serverSequences = new SortedSet<long>();
            var serverEvents = serverResult.events ?? new List<SessionReconciliationEventReference>();
            report.serverEvents = serverEvents.Count;

            for (var i = 0; i < serverEvents.Count; i++)
            {
                if (serverEvents[i] != null && serverEvents[i].sequence > 0)
                {
                    serverSequences.Add(serverEvents[i].sequence);
                }
            }

            report.serverSequenceCount = serverSequences.Count;
            ResolveSequenceRange(serverSequences, out report.serverMinSequence, out report.serverMaxSequence);

            foreach (var localSequence in localSequences)
            {
                if (!serverSequences.Contains(localSequence))
                {
                    report.missingOnServerSequences.Add(localSequence);
                }
            }

            foreach (var serverSequence in serverSequences)
            {
                if (!localSequences.Contains(serverSequence))
                {
                    report.missingOnDeviceSequences.Add(serverSequence);
                }
            }

            report.missingOnServerCount = report.missingOnServerSequences.Count;
            report.missingOnDeviceCount = report.missingOnDeviceSequences.Count;
            report.success = true;
            report.reasonCode = report.missingOnServerCount == 0 && report.missingOnDeviceCount == 0
                ? "IN_SYNC"
                : "MISSING_EVENTS_DETECTED";
            return report;
        }

        public async Task<SessionManualResyncReport> RunManualResyncAsync(
            string sessionId,
            bool includeSyncedEvents,
            string requestedBy,
            string reasonCode)
        {
            var normalizedSessionId = string.IsNullOrWhiteSpace(sessionId) ? string.Empty : sessionId.Trim();
            var report = new SessionManualResyncReport
            {
                success = false,
                reasonCode = "UNINITIALIZED",
                sessionId = normalizedSessionId,
                requestedBy = string.IsNullOrWhiteSpace(requestedBy) ? string.Empty : requestedBy.Trim(),
                requestedReasonCode = string.IsNullOrWhiteSpace(reasonCode) ? "MANUAL_RESYNC" : reasonCode.Trim(),
                requestedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                includeSyncedEvents = includeSyncedEvents,
                targetedEvents = 0,
                outboxRowsUpdated = 0,
                uploadCycleTriggered = false,
                localEvents = 0,
                beforeReasonCode = string.Empty,
                beforeMissingOnServerCount = 0,
                beforeMissingOnDeviceCount = 0,
                beforeOutboxPending = 0,
                afterReasonCode = string.Empty,
                afterMissingOnServerCount = 0,
                afterMissingOnDeviceCount = 0,
                afterOutboxPending = 0,
                targetedSequencePreview = new List<long>(),
                details = string.Empty,
            };

            if (string.IsNullOrWhiteSpace(normalizedSessionId))
            {
                report.reasonCode = "SESSION_ID_REQUIRED";
                return report;
            }

            if (_sessionEventStore == null)
            {
                report.reasonCode = "DURABLE_STORE_UNAVAILABLE";
                return report;
            }

            var localResult = await Task.Run(() =>
            {
                var result = new SessionSequenceIndexFetchResult
                {
                    success = false,
                    error = string.Empty,
                    records = new List<SessionSequenceIndexRecord>(),
                };

                if (_sessionEventStore == null)
                {
                    result.error = "Session event store unavailable.";
                    return result;
                }

                result.success = _sessionEventStore.TryGetSessionSequenceIndex(
                    normalizedSessionId,
                    out var records,
                    out var error);
                result.records = records ?? new List<SessionSequenceIndexRecord>();
                result.error = error;
                return result;
            });

            if (!localResult.success)
            {
                report.reasonCode = "LOCAL_RECONCILIATION_READ_FAILED";
                report.details = localResult.error ?? string.Empty;
                return report;
            }

            report.localEvents = localResult.records.Count;

            var before = await BuildSessionReconciliationReportAsync(normalizedSessionId);
            report.beforeReasonCode = before.reasonCode ?? string.Empty;
            report.beforeMissingOnServerCount = before.missingOnServerCount;
            report.beforeMissingOnDeviceCount = before.missingOnDeviceCount;
            report.beforeOutboxPending = before.localOutboxPending;

            var missingOnServer = new HashSet<long>(before.missingOnServerSequences ?? new List<long>());
            var seenEventIds = new HashSet<string>(StringComparer.Ordinal);
            var targetEventIds = new List<string>(Math.Max(16, localResult.records.Count));

            for (var i = 0; i < localResult.records.Count; i++)
            {
                var localRecord = localResult.records[i];
                if (string.IsNullOrWhiteSpace(localRecord.eventId))
                {
                    continue;
                }

                var shouldTarget = includeSyncedEvents;
                if (!shouldTarget)
                {
                    if (localRecord.sequence > 0 && missingOnServer.Contains(localRecord.sequence))
                    {
                        shouldTarget = true;
                    }
                    else
                    {
                        var status = localRecord.outboxStatus ?? string.Empty;
                        shouldTarget = !string.Equals(status, "SYNCED", StringComparison.OrdinalIgnoreCase);
                    }
                }

                if (!shouldTarget)
                {
                    continue;
                }

                if (!seenEventIds.Add(localRecord.eventId))
                {
                    continue;
                }

                targetEventIds.Add(localRecord.eventId);
                if (localRecord.sequence > 0 && report.targetedSequencePreview.Count < 24)
                {
                    report.targetedSequencePreview.Add(localRecord.sequence);
                }
            }

            report.targetedEvents = targetEventIds.Count;

            if (targetEventIds.Count == 0)
            {
                report.success = true;
                report.reasonCode = "NO_EVENTS_TO_RESYNC";
                report.afterReasonCode = report.beforeReasonCode;
                report.afterMissingOnServerCount = report.beforeMissingOnServerCount;
                report.afterMissingOnDeviceCount = report.beforeMissingOnDeviceCount;
                report.afterOutboxPending = report.beforeOutboxPending;
                report.details = includeSyncedEvents
                    ? "No local events found for requested session."
                    : "No missing-on-server or unsynced events found.";
                return report;
            }

            var forceResult = await Task.Run(() =>
            {
                var result = new OutboxForceResult
                {
                    success = false,
                    rowsUpdated = 0,
                    error = string.Empty,
                };

                if (_sessionEventStore == null)
                {
                    result.error = "Session event store unavailable.";
                    return result;
                }

                result.success = _sessionEventStore.ForceOutboxPending(
                    targetEventIds,
                    DateTime.UtcNow,
                    report.requestedReasonCode,
                    out var rowsUpdated,
                    out var error);
                result.rowsUpdated = rowsUpdated;
                result.error = error;
                return result;
            });

            report.outboxRowsUpdated = Math.Max(0, forceResult.rowsUpdated);
            if (!forceResult.success)
            {
                report.reasonCode = "OUTBOX_FORCE_PENDING_FAILED";
                report.details = forceResult.error ?? string.Empty;
                return report;
            }

            report.uploadCycleTriggered = await TriggerOutboxSyncForSupportAsync();
            if (!report.uploadCycleTriggered)
            {
                report.reasonCode = "OUTBOX_SYNC_TRIGGER_FAILED";
                report.details = "Outbox sync was not available to run immediately.";
                return report;
            }

            var after = await BuildSessionReconciliationReportAsync(normalizedSessionId);
            report.afterReasonCode = after.reasonCode ?? string.Empty;
            report.afterMissingOnServerCount = after.missingOnServerCount;
            report.afterMissingOnDeviceCount = after.missingOnDeviceCount;
            report.afterOutboxPending = after.localOutboxPending;

            if (after.success)
            {
                report.success = true;
                report.reasonCode = after.missingOnServerCount == 0
                    ? "RESYNC_COMPLETED_IN_SYNC"
                    : "RESYNC_COMPLETED_WITH_GAPS";
                report.details =
                    $"Missing-on-server before/after: {report.beforeMissingOnServerCount}/{report.afterMissingOnServerCount}.";
            }
            else
            {
                report.success = true;
                report.reasonCode = "RESYNC_TRIGGERED_SERVER_CHECK_UNAVAILABLE";
                report.details = string.IsNullOrWhiteSpace(after.reasonCode)
                    ? "Upload cycle completed but post-check could not be verified."
                    : $"Upload cycle completed; post-check reason={after.reasonCode}.";
            }

            return report;
        }

        private async Task<bool> TriggerOutboxSyncForSupportAsync()
        {
            if (_sessionEventStore == null || !_sessionEventStore.SupportsOutboxSync)
            {
                return false;
            }

            var deadline = DateTime.UtcNow.AddSeconds(Math.Max(5, _firebaseRequestTimeoutSeconds));
            while (_isOutboxSyncRunning && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            if (!_isOutboxSyncRunning)
            {
                TriggerOutboxSync();
            }

            while (_isOutboxSyncRunning && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            return !_isOutboxSyncRunning;
        }
        
        private IEnumerator BackgroundWorker()
        {
            while (true)
            {
                float timeSinceLastWrite = (float)(DateTime.UtcNow - _lastBatchWrite).TotalSeconds;
                
                if (_writeQueue.Count > 0 && timeSinceLastWrite >= _batchInterval)
                {
                    TriggerBatchWrite();
                }

                if (ShouldTriggerOutboxSync())
                {
                    TriggerOutboxSync();
                }
                
                yield return new WaitForSeconds(1f);
            }
        }
        
        private async void TriggerBatchWrite()
        {
            if (_isProcessing)
            {
                Logger.Debug("[FirebaseData] Already writing batch, skipping");
                return;
            }
            
            if (_writeQueue.Count == 0) return;
            
            _isProcessing = true;
            
            try
            {
                var batch = DequeueBatch();
                
                if (_logWrites)
                {
                    Logger.Debug($"[FirebaseData] Writing batch of {batch.Count} points");
                }
                
                await WriteBatchToFirebase(batch);
                
                _lastBatchWrite = DateTime.UtcNow;
                _batchesWritten++;
                _pointsWritten += batch.Count;
                
                if (_logWrites)
                {
                    Logger.Info($"[FirebaseData] Batch written successfully ({batch.Count} points)");
                }
            }
            catch (Exception e)
            {
                Logger.Error($"[FirebaseData] Batch write failed: {e.Message}", e);
                _pointsFailed += _batchSize;
            }
            finally
            {
                _isProcessing = false;
            }
        }
        
        private List<GameDataPoint> DequeueBatch()
        {
            var batch = new List<GameDataPoint>();
            int count = Mathf.Min(_batchSize, _writeQueue.Count);
            
            for (int i = 0; i < count; i++)
            {
                batch.Add(_writeQueue.Dequeue());
            }
            
            return batch;
        }
        
        private async Task WriteBatchToFirebase(List<GameDataPoint> batch)
        {
            if (_simulateFirebase)
            {
                var delayMs = Math.Max(50, UnityEngine.Random.Range(50, 200));
                await Task.Delay(delayMs).ConfigureAwait(false);
                return;
            }
            
            // TODO: Implement actual Firebase write
            // See PRODUCTION-ISSUES-SOLUTIONS.md for implementation
        }

        private bool ShouldTriggerOutboxSync()
        {
            if (!_enableOutboxSync || _isOutboxSyncRunning)
            {
                return false;
            }

            if (_sessionEventStore == null || !_sessionEventStore.SupportsOutboxSync)
            {
                return false;
            }

            var intervalSeconds = Math.Max(0.25f, _outboxSyncIntervalSeconds);
            return (DateTime.UtcNow - _lastOutboxSyncAtUtc).TotalSeconds >= intervalSeconds;
        }

        private async void TriggerOutboxSync()
        {
            if (_isOutboxSyncRunning)
            {
                return;
            }

            if (_sessionEventStore == null || !_sessionEventStore.SupportsOutboxSync)
            {
                return;
            }

            _isOutboxSyncRunning = true;
            _lastOutboxSyncAtUtc = DateTime.UtcNow;
            List<SessionOutboxBatchItem> claimedBatch = null;
            try
            {
                var claimBatchSize = Math.Max(1, _outboxBatchSize);
                var workerIdentity = ResolveOutboxWorkerIdentity();
                var claimResult = await Task.Run(() =>
                {
                    var result = new OutboxClaimResult
                    {
                        success = false,
                        batch = new List<SessionOutboxBatchItem>(),
                        error = string.Empty,
                    };

                    if (_sessionEventStore == null)
                    {
                        result.error = "Session event store unavailable.";
                        return result;
                    }

                    result.success = _sessionEventStore.TryClaimOutboxBatch(
                        claimBatchSize,
                        workerIdentity,
                        out var claimed,
                        out var claimError);
                    result.batch = claimed ?? new List<SessionOutboxBatchItem>();
                    result.error = claimError;
                    return result;
                });

                if (!claimResult.success)
                {
                    _outboxSyncFailures++;
                    if (_logOutboxSync)
                    {
                        Logger.Warning($"[FirebaseData] Outbox claim failed: {claimResult.error}");
                    }

                    return;
                }

                claimedBatch = claimResult.batch;

                if (claimedBatch == null || claimedBatch.Count == 0)
                {
                    return;
                }

                var uploadResult = await UploadOutboxBatchAsync(claimedBatch);
                if (uploadResult.success)
                {
                    if (uploadResult.syncedEventIds == null)
                    {
                        uploadResult.syncedEventIds = new List<string>();
                    }

                    if (uploadResult.retryRecords == null)
                    {
                        uploadResult.retryRecords = new List<SessionOutboxRetryRecord>();
                    }

                    if (uploadResult.syncedEventIds.Count > 0)
                    {
                        var syncedEventIds = uploadResult.syncedEventIds;
                        var markResult = await Task.Run(() =>
                        {
                            var result = new OutboxStoreResult
                            {
                                success = false,
                                error = string.Empty,
                            };

                            if (_sessionEventStore == null)
                            {
                                result.error = "Session event store unavailable.";
                                return result;
                            }

                            result.success = _sessionEventStore.MarkOutboxBatchSynced(syncedEventIds, out var markError);
                            result.error = markError;
                            return result;
                        });

                        if (markResult.success)
                        {
                            _outboxBatchesSynced++;
                            _outboxEventsSynced += syncedEventIds.Count;
                            _outboxDuplicatesAcknowledged += Math.Max(0, uploadResult.duplicateCount);

                            if (_logOutboxSync)
                            {
                                Logger.Info(
                                    $"[FirebaseData] Outbox synced {syncedEventIds.Count} events " +
                                    $"(duplicates acknowledged: {uploadResult.duplicateCount}).");
                            }
                        }
                        else
                        {
                            _outboxSyncFailures++;
                            if (_logOutboxSync)
                            {
                                Logger.Warning($"[FirebaseData] Outbox mark-synced failed: {markResult.error}");
                            }

                            await TryRescheduleOutboxBatchAsync(claimedBatch, "OUTBOX_MARK_SYNC_FAILED");
                            return;
                        }
                    }

                    if (uploadResult.retryRecords.Count > 0)
                    {
                        await TryRescheduleOutboxRecordsAsync(
                            uploadResult.retryRecords,
                            "INGEST_RETRY_REQUIRED");

                        if (_logOutboxSync)
                        {
                            Logger.Warning(
                                $"[FirebaseData] Outbox ingest requested retry for {uploadResult.retryRecords.Count} events.");
                        }
                    }
                }
                else
                {
                    _outboxSyncFailures++;
                    await TryRescheduleOutboxBatchAsync(claimedBatch, uploadResult.errorCode);
                }
            }
            catch (Exception e)
            {
                _outboxSyncFailures++;
                Logger.Warning($"[FirebaseData] Outbox sync cycle failed: {e.Message}");
                if (claimedBatch != null && claimedBatch.Count > 0)
                {
                    await TryRescheduleOutboxBatchAsync(claimedBatch, "OUTBOX_SYNC_EXCEPTION");
                }
            }
            finally
            {
                _lastOutboxSyncAtUtc = DateTime.UtcNow;
                _isOutboxSyncRunning = false;
            }
        }

        private async Task<OutboxUploadResult> UploadOutboxBatchAsync(IReadOnlyList<SessionOutboxBatchItem> batch)
        {
            if (batch == null || batch.Count == 0)
            {
                return new OutboxUploadResult
                {
                    success = true,
                    errorCode = string.Empty,
                    duplicateCount = 0,
                    syncedEventIds = new List<string>(),
                    retryRecords = new List<SessionOutboxRetryRecord>(),
                };
            }

            var request = BuildSessionIngestBatchRequest(batch);
            var ingestResponse = await SubmitSessionIngestBatchAsync(request);
            if (ingestResponse == null || !ingestResponse.success)
            {
                return new OutboxUploadResult
                {
                    success = false,
                    errorCode = ingestResponse?.errorCode ?? "INGEST_CALL_FAILED",
                    duplicateCount = 0,
                    syncedEventIds = new List<string>(),
                    retryRecords = new List<SessionOutboxRetryRecord>(),
                };
            }

            var syncedEventIds = new List<string>(batch.Count);
            var syncedSet = new HashSet<string>(StringComparer.Ordinal);
            var retryRecords = new List<SessionOutboxRetryRecord>(batch.Count);
            var duplicateCount = 0;
            var attemptByEventId = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var i = 0; i < batch.Count; i++)
            {
                var eventId = batch[i].record?.eventId;
                if (string.IsNullOrWhiteSpace(eventId))
                {
                    continue;
                }

                attemptByEventId[eventId] = Math.Max(1, batch[i].attemptCount);
            }

            if (ingestResponse.eventResults != null)
            {
                for (var i = 0; i < ingestResponse.eventResults.Count; i++)
                {
                    var result = ingestResponse.eventResults[i];
                    var eventId = result?.eventId;
                    if (string.IsNullOrWhiteSpace(eventId) || !attemptByEventId.TryGetValue(eventId, out var attemptCount))
                    {
                        continue;
                    }

                    var status = result.status ?? string.Empty;
                    if (string.Equals(status, SessionIngestStatus.Accepted, StringComparison.OrdinalIgnoreCase))
                    {
                        if (syncedSet.Add(eventId))
                        {
                            syncedEventIds.Add(eventId);
                        }
                    }
                    else if (string.Equals(status, SessionIngestStatus.Duplicate, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicateCount++;
                        if (syncedSet.Add(eventId))
                        {
                            syncedEventIds.Add(eventId);
                        }
                    }
                    else
                    {
                        var retryReason = NormalizeOutboxErrorCode(
                            string.IsNullOrWhiteSpace(result.reasonCode)
                                ? "INGEST_RETRY_REQUESTED"
                                : result.reasonCode);
                        retryRecords.Add(new SessionOutboxRetryRecord
                        {
                            eventId = eventId,
                            nextAttemptUtc = ComputeNextOutboxAttemptUtc(attemptCount),
                            errorCode = retryReason,
                        });

                        if (_logFirebaseBackendDiagnostics || _logOutboxSync)
                        {
                            Logger.Warning(
                                $"[FirebaseData] Outbox event {eventId} requested retry (reason={retryReason}, attempt={attemptCount}).");
                        }
                    }

                    attemptByEventId.Remove(eventId);
                }
            }

            foreach (var pending in attemptByEventId)
            {
                retryRecords.Add(new SessionOutboxRetryRecord
                {
                    eventId = pending.Key,
                    nextAttemptUtc = ComputeNextOutboxAttemptUtc(pending.Value),
                    errorCode = "INGEST_MISSING_EVENT_RESULT",
                });
            }

            return new OutboxUploadResult
            {
                success = true,
                errorCode = string.Empty,
                duplicateCount = duplicateCount,
                syncedEventIds = syncedEventIds,
                retryRecords = retryRecords,
            };
        }

        private SessionIngestBatchRequest BuildSessionIngestBatchRequest(IReadOnlyList<SessionOutboxBatchItem> batch)
        {
            var events = new List<SessionIngestEventEnvelope>(batch?.Count ?? 0);
            if (batch != null)
            {
                for (var i = 0; i < batch.Count; i++)
                {
                    var record = batch[i].record;
                    if (record == null || string.IsNullOrWhiteSpace(record.eventId))
                    {
                        continue;
                    }

                    events.Add(new SessionIngestEventEnvelope
                    {
                        eventId = record.eventId,
                        sessionId = record.sessionId,
                        patientId = record.patientId,
                        therapistId = record.therapistId,
                        deviceId = record.deviceId,
                        sequence = record.sequence,
                        eventType = record.eventType,
                        eventVersion = record.eventVersion,
                        createdAtUtc = record.createdAtUtc,
                        payloadJson = record.payloadJson,
                        checksum = record.checksum,
                    });
                }
            }

            var sourceDeviceId = ResolveDeviceIdentifierSafe();

            return new SessionIngestBatchRequest
            {
                contractVersion = 1,
                ingestBatchId = Guid.NewGuid().ToString(),
                sourceDeviceId = sourceDeviceId,
                submittedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                events = events,
            };
        }

        private async Task<SessionIngestBatchResponse> SubmitSessionIngestBatchAsync(SessionIngestBatchRequest request)
        {
            if (request == null || request.events == null || request.events.Count == 0)
            {
                return new SessionIngestBatchResponse
                {
                    success = true,
                    errorCode = string.Empty,
                    eventResults = new List<SessionIngestEventResult>(),
                };
            }

            if (_simulateFirebase)
            {
                await Task.Delay(UnityEngine.Random.Range(40, 140));
                var simulated = SimulateSessionIngestResponse(request);
                LogIngestResult(
                    operationMode: "SIMULATED",
                    endpoint: "simulated",
                    response: simulated,
                    attemptedEvents: request.events.Count);
                return simulated;
            }

            if (!TryResolveBackendEndpointUrl(_sessionIngestEndpointUrl, out var endpointUrl, out var endpointErrorCode))
            {
                LogBackendCallFailure(
                    operationName: "SESSION_INGEST",
                    endpoint: _sessionIngestEndpointUrl,
                    errorCode: endpointErrorCode,
                    details: "Endpoint resolution failed.");
                return new SessionIngestBatchResponse
                {
                    success = false,
                    errorCode = endpointErrorCode,
                    eventResults = new List<SessionIngestEventResult>(),
                };
            }

            string requestJson;
            try
            {
                requestJson = JsonUtility.ToJson(request);
            }
            catch (Exception e)
            {
                LogBackendCallFailure(
                    operationName: "SESSION_INGEST",
                    endpoint: endpointUrl,
                    errorCode: "INGEST_REQUEST_SERIALIZE_FAILED",
                    details: e.Message);
                return new SessionIngestBatchResponse
                {
                    success = false,
                    errorCode = "INGEST_REQUEST_SERIALIZE_FAILED",
                    eventResults = new List<SessionIngestEventResult>(),
                };
            }

            var backendResult = await PostBackendJsonAsync(endpointUrl, requestJson, "SESSION_INGEST");
            if (!backendResult.success)
            {
                LogBackendCallFailure(
                    operationName: "SESSION_INGEST",
                    endpoint: endpointUrl,
                    errorCode: backendResult.errorCode,
                    details: $"status={backendResult.statusCode}");
                return new SessionIngestBatchResponse
                {
                    success = false,
                    errorCode = backendResult.errorCode,
                    eventResults = new List<SessionIngestEventResult>(),
                };
            }

            if (!TryParseSessionIngestResponse(
                    backendResult.responseBody,
                    out var response,
                    out var parseErrorCode))
            {
                LogBackendCallFailure(
                    operationName: "SESSION_INGEST",
                    endpoint: endpointUrl,
                    errorCode: parseErrorCode,
                    details: "Response parse failed.");
                return new SessionIngestBatchResponse
                {
                    success = false,
                    errorCode = parseErrorCode,
                    eventResults = new List<SessionIngestEventResult>(),
                };
            }

            if (response.eventResults == null)
            {
                response.eventResults = new List<SessionIngestEventResult>();
            }

            if (!response.success && string.IsNullOrWhiteSpace(response.errorCode))
            {
                response.errorCode = "INGEST_BACKEND_REJECTED";
            }

            LogIngestResult(
                operationMode: "NETWORK",
                endpoint: endpointUrl,
                response: response,
                attemptedEvents: request.events.Count);
            return response;
        }

        private static SessionIngestBatchResponse SimulateSessionIngestResponse(SessionIngestBatchRequest request)
        {
            var response = new SessionIngestBatchResponse
            {
                success = true,
                errorCode = string.Empty,
                eventResults = new List<SessionIngestEventResult>(request?.events?.Count ?? 0),
            };

            if (request?.events == null)
            {
                return response;
            }

            lock (SimulatedIngestLock)
            {
                for (var i = 0; i < request.events.Count; i++)
                {
                    var evt = request.events[i];
                    if (evt == null || string.IsNullOrWhiteSpace(evt.eventId))
                    {
                        response.eventResults.Add(new SessionIngestEventResult
                        {
                            eventId = evt?.eventId ?? string.Empty,
                            status = SessionIngestStatus.Retry,
                            reasonCode = "INVALID_EVENT_ID",
                        });
                        continue;
                    }

                    if (SimulatedIngestedEventIds.Contains(evt.eventId))
                    {
                        TrackSimulatedServerEvent(evt);
                        response.eventResults.Add(new SessionIngestEventResult
                        {
                            eventId = evt.eventId,
                            status = SessionIngestStatus.Duplicate,
                            reasonCode = "EVENT_ID_ALREADY_EXISTS",
                        });
                        continue;
                    }

                    SimulatedIngestedEventIds.Add(evt.eventId);
                    TrackSimulatedServerEvent(evt);
                    response.eventResults.Add(new SessionIngestEventResult
                    {
                        eventId = evt.eventId,
                        status = SessionIngestStatus.Accepted,
                        reasonCode = string.Empty,
                    });
                }
            }

            return response;
        }

        private static void TrackSimulatedServerEvent(SessionIngestEventEnvelope evt)
        {
            if (evt == null || string.IsNullOrWhiteSpace(evt.sessionId) || evt.sequence <= 0)
            {
                return;
            }

            if (!SimulatedServerSessionEvents.TryGetValue(evt.sessionId, out var sequenceMap))
            {
                sequenceMap = new Dictionary<long, string>();
                SimulatedServerSessionEvents[evt.sessionId] = sequenceMap;
            }

            if (!sequenceMap.ContainsKey(evt.sequence))
            {
                sequenceMap[evt.sequence] = evt.eventId ?? string.Empty;
            }
        }

        private async Task<SessionReconciliationFetchResult> FetchServerReconciliationIndexAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return new SessionReconciliationFetchResult
                {
                    success = false,
                    errorCode = "SESSION_ID_REQUIRED",
                    events = new List<SessionReconciliationEventReference>(),
                };
            }

            var request = BuildSessionReconciliationRequest(sessionId);
            var response = await SubmitSessionReconciliationRequestAsync(request);
            if (response == null)
            {
                return new SessionReconciliationFetchResult
                {
                    success = false,
                    errorCode = "RECONCILIATION_CALL_FAILED",
                    events = new List<SessionReconciliationEventReference>(),
                };
            }

            return new SessionReconciliationFetchResult
            {
                success = response.success,
                errorCode = response.errorCode,
                events = response.events ?? new List<SessionReconciliationEventReference>(),
            };
        }

        private SessionReconciliationRequest BuildSessionReconciliationRequest(string sessionId)
        {
            var sourceDeviceId = ResolveDeviceIdentifierSafe();

            return new SessionReconciliationRequest
            {
                contractVersion = 1,
                sessionId = sessionId?.Trim() ?? string.Empty,
                requestedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                sourceDeviceId = sourceDeviceId,
            };
        }

        private async Task<SessionReconciliationResponse> SubmitSessionReconciliationRequestAsync(
            SessionReconciliationRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.sessionId))
            {
                return new SessionReconciliationResponse
                {
                    success = false,
                    errorCode = "SESSION_ID_REQUIRED",
                    events = new List<SessionReconciliationEventReference>(),
                };
            }

            if (_simulateFirebase)
            {
                await Task.Delay(UnityEngine.Random.Range(20, 80));
                var simulated = SimulateSessionReconciliationResponse(request);
                LogReconciliationResult(
                    operationMode: "SIMULATED",
                    endpoint: "simulated",
                    response: simulated,
                    sessionId: request.sessionId);
                return simulated;
            }

            if (!TryResolveBackendEndpointUrl(
                    _sessionReconciliationEndpointUrl,
                    out var endpointUrl,
                    out var endpointErrorCode))
            {
                LogBackendCallFailure(
                    operationName: "SESSION_RECONCILIATION",
                    endpoint: _sessionReconciliationEndpointUrl,
                    errorCode: endpointErrorCode,
                    details: "Endpoint resolution failed.");
                return new SessionReconciliationResponse
                {
                    success = false,
                    errorCode = endpointErrorCode,
                    events = new List<SessionReconciliationEventReference>(),
                };
            }

            string requestJson;
            try
            {
                requestJson = JsonUtility.ToJson(request);
            }
            catch (Exception)
            {
                LogBackendCallFailure(
                    operationName: "SESSION_RECONCILIATION",
                    endpoint: endpointUrl,
                    errorCode: "RECONCILIATION_REQUEST_SERIALIZE_FAILED",
                    details: "Request serialization failed.");
                return new SessionReconciliationResponse
                {
                    success = false,
                    errorCode = "RECONCILIATION_REQUEST_SERIALIZE_FAILED",
                    events = new List<SessionReconciliationEventReference>(),
                };
            }

            var backendResult = await PostBackendJsonAsync(endpointUrl, requestJson, "SESSION_RECONCILIATION");
            if (!backendResult.success)
            {
                LogBackendCallFailure(
                    operationName: "SESSION_RECONCILIATION",
                    endpoint: endpointUrl,
                    errorCode: backendResult.errorCode,
                    details: $"status={backendResult.statusCode}");
                return new SessionReconciliationResponse
                {
                    success = false,
                    errorCode = backendResult.errorCode,
                    events = new List<SessionReconciliationEventReference>(),
                };
            }

            if (!TryParseSessionReconciliationResponse(
                    backendResult.responseBody,
                    out var response,
                    out var parseErrorCode))
            {
                LogBackendCallFailure(
                    operationName: "SESSION_RECONCILIATION",
                    endpoint: endpointUrl,
                    errorCode: parseErrorCode,
                    details: "Response parse failed.");
                return new SessionReconciliationResponse
                {
                    success = false,
                    errorCode = parseErrorCode,
                    events = new List<SessionReconciliationEventReference>(),
                };
            }

            if (response.events == null)
            {
                response.events = new List<SessionReconciliationEventReference>();
            }

            if (!response.success && string.IsNullOrWhiteSpace(response.errorCode))
            {
                response.errorCode = "RECONCILIATION_BACKEND_REJECTED";
            }

            LogReconciliationResult(
                operationMode: "NETWORK",
                endpoint: endpointUrl,
                response: response,
                sessionId: request.sessionId);
            return response;
        }

        private static SessionReconciliationResponse SimulateSessionReconciliationResponse(
            SessionReconciliationRequest request)
        {
            var response = new SessionReconciliationResponse
            {
                success = true,
                errorCode = string.Empty,
                events = new List<SessionReconciliationEventReference>(),
            };

            if (request == null || string.IsNullOrWhiteSpace(request.sessionId))
            {
                response.success = false;
                response.errorCode = "SESSION_ID_REQUIRED";
                return response;
            }

            lock (SimulatedIngestLock)
            {
                if (!SimulatedServerSessionEvents.TryGetValue(request.sessionId, out var sequenceMap) || sequenceMap == null)
                {
                    return response;
                }

                var orderedSequences = new List<long>(sequenceMap.Keys);
                orderedSequences.Sort();
                for (var i = 0; i < orderedSequences.Count; i++)
                {
                    var sequence = orderedSequences[i];
                    sequenceMap.TryGetValue(sequence, out var eventId);
                    response.events.Add(new SessionReconciliationEventReference
                    {
                        eventId = eventId ?? string.Empty,
                        sequence = sequence,
                    });
                }
            }

            return response;
        }

        private bool TryResolveBackendEndpointUrl(string configuredUrl, out string resolvedUrl, out string errorCode)
        {
            resolvedUrl = string.Empty;
            errorCode = string.Empty;

            var trimmed = configuredUrl?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                errorCode = "FIREBASE_ENDPOINT_NOT_CONFIGURED";
                return false;
            }

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
            {
                errorCode = "FIREBASE_ENDPOINT_INVALID";
                return false;
            }

            if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            {
                errorCode = "FIREBASE_ENDPOINT_INVALID_SCHEME";
                return false;
            }

            resolvedUrl = AppendApiKeyToEndpoint(parsed.ToString());
            return true;
        }

        private string AppendApiKeyToEndpoint(string endpointUrl)
        {
            if (string.IsNullOrWhiteSpace(endpointUrl))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(_firebaseApiKey))
            {
                return endpointUrl;
            }

            if (endpointUrl.IndexOf("key=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return endpointUrl;
            }

            var separator = endpointUrl.Contains("?") ? "&" : "?";
            return $"{endpointUrl}{separator}key={UnityWebRequest.EscapeURL(_firebaseApiKey.Trim())}";
        }

        private async Task<BackendPostResult> PostBackendJsonAsync(
            string endpointUrl,
            string requestJson,
            string operationName)
        {
            var result = new BackendPostResult
            {
                success = false,
                errorCode = "BACKEND_CALL_UNINITIALIZED",
                statusCode = 0,
                responseBody = string.Empty,
            };

            if (string.IsNullOrWhiteSpace(endpointUrl))
            {
                result.errorCode = "FIREBASE_ENDPOINT_NOT_CONFIGURED";
                return result;
            }

            var payloadJson = string.IsNullOrWhiteSpace(requestJson) ? "{}" : requestJson;
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            var timeoutSeconds = Mathf.Clamp(_firebaseRequestTimeoutSeconds, 3, 120);

            using (var request = new UnityWebRequest(endpointUrl, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(payloadBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = timeoutSeconds;
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept", "application/json");

                if (!string.IsNullOrWhiteSpace(_firebaseAuthBearerToken))
                {
                    request.SetRequestHeader("Authorization", $"Bearer {_firebaseAuthBearerToken.Trim()}");
                }

                if (!string.IsNullOrWhiteSpace(_firebaseApiKey))
                {
                    request.SetRequestHeader("x-api-key", _firebaseApiKey.Trim());
                }

                if (_logFirebaseBackendPayloads)
                {
                    Logger.Info(
                        $"[FirebaseData] Backend {operationName} request -> {endpointUrl} ({payloadBytes.Length} bytes)");
                }

                var asyncOperation = request.SendWebRequest();
                while (!asyncOperation.isDone)
                {
                    await Task.Yield();
                }

                result.statusCode = request.responseCode;
                result.responseBody = request.downloadHandler?.text ?? string.Empty;

                var successStatusCode = request.responseCode >= 200 && request.responseCode < 300;
                if (request.result == UnityWebRequest.Result.Success && successStatusCode)
                {
                    result.success = true;
                    result.errorCode = string.Empty;
                    if (_logFirebaseBackendDiagnostics || _logOutboxSync || _logFirebaseBackendPayloads)
                    {
                        Logger.Info(
                            $"[FirebaseData] Backend {operationName} success (status={request.responseCode}, bytes={payloadBytes.Length})");
                    }
                    return result;
                }

                if (request.result == UnityWebRequest.Result.ConnectionError)
                {
                    result.errorCode = $"{operationName}_CONNECTION_ERROR";
                }
                else if (request.result == UnityWebRequest.Result.DataProcessingError)
                {
                    result.errorCode = $"{operationName}_DATA_PROCESSING_ERROR";
                }
                else if (request.result == UnityWebRequest.Result.ProtocolError)
                {
                    result.errorCode = request.responseCode > 0
                        ? $"{operationName}_HTTP_{request.responseCode}"
                        : $"{operationName}_HTTP_ERROR";
                }
                else
                {
                    result.errorCode = $"{operationName}_REQUEST_FAILED";
                }

                if (_logOutboxSync || _logFirebaseBackendPayloads)
                {
                    var networkError = request.error ?? string.Empty;
                    Logger.Warning(
                        $"[FirebaseData] Backend {operationName} failed: {result.errorCode} " +
                        $"(status={request.responseCode}, unityResult={request.result}, error={networkError})");
                }

                return result;
            }
        }

        private void LogBackendCallFailure(
            string operationName,
            string endpoint,
            string errorCode,
            string details)
        {
            if (!_logFirebaseBackendDiagnostics && !_logOutboxSync && !_logFirebaseBackendPayloads)
            {
                return;
            }

            Logger.Warning(
                $"[FirebaseData] {operationName} failed: {NormalizeOutboxErrorCode(errorCode)} " +
                $"(endpoint={endpoint}, details={details})");
        }

        private void LogIngestResult(
            string operationMode,
            string endpoint,
            SessionIngestBatchResponse response,
            int attemptedEvents)
        {
            if (!_logFirebaseBackendDiagnostics && !_logOutboxSync && !_logFirebaseBackendPayloads)
            {
                return;
            }

            if (response == null)
            {
                Logger.Warning(
                    $"[FirebaseData] SESSION_INGEST {operationMode} returned null response (endpoint={endpoint}).");
                return;
            }

            var acceptedCount = 0;
            var duplicateCount = 0;
            var retryCount = 0;
            if (response.eventResults != null)
            {
                for (var i = 0; i < response.eventResults.Count; i++)
                {
                    var status = response.eventResults[i]?.status ?? string.Empty;
                    if (string.Equals(status, SessionIngestStatus.Accepted, StringComparison.OrdinalIgnoreCase))
                    {
                        acceptedCount++;
                    }
                    else if (string.Equals(status, SessionIngestStatus.Duplicate, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicateCount++;
                    }
                    else
                    {
                        retryCount++;
                    }
                }
            }

            if (response.success)
            {
                Logger.Info(
                    $"[FirebaseData] SESSION_INGEST {operationMode} success: attempted={attemptedEvents}, accepted={acceptedCount}, duplicates={duplicateCount}, retry={retryCount}, endpoint={endpoint}");
                return;
            }

            Logger.Warning(
                $"[FirebaseData] SESSION_INGEST {operationMode} failed: reason={NormalizeOutboxErrorCode(response.errorCode)}, attempted={attemptedEvents}, accepted={acceptedCount}, duplicates={duplicateCount}, retry={retryCount}, endpoint={endpoint}");
        }

        private void LogReconciliationResult(
            string operationMode,
            string endpoint,
            SessionReconciliationResponse response,
            string sessionId)
        {
            if (!_logFirebaseBackendDiagnostics && !_logOutboxSync && !_logFirebaseBackendPayloads)
            {
                return;
            }

            if (response == null)
            {
                Logger.Warning(
                    $"[FirebaseData] SESSION_RECONCILIATION {operationMode} returned null response (session={sessionId}, endpoint={endpoint}).");
                return;
            }

            var eventCount = response.events == null ? 0 : response.events.Count;
            if (response.success)
            {
                Logger.Info(
                    $"[FirebaseData] SESSION_RECONCILIATION {operationMode} success: session={sessionId}, events={eventCount}, endpoint={endpoint}");
                return;
            }

            Logger.Warning(
                $"[FirebaseData] SESSION_RECONCILIATION {operationMode} failed: session={sessionId}, reason={NormalizeOutboxErrorCode(response.errorCode)}, events={eventCount}, endpoint={endpoint}");
        }

        private static bool TryParseSessionIngestResponse(
            string responseBody,
            out SessionIngestBatchResponse response,
            out string errorCode)
        {
            response = null;
            errorCode = string.Empty;

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                errorCode = "INGEST_EMPTY_RESPONSE";
                return false;
            }

            try
            {
                response = JsonUtility.FromJson<SessionIngestBatchResponse>(responseBody);
                if (response != null &&
                    (!string.IsNullOrWhiteSpace(response.errorCode) ||
                     response.success ||
                     response.eventResults != null))
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Ignore and try wrapped response parsing.
            }

            try
            {
                var wrapped = JsonUtility.FromJson<SessionIngestBatchResponseEnvelope>(responseBody);
                response = wrapped?.result ?? wrapped?.data;
                if (response != null)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Ignore and return parse failure.
            }

            errorCode = "INGEST_RESPONSE_PARSE_FAILED";
            return false;
        }

        private static bool TryParseSessionReconciliationResponse(
            string responseBody,
            out SessionReconciliationResponse response,
            out string errorCode)
        {
            response = null;
            errorCode = string.Empty;

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                errorCode = "RECONCILIATION_EMPTY_RESPONSE";
                return false;
            }

            try
            {
                response = JsonUtility.FromJson<SessionReconciliationResponse>(responseBody);
                if (response != null &&
                    (!string.IsNullOrWhiteSpace(response.errorCode) ||
                     response.success ||
                     response.events != null))
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Ignore and try wrapped response parsing.
            }

            try
            {
                var wrapped = JsonUtility.FromJson<SessionReconciliationResponseEnvelope>(responseBody);
                response = wrapped?.result ?? wrapped?.data;
                if (response != null)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Ignore and return parse failure.
            }

            errorCode = "RECONCILIATION_RESPONSE_PARSE_FAILED";
            return false;
        }

        private async Task<bool> TryRescheduleOutboxBatchAsync(
            IReadOnlyList<SessionOutboxBatchItem> batch,
            string errorCode)
        {
            if (_sessionEventStore == null || batch == null || batch.Count == 0)
            {
                return false;
            }

            var retryRecords = new List<SessionOutboxRetryRecord>(batch.Count);
            for (var i = 0; i < batch.Count; i++)
            {
                var item = batch[i];
                if (item.record == null || string.IsNullOrWhiteSpace(item.record.eventId))
                {
                    continue;
                }

                retryRecords.Add(new SessionOutboxRetryRecord
                {
                    eventId = item.record.eventId,
                    nextAttemptUtc = ComputeNextOutboxAttemptUtc(item.attemptCount),
                    errorCode = NormalizeOutboxErrorCode(errorCode),
                });
            }

            if (retryRecords.Count == 0)
            {
                return false;
            }

            return await TryRescheduleOutboxRecordsAsync(retryRecords, errorCode);
        }

        private async Task<bool> TryRescheduleOutboxRecordsAsync(
            IReadOnlyList<SessionOutboxRetryRecord> retryRecords,
            string fallbackErrorCode)
        {
            if (_sessionEventStore == null || retryRecords == null || retryRecords.Count == 0)
            {
                return false;
            }

            var normalizedRetries = new List<SessionOutboxRetryRecord>(retryRecords.Count);
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

                normalizedRetries.Add(new SessionOutboxRetryRecord
                {
                    eventId = retryRecord.eventId,
                    nextAttemptUtc = nextAttemptUtc,
                    errorCode = NormalizeOutboxErrorCode(
                        string.IsNullOrWhiteSpace(retryRecord.errorCode)
                            ? fallbackErrorCode
                            : retryRecord.errorCode),
                });
            }

            if (normalizedRetries.Count == 0)
            {
                return false;
            }

            var retryResult = await Task.Run(() =>
            {
                var result = new OutboxStoreResult
                {
                    success = false,
                    error = string.Empty,
                };

                if (_sessionEventStore == null)
                {
                    result.error = "Session event store unavailable.";
                    return result;
                }

                result.success = _sessionEventStore.RescheduleOutboxBatch(normalizedRetries, out var retryError);
                result.error = retryError;
                return result;
            });

            if (retryResult.success)
            {
                _outboxRetriesScheduled += normalizedRetries.Count;

                if (_logOutboxSync)
                {
                    Logger.Warning(
                        $"[FirebaseData] Outbox retry scheduled for {normalizedRetries.Count} events ({NormalizeOutboxErrorCode(fallbackErrorCode)}).");
                }

                return true;
            }

            _outboxSyncFailures++;
            Logger.Warning($"[FirebaseData] Outbox retry scheduling failed: {retryResult.error}");
            return false;
        }

        private DateTime ComputeNextOutboxAttemptUtc(int attemptCount)
        {
            var safeAttempt = Math.Max(1, attemptCount);
            var baseSeconds = Math.Max(0.1f, _outboxBackoffBaseSeconds);
            var maxSeconds = Math.Max(baseSeconds, _outboxBackoffMaxSeconds);
            var jitter = Mathf.Clamp01(_outboxBackoffJitter);

            var delaySeconds = baseSeconds * Math.Pow(2d, safeAttempt - 1d);
            delaySeconds = Math.Min(delaySeconds, maxSeconds);

            var jitterFactor = 1d + UnityEngine.Random.Range(-jitter, jitter);
            if (jitterFactor < 0.1d)
            {
                jitterFactor = 0.1d;
            }

            var jitteredDelaySeconds = Math.Min(maxSeconds, Math.Max(0.05d, delaySeconds * jitterFactor));
            return DateTime.UtcNow.AddSeconds(jitteredDelaySeconds);
        }

        private string ResolveOutboxWorkerIdentity()
        {
            if (!string.IsNullOrWhiteSpace(_outboxWorkerId))
            {
                return _outboxWorkerId.Trim();
            }

            var uniqueDeviceId = ResolveDeviceIdentifierSafe();

            return $"quest_{uniqueDeviceId}";
        }

        private static string ResolveDeviceIdentifierSafe()
        {
            try
            {
                var deviceId = SystemInfo.deviceUniqueIdentifier;
                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    deviceId = SystemInfo.deviceName;
                }

                return string.IsNullOrWhiteSpace(deviceId) ? "unknown_device" : deviceId;
            }
            catch (Exception)
            {
                return "unknown_device";
            }
        }

        private static string NormalizeOutboxErrorCode(string rawErrorCode)
        {
            if (string.IsNullOrWhiteSpace(rawErrorCode))
            {
                return "OUTBOX_UPLOAD_FAILED";
            }

            var normalized = rawErrorCode.Trim();
            if (normalized.Length <= 64)
            {
                return normalized;
            }

            return normalized.Substring(0, 64);
        }

        private static void ResolveSequenceRange(SortedSet<long> sequences, out long minSequence, out long maxSequence)
        {
            minSequence = 0;
            maxSequence = 0;

            if (sequences == null || sequences.Count == 0)
            {
                return;
            }

            using (var enumerator = sequences.GetEnumerator())
            {
                if (enumerator.MoveNext())
                {
                    minSequence = enumerator.Current;
                    maxSequence = enumerator.Current;
                    while (enumerator.MoveNext())
                    {
                        maxSequence = enumerator.Current;
                    }
                }
            }
        }

        private static string FormatSequencePreview(IReadOnlyList<long> sequences, int maxItems)
        {
            if (sequences == null || sequences.Count == 0)
            {
                return "[]";
            }

            var safeMax = Math.Max(1, maxItems);
            var count = Math.Min(safeMax, sequences.Count);
            var builder = new StringBuilder();
            builder.Append('[');
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(sequences[i].ToString(CultureInfo.InvariantCulture));
            }

            if (sequences.Count > count)
            {
                builder.Append(",...");
            }

            builder.Append(']');
            return builder.ToString();
        }

        private void InitializeDurableStore()
        {
            if (!_persistCriticalSessionEventsLocally)
            {
                return;
            }

            if (_sessionEventStore != null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_localDurablePath))
            {
                var folder = string.IsNullOrWhiteSpace(_localDurableFolder) ? "session_resilience" : _localDurableFolder.Trim();
                var fileName = string.IsNullOrWhiteSpace(_localDurableFileName) ? "events.ndjson" : _localDurableFileName.Trim();
                _localDurablePath = Path.Combine(Application.persistentDataPath, folder, fileName);
            }

            if (string.IsNullOrWhiteSpace(_sqliteStorePath))
            {
                var folder = string.IsNullOrWhiteSpace(_localDurableFolder) ? "session_resilience" : _localDurableFolder.Trim();
                var sqliteFileName = string.IsNullOrWhiteSpace(_sqliteStoreFileName) ? "session_events.db" : _sqliteStoreFileName.Trim();
                _sqliteStorePath = Path.Combine(Application.persistentDataPath, folder, sqliteFileName);
            }

            _sessionEventStore = new SessionEventStore(
                enabled: true,
                sqlitePath: _sqliteStorePath,
                jsonLinePath: _localDurablePath,
                preferSqliteWal: _preferSqliteWalStore,
                mirrorToJsonLine: _mirrorDurableEventsToNdjson,
                maxPendingWrites: _maxDurableStoreQueueSize,
                logVerbose: _logLocalPersistence);
        }

        private void DisposeDurableStore()
        {
            if (_sessionEventStore == null)
            {
                return;
            }

            try
            {
                _sessionEventStore.Flush(TimeSpan.FromSeconds(2));
                _sessionEventStore.Dispose();
            }
            finally
            {
                _sessionEventStore = null;
            }
        }

        private bool ShouldPersistLocallyBeforeNetwork(GameDataPoint dataPoint)
        {
            if (!_persistCriticalSessionEventsLocally)
            {
                return false;
            }

            if (dataPoint == null || string.IsNullOrWhiteSpace(dataPoint.dataType))
            {
                return false;
            }

            if (_persistAllEventsToDurableStore)
            {
                return true;
            }

            return CriticalDurableEventTypes.Contains(dataPoint.dataType);
        }

        private bool TryPersistCriticalEventLocally(GameDataPoint dataPoint, out string error)
        {
            error = string.Empty;

            if (dataPoint == null)
            {
                error = "Data point is null.";
                return false;
            }

            if (_sessionEventStore == null)
            {
                error = "Durable event store is not initialized.";
                return false;
            }

            try
            {
                var record = new DurableSessionEventRecord
                {
                    eventId = Guid.NewGuid().ToString(),
                    sessionId = ResolveSessionId(dataPoint),
                    patientId = ResolvePatientId(dataPoint),
                    therapistId = ResolveTherapistId(dataPoint),
                    deviceId = ResolveDeviceIdentifierSafe(),
                    sequence = 0,
                    eventType = dataPoint.dataType ?? "unknown_event",
                    eventVersion = 1,
                    createdAtUtc = ResolveTimestamp(dataPoint).ToString("O", CultureInfo.InvariantCulture),
                    payloadJson = SerializePayloadDictionary(dataPoint.payload),
                };
                record.checksum = SessionEventStore.ComputeChecksum(record);

                if (!_sessionEventStore.TryEnqueue(record, out error))
                {
                    return false;
                }

                if (_logLocalPersistence)
                {
                    Logger.Info($"[FirebaseData] Durable store enqueue: {record.eventType} ({record.sessionId})");
                }

                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        private void HandleSessionChanged()
        {
            RefreshSessionMetadataFromContext();
        }

        private void RefreshSessionMetadataFromContext()
        {
            if (_sessionContext == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_sessionContext.SessionId))
            {
                _sessionId = _sessionContext.SessionId;
            }

            if (!string.IsNullOrWhiteSpace(_sessionContext.PatientId))
            {
                _patientId = _sessionContext.PatientId;
            }

            if (!string.IsNullOrWhiteSpace(_sessionContext.TherapistId))
            {
                _therapistId = _sessionContext.TherapistId;
            }
        }

        private string ResolveSessionId(GameDataPoint dataPoint)
        {
            if (TryGetPayloadString(dataPoint?.payload, "sessionId", out var fromPayload))
            {
                return fromPayload;
            }

            RefreshSessionMetadataFromContext();
            return string.IsNullOrWhiteSpace(_sessionId) ? "unknown_session" : _sessionId;
        }

        private string ResolvePatientId(GameDataPoint dataPoint)
        {
            if (TryGetPayloadString(dataPoint?.payload, "patientId", out var fromPayload))
            {
                return fromPayload;
            }

            RefreshSessionMetadataFromContext();
            return string.IsNullOrWhiteSpace(_patientId) ? "unknown_patient" : _patientId;
        }

        private string ResolveTherapistId(GameDataPoint dataPoint)
        {
            if (TryGetPayloadString(dataPoint?.payload, "therapistId", out var fromPayload))
            {
                return fromPayload;
            }

            RefreshSessionMetadataFromContext();
            return string.IsNullOrWhiteSpace(_therapistId) ? "unknown_therapist" : _therapistId;
        }

        private static bool TryGetPayloadString(IReadOnlyDictionary<string, object> payload, string key, out string value)
        {
            value = string.Empty;
            if (payload == null || string.IsNullOrWhiteSpace(key) || !payload.TryGetValue(key, out var raw) || raw == null)
            {
                return false;
            }

            var converted = Convert.ToString(raw, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(converted))
            {
                return false;
            }

            value = converted;
            return true;
        }

        private static DateTime ResolveTimestamp(GameDataPoint dataPoint)
        {
            if (dataPoint == null || dataPoint.timestamp == default)
            {
                return DateTime.UtcNow;
            }

            return dataPoint.timestamp.Kind == DateTimeKind.Utc
                ? dataPoint.timestamp
                : dataPoint.timestamp.ToUniversalTime();
        }

        private static string SerializePayloadDictionary(IReadOnlyDictionary<string, object> payload)
        {
            if (payload == null || payload.Count == 0)
            {
                return "{}";
            }

            var builder = new StringBuilder(256);
            AppendJsonObject(builder, payload);
            return builder.ToString();
        }

        private static void AppendJsonObject(StringBuilder builder, IReadOnlyDictionary<string, object> values)
        {
            builder.Append('{');

            var first = true;
            foreach (var kvp in values)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                AppendJsonString(builder, kvp.Key);
                builder.Append(':');
                AppendJsonValue(builder, kvp.Value);
            }

            builder.Append('}');
        }

        private static void AppendJsonValue(StringBuilder builder, object value)
        {
            if (value == null)
            {
                builder.Append("null");
                return;
            }

            switch (value)
            {
                case string text:
                    AppendJsonString(builder, text);
                    return;
                case bool boolValue:
                    builder.Append(boolValue ? "true" : "false");
                    return;
                case byte _:
                case sbyte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                case float _:
                case double _:
                case decimal _:
                    builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                case DateTime dateTimeValue:
                    var utc = dateTimeValue.Kind == DateTimeKind.Utc ? dateTimeValue : dateTimeValue.ToUniversalTime();
                    AppendJsonString(builder, utc.ToString("O", CultureInfo.InvariantCulture));
                    return;
                case IReadOnlyDictionary<string, object> dictionaryValue:
                    AppendJsonObject(builder, dictionaryValue);
                    return;
                case IDictionary<string, object> mutableDictionaryValue:
                    AppendJsonObject(builder, new Dictionary<string, object>(mutableDictionaryValue));
                    return;
                case IEnumerable enumerableValue when !(value is string):
                    AppendJsonArray(builder, enumerableValue);
                    return;
                default:
                    AppendJsonString(builder, Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
            }
        }

        private static void AppendJsonArray(StringBuilder builder, IEnumerable values)
        {
            builder.Append('[');

            var first = true;
            foreach (var item in values)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                AppendJsonValue(builder, item);
            }

            builder.Append(']');
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');

            if (!string.IsNullOrEmpty(value))
            {
                foreach (var ch in value)
                {
                    switch (ch)
                    {
                        case '"':
                            builder.Append("\\\"");
                            break;
                        case '\\':
                            builder.Append("\\\\");
                            break;
                        case '\b':
                            builder.Append("\\b");
                            break;
                        case '\f':
                            builder.Append("\\f");
                            break;
                        case '\n':
                            builder.Append("\\n");
                            break;
                        case '\r':
                            builder.Append("\\r");
                            break;
                        case '\t':
                            builder.Append("\\t");
                            break;
                        default:
                            if (ch < 32)
                            {
                                builder.Append("\\u");
                                builder.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                builder.Append(ch);
                            }
                            break;
                    }
                }
            }

            builder.Append('"');
        }
        
#if UNITY_EDITOR
        [ContextMenu("Log Statistics")]
        private void DebugLogStatistics()
        {
            var stats = GetStatistics();
            
            Logger.Info(
                $"[FirebaseData] Statistics:\n" +
                $"Queue size: {stats.queueSize}\n" +
                $"Points queued: {stats.pointsQueued}\n" +
                $"Points written: {stats.pointsWritten}\n" +
                $"Points failed: {stats.pointsFailed}\n" +
                $"Batches written: {stats.batchesWritten}\n" +
                $"Durable critical events: {stats.criticalEventsPersisted}\n" +
                $"Durable persist failures: {stats.criticalEventPersistFailures}\n" +
                $"Durable store mode: {stats.durableStoreMode}\n" +
                $"Durable pending writes: {stats.durablePendingWrites}\n" +
                $"Durable events persisted: {stats.durableEventsPersisted}\n" +
                $"Durable events failed: {stats.durableEventsFailed}\n" +
                $"Durable events dropped: {stats.durableEventsDropped}\n" +
                $"Outbox supported: {stats.durableOutboxSupported}\n" +
                $"Outbox pending: {stats.durableOutboxPending}\n" +
                $"Outbox in-flight: {stats.durableOutboxInFlight}\n" +
                $"Outbox synced: {stats.durableOutboxSynced}\n" +
                $"Outbox retries (store): {stats.durableOutboxRetryCount}\n" +
                $"Outbox batches synced: {stats.outboxBatchesSynced}\n" +
                $"Outbox events synced: {stats.outboxEventsSynced}\n" +
                $"Outbox sync failures: {stats.outboxSyncFailures}\n" +
                $"Outbox retries scheduled: {stats.outboxRetriesScheduled}\n" +
                $"Outbox duplicates acknowledged: {stats.outboxDuplicatesAcknowledged}\n" +
                $"Success rate: {stats.successRate:F1}%"
            );
        }

        [ContextMenu("Log Reconciliation Report")]
        private async void DebugLogReconciliationReport()
        {
            RefreshSessionMetadataFromContext();
            var targetSessionId = string.IsNullOrWhiteSpace(_sessionId)
                ? (_sessionContext?.SessionId ?? string.Empty)
                : _sessionId;
            var report = await BuildSessionReconciliationReportAsync(targetSessionId);

            Logger.Info(
                $"[FirebaseData] Reconciliation report:\n" +
                $"Session: {report.sessionId}\n" +
                $"Success: {report.success}\n" +
                $"Reason: {report.reasonCode}\n" +
                $"Local events: {report.localEvents}\n" +
                $"Local sequences: {report.localSequenceCount}\n" +
                $"Local sequence range: {report.localMinSequence}..{report.localMaxSequence}\n" +
                $"Server events: {report.serverEvents}\n" +
                $"Server sequences: {report.serverSequenceCount}\n" +
                $"Server sequence range: {report.serverMinSequence}..{report.serverMaxSequence}\n" +
                $"Missing on server: {report.missingOnServerCount} {FormatSequencePreview(report.missingOnServerSequences, 12)}\n" +
                $"Missing on device: {report.missingOnDeviceCount} {FormatSequencePreview(report.missingOnDeviceSequences, 12)}\n" +
                $"Local outbox pending/in-flight/synced: {report.localOutboxPending}/{report.localOutboxInFlight}/{report.localOutboxSynced}"
            );
        }

        [ContextMenu("Manual Re-sync Current Session")]
        private async void DebugManualResyncCurrentSession()
        {
            RefreshSessionMetadataFromContext();
            var targetSessionId = string.IsNullOrWhiteSpace(_sessionId)
                ? (_sessionContext?.SessionId ?? string.Empty)
                : _sessionId;
            var report = await RunManualResyncAsync(
                targetSessionId,
                includeSyncedEvents: false,
                requestedBy: "UNITY_EDITOR",
                reasonCode: "MANUAL_CONTEXT_MENU");

            Logger.Info(
                $"[FirebaseData] Manual re-sync report:\n" +
                $"Session: {report.sessionId}\n" +
                $"Success: {report.success}\n" +
                $"Reason: {report.reasonCode}\n" +
                $"Targeted events: {report.targetedEvents}\n" +
                $"Outbox rows updated: {report.outboxRowsUpdated}\n" +
                $"Upload cycle triggered: {report.uploadCycleTriggered}\n" +
                $"Missing on server before/after: {report.beforeMissingOnServerCount}/{report.afterMissingOnServerCount}\n" +
                $"Missing on device before/after: {report.beforeMissingOnDeviceCount}/{report.afterMissingOnDeviceCount}\n" +
                $"Outbox pending before/after: {report.beforeOutboxPending}/{report.afterOutboxPending}\n" +
                $"Sequence preview: {FormatSequencePreview(report.targetedSequencePreview, 16)}\n" +
                $"Details: {report.details}"
            );
        }
        
        [ContextMenu("Test - Queue 100 Points")]
        private void DebugQueue100Points()
        {
            for (int i = 0; i < 100; i++)
            {
                QueueDataPoint(new GameDataPoint
                {
                    timestamp = DateTime.UtcNow,
                    dataType = "test_data",
                    payload = new Dictionary<string, object>
                    {
                        { "index", i },
                        { "random", UnityEngine.Random.value }
                    }
                });
            }
            
            Logger.Info("[FirebaseData] Queued 100 test points");
        }
#endif
    }
    
    [Serializable]
    public struct QueueStatistics
    {
        public int queueSize;
        public int pointsQueued;
        public int pointsWritten;
        public int pointsFailed;
        public int batchesWritten;
        public int criticalEventsPersisted;
        public int criticalEventPersistFailures;
        public int durablePendingWrites;
        public int durableEventsPersisted;
        public int durableEventsFailed;
        public int durableEventsDropped;
        public bool durableOutboxSupported;
        public int durableOutboxPending;
        public int durableOutboxInFlight;
        public int durableOutboxSynced;
        public int durableOutboxRetryCount;
        public int outboxBatchesSynced;
        public int outboxEventsSynced;
        public int outboxSyncFailures;
        public int outboxRetriesScheduled;
        public int outboxDuplicatesAcknowledged;
        public string durableStoreMode;
        public float successRate;
    }

    [Serializable]
    public sealed class SessionReconciliationReport
    {
        public bool success;
        public string reasonCode;
        public string sessionId;
        public string generatedAtUtc;

        public int localEvents;
        public int localSequenceCount;
        public long localMinSequence;
        public long localMaxSequence;
        public int localOutboxPending;
        public int localOutboxInFlight;
        public int localOutboxSynced;

        public int serverEvents;
        public int serverSequenceCount;
        public long serverMinSequence;
        public long serverMaxSequence;

        public int missingOnServerCount;
        public int missingOnDeviceCount;
        public List<long> missingOnServerSequences;
        public List<long> missingOnDeviceSequences;
    }

    [Serializable]
    public sealed class SessionManualResyncReport
    {
        public bool success;
        public string reasonCode;
        public string sessionId;
        public string requestedBy;
        public string requestedReasonCode;
        public string requestedAtUtc;
        public bool includeSyncedEvents;
        public int targetedEvents;
        public int outboxRowsUpdated;
        public bool uploadCycleTriggered;
        public int localEvents;
        public string beforeReasonCode;
        public int beforeMissingOnServerCount;
        public int beforeMissingOnDeviceCount;
        public int beforeOutboxPending;
        public string afterReasonCode;
        public int afterMissingOnServerCount;
        public int afterMissingOnDeviceCount;
        public int afterOutboxPending;
        public List<long> targetedSequencePreview;
        public string details;
    }
}
