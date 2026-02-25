using System;
using System.Collections.Generic;
using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;
using TheraplyCore.Interactions;
using TheraplyCore.Network.Connection;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Control gateway that routes runtime lifecycle commands from remote transport or local fallback.
    /// Enforces control-mode policy (`remote_only`, `local_only`, `hybrid`) with explicit reason codes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ControlRuntimeGateway : MonoBehaviour
    {
        private enum ControlSource
        {
            Local = 0,
            Remote = 1,
        }

        [Header("Dependencies")]
        [SerializeField] private SessionFlowRunner _sessionFlowRunner;
        [SerializeField] private SessionRuntimeBridge _sessionRuntimeBridge;
        [SerializeField] private FlowConfigProvider _flowConfigProvider;
        [SerializeField] private GameSessionContext _sessionContext;
        [SerializeField] private GameCommandBus _commandBus;
        [SerializeField] private TCPServerService _tcpServerService;
        [SerializeField] private InteractionEventBridge _interactionEventBridge;

        [Header("Behavior")]
        [SerializeField] private bool _subscribeToRemoteCommands = true;
        [SerializeField] private bool _enableLocalControlApi = true;
        [SerializeField] private bool _autoStartLocalInLocalOnly;
        [SerializeField] private bool _autoStartLocalInHybridIfNoRemote;
        [SerializeField] private bool _emitControlTelemetry = true;
        [SerializeField] private bool _logRouting;

        [Header("Debug Input")]
        [SerializeField] private bool _enableDebugKeyboardShortcuts;
        [SerializeField] private KeyCode _startKey = KeyCode.F5;
        [SerializeField] private KeyCode _pauseKey = KeyCode.F6;
        [SerializeField] private KeyCode _resumeKey = KeyCode.F7;
        [SerializeField] private KeyCode _stopKey = KeyCode.F8;

        private string _activeControlMode = GameContracts.SessionFlowControlModes.Hybrid;
        private bool _autoStartAttempted;

        public string ActiveControlMode => _activeControlMode;

        private void Awake()
        {
            ResolveDependencies();
            RefreshControlMode();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            RefreshControlMode();
            SubscribeRemoteCommands();
            SubscribeTransportSignals();
            TryAutoStartLocalFallback();
        }

        private void OnDisable()
        {
            UnsubscribeRemoteCommands();
            UnsubscribeTransportSignals();
            _autoStartAttempted = false;
        }

        private void Update()
        {
            if (!_enableDebugKeyboardShortcuts)
            {
                return;
            }

            if (Input.GetKeyDown(_startKey))
            {
                TryStartLocal(out _);
            }

            if (Input.GetKeyDown(_pauseKey))
            {
                TryPauseLocal(out _);
            }

            if (Input.GetKeyDown(_resumeKey))
            {
                TryResumeLocal(out _);
            }

            if (Input.GetKeyDown(_stopKey))
            {
                TryStopLocal(out _);
            }
        }

        public bool TryStartLocal(out string reasonCode)
        {
            return TryExecuteControlCommand(
                ControlSource.Local,
                GameContracts.GameCommandIds.StartGame,
                startCommand: null,
                out reasonCode);
        }

        public bool TryPauseLocal(out string reasonCode)
        {
            return TryExecuteControlCommand(
                ControlSource.Local,
                GameContracts.GameCommandIds.PauseGame,
                startCommand: null,
                out reasonCode);
        }

        public bool TryResumeLocal(out string reasonCode)
        {
            return TryExecuteControlCommand(
                ControlSource.Local,
                GameContracts.GameCommandIds.ResumeGame,
                startCommand: null,
                out reasonCode);
        }

        public bool TryStopLocal(out string reasonCode)
        {
            return TryExecuteControlCommand(
                ControlSource.Local,
                GameContracts.GameCommandIds.StopGame,
                startCommand: null,
                out reasonCode);
        }

        private bool TryExecuteControlCommand(
            ControlSource source,
            string commandId,
            GameContracts.StartGameCommand startCommand,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            ResolveDependencies();
            RefreshControlMode();
            EmitControlTelemetry(
                "control_command_received",
                source,
                commandId,
                "CONTROL_COMMAND_RECEIVED",
                applied: false);

            if (!IsControlSourceAllowed(source, out reasonCode))
            {
                EmitControlTelemetry(
                    "control_command_rejected",
                    source,
                    commandId,
                    reasonCode,
                    applied: false);
                MaybeLog("CONTROL_COMMAND_REJECTED", commandId, source, reasonCode);
                return false;
            }

            if (_sessionFlowRunner == null)
            {
                reasonCode = "CONTROL_SESSION_FLOW_RUNNER_MISSING";
                EmitControlTelemetry(
                    "control_command_rejected",
                    source,
                    commandId,
                    reasonCode,
                    applied: false);
                return false;
            }

            var success = false;
            switch (Normalize(commandId))
            {
                case "START_GAME":
                    success = TryStartFlowInternal(startCommand, out reasonCode);
                    break;
                case "PAUSE_GAME":
                    success = _sessionFlowRunner.TryPauseFlow(out reasonCode);
                    break;
                case "RESUME_GAME":
                    success = _sessionFlowRunner.TryResumeFlow(out reasonCode);
                    break;
                case "STOP_GAME":
                    success = _sessionFlowRunner.TryStopFlow(out reasonCode);
                    break;
                default:
                    reasonCode = "CONTROL_COMMAND_UNSUPPORTED";
                    success = false;
                    break;
            }

            EmitControlTelemetry(
                success ? "control_command_applied" : "control_command_rejected",
                source,
                commandId,
                NormalizeOrFallback(reasonCode, success ? "CONTROL_COMMAND_APPLIED" : "CONTROL_COMMAND_FAILED"),
                applied: success);
            MaybeLog(
                success ? "CONTROL_COMMAND_APPLIED" : "CONTROL_COMMAND_FAILED",
                commandId,
                source,
                reasonCode);
            return success;
        }

        private bool TryStartFlowInternal(GameContracts.StartGameCommand startCommand, out string reasonCode)
        {
            reasonCode = string.Empty;
            if (_sessionFlowRunner == null)
            {
                reasonCode = "CONTROL_SESSION_FLOW_RUNNER_MISSING";
                return false;
            }

            if (!IsStartGameIdAllowed(startCommand, out reasonCode))
            {
                return false;
            }

            var patientId = _sessionContext == null ? string.Empty : Normalize(_sessionContext.PatientId);
            var therapistId = _sessionContext == null ? string.Empty : Normalize(_sessionContext.TherapistId);
            var requestedSessionId = _sessionContext == null ? string.Empty : Normalize(_sessionContext.SessionId);

            return _sessionFlowRunner.TryStartFlow(
                patientId,
                therapistId,
                requestedSessionId,
                out reasonCode);
        }

        private bool IsStartGameIdAllowed(GameContracts.StartGameCommand command, out string reasonCode)
        {
            reasonCode = string.Empty;
            var requestedGameId = Normalize(command == null ? string.Empty : command.gameId);
            if (string.IsNullOrWhiteSpace(requestedGameId))
            {
                return true;
            }

            var definitionGameId = ResolveDefinitionGameId();
            if (string.IsNullOrWhiteSpace(definitionGameId))
            {
                return true;
            }

            if (string.Equals(definitionGameId, requestedGameId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            reasonCode = "CONTROL_GAME_ID_MISMATCH";
            return false;
        }

        private bool IsControlSourceAllowed(ControlSource source, out string reasonCode)
        {
            reasonCode = string.Empty;
            var normalizedMode = GameContracts.SessionFlowControlModes.NormalizeOrDefault(_activeControlMode);

            if (source == ControlSource.Local && !_enableLocalControlApi)
            {
                reasonCode = "CONTROL_LOCAL_API_DISABLED";
                return false;
            }

            if (string.Equals(normalizedMode, GameContracts.SessionFlowControlModes.RemoteOnly, StringComparison.Ordinal))
            {
                if (source != ControlSource.Remote)
                {
                    reasonCode = "CONTROL_SOURCE_LOCAL_NOT_ALLOWED";
                    return false;
                }

                return true;
            }

            if (string.Equals(normalizedMode, GameContracts.SessionFlowControlModes.LocalOnly, StringComparison.Ordinal))
            {
                if (source != ControlSource.Local)
                {
                    reasonCode = "CONTROL_SOURCE_REMOTE_NOT_ALLOWED";
                    return false;
                }

                return true;
            }

            return true;
        }

        private void HandleRemoteStartCommand(GameContracts.StartGameCommand command)
        {
            TryExecuteControlCommand(
                ControlSource.Remote,
                GameContracts.GameCommandIds.StartGame,
                command,
                out _);
        }

        private void HandleRemotePauseCommand(GameContracts.PauseGameCommand command)
        {
            TryExecuteControlCommand(
                ControlSource.Remote,
                GameContracts.GameCommandIds.PauseGame,
                startCommand: null,
                out _);
        }

        private void HandleRemoteResumeCommand(GameContracts.ResumeGameCommand command)
        {
            TryExecuteControlCommand(
                ControlSource.Remote,
                GameContracts.GameCommandIds.ResumeGame,
                startCommand: null,
                out _);
        }

        private void HandleRemoteStopCommand(GameContracts.StopGameCommand command)
        {
            TryExecuteControlCommand(
                ControlSource.Remote,
                GameContracts.GameCommandIds.StopGame,
                startCommand: null,
                out _);
        }

        private void HandleRemoteClientConnected(string clientIp)
        {
            TryAutoStartLocalFallback();
        }

        private void HandleRemoteClientDisconnected()
        {
            TryAutoStartLocalFallback();
        }

        private void SubscribeRemoteCommands()
        {
            if (!_subscribeToRemoteCommands || _commandBus == null)
            {
                return;
            }

            _commandBus.Subscribe<GameContracts.StartGameCommand>(HandleRemoteStartCommand);
            _commandBus.Subscribe<GameContracts.PauseGameCommand>(HandleRemotePauseCommand);
            _commandBus.Subscribe<GameContracts.ResumeGameCommand>(HandleRemoteResumeCommand);
            _commandBus.Subscribe<GameContracts.StopGameCommand>(HandleRemoteStopCommand);
        }

        private void UnsubscribeRemoteCommands()
        {
            if (_commandBus == null)
            {
                return;
            }

            _commandBus.Unsubscribe<GameContracts.StartGameCommand>(HandleRemoteStartCommand);
            _commandBus.Unsubscribe<GameContracts.PauseGameCommand>(HandleRemotePauseCommand);
            _commandBus.Unsubscribe<GameContracts.ResumeGameCommand>(HandleRemoteResumeCommand);
            _commandBus.Unsubscribe<GameContracts.StopGameCommand>(HandleRemoteStopCommand);
        }

        private void SubscribeTransportSignals()
        {
            if (_tcpServerService == null)
            {
                return;
            }

            _tcpServerService.OnClientConnected += HandleRemoteClientConnected;
            _tcpServerService.OnClientDisconnected += HandleRemoteClientDisconnected;
        }

        private void UnsubscribeTransportSignals()
        {
            if (_tcpServerService == null)
            {
                return;
            }

            _tcpServerService.OnClientConnected -= HandleRemoteClientConnected;
            _tcpServerService.OnClientDisconnected -= HandleRemoteClientDisconnected;
        }

        private void TryAutoStartLocalFallback()
        {
            if (_autoStartAttempted || !_enableLocalControlApi || _sessionFlowRunner == null)
            {
                return;
            }

            RefreshControlMode();
            if (_sessionFlowRunner.IsRunning)
            {
                return;
            }

            var mode = GameContracts.SessionFlowControlModes.NormalizeOrDefault(_activeControlMode);
            var canAutoStart =
                (string.Equals(mode, GameContracts.SessionFlowControlModes.LocalOnly, StringComparison.Ordinal) &&
                 _autoStartLocalInLocalOnly) ||
                (string.Equals(mode, GameContracts.SessionFlowControlModes.Hybrid, StringComparison.Ordinal) &&
                 _autoStartLocalInHybridIfNoRemote &&
                 !HasRemoteTransportRoute());

            if (!canAutoStart)
            {
                return;
            }

            _autoStartAttempted = true;
            TryStartLocal(out _);
        }

        private bool HasRemoteTransportRoute()
        {
            if (_tcpServerService == null)
            {
                return false;
            }

            return _tcpServerService.HasClient;
        }

        private void RefreshControlMode()
        {
            var resolvedMode = string.Empty;
            if (_flowConfigProvider != null &&
                _flowConfigProvider.TryGetGameDefinition(out var definition, out _) &&
                definition != null)
            {
                var policyMode = definition.policies != null && definition.policies.controlPolicy != null
                    ? definition.policies.controlPolicy.mode
                    : string.Empty;
                resolvedMode = GameContracts.SessionFlowControlModes.IsSupported(policyMode)
                    ? policyMode
                    : definition.controlMode;
            }

            if (string.IsNullOrWhiteSpace(resolvedMode) &&
                _sessionRuntimeBridge != null &&
                GameContracts.SessionFlowControlModes.IsSupported(_sessionRuntimeBridge.ActiveControlMode))
            {
                resolvedMode = _sessionRuntimeBridge.ActiveControlMode;
            }

            _activeControlMode = GameContracts.SessionFlowControlModes.NormalizeOrDefault(resolvedMode);
        }

        private void ResolveDependencies()
        {
            if (_sessionFlowRunner == null)
            {
                _sessionFlowRunner = GetComponent<SessionFlowRunner>();
                if (_sessionFlowRunner == null)
                {
                    _sessionFlowRunner = FindFirstObjectByType<SessionFlowRunner>();
                }
            }

            if (_sessionRuntimeBridge == null)
            {
                _sessionRuntimeBridge = GetComponent<SessionRuntimeBridge>();
                if (_sessionRuntimeBridge == null)
                {
                    _sessionRuntimeBridge = FindFirstObjectByType<SessionRuntimeBridge>();
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

            if (_sessionContext == null)
            {
                _sessionContext = GetComponent<GameSessionContext>();
                if (_sessionContext == null)
                {
                    _sessionContext = FindFirstObjectByType<GameSessionContext>();
                }
            }

            if (_commandBus == null)
            {
                _commandBus = GetComponent<GameCommandBus>();
                if (_commandBus == null)
                {
                    _commandBus = FindFirstObjectByType<GameCommandBus>();
                }
            }

            if (_tcpServerService == null)
            {
                _tcpServerService = GetComponent<TCPServerService>();
                if (_tcpServerService == null)
                {
                    _tcpServerService = FindFirstObjectByType<TCPServerService>();
                }
            }

            if (_interactionEventBridge == null)
            {
                _interactionEventBridge = InteractionEventBridge.Instance;
                if (_interactionEventBridge == null)
                {
                    _interactionEventBridge = FindFirstObjectByType<InteractionEventBridge>();
                }
            }
        }

        private void EmitControlTelemetry(
            string eventName,
            ControlSource source,
            string commandId,
            string reasonCode,
            bool applied)
        {
            if (!_emitControlTelemetry || _interactionEventBridge == null)
            {
                return;
            }

            var payload = new Dictionary<string, object>
            {
                { "flowId", NormalizeOrFallback(_sessionFlowRunner == null ? string.Empty : _sessionFlowRunner.ActiveFlowId, "session_flow") },
                { "stepId", NormalizeOrFallback(_sessionFlowRunner == null ? string.Empty : _sessionFlowRunner.ActiveNodeId, string.Empty) },
                { "nodeId", NormalizeOrFallback(_sessionFlowRunner == null ? string.Empty : _sessionFlowRunner.ActiveNodeId, string.Empty) },
                { "controlMode", _activeControlMode },
                { "controlSource", source == ControlSource.Remote ? "remote" : "local" },
                { "commandId", NormalizeOrFallback(commandId, string.Empty) },
                { "reasonCode", NormalizeOrFallback(reasonCode, string.Empty) },
                { "eventType", NormalizeOrFallback(eventName, "control_command") },
                { "payloadVersion", 1 },
                { "monotonicSec", Time.realtimeSinceStartup },
                { "actionOutcome", applied ? "CORRECT" : "OBSERVED" },
            };

            _interactionEventBridge.RecordGameplayEvent(
                ResolveDefinitionGameId(),
                eventName,
                _sessionRuntimeBridge == null ? string.Empty : _sessionRuntimeBridge.SessionState.ToString(),
                payload,
                nameof(ControlRuntimeGateway));
        }

        private string ResolveDefinitionGameId()
        {
            if (_flowConfigProvider != null &&
                _flowConfigProvider.TryGetGameDefinition(out var definition, out _) &&
                definition != null &&
                !string.IsNullOrWhiteSpace(definition.gameId))
            {
                return definition.gameId.Trim();
            }

            if (_sessionFlowRunner != null && !string.IsNullOrWhiteSpace(_sessionFlowRunner.ActiveFlowId))
            {
                return _sessionFlowRunner.ActiveFlowId.Trim();
            }

            return "session_flow";
        }

        private void MaybeLog(string marker, string commandId, ControlSource source, string reasonCode)
        {
            if (!_logRouting)
            {
                return;
            }

            Logger.Info(
                "[ControlRuntimeGateway] marker=" + NormalizeOrFallback(marker, "CONTROL_MARKER") +
                " commandId=" + NormalizeOrFallback(commandId, string.Empty) +
                " source=" + (source == ControlSource.Remote ? "remote" : "local") +
                " mode=" + NormalizeOrFallback(_activeControlMode, GameContracts.SessionFlowControlModes.Hybrid) +
                " reasonCode=" + NormalizeOrFallback(reasonCode, string.Empty));
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string NormalizeOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? (fallback ?? string.Empty)
                : value.Trim();
        }
    }
}
