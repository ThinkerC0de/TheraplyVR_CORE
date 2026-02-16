using System;
using System.Collections.Generic;
using UnityEngine;
using TheraplyCore.Firebase;
using TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Bridges contract telemetry to the existing Firebase data queue.
    /// </summary>
    [DisallowMultipleComponent]
    public class MiniGameTelemetryService : MonoBehaviour, ITelemetryService
    {
        [Header("Dependencies")]
        [SerializeField] private FirebaseDataService _firebaseDataService;
        [SerializeField] private MiniGameSessionContext _sessionContext;

        [Header("Behavior")]
        [SerializeField] private bool _enrichPayloadWithSessionMetadata = true;
        [SerializeField] private bool _logTelemetry = false;

        private void Awake()
        {
            if (_firebaseDataService == null)
            {
                _firebaseDataService = FindFirstObjectByType<FirebaseDataService>();
            }

            if (_sessionContext == null)
            {
                _sessionContext = FindFirstObjectByType<MiniGameSessionContext>();
            }
        }

        public void Track(string eventName, IReadOnlyDictionary<string, object> payload, int payloadVersion = 1)
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                Logger.Warning("[Telemetry] Ignoring event with empty name.");
                return;
            }

            var mutablePayload = payload != null
                ? new Dictionary<string, object>(payload)
                : new Dictionary<string, object>();

            mutablePayload["eventName"] = eventName;
            mutablePayload["payloadVersion"] = payloadVersion;

            if (_enrichPayloadWithSessionMetadata && _sessionContext != null)
            {
                if (!mutablePayload.ContainsKey("sessionId")) mutablePayload["sessionId"] = _sessionContext.SessionId;
                if (!mutablePayload.ContainsKey("patientId")) mutablePayload["patientId"] = _sessionContext.PatientId;
                if (!mutablePayload.ContainsKey("therapistId")) mutablePayload["therapistId"] = _sessionContext.TherapistId;
            }

            var point = new GameDataPoint
            {
                timestamp = DateTime.UtcNow,
                dataType = eventName,
                payload = mutablePayload,
            };

            if (_firebaseDataService != null)
            {
                _firebaseDataService.QueueDataPoint(point);
            }
            else
            {
                Logger.Warning($"[Telemetry] FirebaseDataService not found, dropped event: {eventName}");
            }

            if (_logTelemetry)
            {
                Logger.Debug($"[Telemetry] {eventName} tracked (payload keys: {mutablePayload.Count})");
            }
        }
    }
}
