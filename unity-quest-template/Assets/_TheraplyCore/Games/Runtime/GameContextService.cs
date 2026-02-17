using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Concrete GameContracts.IGameContext composition root.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameContextService : MonoBehaviour, GameContracts.IGameContext
    {
        [Header("Context Dependencies")]
        [SerializeField] private GameSessionContext _sessionContext;
        [SerializeField] private GameCommandBus _commandBus;
        [SerializeField] private GameTelemetryService _telemetryService;
        [SerializeField] private GameClockService _clockService;
        [SerializeField] private GameFeedbackService _feedbackService;
        [SerializeField] private GameSessionSnapshotService _snapshotService;

        public GameContracts.ISessionContext Session => _sessionContext;
        public GameContracts.ICommandBus CommandBus => _commandBus;
        public GameContracts.ITelemetryService Telemetry => _telemetryService;
        public GameContracts.IGameClock Clock => _clockService;
        public GameContracts.IGameFeedback Feedback => _feedbackService;

        private void Awake()
        {
            ResolveMissingReferences();
            ValidateContext();
        }

        private void ResolveMissingReferences()
        {
            if (_sessionContext == null) _sessionContext = GetComponent<GameSessionContext>();
            if (_commandBus == null) _commandBus = GetComponent<GameCommandBus>();
            if (_telemetryService == null) _telemetryService = GetComponent<GameTelemetryService>();
            if (_clockService == null) _clockService = GetComponent<GameClockService>();
            if (_feedbackService == null) _feedbackService = GetComponent<GameFeedbackService>();
            if (_snapshotService == null) _snapshotService = GetComponent<GameSessionSnapshotService>();

            if (_sessionContext == null) _sessionContext = FindFirstObjectByType<GameSessionContext>();
            if (_commandBus == null) _commandBus = FindFirstObjectByType<GameCommandBus>();
            if (_telemetryService == null) _telemetryService = FindFirstObjectByType<GameTelemetryService>();
            if (_clockService == null) _clockService = FindFirstObjectByType<GameClockService>();
            if (_feedbackService == null) _feedbackService = FindFirstObjectByType<GameFeedbackService>();
            if (_snapshotService == null) _snapshotService = FindFirstObjectByType<GameSessionSnapshotService>();

            if (_snapshotService == null && _sessionContext != null)
            {
                _snapshotService = _sessionContext.gameObject.AddComponent<GameSessionSnapshotService>();
                Logger.Info("[GameContext] Added GameSessionSnapshotService for periodic checkpointing.");
            }
        }

        private void ValidateContext()
        {
            if (_sessionContext == null) Logger.Warning("[GameContext] Missing SessionContext.");
            if (_commandBus == null) Logger.Warning("[GameContext] Missing CommandBus.");
            if (_telemetryService == null) Logger.Warning("[GameContext] Missing TelemetryService.");
            if (_clockService == null) Logger.Warning("[GameContext] Missing ClockService.");
            if (_feedbackService == null) Logger.Warning("[GameContext] Missing FeedbackService.");
            if (_snapshotService == null) Logger.Warning("[GameContext] Missing SessionSnapshotService.");
        }
    }
}
