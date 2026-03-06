using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using TheraplyCore.Interactions;

namespace TheraplyExamples
{
    /// <summary>
    /// Simple sample game:
    /// tap moving pulse targets one by one until target count is reached.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PulseTargetsGameModule : GameModuleBase,
        GameContracts.IDefaultGameConfigProvider,
        GameContracts.IStartCommandConfigProvider
    {
        [Header("Defaults")]
        [SerializeField] private int _defaultTargetCount = 8;
        [SerializeField] private float _defaultTargetSpeed = 0.7f;
        [SerializeField] private float _defaultTargetScale = 0.3f;
        [SerializeField] private float _viewportPadding = 0.18f;
        [SerializeField] private Camera _targetCamera;
        [SerializeField] private Shader _targetFallbackShader;

        [Header("Task Stack")]
        [SerializeField] private bool _emitSequenceTelemetry = true;
        [SerializeField] private float _stimulusLeadSec = 0f;
        [SerializeField] private float _cueTimeoutSec = 2.5f;

        [Header("Adaptive")]
        [SerializeField] private bool _adaptiveDifficultyEnabled = true;
        [SerializeField] [Range(0f, 1f)] private float _adaptiveDifficultySensitivity = 0.55f;
        [SerializeField] private bool _labelPipelineEnabled = true;

        private PulseTargetsGameConfig _activeConfig;
        private TargetRuntime _activeTarget;
        private int _nextTargetId = 1;
        private int _hits;
        private int _misses;
        private float _lastHitElapsedSec = -1f;
        private int _sequenceStepOrdinal;
        private string _taskRunId = string.Empty;
        private bool _taskSummaryEmitted;
        private TaskOutcomeAggregator.TaskOutcomeSummary _latestTaskSummary;
        private AdaptiveDifficultyController.DifficultyDecision _latestDifficultyDecision;
        private TaskLabelPipeline.TaskLabel _latestTaskLabel;
        private float _effectiveTargetSpeed;
        private float _effectiveTargetScale;
        private float _effectiveCueTimeoutSec;
        private bool _labelPipelineActive;
        private Material _runtimeTargetMaterial;
        private bool _loggedTargetShaderSelection;

        private readonly StimulusScheduler _stimulusScheduler = new StimulusScheduler();
        private readonly SequenceTaskEngine _sequenceTaskEngine = new SequenceTaskEngine();
        private readonly TaskOutcomeAggregator _taskOutcomeAggregator = new TaskOutcomeAggregator();
        private readonly AdaptiveDifficultyController _adaptiveDifficultyController =
            new AdaptiveDifficultyController();
        private readonly TaskLabelPipeline _taskLabelPipeline = new TaskLabelPipeline();
        private readonly List<StimulusScheduler.ScheduledCue> _dueStimuli =
            new List<StimulusScheduler.ScheduledCue>(8);
        private readonly List<SequenceTaskEngine.SequenceOutcome> _timeoutOutcomes =
            new List<SequenceTaskEngine.SequenceOutcome>(8);

        public override string GameId => PulseTargetsGameConfig.DefaultGameId;

        public GameContracts.IGameConfig CreateDefaultConfig()
        {
            return PulseTargetsGameConfig.CreateDefault(
                _defaultTargetCount,
                _defaultTargetSpeed,
                _defaultTargetScale);
        }

        public bool TryCreateConfigFromStartCommand(
            StartGameCommand command,
            GameContracts.IGameConfig previousConfig,
            out GameContracts.IGameConfig resolvedConfig,
            out string reasonCode)
        {
            if (command != null &&
                !string.IsNullOrWhiteSpace(command.gameId) &&
                !string.Equals(command.gameId, GameId, StringComparison.OrdinalIgnoreCase))
            {
                resolvedConfig = null;
                reasonCode = "GAME_ID_MISMATCH";
                return false;
            }

            var previous = previousConfig as PulseTargetsGameConfig;
            resolvedConfig = PulseTargetsGameConfig.CreateFromStartCommand(
                command,
                previous,
                _defaultTargetCount,
                _defaultTargetSpeed,
                _defaultTargetScale);
            reasonCode = string.Empty;
            return true;
        }

        public override void Initialize(GameContracts.IGameConfig config, GameContracts.IGameContext context)
        {
            _activeConfig = config as PulseTargetsGameConfig ??
                            PulseTargetsGameConfig.CreateDefault(
                                _defaultTargetCount,
                                _defaultTargetSpeed,
                                _defaultTargetScale);
            EnsureCamera();
            ClearActiveTarget();
            _nextTargetId = 1;
            _hits = 0;
            _misses = 0;
            _lastHitElapsedSec = -1f;
            _sequenceStepOrdinal = 0;
            _taskRunId = Guid.NewGuid().ToString();
            _taskSummaryEmitted = false;
            _latestTaskSummary = default;
            _latestTaskLabel = default;
            _stimulusScheduler.Reset();
            _sequenceTaskEngine.Reset(_taskRunId);
            _taskOutcomeAggregator.Reset(_taskRunId, GameId);
            _dueStimuli.Clear();
            _timeoutOutcomes.Clear();
            ConfigureAdaptiveDifficultyPolicy();

            base.Initialize(_activeConfig, context);
            EmitTaskRunStarted();

            TrackEvent("pulse_targets_config", new Dictionary<string, object>
            {
                { "targetCount", _activeConfig.TargetCount },
                { "targetSpeed", _activeConfig.TargetSpeed },
                { "targetScale", _activeConfig.TargetScale },
                { "adaptiveDifficultyEnabled", _latestDifficultyDecision.enabled },
                { "adaptiveDifficultySensitivity", _latestDifficultyDecision.sensitivity },
                { "effectiveTargetSpeed", _effectiveTargetSpeed },
                { "effectiveTargetScale", _effectiveTargetScale },
                { "effectiveCueTimeoutSec", _effectiveCueTimeoutSec },
                { "labelPipelineEnabled", _labelPipelineActive },
            });
        }

        public override void StartGame()
        {
            if ((State == GameContracts.GameState.Completed || State == GameContracts.GameState.Failed) &&
                CurrentConfig != null &&
                Context != null)
            {
                Initialize(CurrentConfig, Context);
            }

            base.StartGame();

            if (State != GameContracts.GameState.Playing)
            {
                return;
            }

            if (_activeTarget == null)
            {
                SpawnNextTarget();
            }
        }

        public override void StopGame(GameContracts.GameStopReason reason)
        {
            if (State == GameContracts.GameState.Completed || State == GameContracts.GameState.Failed)
            {
                return;
            }

            base.StopGame(reason);
            EmitTaskOutcomeSummary(reason.ToString());
            ClearActiveTarget();
        }

        public override GameContracts.IGameResult BuildResult()
        {
            if (!_taskSummaryEmitted)
            {
                EmitTaskOutcomeSummary(State.ToString());
            }

            return new GameResult(
                GameId,
                State == GameContracts.GameState.Completed,
                GetDurationSeconds(),
                new Dictionary<string, object>
                {
                    { "hits", _hits },
                    { "misses", _misses },
                    { "targetCount", _activeConfig == null ? 0 : _activeConfig.TargetCount },
                    { "targetSpeed", _effectiveTargetSpeed },
                    { "targetScale", _effectiveTargetScale },
                    { "cueTimeoutSec", _effectiveCueTimeoutSec },
                    { "adaptiveDifficultyScore", _latestDifficultyDecision.nextDifficulty },
                    { "adaptiveDifficultyReasonCode", _latestDifficultyDecision.reasonCode ?? string.Empty },
                    { "labelPerformanceBand", _latestTaskLabel.performanceBand ?? string.Empty },
                    { "labelFatigueBand", _latestTaskLabel.fatigueBand ?? string.Empty },
                    { "durationSec", GetDurationSeconds() },
                    { "taskRunId", _latestTaskSummary.taskRunId ?? string.Empty },
                    { "taskCuesPresented", _latestTaskSummary.cuesPresented },
                    { "taskCompletionRatio", _latestTaskSummary.completionRatio },
                    { "taskAverageReactionSec", _latestTaskSummary.averageReactionSec },
                });
        }

        private void Update()
        {
            if (State != GameContracts.GameState.Playing || _activeConfig == null)
            {
                return;
            }

            if (_activeTarget != null)
            {
                _activeTarget.viewportPos += _activeTarget.direction *
                                             Mathf.Max(0.1f, _effectiveTargetSpeed) *
                                             Time.deltaTime;

                if (_activeTarget.viewportPos.x <= _viewportPadding ||
                    _activeTarget.viewportPos.x >= 1f - _viewportPadding)
                {
                    _activeTarget.direction.x *= -1f;
                    _activeTarget.viewportPos.x = Mathf.Clamp(
                        _activeTarget.viewportPos.x,
                        _viewportPadding,
                        1f - _viewportPadding);
                }

                if (_activeTarget.viewportPos.y <= _viewportPadding ||
                    _activeTarget.viewportPos.y >= 1f - _viewportPadding)
                {
                    _activeTarget.direction.y *= -1f;
                    _activeTarget.viewportPos.y = Mathf.Clamp(
                        _activeTarget.viewportPos.y,
                        _viewportPadding,
                        1f - _viewportPadding);
                }

                if (_activeTarget.instance != null)
                {
                    _activeTarget.instance.transform.position = ViewportToWorld(_activeTarget.viewportPos);
                }
            }

            FlushDueStimuli();
            FlushTimeoutOutcomes();
        }

        private void SpawnNextTarget()
        {
            EnsureCamera();
            if (_targetCamera == null || _activeConfig == null)
            {
                return;
            }

            var direction = UnityEngine.Random.insideUnitCircle;
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = Vector2.right;
            }

            direction.Normalize();

            var viewportPos = new Vector2(
                UnityEngine.Random.Range(_viewportPadding, 1f - _viewportPadding),
                UnityEngine.Random.Range(_viewportPadding, 1f - _viewportPadding));

            var targetObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            targetObject.name = "PulseTarget_" + _nextTargetId;
            targetObject.transform.SetParent(transform, worldPositionStays: true);
            targetObject.transform.localScale = Vector3.one * _effectiveTargetScale;
            targetObject.transform.position = ViewportToWorld(viewportPos);

            var renderer = targetObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                EnsureTargetMaterial(renderer);
                var material = renderer.material;
                if (material != null && material.HasProperty("_Color"))
                {
                    material.color = Color.Lerp(Color.yellow, Color.red, 0.35f);
                }
            }

            var clickTarget = targetObject.AddComponent<PulseTargetClickTarget>();
            clickTarget.Configure(_nextTargetId, HandleTargetClicked);

            var validationZone = targetObject.AddComponent<TargetValidationZone>();
            validationZone.Configure(
                _nextTargetId.ToString(CultureInfo.InvariantCulture),
                "PULSE_TARGET",
                defaultValidTarget: true);

            _activeTarget = new TargetRuntime
            {
                targetId = _nextTargetId,
                viewportPos = viewportPos,
                direction = direction,
                instance = targetObject,
                sequenceStepId = string.Empty,
                stimulusCueId = string.Empty,
            };

            ScheduleStimulusForTarget(_activeTarget);
            _nextTargetId++;

            TrackEvent("pulse_target_spawned", new Dictionary<string, object>
            {
                { "targetId", _activeTarget.targetId },
                { "hits", _hits },
                { "remaining", Mathf.Max(0, _activeConfig.TargetCount - _hits) },
            });
        }

        private void HandleTargetClicked(int targetId, string inputSource)
        {
            if (State != GameContracts.GameState.Playing || _activeTarget == null)
            {
                return;
            }

            var taskOutcome = RecordTaskActionOutcome(targetId, inputSource);

            if (targetId != _activeTarget.targetId)
            {
                _misses++;
                TrackEvent("pulse_target_miss", new Dictionary<string, object>
                {
                    { "targetId", targetId },
                    { "expectedTargetId", _activeTarget.targetId },
                    { "misses", _misses },
                    { "inputSource", string.IsNullOrWhiteSpace(inputSource) ? "UNKNOWN" : inputSource },
                    { "targetName", _activeTarget.instance == null ? string.Empty : _activeTarget.instance.name },
                    { "targetValid", false },
                    { "sequenceOutcome", MapOutcomeToken(taskOutcome.outcomeType) },
                });
                return;
            }

            _hits++;

            var elapsedSec = GetDurationSeconds();
            var reactionSec = _lastHitElapsedSec < 0f
                ? elapsedSec
                : Mathf.Max(0f, elapsedSec - _lastHitElapsedSec);
            _lastHitElapsedSec = elapsedSec;

            TrackEvent("pulse_target_hit", new Dictionary<string, object>
            {
                { "targetId", targetId },
                { "hits", _hits },
                { "reactionSec", reactionSec },
                { "remaining", Mathf.Max(0, _activeConfig.TargetCount - _hits) },
                { "inputSource", string.IsNullOrWhiteSpace(inputSource) ? "UNKNOWN" : inputSource },
                { "targetName", _activeTarget.instance == null ? string.Empty : _activeTarget.instance.name },
                { "targetValid", true },
                { "sequenceOutcome", MapOutcomeToken(taskOutcome.outcomeType) },
            });

            ClearActiveTarget();

            if (_hits >= _activeConfig.TargetCount)
            {
                TrackEvent("pulse_targets_completed", new Dictionary<string, object>
                {
                    { "durationSec", GetDurationSeconds() },
                    { "hits", _hits },
                    { "misses", _misses },
                });
                StopGame(GameContracts.GameStopReason.Completed);
                return;
            }

            SpawnNextTarget();
        }

        private void ClearActiveTarget()
        {
            if (_activeTarget != null && _activeTarget.instance != null)
            {
                Destroy(_activeTarget.instance);
            }

            _activeTarget = null;
        }

        private void EmitTaskRunStarted()
        {
            if (!_emitSequenceTelemetry)
            {
                return;
            }

            TrackEvent("task_run_started", new Dictionary<string, object>
            {
                { "taskRunId", _taskRunId },
                { "taskEngine", nameof(SequenceTaskEngine) },
                { "stimulusScheduler", nameof(StimulusScheduler) },
                { "taskOutcomeAggregator", nameof(TaskOutcomeAggregator) },
                { "cueTimeoutSec", Mathf.Max(0.01f, _effectiveCueTimeoutSec) },
                { "stimulusLeadSec", Mathf.Max(0f, _stimulusLeadSec) },
                { "adaptiveDifficultyEnabled", _latestDifficultyDecision.enabled },
                { "adaptiveDifficultySensitivity", _latestDifficultyDecision.sensitivity },
                { "adaptiveDifficultyScore", _latestDifficultyDecision.nextDifficulty },
                { "labelPipelineEnabled", _labelPipelineActive },
            });
        }

        private void ScheduleStimulusForTarget(TargetRuntime target)
        {
            if (!_emitSequenceTelemetry || target == null)
            {
                return;
            }

            _sequenceStepOrdinal++;
            var stepId = "pulse_step_" + _sequenceStepOrdinal.ToString(CultureInfo.InvariantCulture);
            var cueId = Guid.NewGuid().ToString();
            var targetId = target.targetId.ToString(CultureInfo.InvariantCulture);
            var dueAt = Mathf.Max(0f, GetDurationSeconds() + Mathf.Max(0f, _stimulusLeadSec));
            var timeoutSec = Mathf.Max(0.01f, _effectiveCueTimeoutSec);

            target.sequenceStepId = stepId;
            target.stimulusCueId = cueId;

            _stimulusScheduler.ScheduleCue(new StimulusScheduler.ScheduledCue
            {
                taskRunId = _taskRunId,
                stepId = stepId,
                cueId = cueId,
                stimulusId = "PULSE_TARGET_CUE",
                stimulusChannel = "VISUAL",
                expectedTargetId = targetId,
                requiredAction = "SELECT",
                dueAtElapsedSec = dueAt,
                cueTimeoutSec = timeoutSec,
            });
        }

        private void FlushDueStimuli()
        {
            if (!_emitSequenceTelemetry)
            {
                return;
            }

            _dueStimuli.Clear();
            var elapsedSec = GetDurationSeconds();
            _stimulusScheduler.CollectDueCues(elapsedSec, _dueStimuli);

            for (var i = 0; i < _dueStimuli.Count; i++)
            {
                var cue = _dueStimuli[i];
                _sequenceTaskEngine.RegisterCue(new SequenceTaskEngine.SequenceCue
                {
                    taskRunId = cue.taskRunId,
                    stepId = cue.stepId,
                    cueId = cue.cueId,
                    expectedTargetId = cue.expectedTargetId,
                    requiredAction = cue.requiredAction,
                    cueAtElapsedSec = elapsedSec,
                    cueTimeoutSec = cue.cueTimeoutSec,
                });

                _taskOutcomeAggregator.RecordCue(cue);

                TrackEvent("task_stimulus_presented", new Dictionary<string, object>
                {
                    { "taskRunId", cue.taskRunId },
                    { "stepId", cue.stepId },
                    { "cueId", cue.cueId },
                    { "stimulusId", cue.stimulusId },
                    { "stimulusChannel", cue.stimulusChannel },
                    { "requiredAction", cue.requiredAction },
                    { "expectedTargetId", cue.expectedTargetId },
                    { "cueTimeoutSec", cue.cueTimeoutSec },
                    { "scheduledDueAtSec", cue.dueAtElapsedSec },
                    { "actionOutcome", "REQUIRED" },
                    { "reasonCode", "TASK_STIMULUS_DISPATCHED" },
                });
            }
        }

        private void FlushTimeoutOutcomes()
        {
            if (!_emitSequenceTelemetry)
            {
                return;
            }

            _timeoutOutcomes.Clear();
            _sequenceTaskEngine.CollectTimeoutOutcomes(GetDurationSeconds(), _timeoutOutcomes);

            for (var i = 0; i < _timeoutOutcomes.Count; i++)
            {
                var outcome = _timeoutOutcomes[i];
                _taskOutcomeAggregator.RecordOutcome(outcome);
                EmitTaskActionOutcomeTelemetry(outcome);
                ApplyAdaptiveDifficultyFromCurrentSummary("TASK_TIMEOUT_OUTCOME");
            }
        }

        private SequenceTaskEngine.SequenceOutcome RecordTaskActionOutcome(int targetId, string inputSource)
        {
            if (!_emitSequenceTelemetry)
            {
                return default;
            }

            _sequenceTaskEngine.TryRecordAction(
                new SequenceTaskEngine.SequenceAction
                {
                    stepId = _activeTarget == null ? string.Empty : _activeTarget.sequenceStepId,
                    cueId = _activeTarget == null ? string.Empty : _activeTarget.stimulusCueId,
                    expectedTargetId = _activeTarget == null
                        ? string.Empty
                        : _activeTarget.targetId.ToString(CultureInfo.InvariantCulture),
                    targetId = targetId.ToString(CultureInfo.InvariantCulture),
                    inputSource = string.IsNullOrWhiteSpace(inputSource) ? string.Empty : inputSource.Trim(),
                    actionAtElapsedSec = GetDurationSeconds(),
                },
                out var outcome);

            _taskOutcomeAggregator.RecordOutcome(outcome);
            EmitTaskActionOutcomeTelemetry(outcome);
            ApplyAdaptiveDifficultyFromCurrentSummary("TASK_ACTION_OUTCOME");
            return outcome;
        }

        private void EmitTaskActionOutcomeTelemetry(SequenceTaskEngine.SequenceOutcome outcome)
        {
            var actionOutcome = MapOutcomeToken(outcome.outcomeType);

            TrackEvent("task_action_outcome", new Dictionary<string, object>
            {
                { "taskRunId", outcome.taskRunId },
                { "stepId", outcome.stepId },
                { "cueId", outcome.cueId },
                { "requiredAction", outcome.requiredAction },
                { "expectedTargetId", outcome.expectedTargetId },
                { "actualTargetId", outcome.actualTargetId },
                { "reactionSec", outcome.reactionSec },
                { "cueTimeoutSec", outcome.timeoutSec },
                { "isResolvedStep", outcome.isResolvedStep },
                { "inputSource", outcome.inputSource ?? string.Empty },
                { "actionOutcome", actionOutcome },
                { "reasonCode", string.IsNullOrWhiteSpace(outcome.reasonCode) ? "TASK_ACTION_OBSERVED" : outcome.reasonCode },
            });
        }

        private void EmitTaskOutcomeSummary(string stopReason)
        {
            if (!_emitSequenceTelemetry || _taskSummaryEmitted)
            {
                return;
            }

            _latestTaskSummary = _taskOutcomeAggregator.BuildSummary(GetDurationSeconds());
            if (_latestTaskSummary.actionsObserved <= 0)
            {
                _latestDifficultyDecision = _adaptiveDifficultyController.CurrentDecision;
                _effectiveTargetSpeed = _latestDifficultyDecision.targetSpeed;
                _effectiveTargetScale = _latestDifficultyDecision.targetScale;
                _effectiveCueTimeoutSec = _latestDifficultyDecision.cueTimeoutSec;
            }
            _taskSummaryEmitted = true;

            TrackEvent("task_outcome_summary", new Dictionary<string, object>
            {
                { "taskRunId", _latestTaskSummary.taskRunId },
                { "cuesPresented", _latestTaskSummary.cuesPresented },
                { "actionsObserved", _latestTaskSummary.actionsObserved },
                { "correctCount", _latestTaskSummary.correctCount },
                { "incorrectCount", _latestTaskSummary.incorrectCount },
                { "lateCount", _latestTaskSummary.lateCount },
                { "omittedCount", _latestTaskSummary.omittedCount },
                { "redundantCount", _latestTaskSummary.redundantCount },
                { "firstActionLatencySec", _latestTaskSummary.firstActionLatencySec },
                { "averageReactionSec", _latestTaskSummary.averageReactionSec },
                { "completionRatio", _latestTaskSummary.completionRatio },
                { "elapsedSec", _latestTaskSummary.elapsedSec },
                { "actionOutcome", _latestTaskSummary.completionRatio >= 0.999f ? "CORRECT" : "OBSERVED" },
                { "reasonCode", string.IsNullOrWhiteSpace(stopReason) ? "TASK_SUMMARY_EMITTED" : stopReason.Trim() },
                { "adaptiveDifficultyScore", _latestDifficultyDecision.nextDifficulty },
                { "adaptiveDifficultyReasonCode", _latestDifficultyDecision.reasonCode ?? string.Empty },
            });

            EmitTaskLabel();
        }

        private void ConfigureAdaptiveDifficultyPolicy()
        {
            var sensitivity = _activeConfig == null
                ? _adaptiveDifficultySensitivity
                : _activeConfig.AdaptiveDifficultySensitivity;
            var enabled = _activeConfig == null
                ? _adaptiveDifficultyEnabled
                : _activeConfig.AdaptiveDifficultyEnabled;
            var seedLevel = _activeConfig == null
                ? 3
                : _activeConfig.AdaptiveDifficultyLevel;
            var seedDifficulty = Mathf.InverseLerp(1f, 5f, Mathf.Clamp(seedLevel, 1, 5));
            var baseTargetSpeed = _activeConfig == null ? _defaultTargetSpeed : _activeConfig.TargetSpeed;
            var baseTargetScale = _activeConfig == null ? _defaultTargetScale : _activeConfig.TargetScale;
            var baseCueTimeoutSec = Mathf.Max(0.05f, _cueTimeoutSec);

            var policy = AdaptiveDifficultyController.CreateDefaultPolicy();
            policy.enabled = enabled;
            policy.sensitivity = sensitivity;
            policy.minTargetSpeed = Mathf.Clamp(baseTargetSpeed * 0.72f, 0.1f, 3f);
            policy.maxTargetSpeed = Mathf.Clamp(baseTargetSpeed * 1.4f, policy.minTargetSpeed, 3f);
            policy.minTargetScale = Mathf.Clamp(baseTargetScale * 0.68f, 0.12f, 1.2f);
            policy.maxTargetScale = Mathf.Clamp(baseTargetScale * 1.28f, policy.minTargetScale, 1.2f);
            policy.minCueTimeoutSec = Mathf.Clamp(baseCueTimeoutSec * 0.58f, 0.35f, 8f);
            policy.maxCueTimeoutSec = Mathf.Clamp(baseCueTimeoutSec * 1.35f, policy.minCueTimeoutSec, 10f);

            _adaptiveDifficultyController.Configure(policy, seedDifficulty);
            _latestDifficultyDecision = _adaptiveDifficultyController.CurrentDecision;
            _effectiveTargetSpeed = _latestDifficultyDecision.targetSpeed;
            _effectiveTargetScale = _latestDifficultyDecision.targetScale;
            _effectiveCueTimeoutSec = _latestDifficultyDecision.cueTimeoutSec;
            _labelPipelineActive = _activeConfig == null ? _labelPipelineEnabled : _activeConfig.LabelPipelineEnabled;
        }

        private void ApplyAdaptiveDifficultyFromCurrentSummary(string signalReasonCode)
        {
            if (_activeConfig == null)
            {
                return;
            }

            var summary = _taskOutcomeAggregator.BuildSummary(GetDurationSeconds());
            _latestDifficultyDecision = _adaptiveDifficultyController.Evaluate(summary);
            _effectiveTargetSpeed = _latestDifficultyDecision.targetSpeed;
            _effectiveTargetScale = _latestDifficultyDecision.targetScale;
            _effectiveCueTimeoutSec = _latestDifficultyDecision.cueTimeoutSec;

            if (!_latestDifficultyDecision.changed)
            {
                return;
            }

            TrackEvent("adaptive_difficulty_adjusted", new Dictionary<string, object>
            {
                { "taskRunId", _taskRunId },
                { "signalReasonCode", string.IsNullOrWhiteSpace(signalReasonCode) ? "TASK_SIGNAL" : signalReasonCode.Trim() },
                { "previousDifficulty", _latestDifficultyDecision.previousDifficulty },
                { "nextDifficulty", _latestDifficultyDecision.nextDifficulty },
                { "difficultyDelta", _latestDifficultyDecision.delta },
                { "targetSpeed", _latestDifficultyDecision.targetSpeed },
                { "targetScale", _latestDifficultyDecision.targetScale },
                { "cueTimeoutSec", _latestDifficultyDecision.cueTimeoutSec },
                { "completionRatio", _latestDifficultyDecision.completionRatio },
                { "averageReactionSec", _latestDifficultyDecision.averageReactionSec },
                { "omittedRatio", _latestDifficultyDecision.omittedRatio },
                { "lateRatio", _latestDifficultyDecision.lateRatio },
                { "actionOutcome", _latestDifficultyDecision.delta >= 0f ? "CORRECT" : "INCORRECT" },
                { "reasonCode", string.IsNullOrWhiteSpace(_latestDifficultyDecision.reasonCode) ? "KEEP_DIFFICULTY" : _latestDifficultyDecision.reasonCode },
            });
        }

        private void EmitTaskLabel()
        {
            if (!_labelPipelineActive)
            {
                return;
            }

            _latestTaskLabel = _taskLabelPipeline.CreateTaskLabel(
                _latestTaskSummary,
                _latestDifficultyDecision);

            TrackEvent("task_label_generated", new Dictionary<string, object>
            {
                { "taskRunId", _latestTaskLabel.taskRunId },
                { "labelId", _latestTaskLabel.labelId },
                { "labelSchema", _latestTaskLabel.labelSchema },
                { "labelVersion", _latestTaskLabel.labelVersion },
                { "labelType", _latestTaskLabel.labelType },
                { "performanceBand", _latestTaskLabel.performanceBand },
                { "paceBand", _latestTaskLabel.paceBand },
                { "fatigueBand", _latestTaskLabel.fatigueBand },
                { "adaptationRecommendation", _latestTaskLabel.adaptationRecommendation },
                { "confidence", _latestTaskLabel.confidence },
                { "completionRatio", _latestTaskLabel.completionRatio },
                { "averageReactionSec", _latestTaskLabel.averageReactionSec },
                { "omittedRatio", _latestTaskLabel.omittedRatio },
                { "lateRatio", _latestTaskLabel.lateRatio },
                { "difficultyScore", _latestTaskLabel.difficultyScore },
                { "recommendedDifficultyLevel", _latestTaskLabel.recommendedDifficultyLevel },
                { "actionOutcome", _latestTaskLabel.performanceBand == "PERFORMANCE_HIGH" ? "CORRECT" : "OBSERVED" },
                { "reasonCode", _latestTaskLabel.reasonCode },
            });
        }

        private static string MapOutcomeToken(SequenceOutcomeType outcomeType)
        {
            switch (outcomeType)
            {
                case SequenceOutcomeType.Correct:
                    return "CORRECT";
                case SequenceOutcomeType.Incorrect:
                    return "INCORRECT";
                case SequenceOutcomeType.Late:
                    return "LATE";
                case SequenceOutcomeType.Redundant:
                    return "REDUNDANT";
                case SequenceOutcomeType.Omitted:
                    return "OMITTED";
                default:
                    return "OBSERVED";
            }
        }

        private Vector3 ViewportToWorld(Vector2 viewportPos)
        {
            if (_targetCamera == null)
            {
                return new Vector3(0f, 1f, 2f);
            }

            var depth = Mathf.Max(2f, _targetCamera.nearClipPlane + 2.5f);
            return _targetCamera.ViewportToWorldPoint(new Vector3(
                Mathf.Clamp01(viewportPos.x),
                Mathf.Clamp01(viewportPos.y),
                depth));
        }

        private void EnsureCamera()
        {
            if (_targetCamera != null)
            {
                return;
            }

            _targetCamera = Camera.main;
            if (_targetCamera == null)
            {
                _targetCamera = FindFirstObjectByType<Camera>();
            }
        }

        private void EnsureTargetMaterial(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            var sharedMaterial = renderer.sharedMaterial;
            if (!IsMaterialShaderBroken(sharedMaterial))
            {
                return;
            }

            var fallbackMaterial = ResolveRuntimeTargetMaterial();
            if (fallbackMaterial == null)
            {
                return;
            }

            renderer.sharedMaterial = fallbackMaterial;
        }

        private Material ResolveRuntimeTargetMaterial()
        {
            if (_runtimeTargetMaterial != null && _runtimeTargetMaterial.shader != null)
            {
                return _runtimeTargetMaterial;
            }

            var shader = ResolveRuntimeTargetShader();
            if (shader == null)
            {
                return null;
            }

            _runtimeTargetMaterial = new Material(shader)
            {
                name = "PulseTargetRuntimeMaterial",
            };
            if (_runtimeTargetMaterial.HasProperty("_Color"))
            {
                _runtimeTargetMaterial.color = Color.white;
            }

            return _runtimeTargetMaterial;
        }

        private Shader ResolveRuntimeTargetShader()
        {
            if (_targetFallbackShader != null && _targetFallbackShader.isSupported)
            {
                LogResolvedRuntimeShader(_targetFallbackShader, "serialized_fallback");
                return _targetFallbackShader;
            }

            var shaderNames = new[]
            {
                "Standard",
                "Legacy Shaders/Diffuse",
                "Legacy Shaders/Transparent/Diffuse",
                "Mobile/Diffuse",
                "Unlit/Color",
                "Universal Render Pipeline/Simple Lit",
                "Universal Render Pipeline/Unlit",
                "Universal Render Pipeline/Lit",
                "Sprites/Default",
                "Hidden/Internal-Colored",
            };

            for (var i = 0; i < shaderNames.Length; i++)
            {
                var shader = Shader.Find(shaderNames[i]);
                if (shader == null)
                {
                    continue;
                }

                var canUseUnsupportedInternal = string.Equals(
                    shader.name,
                    "Hidden/Internal-Colored",
                    StringComparison.Ordinal);
                if (shader.isSupported || canUseUnsupportedInternal)
                {
                    LogResolvedRuntimeShader(shader, "lookup:" + shaderNames[i]);
                    return shader;
                }
            }

            if (!_loggedTargetShaderSelection)
            {
                _loggedTargetShaderSelection = true;
                Debug.LogWarning(
                    "[PulseTargets] No supported fallback shader found. Targets may render pink/invisible on Quest.");
            }

            return null;
        }

        private void LogResolvedRuntimeShader(Shader shader, string source)
        {
            if (_loggedTargetShaderSelection || shader == null)
            {
                return;
            }

            _loggedTargetShaderSelection = true;
            Debug.Log(
                "[PulseTargets] Runtime fallback shader selected: " +
                shader.name +
                " (supported=" +
                shader.isSupported +
                ", source=" +
                source +
                ")");
        }

        private static bool IsMaterialShaderBroken(Material material)
        {
            return material == null ||
                material.shader == null ||
                string.Equals(
                    material.shader.name,
                    "Hidden/InternalErrorShader",
                    StringComparison.Ordinal) ||
                !material.shader.isSupported;
        }

        [Serializable]
        private sealed class TargetRuntime
        {
            public int targetId;
            public Vector2 viewportPos;
            public Vector2 direction;
            public GameObject instance;
            public string sequenceStepId;
            public string stimulusCueId;
        }
    }

    [Serializable]
    public sealed class PulseTargetsGameConfig : GameContracts.IGameConfig
    {
        public const string DefaultGameId = "pulse_target_tap";

        [SerializeField] private string _gameId = DefaultGameId;
        [SerializeField] private int _version = 1;
        [SerializeField] private int _targetCount = 8;
        [SerializeField] private float _targetSpeed = 0.7f;
        [SerializeField] private float _targetScale = 0.3f;
        [SerializeField] private bool _adaptiveDifficultyEnabled = true;
        [SerializeField] private float _adaptiveDifficultySensitivity = 0.55f;
        [SerializeField] private int _adaptiveDifficultyLevel = 3;
        [SerializeField] private bool _labelPipelineEnabled = true;

        public string GameId => string.IsNullOrWhiteSpace(_gameId) ? DefaultGameId : _gameId;
        public int Version => _version <= 0 ? 1 : _version;
        public int TargetCount => Mathf.Clamp(_targetCount, 3, 64);
        public float TargetSpeed => Mathf.Clamp(_targetSpeed, 0.1f, 3f);
        public float TargetScale => Mathf.Clamp(_targetScale, 0.12f, 1.2f);
        public bool AdaptiveDifficultyEnabled => _adaptiveDifficultyEnabled;
        public float AdaptiveDifficultySensitivity => Mathf.Clamp01(_adaptiveDifficultySensitivity);
        public int AdaptiveDifficultyLevel => Mathf.Clamp(_adaptiveDifficultyLevel, 1, 5);
        public bool LabelPipelineEnabled => _labelPipelineEnabled;

        public static PulseTargetsGameConfig CreateDefault(
            int fallbackTargetCount,
            float fallbackTargetSpeed,
            float fallbackTargetScale)
        {
            return new PulseTargetsGameConfig
            {
                _gameId = DefaultGameId,
                _version = 1,
                _targetCount = Mathf.Clamp(fallbackTargetCount, 3, 64),
                _targetSpeed = Mathf.Clamp(fallbackTargetSpeed, 0.1f, 3f),
                _targetScale = Mathf.Clamp(fallbackTargetScale, 0.12f, 1.2f),
                _adaptiveDifficultyEnabled = true,
                _adaptiveDifficultySensitivity = 0.55f,
                _adaptiveDifficultyLevel = 3,
                _labelPipelineEnabled = true,
            };
        }

        public static PulseTargetsGameConfig CreateFromStartCommand(
            StartGameCommand command,
            PulseTargetsGameConfig previous,
            int fallbackTargetCount,
            float fallbackTargetSpeed,
            float fallbackTargetScale)
        {
            var baseline = previous ?? CreateDefault(
                fallbackTargetCount,
                fallbackTargetSpeed,
                fallbackTargetScale);
            var settings = ParseStartSettings(command);

            var nextTargetCount = settings != null && settings.targetCount > 0
                ? settings.targetCount
                : baseline.TargetCount;
            var nextTargetSpeed = baseline.TargetSpeed;
            if (settings != null)
            {
                if (settings.targetSpeed > 0f)
                {
                    nextTargetSpeed = settings.targetSpeed;
                }
                else if (settings.spawnSpeed > 0f)
                {
                    // Backward-compatible alias used by early mobile control schemas.
                    nextTargetSpeed = settings.spawnSpeed;
                }
            }
            var nextTargetScale = settings != null && settings.targetScale > 0f
                ? settings.targetScale
                : baseline.TargetScale;
            var hasAdaptiveEnabled = HasStartSetting(command, "adaptiveDifficultyEnabled");
            var hasLabelPipelineEnabled = HasStartSetting(command, "labelPipelineEnabled");
            var hasAdaptiveSensitivity = HasStartSetting(command, "adaptiveDifficultySensitivity");
            var hasAdaptiveLevel = HasStartSetting(command, "adaptiveDifficultyLevel");

            var nextAdaptiveDifficultyEnabled = hasAdaptiveEnabled && settings != null
                ? settings.adaptiveDifficultyEnabled
                : baseline.AdaptiveDifficultyEnabled;
            var nextLabelPipelineEnabled = hasLabelPipelineEnabled && settings != null
                ? settings.labelPipelineEnabled
                : baseline.LabelPipelineEnabled;
            var nextAdaptiveDifficultySensitivity = hasAdaptiveSensitivity && settings != null
                ? settings.adaptiveDifficultySensitivity
                : baseline.AdaptiveDifficultySensitivity;
            var nextAdaptiveDifficultyLevel = hasAdaptiveLevel && settings != null
                ? settings.adaptiveDifficultyLevel
                : baseline.AdaptiveDifficultyLevel;

            var nextVersion = baseline.Version;
            if (command != null && command.gameConfigVersion > 0)
            {
                nextVersion = command.gameConfigVersion;
            }
            else if (settings != null && settings.version > 0)
            {
                nextVersion = settings.version;
            }

            return new PulseTargetsGameConfig
            {
                _gameId = DefaultGameId,
                _version = Mathf.Max(1, nextVersion),
                _targetCount = Mathf.Clamp(nextTargetCount, 3, 64),
                _targetSpeed = Mathf.Clamp(nextTargetSpeed, 0.1f, 3f),
                _targetScale = Mathf.Clamp(nextTargetScale, 0.12f, 1.2f),
                _adaptiveDifficultyEnabled = nextAdaptiveDifficultyEnabled,
                _adaptiveDifficultySensitivity = Mathf.Clamp01(nextAdaptiveDifficultySensitivity),
                _adaptiveDifficultyLevel = Mathf.Clamp(nextAdaptiveDifficultyLevel, 1, 5),
                _labelPipelineEnabled = nextLabelPipelineEnabled,
            };
        }

        private static PulseTargetsStartSettings ParseStartSettings(StartGameCommand command)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.gameConfigJson))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<PulseTargetsStartSettings>(command.gameConfigJson);
            }
            catch
            {
                return null;
            }
        }

        private static bool HasStartSetting(StartGameCommand command, string key)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.gameConfigJson) || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            var marker = "\"" + key.Trim() + "\"";
            return command.gameConfigJson.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        [Serializable]
        private sealed class PulseTargetsStartSettings
        {
            public int targetCount;
            public float targetSpeed;
            public float spawnSpeed;
            public float targetScale;
            public int version;
            public bool adaptiveDifficultyEnabled = true;
            public float adaptiveDifficultySensitivity = 0.55f;
            public int adaptiveDifficultyLevel = 3;
            public bool labelPipelineEnabled = true;
        }
    }

    public sealed class PulseTargetClickTarget : MonoBehaviour, IQuestPointerTarget
    {
        private int _targetId;
        private Action<int, string> _onClicked;

        public void Configure(int targetId, Action<int, string> onClicked)
        {
            _targetId = targetId;
            _onClicked = onClicked;
        }

#if UNITY_EDITOR
        private void OnMouseDown()
        {
            ActivateFromPointer("MOUSE");
        }
#endif

        public void ActivateFromPointer(string source)
        {
            _onClicked?.Invoke(_targetId, string.IsNullOrWhiteSpace(source) ? "UNKNOWN" : source.Trim());
        }
    }
}
