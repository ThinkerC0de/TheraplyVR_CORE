using System;
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

        private DateTime _startedAtUtc;

        public string SessionId => _sessionId;
        public string PatientId => _patientId;
        public string TherapistId => _therapistId;
        public DateTime StartedAtUtc => _startedAtUtc;

        public event Action OnSessionChanged;

        private void Awake()
        {
            if (_autoStartSessionOnAwake)
            {
                BeginSession(_patientId, _therapistId, _sessionId);
            }
        }

        public void BeginSession(string patientId, string therapistId, string sessionId = null)
        {
            _patientId = string.IsNullOrWhiteSpace(patientId) ? "unknown_patient" : patientId;
            _therapistId = string.IsNullOrWhiteSpace(therapistId) ? "unknown_therapist" : therapistId;
            _sessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString() : sessionId;
            _startedAtUtc = DateTime.UtcNow;

            Logger.Info($"[SessionContext] Session started: {_sessionId} (patient={_patientId}, therapist={_therapistId})");
            OnSessionChanged?.Invoke();
        }

        public void UpdateParticipantIds(string patientId, string therapistId)
        {
            _patientId = string.IsNullOrWhiteSpace(patientId) ? _patientId : patientId;
            _therapistId = string.IsNullOrWhiteSpace(therapistId) ? _therapistId : therapistId;
            OnSessionChanged?.Invoke();
        }

#if UNITY_EDITOR
        [ContextMenu("Start Debug Session")]
        private void DebugStartSession()
        {
            BeginSession("debug_patient", "debug_therapist");
        }
#endif
    }
}
