using UnityEngine;
using TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Concrete IMiniGameContext composition root.
    /// </summary>
    [DisallowMultipleComponent]
    public class MiniGameContextService : MonoBehaviour, IMiniGameContext
    {
        [Header("Context Dependencies")]
        [SerializeField] private MiniGameSessionContext _sessionContext;
        [SerializeField] private MiniGameCommandBus _commandBus;
        [SerializeField] private MiniGameTelemetryService _telemetryService;
        [SerializeField] private MiniGameClockService _clockService;
        [SerializeField] private MiniGameFeedbackService _feedbackService;
        [SerializeField] private MiniGameSessionSnapshotService _snapshotService;

        public ISessionContext Session => _sessionContext;
        public ICommandBus CommandBus => _commandBus;
        public ITelemetryService Telemetry => _telemetryService;
        public IGameClock Clock => _clockService;
        public IGameFeedback Feedback => _feedbackService;

        private void Awake()
        {
            ResolveMissingReferences();
            ValidateContext();
        }

        private void ResolveMissingReferences()
        {
            if (_sessionContext == null) _sessionContext = GetComponent<MiniGameSessionContext>();
            if (_commandBus == null) _commandBus = GetComponent<MiniGameCommandBus>();
            if (_telemetryService == null) _telemetryService = GetComponent<MiniGameTelemetryService>();
            if (_clockService == null) _clockService = GetComponent<MiniGameClockService>();
            if (_feedbackService == null) _feedbackService = GetComponent<MiniGameFeedbackService>();
            if (_snapshotService == null) _snapshotService = GetComponent<MiniGameSessionSnapshotService>();

            if (_sessionContext == null) _sessionContext = FindFirstObjectByType<MiniGameSessionContext>();
            if (_commandBus == null) _commandBus = FindFirstObjectByType<MiniGameCommandBus>();
            if (_telemetryService == null) _telemetryService = FindFirstObjectByType<MiniGameTelemetryService>();
            if (_clockService == null) _clockService = FindFirstObjectByType<MiniGameClockService>();
            if (_feedbackService == null) _feedbackService = FindFirstObjectByType<MiniGameFeedbackService>();
            if (_snapshotService == null) _snapshotService = FindFirstObjectByType<MiniGameSessionSnapshotService>();

            if (_snapshotService == null && _sessionContext != null)
            {
                _snapshotService = _sessionContext.gameObject.AddComponent<MiniGameSessionSnapshotService>();
                Logger.Info("[MiniGameContext] Added MiniGameSessionSnapshotService for periodic checkpointing.");
            }
        }

        private void ValidateContext()
        {
            if (_sessionContext == null) Logger.Warning("[MiniGameContext] Missing SessionContext.");
            if (_commandBus == null) Logger.Warning("[MiniGameContext] Missing CommandBus.");
            if (_telemetryService == null) Logger.Warning("[MiniGameContext] Missing TelemetryService.");
            if (_clockService == null) Logger.Warning("[MiniGameContext] Missing ClockService.");
            if (_feedbackService == null) Logger.Warning("[MiniGameContext] Missing FeedbackService.");
            if (_snapshotService == null) Logger.Warning("[MiniGameContext] Missing SessionSnapshotService.");
        }
    }
}
