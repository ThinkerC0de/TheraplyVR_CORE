using System;
using System.Collections.Generic;
using UnityEngine;
using TheraplyCore.Firebase;
using GameContracts = TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Bridges contract telemetry to the existing Firebase data queue.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameTelemetryService : MonoBehaviour, GameContracts.ITelemetryService
    {
        [Header("Dependencies")]
        [SerializeField] private FirebaseDataService _firebaseDataService;
        [SerializeField] private GameSessionContext _sessionContext;

        [Header("Behavior")]
        [SerializeField] private bool _enrichPayloadWithSessionMetadata = true;
        [SerializeField] private bool _logTelemetry = false;
        [SerializeField] private int _maxPendingTelemetryFallback = 256;
        [SerializeField] private bool _logTelemetryFallback = false;

        private readonly Queue<GameDataPoint> _pendingFallbackPoints = new Queue<GameDataPoint>();
        private bool _fallbackWarningIssued;

        private void Awake()
        {
            ResolveDependencies();
            TryFlushFallbackQueue();
        }

        public void Track(string eventName, IReadOnlyDictionary<string, object> payload, int payloadVersion = 1)
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                Logger.Warning("[Telemetry] Ignoring event with empty name.");
                return;
            }

            try
            {
                ResolveDependencies();
                TryFlushFallbackQueue();

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
                    BufferFallbackPoint(point, "FirebaseDataService unresolved");
                }

                if (_logTelemetry)
                {
                    Logger.Debug($"[Telemetry] {eventName} tracked (payload keys: {mutablePayload.Count})");
                }
            }
            catch (Exception e)
            {
                BufferFallbackPoint(
                    new GameDataPoint
                    {
                        timestamp = DateTime.UtcNow,
                        dataType = eventName,
                        payload = payload != null
                            ? new Dictionary<string, object>(payload)
                            : new Dictionary<string, object>(),
                    },
                    $"Track exception: {e.Message}");
            }
        }

        private void ResolveDependencies()
        {
            if (_firebaseDataService == null)
            {
                _firebaseDataService = FindFirstObjectByType<FirebaseDataService>();
            }

            if (_sessionContext == null)
            {
                _sessionContext = FindFirstObjectByType<GameSessionContext>();
            }
        }

        private void BufferFallbackPoint(GameDataPoint point, string reason)
        {
            if (point == null)
            {
                return;
            }

            var safeLimit = Math.Max(16, _maxPendingTelemetryFallback);
            if (_pendingFallbackPoints.Count >= safeLimit)
            {
                _pendingFallbackPoints.Dequeue();
                Logger.Warning($"[Telemetry] Fallback buffer full. Dropped oldest point (limit={safeLimit}).");
            }

            _pendingFallbackPoints.Enqueue(point);
            if (!_fallbackWarningIssued)
            {
                _fallbackWarningIssued = true;
                Logger.Warning($"[Telemetry] Entered fallback buffering mode ({reason}).");
            }
            else if (_logTelemetryFallback)
            {
                Logger.Debug($"[Telemetry] Buffered event '{point.dataType}'. Pending={_pendingFallbackPoints.Count}.");
            }
        }

        private void TryFlushFallbackQueue()
        {
            if (_firebaseDataService == null || _pendingFallbackPoints.Count == 0)
            {
                return;
            }

            var flushBudget = _pendingFallbackPoints.Count;
            var flushed = 0;
            for (var i = 0; i < flushBudget; i++)
            {
                if (_pendingFallbackPoints.Count == 0)
                {
                    break;
                }

                var pending = _pendingFallbackPoints.Peek();
                if (pending == null)
                {
                    _pendingFallbackPoints.Dequeue();
                    continue;
                }

                try
                {
                    _firebaseDataService.QueueDataPoint(pending);
                    _pendingFallbackPoints.Dequeue();
                    flushed++;
                }
                catch (Exception e)
                {
                    Logger.Warning($"[Telemetry] Fallback flush paused: {e.Message}");
                    break;
                }
            }

            if (_logTelemetryFallback && flushed > 0)
            {
                Logger.Info(
                    $"[Telemetry] Flushed {flushed} buffered event(s). Remaining: {_pendingFallbackPoints.Count}.");
            }

            if (_pendingFallbackPoints.Count == 0)
            {
                _fallbackWarningIssued = false;
            }
        }
    }
}
