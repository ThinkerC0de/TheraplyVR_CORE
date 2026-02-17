using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Periodic and phase-boundary session snapshotting with restore support.
    /// Writes are offloaded to a background worker to keep gameplay thread idle.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameSessionSnapshotService : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private GameSessionContext _sessionContext;
        [SerializeField] private GameRuntimeService _runtimeService;

        [Header("Snapshot Policy")]
        [SerializeField] private bool _autoRestoreOnStart = true;
        [SerializeField] private bool _recoverToInterruptedOnStart = true;
        [SerializeField] private string _recoveryReasonCode = "AUTO_RECOVERY_APP_RESTART";
        [SerializeField] private bool _snapshotOnSessionChanged = true;
        [SerializeField] private bool _snapshotOnStateBoundaries = true;
        [SerializeField] private float _snapshotIntervalSeconds = 5f;

        [Header("Persistence")]
        [SerializeField] private string _snapshotFolder = "session_resilience";
        [SerializeField] private string _snapshotFileName = "snapshot.json";
        [SerializeField] private int _maxPendingSnapshots = 32;

        [Header("Debug")]
        [SerializeField] private bool _logSnapshots = false;

        private string _snapshotPath;
        private BlockingCollection<SessionSnapshotRecord> _pendingSnapshots;
        private CancellationTokenSource _cancelSource;
        private Task _writerTask;
        private Coroutine _periodicSnapshotRoutine;

        private int _snapshotsWritten;
        private int _snapshotWriteFailures;
        private int _snapshotDropped;
        private int _restoreSuccessCount;
        private int _restoreFailureCount;

        private void Awake()
        {
            ResolveDependencies();

            var folder = string.IsNullOrWhiteSpace(_snapshotFolder) ? "session_resilience" : _snapshotFolder.Trim();
            var fileName = string.IsNullOrWhiteSpace(_snapshotFileName) ? "snapshot.json" : _snapshotFileName.Trim();
            _snapshotPath = Path.Combine(Application.persistentDataPath, folder, fileName);

            _pendingSnapshots = new BlockingCollection<SessionSnapshotRecord>(
                new ConcurrentQueue<SessionSnapshotRecord>(),
                Math.Max(4, _maxPendingSnapshots));
            _cancelSource = new CancellationTokenSource();
            _writerTask = Task.Run(() => SnapshotWriterLoop(_cancelSource.Token), _cancelSource.Token);
        }

        private void Start()
        {
            if (_autoRestoreOnStart)
            {
                TryRestoreLatestSnapshot();
            }
        }

        private void OnEnable()
        {
            if (_sessionContext != null)
            {
                _sessionContext.OnSessionChanged += HandleSessionChanged;
                _sessionContext.OnSessionStateChanged += HandleSessionStateChanged;
            }

            StartPeriodicSnapshots();
        }

        private void OnDisable()
        {
            if (_sessionContext != null)
            {
                _sessionContext.OnSessionChanged -= HandleSessionChanged;
                _sessionContext.OnSessionStateChanged -= HandleSessionStateChanged;
            }

            StopPeriodicSnapshots();
        }

        private void OnApplicationPause(bool pause)
        {
            if (!pause)
            {
                return;
            }

            EnqueueSnapshot("APP_PAUSE", force: true);
            FlushPending(TimeSpan.FromSeconds(2));
        }

        private void OnApplicationQuit()
        {
            EnqueueSnapshot("APP_QUIT", force: true);
            FlushPending(TimeSpan.FromSeconds(2));
        }

        private void OnDestroy()
        {
            StopPeriodicSnapshots();
            DisposeWorker();
        }

        public bool TryRestoreLatestSnapshot()
        {
            ResolveDependencies();

            if (_sessionContext == null)
            {
                _restoreFailureCount++;
                Logger.Warning("[SessionSnapshot] Restore skipped: session context missing.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(_snapshotPath) || !File.Exists(_snapshotPath))
            {
                return false;
            }

            try
            {
                var json = File.ReadAllText(_snapshotPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    _restoreFailureCount++;
                    return false;
                }

                var snapshot = JsonUtility.FromJson<SessionSnapshotRecord>(json);
                if (!IsSnapshotValid(snapshot))
                {
                    _restoreFailureCount++;
                    Logger.Warning("[SessionSnapshot] Restore failed: snapshot payload is invalid.");
                    return false;
                }

                if (!GameContracts.SessionFsmContract.TryParseWireState(snapshot.sessionState, out var restoredState))
                {
                    restoredState = GameContracts.SessionLifecycleState.CREATED;
                }

                if (IsTerminalState(restoredState))
                {
                    if (_logSnapshots)
                    {
                        Logger.Info(
                            $"[SessionSnapshot] Auto-restore skipped for terminal snapshot state={restoredState} session={snapshot.sessionId}");
                    }
                    return false;
                }

                var sourceState = restoredState;
                var recoveredState = ResolveRecoveredState(restoredState);
                var restoreReasonCode = string.IsNullOrWhiteSpace(_recoveryReasonCode)
                    ? "AUTO_RECOVERY_APP_RESTART"
                    : _recoveryReasonCode.Trim();

                var startedAtUtc = ParseUtc(snapshot.startedAtUtc);
                var restored = _sessionContext.RestoreSession(
                    patientId: snapshot.patientId,
                    therapistId: snapshot.therapistId,
                    sessionId: snapshot.sessionId,
                    startedAtUtc: startedAtUtc,
                    restoredState: recoveredState,
                    reasonCode: restoreReasonCode,
                    forceReplaceActive: true);

                if (!restored)
                {
                    _restoreFailureCount++;
                    return false;
                }

                if (_runtimeService != null && !string.IsNullOrWhiteSpace(snapshot.activeGameId))
                {
                    _runtimeService.SetActiveGame(snapshot.activeGameId);
                }

                _restoreSuccessCount++;

                if (_logSnapshots)
                {
                    Logger.Info(
                        $"[SessionSnapshot] Restored snapshot for session={snapshot.sessionId}, sourceState={sourceState}, recoveredState={recoveredState}, game={snapshot.activeGameId}");
                }

                // Persist immediate checkpoint after restore to keep capture timestamp fresh.
                EnqueueSnapshot("RESTORE_APPLIED", force: true);
                return true;
            }
            catch (Exception e)
            {
                _restoreFailureCount++;
                Logger.Warning($"[SessionSnapshot] Restore failed: {e.Message}");
                return false;
            }
        }

        private void HandleSessionChanged()
        {
            if (_snapshotOnSessionChanged)
            {
                EnqueueSnapshot("SESSION_CHANGED", force: false);
            }
        }

        private void HandleSessionStateChanged(
            GameContracts.SessionLifecycleState previousState,
            GameContracts.SessionLifecycleState currentState,
            string reasonCode)
        {
            if (_snapshotOnStateBoundaries)
            {
                EnqueueSnapshot(
                    $"STATE_BOUNDARY:{previousState}->{currentState}:{reasonCode ?? "unspecified"}",
                    force: true);
            }
        }

        private void StartPeriodicSnapshots()
        {
            StopPeriodicSnapshots();
            _periodicSnapshotRoutine = StartCoroutine(PeriodicSnapshotLoop());
        }

        private void StopPeriodicSnapshots()
        {
            if (_periodicSnapshotRoutine == null)
            {
                return;
            }

            StopCoroutine(_periodicSnapshotRoutine);
            _periodicSnapshotRoutine = null;
        }

        private System.Collections.IEnumerator PeriodicSnapshotLoop()
        {
            var interval = Mathf.Max(1f, _snapshotIntervalSeconds);
            var wait = new WaitForSeconds(interval);

            while (true)
            {
                EnqueueSnapshot("PERIODIC", force: false);
                yield return wait;
            }
        }

        private void EnqueueSnapshot(string reasonCode, bool force)
        {
            if (_pendingSnapshots == null || _pendingSnapshots.IsAddingCompleted)
            {
                return;
            }

            var snapshot = BuildSnapshotRecord(reasonCode);
            if (!force && !ShouldPersistSnapshot(snapshot))
            {
                return;
            }

            if (!_pendingSnapshots.TryAdd(snapshot))
            {
                Interlocked.Increment(ref _snapshotDropped);
                if (_logSnapshots)
                {
                    Logger.Warning("[SessionSnapshot] Pending queue full, snapshot dropped.");
                }
            }
        }

        private SessionSnapshotRecord BuildSnapshotRecord(string reasonCode)
        {
            ResolveDependencies();

            var nowUtc = DateTime.UtcNow;
            var state = _sessionContext != null
                ? GameContracts.SessionFsmContract.ToWireState(_sessionContext.SessionState)
                : GameContracts.SessionFsmContract.ToWireState(GameContracts.SessionLifecycleState.CREATED);

            var startedAtUtc = _sessionContext?.StartedAtUtc ?? DateTime.UtcNow;
            if (startedAtUtc == default)
            {
                startedAtUtc = DateTime.UtcNow;
            }

            return new SessionSnapshotRecord
            {
                snapshotVersion = 1,
                snapshotId = Guid.NewGuid().ToString(),
                sessionId = _sessionContext?.SessionId ?? string.Empty,
                patientId = _sessionContext?.PatientId ?? "unknown_patient",
                therapistId = _sessionContext?.TherapistId ?? "unknown_therapist",
                sessionState = state,
                startedAtUtc = startedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                activeGameId = _runtimeService?.ActiveGameId ?? string.Empty,
                capturedAtUtc = nowUtc.ToString("O", CultureInfo.InvariantCulture),
                capturedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                reasonCode = reasonCode ?? string.Empty,
            };
        }

        private bool ShouldPersistSnapshot(SessionSnapshotRecord snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }

            // Avoid writing empty snapshots before any session identity is available.
            return !string.IsNullOrWhiteSpace(snapshot.sessionId);
        }

        private void SnapshotWriterLoop(CancellationToken cancellationToken)
        {
            try
            {
                foreach (var snapshot in _pendingSnapshots.GetConsumingEnumerable(cancellationToken))
                {
                    if (snapshot == null)
                    {
                        continue;
                    }

                    try
                    {
                        PersistSnapshot(snapshot);
                        Interlocked.Increment(ref _snapshotsWritten);
                    }
                    catch (Exception e)
                    {
                        Interlocked.Increment(ref _snapshotWriteFailures);
                        Logger.Warning($"[SessionSnapshot] Persist failed: {e.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown path.
            }
            catch (Exception e)
            {
                Interlocked.Increment(ref _snapshotWriteFailures);
                Logger.Error($"[SessionSnapshot] Writer loop crashed: {e.Message}", e);
            }
        }

        private void PersistSnapshot(SessionSnapshotRecord snapshot)
        {
            if (string.IsNullOrWhiteSpace(_snapshotPath))
            {
                throw new InvalidOperationException("Snapshot path is not initialized.");
            }

            var directory = Path.GetDirectoryName(_snapshotPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = _snapshotPath + ".tmp";
            var json = JsonUtility.ToJson(snapshot);

            File.WriteAllText(tempPath, json, Encoding.UTF8);
            if (File.Exists(_snapshotPath))
            {
                File.Delete(_snapshotPath);
            }

            File.Move(tempPath, _snapshotPath);

            if (_logSnapshots)
            {
                Logger.Debug(
                    $"[SessionSnapshot] Persisted session={snapshot.sessionId}, state={snapshot.sessionState}, reason={snapshot.reasonCode}");
            }
        }

        private void FlushPending(TimeSpan timeout)
        {
            if (_pendingSnapshots == null)
            {
                return;
            }

            var deadline = DateTime.UtcNow.Add(timeout);
            while (_pendingSnapshots.Count > 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(5);
            }
        }

        private void DisposeWorker()
        {
            if (_pendingSnapshots == null)
            {
                return;
            }

            try
            {
                _pendingSnapshots.CompleteAdding();
                _cancelSource?.Cancel();
                _writerTask?.Wait(2000);
            }
            catch (Exception)
            {
                // Ignore shutdown exceptions.
            }
            finally
            {
                _cancelSource?.Dispose();
                _cancelSource = null;
                _pendingSnapshots.Dispose();
                _pendingSnapshots = null;
                _writerTask = null;
            }
        }

        private void ResolveDependencies()
        {
            if (_sessionContext == null)
            {
                _sessionContext = GetComponent<GameSessionContext>();
            }

            if (_runtimeService == null)
            {
                _runtimeService = GetComponent<GameRuntimeService>();
            }

            if (_sessionContext == null)
            {
                _sessionContext = FindFirstObjectByType<GameSessionContext>();
            }

            if (_runtimeService == null)
            {
                _runtimeService = FindFirstObjectByType<GameRuntimeService>();
            }
        }

        private static bool IsSnapshotValid(SessionSnapshotRecord snapshot)
        {
            return snapshot != null && !string.IsNullOrWhiteSpace(snapshot.sessionId);
        }

        private static DateTime ParseUtc(string value)
        {
            if (DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
            }

            return DateTime.UtcNow;
        }

        private static bool IsTerminalState(GameContracts.SessionLifecycleState state)
        {
            return state == GameContracts.SessionLifecycleState.COMPLETED ||
                   state == GameContracts.SessionLifecycleState.ABORTED_BY_THERAPIST ||
                   state == GameContracts.SessionLifecycleState.FAILED_TECHNICAL;
        }

        private GameContracts.SessionLifecycleState ResolveRecoveredState(GameContracts.SessionLifecycleState sourceState)
        {
            if (!_recoverToInterruptedOnStart)
            {
                return sourceState;
            }

            if (IsTerminalState(sourceState))
            {
                return sourceState;
            }

            return GameContracts.SessionLifecycleState.INTERRUPTED;
        }

#if UNITY_EDITOR
        [ContextMenu("Snapshot: Save Now")]
        private void DebugSaveSnapshotNow()
        {
            EnqueueSnapshot("DEBUG_MANUAL", force: true);
        }

        [ContextMenu("Snapshot: Restore Now")]
        private void DebugRestoreSnapshotNow()
        {
            TryRestoreLatestSnapshot();
        }

        [ContextMenu("Snapshot: Log Stats")]
        private void DebugLogSnapshotStats()
        {
            Logger.Info(
                $"[SessionSnapshot] Stats\n" +
                $"Path: {_snapshotPath}\n" +
                $"Pending: {_pendingSnapshots?.Count ?? 0}\n" +
                $"Written: {_snapshotsWritten}\n" +
                $"Write failures: {_snapshotWriteFailures}\n" +
                $"Dropped: {_snapshotDropped}\n" +
                $"Restore success: {_restoreSuccessCount}\n" +
                $"Restore failures: {_restoreFailureCount}");
        }
#endif
    }

    [Serializable]
    internal sealed class SessionSnapshotRecord
    {
        public int snapshotVersion;
        public string snapshotId;
        public string sessionId;
        public string patientId;
        public string therapistId;
        public string sessionState;
        public string startedAtUtc;
        public string activeGameId;
        public string capturedAtUtc;
        public long capturedAtUnixMs;
        public string reasonCode;
    }
}
