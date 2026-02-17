using System;
using System.IO;
using UnityEngine;
using TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Runtime session context shared by all mini-games.
    /// </summary>
    [DisallowMultipleComponent]
    public class MiniGameSessionContext : MonoBehaviour, ISessionContext
    {
        [Header("Default Session Values")]
        [SerializeField] private string _patientId = "unknown_patient";
        [SerializeField] private string _therapistId = "unknown_therapist";
        [SerializeField] private string _sessionId = "";
        [SerializeField] private bool _autoStartSessionOnAwake = true;
        [SerializeField] private bool _deferAutoStartWhenRecoverySnapshotExists = true;
        [SerializeField] private string _recoverySnapshotFolder = "session_resilience";
        [SerializeField] private string _recoverySnapshotFileName = "snapshot.json";
        [SerializeField] private SessionLifecycleState _sessionState = SessionLifecycleState.CREATED;

        private DateTime _startedAtUtc;

        public string SessionId => _sessionId;
        public string PatientId => _patientId;
        public string TherapistId => _therapistId;
        public DateTime StartedAtUtc => _startedAtUtc;
        public SessionLifecycleState SessionState => _sessionState;

        public event Action OnSessionChanged;
        public event Action<SessionLifecycleState, SessionLifecycleState, string> OnSessionStateChanged;

        private void Awake()
        {
            if (_autoStartSessionOnAwake)
            {
                if (ShouldDeferAutoStartForRecovery())
                {
                    Logger.Info("[SessionContext] Auto-start deferred: recovery snapshot detected.");
                    return;
                }

                BeginSession(_patientId, _therapistId, _sessionId);
            }
        }

        public void BeginSession(string patientId, string therapistId, string sessionId = null)
        {
            var requestedPatientId = string.IsNullOrWhiteSpace(patientId) ? "unknown_patient" : patientId;
            var requestedTherapistId = string.IsNullOrWhiteSpace(therapistId) ? "unknown_therapist" : therapistId;
            var requestedSessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString() : sessionId;

            if (HasActiveSessionLock() &&
                !string.Equals(_sessionId, requestedSessionId, StringComparison.Ordinal))
            {
                Logger.Warning(
                    $"[SessionContext] Active session lock is held by {_sessionId} (state={_sessionState}). Ignoring request for new session {requestedSessionId}.");
                return;
            }

            var previousState = _sessionState;
            _patientId = requestedPatientId;
            _therapistId = requestedTherapistId;
            _sessionId = requestedSessionId;
            _startedAtUtc = DateTime.UtcNow;
            _sessionState = SessionLifecycleState.CREATED;

            Logger.Info($"[SessionContext] Session created: {_sessionId} (patient={_patientId}, therapist={_therapistId}, state={_sessionState})");
            OnSessionStateChanged?.Invoke(previousState, _sessionState, "BEGIN_SESSION");
            OnSessionChanged?.Invoke();
        }

        public bool RestoreSession(
            string patientId,
            string therapistId,
            string sessionId,
            DateTime startedAtUtc,
            SessionLifecycleState restoredState,
            string reasonCode = "RESTORE_SNAPSHOT",
            bool forceReplaceActive = true)
        {
            var requestedPatientId = string.IsNullOrWhiteSpace(patientId) ? "unknown_patient" : patientId;
            var requestedTherapistId = string.IsNullOrWhiteSpace(therapistId) ? "unknown_therapist" : therapistId;
            var requestedSessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString() : sessionId;

            if (!forceReplaceActive &&
                HasActiveSessionLock() &&
                !string.Equals(_sessionId, requestedSessionId, StringComparison.Ordinal))
            {
                Logger.Warning(
                    $"[SessionContext] Restore rejected due active lock. activeSession={_sessionId}, incomingSession={requestedSessionId}, state={_sessionState}");
                return false;
            }

            var previousState = _sessionState;

            _patientId = requestedPatientId;
            _therapistId = requestedTherapistId;
            _sessionId = requestedSessionId;
            _startedAtUtc = NormalizeUtc(startedAtUtc);
            _sessionState = restoredState;

            Logger.Info(
                $"[SessionContext] Session restored: {_sessionId} (patient={_patientId}, therapist={_therapistId}, state={_sessionState}, startedAt={_startedAtUtc:O})");

            OnSessionStateChanged?.Invoke(previousState, _sessionState, reasonCode ?? "RESTORE_SNAPSHOT");
            OnSessionChanged?.Invoke();
            return true;
        }

        private bool HasActiveSessionLock()
        {
            return !string.IsNullOrWhiteSpace(_sessionId) && !IsTerminalState(_sessionState);
        }

        private static bool IsTerminalState(SessionLifecycleState state)
        {
            return state == SessionLifecycleState.COMPLETED ||
                   state == SessionLifecycleState.ABORTED_BY_THERAPIST ||
                   state == SessionLifecycleState.FAILED_TECHNICAL;
        }

        private static DateTime NormalizeUtc(DateTime utcCandidate)
        {
            if (utcCandidate == default)
            {
                return DateTime.UtcNow;
            }

            return utcCandidate.Kind == DateTimeKind.Utc
                ? utcCandidate
                : utcCandidate.ToUniversalTime();
        }

        private bool ShouldDeferAutoStartForRecovery()
        {
            if (!_deferAutoStartWhenRecoverySnapshotExists)
            {
                return false;
            }

            var folder = string.IsNullOrWhiteSpace(_recoverySnapshotFolder) ? "session_resilience" : _recoverySnapshotFolder.Trim();
            var fileName = string.IsNullOrWhiteSpace(_recoverySnapshotFileName) ? "snapshot.json" : _recoverySnapshotFileName.Trim();
            var snapshotPath = Path.Combine(Application.persistentDataPath, folder, fileName);

            if (!File.Exists(snapshotPath))
            {
                return false;
            }

            try
            {
                var json = File.ReadAllText(snapshotPath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return false;
                }

                var probe = JsonUtility.FromJson<RecoverySnapshotProbe>(json);
                if (probe == null || string.IsNullOrWhiteSpace(probe.sessionId))
                {
                    return false;
                }

                if (!SessionFsmContract.TryParseWireState(probe.sessionState, out var state))
                {
                    return true;
                }

                return !IsTerminalState(state);
            }
            catch (Exception e)
            {
                Logger.Warning($"[SessionContext] Recovery snapshot probe failed: {e.Message}");
                return false;
            }
        }

        [Serializable]
        private sealed class RecoverySnapshotProbe
        {
            public string sessionId;
            public string sessionState;
        }

        public void UpdateParticipantIds(string patientId, string therapistId)
        {
            _patientId = string.IsNullOrWhiteSpace(patientId) ? _patientId : patientId;
            _therapistId = string.IsNullOrWhiteSpace(therapistId) ? _therapistId : therapistId;
            OnSessionChanged?.Invoke();
        }

        public bool CanTransitionTo(SessionLifecycleState nextState)
        {
            return SessionFsmContract.CanTransition(_sessionState, nextState);
        }

        public bool TryTransitionTo(SessionLifecycleState nextState, string reasonCode = null)
        {
            if (_sessionState == nextState)
            {
                return true;
            }

            if (!SessionFsmContract.CanTransition(_sessionState, nextState))
            {
                Logger.Warning(
                    $"[SessionContext] Invalid transition rejected: {_sessionState} -> {nextState} (session={_sessionId}, reason={reasonCode ?? "unspecified"})");
                return false;
            }

            var previousState = _sessionState;
            _sessionState = nextState;

            Logger.Info(
                $"[SessionContext] State transition: {previousState} -> {_sessionState} (session={_sessionId}, reason={reasonCode ?? "unspecified"})");

            OnSessionStateChanged?.Invoke(previousState, _sessionState, reasonCode);
            OnSessionChanged?.Invoke();
            return true;
        }

#if UNITY_EDITOR
        [ContextMenu("Start Debug Session")]
        private void DebugStartSession()
        {
            BeginSession("debug_patient", "debug_therapist");
        }

        [ContextMenu("Debug Transition: CREATED -> IN_PROGRESS")]
        private void DebugTransitionToInProgress()
        {
            TryTransitionTo(SessionLifecycleState.IN_PROGRESS, "debug_transition");
        }
#endif
    }
}
