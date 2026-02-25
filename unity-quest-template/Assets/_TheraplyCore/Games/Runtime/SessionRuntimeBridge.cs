using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Session runtime bridge aligned with canonical FSM.
    /// Handles start/pause/resume/stop-like transitions without game-specific logic.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionRuntimeBridge : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private GameSessionContext _sessionContext;
        [SerializeField] private FlowConfigProvider _flowConfigProvider;

        [Header("Defaults")]
        [SerializeField] private string _defaultPatientId = "unknown_patient";
        [SerializeField] private string _defaultTherapistId = "unknown_therapist";
        [SerializeField] private bool _autoStartOnAwake;

        [Header("Diagnostics")]
        [SerializeField] private bool _logLifecycle;

        private string _activeControlMode = GameContracts.SessionFlowControlModes.Hybrid;

        public string ActiveControlMode => _activeControlMode;
        public string ActiveSessionId => _sessionContext == null ? string.Empty : _sessionContext.SessionId;
        public GameContracts.SessionLifecycleState SessionState => _sessionContext == null
            ? GameContracts.SessionLifecycleState.CREATED
            : _sessionContext.SessionState;

        private void Awake()
        {
            ResolveDependencies();

            if (_autoStartOnAwake)
            {
                TryStartSession(_defaultPatientId, _defaultTherapistId, string.Empty, out _);
            }
        }

        public bool TryStartSession(
            string patientId,
            string therapistId,
            string requestedSessionId,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            ResolveDependencies();

            if (_sessionContext == null)
            {
                reasonCode = "SESSION_CONTEXT_MISSING";
                return false;
            }

            if (!TryResolveControlMode(out reasonCode))
            {
                return false;
            }

            _sessionContext.BeginSession(
                string.IsNullOrWhiteSpace(patientId) ? _defaultPatientId : patientId.Trim(),
                string.IsNullOrWhiteSpace(therapistId) ? _defaultTherapistId : therapistId.Trim(),
                string.IsNullOrWhiteSpace(requestedSessionId) ? null : requestedSessionId.Trim());

            if (!_sessionContext.TryTransitionTo(GameContracts.SessionLifecycleState.IN_PROGRESS, "SESSION_RUNTIME_START"))
            {
                reasonCode = "FSM_TRANSITION_REJECTED";
                return false;
            }

            MaybeLog("SESSION_STARTED");
            return true;
        }

        public bool TryPauseSession(out string reasonCode)
        {
            return TryTransition(GameContracts.SessionLifecycleState.PAUSED, "SESSION_RUNTIME_PAUSE", out reasonCode);
        }

        public bool TryResumeSession(out string reasonCode)
        {
            return TryTransition(GameContracts.SessionLifecycleState.IN_PROGRESS, "SESSION_RUNTIME_RESUME", out reasonCode);
        }

        public bool TryInterruptSession(out string reasonCode)
        {
            return TryTransition(GameContracts.SessionLifecycleState.INTERRUPTED, "SESSION_RUNTIME_INTERRUPT", out reasonCode);
        }

        public bool TryCompleteSession(out string reasonCode)
        {
            return TryTransition(GameContracts.SessionLifecycleState.COMPLETED, "SESSION_RUNTIME_COMPLETE", out reasonCode);
        }

        public bool TryAbortSession(out string reasonCode)
        {
            return TryTransition(GameContracts.SessionLifecycleState.ABORTED_BY_THERAPIST, "SESSION_RUNTIME_ABORT", out reasonCode);
        }

        public bool TryFailTechnicalSession(out string reasonCode)
        {
            return TryTransition(GameContracts.SessionLifecycleState.FAILED_TECHNICAL, "SESSION_RUNTIME_FAIL", out reasonCode);
        }

        private bool TryTransition(
            GameContracts.SessionLifecycleState nextState,
            string transitionReason,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            ResolveDependencies();

            if (_sessionContext == null)
            {
                reasonCode = "SESSION_CONTEXT_MISSING";
                return false;
            }

            if (!_sessionContext.TryTransitionTo(nextState, transitionReason))
            {
                reasonCode = "FSM_TRANSITION_REJECTED";
                return false;
            }

            MaybeLog("SESSION_TRANSITION_" + nextState);
            return true;
        }

        private bool TryResolveControlMode(out string reasonCode)
        {
            reasonCode = string.Empty;
            _activeControlMode = GameContracts.SessionFlowControlModes.Hybrid;

            if (_flowConfigProvider == null)
            {
                return true;
            }

            if (!_flowConfigProvider.TryGetGameDefinition(out var definition, out reasonCode))
            {
                return false;
            }

            var resolvedMode = definition.controlMode;
            if (definition.policies != null &&
                definition.policies.controlPolicy != null &&
                GameContracts.SessionFlowControlModes.IsSupported(definition.policies.controlPolicy.mode))
            {
                resolvedMode = definition.policies.controlPolicy.mode;
            }

            _activeControlMode = GameContracts.SessionFlowControlModes.NormalizeOrDefault(resolvedMode);
            return true;
        }

        private void ResolveDependencies()
        {
            if (_sessionContext == null)
            {
                _sessionContext = GetComponent<GameSessionContext>();
                if (_sessionContext == null)
                {
                    _sessionContext = FindFirstObjectByType<GameSessionContext>();
                }
            }

            if (_flowConfigProvider == null)
            {
                _flowConfigProvider = GetComponent<FlowConfigProvider>();
                if (_flowConfigProvider == null)
                {
                    _flowConfigProvider = FindFirstObjectByType<FlowConfigProvider>();
                }
            }
        }

        private void MaybeLog(string marker)
        {
            if (!_logLifecycle)
            {
                return;
            }

            Logger.Info(
                "[SessionRuntimeBridge] marker=" + marker +
                " sessionId=" + ActiveSessionId +
                " state=" + SessionState +
                " controlMode=" + _activeControlMode);
        }
    }
}
