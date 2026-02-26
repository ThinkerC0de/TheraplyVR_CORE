using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.XR;
using GameContracts = TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using TheraplyCore.Interactions;

namespace TheraplyGames.BilateralMarkers
{
    public static class BilateralMarkersReasonCodes
    {
        public const string ConfigApplied = "BILATERAL_CONFIG_APPLIED";
        public const string ConfigFallbackUsed = "BILATERAL_CONFIG_FALLBACK_USED";
        public const string ConfigClamped = "BILATERAL_CONFIG_CLAMPED";
        public const string RoundStarted = "BILATERAL_ROUND_STARTED";
        public const string RoundCompleted = "BILATERAL_ROUND_COMPLETED";
        public const string RoundTimeout = "BILATERAL_ROUND_TIMEOUT";
        public const string RoundStopped = "BILATERAL_ROUND_STOPPED";
        public const string InputUnavailable = "BILATERAL_INPUT_UNAVAILABLE";
        public const string HandPhaseLocked = "BILATERAL_HAND_PHASE_LOCKED";
        public const string TracePublished = "BILATERAL_TRACE_PUBLISHED";
        public const string TempAdaptiveApplied = "BILATERAL_ADAPTIVE_TEMP_APPLIED";
        public const string AlternationSyncConfirmed = "BILATERAL_ALTERNATION_SYNC_CONFIRMED";
        public const string AlternationOffsetMismatch = "BILATERAL_ALTERNATION_OFFSET_MISMATCH";
    }

    [DisallowMultipleComponent]
    public sealed class BilateralMarkersSceneGameController : SceneGameController
    {
        public const string DefaultGameId = "bilateral_markers";

        // TEMP_SIMPLE_DEFAULT: conservative runtime defaults until therapeutic calibration pass.
        private const float TempSimpleDefaultTempoBpm = 72f;
        private const float TempSimpleDefaultDurationSec = 45f;
        private const float TempSimpleDefaultTunnelWidthM = 0.11f;
        private const float TempSimpleDefaultPathScaleM = 0.23f;
        private const float TempSimpleDefaultSyncWindowMs = 220f;
        private const float TempSimpleDefaultAlternationOffsetSec = 0.7f;
        private const float TempSimplePauseSpeedThresholdMps = 0.03f;
        private const float TempSimpleStartProgressThreshold = 0.05f;
        private const float TempSimpleAdaptiveInvalidRatioThreshold = 0.35f;
        private const float TempSimpleAdaptiveTunnelScale = 1.35f;

        [Header("Runtime Anchoring")]
        [SerializeField] private Camera _anchorCamera;
        [SerializeField] private float _pathDistanceMeters = 0.85f;
        [SerializeField] private float _pathSeparationMeters = 0.48f;
        [SerializeField] private float _pathVerticalOffsetMeters = -0.05f;
        [SerializeField] private int _pathSampleCount = 96;

        [Header("Sampling")]
        [SerializeField] private float _sampleIntervalSec = 0.05f;
        [SerializeField] private bool _emitDetailedSampleEvents = true;

        [Header("Visuals")]
        [SerializeField] private Color _leftPathColor = new Color(0.12f, 0.95f, 0.35f, 1f);
        [SerializeField] private Color _rightPathColor = new Color(0.15f, 0.55f, 1f, 1f);
        [SerializeField] private bool _showDebugHud;

        [Header("Trace")]
        [SerializeField] private bool _enableTraceRecorder = true;

        private enum BilateralLevel
        {
            Symmetry = 0,
            Mirror = 1,
            Rotational = 2,
            Asymmetry = 3,
            Alternating = 4,
        }

        private enum BilateralPattern
        {
            Wave = 0,
            Spiral = 1,
            FigureEight = 2,
            Zigzag = 3,
            Arc = 4,
        }

        [Serializable]
        private sealed class BilateralConfigPayload
        {
            public int level = (int)BilateralLevel.Symmetry;
            public string pattern = "wave";
            public string leftPattern = string.Empty;
            public string rightPattern = string.Empty;
            public float tempoBpm = TempSimpleDefaultTempoBpm;
            public float durationSec = TempSimpleDefaultDurationSec;
            public float tunnelWidthM = TempSimpleDefaultTunnelWidthM;
            public float pathScaleM = TempSimpleDefaultPathScaleM;
            public float syncWindowMs = TempSimpleDefaultSyncWindowMs;
            public float alternationOffsetSec = TempSimpleDefaultAlternationOffsetSec;
            public string dominantHand = "right";
            public bool adaptiveAssistEnabled = true;
            public int version = 1;
        }

        private struct BilateralSettings
        {
            public BilateralLevel level;
            public BilateralPattern primaryPattern;
            public BilateralPattern leftPattern;
            public BilateralPattern rightPattern;
            public float tempoBpm;
            public float durationSec;
            public float tunnelWidthMeters;
            public float pathScaleMeters;
            public float syncWindowMs;
            public float alternationOffsetSec;
            public bool dominantHandLeft;
            public bool adaptiveAssistEnabled;

            public static BilateralSettings CreateDefault()
            {
                return new BilateralSettings
                {
                    level = BilateralLevel.Symmetry,
                    primaryPattern = BilateralPattern.Wave,
                    leftPattern = BilateralPattern.Spiral,
                    rightPattern = BilateralPattern.Wave,
                    tempoBpm = TempSimpleDefaultTempoBpm,
                    durationSec = TempSimpleDefaultDurationSec,
                    tunnelWidthMeters = TempSimpleDefaultTunnelWidthM,
                    pathScaleMeters = TempSimpleDefaultPathScaleM,
                    syncWindowMs = TempSimpleDefaultSyncWindowMs,
                    alternationOffsetSec = TempSimpleDefaultAlternationOffsetSec,
                    dominantHandLeft = false,
                    adaptiveAssistEnabled = true,
                };
            }
        }

        private sealed class HandRuntime
        {
            public readonly string handToken;
            public readonly string targetId;
            public readonly string targetName;
            public readonly List<Vector3> points = new List<Vector3>(128);

            public int lastNearestIndex;
            public float progress01;
            public float deviationMeters;
            public int sampleCount;
            public int validSampleCount;
            public int invalidSampleCount;
            public float firstMoveAtSec = -1f;
            public bool startMarked;
            public bool completionMarked;
            public float pauseSampleCount;
            public bool hasPreviousPosition;
            public Vector3 previousPosition;
            public float motionRangeMinY = float.MaxValue;
            public float motionRangeMaxY = float.MinValue;

            public HandRuntime(string handToken, string targetId, string targetName)
            {
                this.handToken = handToken;
                this.targetId = targetId;
                this.targetName = targetName;
                Reset();
            }

            public void Reset()
            {
                points.Clear();
                lastNearestIndex = 0;
                progress01 = 0f;
                deviationMeters = 0f;
                sampleCount = 0;
                validSampleCount = 0;
                invalidSampleCount = 0;
                firstMoveAtSec = -1f;
                startMarked = false;
                completionMarked = false;
                pauseSampleCount = 0f;
                hasPreviousPosition = false;
                previousPosition = Vector3.zero;
                motionRangeMinY = float.MaxValue;
                motionRangeMaxY = float.MinValue;
            }
        }

        private struct PathSpec
        {
            public BilateralPattern pattern;
            public bool mirrorX;
            public bool invertY;
            public bool reverse;
        }

        private struct RoundSummary
        {
            public bool completed;
            public string reasonCode;
            public float durationSec;
            public float leftAccuracy01;
            public float rightAccuracy01;
            public float leftSmoothness01;
            public float rightSmoothness01;
            public float leftAmplitudeM;
            public float rightAmplitudeM;
            public float syncDeltaMs;
            public float targetSyncDeltaMs;
            public bool syncWithinWindow;
            public int syncOutOfWindowCount;
            public bool adaptiveApplied;
            public float finalTunnelWidthM;
            public int level;
            public float tempoBpm;
        }

        private HandRuntime _leftHandRuntime = new HandRuntime("LEFT", "left_path", "Left Path");
        private HandRuntime _rightHandRuntime = new HandRuntime("RIGHT", "right_path", "Right Path");
        private BilateralSettings _settings = BilateralSettings.CreateDefault();
        private RoundSummary _lastRoundSummary;

        private DualHandProbe _dualHandProbe;
        private PosePathProbe _posePathProbe;
        private TimelineProbe _timelineProbe;
        private MotionTraceRecorder _motionTraceRecorder;
        private InputDevice _leftDevice;
        private InputDevice _rightDevice;
        private Transform _trackingOrigin;
        private Transform _leftTraceTransform;
        private Transform _rightTraceTransform;
        private GameObject _runtimeVisualRoot;
        private LineRenderer _leftPathRenderer;
        private LineRenderer _rightPathRenderer;
        private Material _pathMaterial;

        private bool _roundActive;
        private bool _roundFinalizing;
        private bool _tracePublished;
        private bool _syncResolved;
        private bool _syncWithinWindow;
        private bool _tempAdaptiveApplied;
        private bool _inputUnavailableLogged;
        private float _effectiveTunnelWidthMeters = TempSimpleDefaultTunnelWidthM;
        private float _syncDeltaMs = -1f;
        private float _targetSyncDeltaMs;
        private int _syncOutOfWindowCount;
        private float _nextSampleAtRealtimeSec;
        private float _nextBeatAtSec;
        private int _beatIndex;
        private string _lastConfigReasonCode = BilateralMarkersReasonCodes.ConfigApplied;

        public override string GameId => DefaultGameId;

        private void Update()
        {
            if (!_roundActive || State != GameContracts.GameState.Playing)
            {
                return;
            }

            var sampleInterval = Mathf.Clamp(_sampleIntervalSec, 0.02f, 0.5f);
            var now = Time.realtimeSinceStartup;
            if (_nextSampleAtRealtimeSec <= 0f)
            {
                _nextSampleAtRealtimeSec = now;
            }

            while (_roundActive && now >= _nextSampleAtRealtimeSec)
            {
                SampleControllers(sampleInterval);
                _nextSampleAtRealtimeSec += sampleInterval;
                now = Time.realtimeSinceStartup;
            }

            if (!_roundActive)
            {
                return;
            }

            EmitTimelineBeatTicks();
            TryApplyTemporaryAdaptiveAssist();
            TryFinalizeRoundByProgressOrTimeout();
            UpdatePathVisualFeedback();
        }

        private void OnDestroy()
        {
            CleanupVisuals();
            CleanupTraceTargets();
            if (_pathMaterial != null)
            {
                Destroy(_pathMaterial);
                _pathMaterial = null;
            }
        }

        protected override void OnInitialized(SceneGameConfig config, GameContracts.IGameContext context)
        {
            ResolveDependencies();
            EnsureAnchorCamera();
            ResetRoundRuntime();
            TrackSceneLifecycle(
                "bilateral_runtime_ready",
                BilateralMarkersReasonCodes.ConfigApplied,
                new Dictionary<string, object>
                {
                    { "level", (int)_settings.level },
                    { "tempoBpm", _settings.tempoBpm },
                    { "durationSec", _settings.durationSec },
                });
        }

        public override void ApplyConfig(SceneGameConfig config)
        {
            var settings = BilateralSettings.CreateDefault();
            var reasonCode = BilateralMarkersReasonCodes.ConfigApplied;

            if (!TryReadConfigPayload<BilateralConfigPayload>(out var payload, out var parseReasonCode))
            {
                reasonCode = string.IsNullOrWhiteSpace(parseReasonCode)
                    ? BilateralMarkersReasonCodes.ConfigFallbackUsed
                    : parseReasonCode;
            }
            else
            {
                settings = BuildSettings(payload, out reasonCode);
            }

            _settings = settings;
            _lastConfigReasonCode = reasonCode;

            TrackSceneLifecycle(
                "bilateral_config_applied",
                reasonCode,
                new Dictionary<string, object>
                {
                    { "level", (int)_settings.level },
                    { "tempoBpm", _settings.tempoBpm },
                    { "durationSec", _settings.durationSec },
                    { "tunnelWidthM", _settings.tunnelWidthMeters },
                    { "pathScaleM", _settings.pathScaleMeters },
                    { "syncWindowMs", _settings.syncWindowMs },
                    { "alternationOffsetSec", _settings.alternationOffsetSec },
                    { "adaptiveAssistEnabled", _settings.adaptiveAssistEnabled },
                    { "tempSimpleDefaults", true },
                });
        }

        protected override void OnStarted()
        {
            BeginRound();
        }

        protected override void OnPaused()
        {
            _roundActive = false;
            TrackSceneLifecycle(
                "bilateral_round_paused",
                BilateralMarkersReasonCodes.RoundStopped,
                new Dictionary<string, object>
                {
                    { "elapsedSec", GetDurationSeconds() },
                });
        }

        protected override void OnResumed()
        {
            _roundActive = true;
            _nextSampleAtRealtimeSec = Time.realtimeSinceStartup;
            TrackSceneLifecycle(
                "bilateral_round_resumed",
                BilateralMarkersReasonCodes.RoundStarted,
                new Dictionary<string, object>
                {
                    { "elapsedSec", GetDurationSeconds() },
                });
        }

        protected override void OnStopped(GameContracts.GameStopReason reason)
        {
            _roundActive = false;
            if (!_tracePublished)
            {
                PublishTraceIfNeeded(ResolveStopReasonCode(reason));
            }

            TrackSceneLifecycle(
                "bilateral_round_stopped",
                ResolveStopReasonCode(reason),
                new Dictionary<string, object>
                {
                    { "elapsedSec", GetDurationSeconds() },
                    { "state", State.ToString() },
                });

            CleanupVisuals();
            CleanupTraceTargets();
        }

        public override GameContracts.IGameResult BuildResult()
        {
            var metrics = new Dictionary<string, object>
            {
                { "level", _lastRoundSummary.level },
                { "tempoBpm", _lastRoundSummary.tempoBpm },
                { "durationSec", _lastRoundSummary.durationSec },
                { "completed", _lastRoundSummary.completed },
                { "reasonCode", _lastRoundSummary.reasonCode ?? string.Empty },
                { "leftAccuracy01", _lastRoundSummary.leftAccuracy01 },
                { "rightAccuracy01", _lastRoundSummary.rightAccuracy01 },
                { "leftSmoothness01", _lastRoundSummary.leftSmoothness01 },
                { "rightSmoothness01", _lastRoundSummary.rightSmoothness01 },
                { "leftAmplitudeM", _lastRoundSummary.leftAmplitudeM },
                { "rightAmplitudeM", _lastRoundSummary.rightAmplitudeM },
                { "syncDeltaMs", _lastRoundSummary.syncDeltaMs },
                { "targetSyncDeltaMs", _lastRoundSummary.targetSyncDeltaMs },
                { "syncWithinWindow", _lastRoundSummary.syncWithinWindow },
                { "syncOutOfWindowCount", _lastRoundSummary.syncOutOfWindowCount },
                { "adaptiveApplied", _lastRoundSummary.adaptiveApplied },
                { "finalTunnelWidthM", _lastRoundSummary.finalTunnelWidthM },
            };

            return new GameResult(
                GameId,
                State == GameContracts.GameState.Completed,
                GetDurationSeconds(),
                metrics);
        }

        private void BeginRound()
        {
            ResolveDependencies();
            EnsureAnchorCamera();
            ResetRoundRuntime();
            BuildPathsAndVisuals();
            EnsureQuestDevices(forceRefresh: true);
            BeginTraceIfNeeded();

            _roundActive = true;
            _roundFinalizing = false;
            _tracePublished = false;
            _nextSampleAtRealtimeSec = Time.realtimeSinceStartup;
            _nextBeatAtSec = 0f;
            _beatIndex = 0;
            _targetSyncDeltaMs = ResolveExpectedSyncDeltaMs();
            _effectiveTunnelWidthMeters = _settings.tunnelWidthMeters;
            _tempAdaptiveApplied = false;
            _syncResolved = false;
            _syncWithinWindow = false;
            _syncDeltaMs = -1f;
            _syncOutOfWindowCount = 0;
            _inputUnavailableLogged = false;

            TrackSceneLifecycle(
                "bilateral_round_started",
                BilateralMarkersReasonCodes.RoundStarted,
                new Dictionary<string, object>
                {
                    { "level", (int)_settings.level },
                    { "tempoBpm", _settings.tempoBpm },
                    { "durationSec", _settings.durationSec },
                    { "expectedSyncDeltaMs", _targetSyncDeltaMs },
                    { "tunnelWidthM", _effectiveTunnelWidthMeters },
                    { "configReasonCode", _lastConfigReasonCode ?? string.Empty },
                });
        }

        private void ResetRoundRuntime()
        {
            _leftHandRuntime.Reset();
            _rightHandRuntime.Reset();
            _roundActive = false;
            _roundFinalizing = false;
            _syncResolved = false;
            _syncWithinWindow = false;
            _syncDeltaMs = -1f;
            _syncOutOfWindowCount = 0;
            _tempAdaptiveApplied = false;
            _effectiveTunnelWidthMeters = _settings.tunnelWidthMeters;
            _tracePublished = false;
            _lastRoundSummary = default;
        }

        private void ResolveDependencies()
        {
            if (_dualHandProbe == null)
            {
                _dualHandProbe = DualHandProbe.Instance;
                if (_dualHandProbe == null)
                {
                    _dualHandProbe = FindFirstObjectByType<DualHandProbe>();
                }
            }

            if (_posePathProbe == null)
            {
                _posePathProbe = PosePathProbe.Instance;
                if (_posePathProbe == null)
                {
                    _posePathProbe = FindFirstObjectByType<PosePathProbe>();
                }
            }

            if (_timelineProbe == null)
            {
                _timelineProbe = TimelineProbe.Instance;
                if (_timelineProbe == null)
                {
                    _timelineProbe = FindFirstObjectByType<TimelineProbe>();
                }
            }

            if (_enableTraceRecorder && _motionTraceRecorder == null)
            {
                _motionTraceRecorder = FindFirstObjectByType<MotionTraceRecorder>();
                if (_motionTraceRecorder == null)
                {
                    _motionTraceRecorder = gameObject.AddComponent<MotionTraceRecorder>();
                }
            }
        }

        private void EnsureAnchorCamera()
        {
            if (_anchorCamera == null)
            {
                _anchorCamera = Camera.main;
                if (_anchorCamera == null)
                {
                    _anchorCamera = FindFirstObjectByType<Camera>();
                }
            }
        }

        private void EnsureQuestDevices(bool forceRefresh)
        {
            if (forceRefresh || !_leftDevice.isValid)
            {
                _leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            }

            if (forceRefresh || !_rightDevice.isValid)
            {
                _rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            }
        }

        private bool TryReadHandPose(
            XRNode node,
            ref InputDevice device,
            out Vector3 worldPosition,
            out Quaternion worldRotation)
        {
            worldPosition = Vector3.zero;
            worldRotation = Quaternion.identity;

            if (!device.isValid)
            {
                device = InputDevices.GetDeviceAtXRNode(node);
            }

            if (!device.isValid)
            {
                return false;
            }

            if (!device.TryGetFeatureValue(CommonUsages.devicePosition, out var localPosition))
            {
                return false;
            }

            if (!device.TryGetFeatureValue(CommonUsages.deviceRotation, out var localRotation))
            {
                localRotation = Quaternion.identity;
            }

            if (_trackingOrigin != null)
            {
                worldPosition = _trackingOrigin.TransformPoint(localPosition);
                worldRotation = _trackingOrigin.rotation * localRotation;
            }
            else
            {
                worldPosition = localPosition;
                worldRotation = localRotation;
            }

            return true;
        }

        private void SampleControllers(float sampleIntervalSec)
        {
            EnsureQuestDevices(forceRefresh: false);

            var hasLeft = TryReadHandPose(XRNode.LeftHand, ref _leftDevice, out var leftPosition, out var leftRotation);
            var hasRight = TryReadHandPose(XRNode.RightHand, ref _rightDevice, out var rightPosition, out var rightRotation);

            if (!hasLeft && !hasRight)
            {
                if (!_inputUnavailableLogged)
                {
                    _inputUnavailableLogged = true;
                    TrackSceneLifecycle(
                        "bilateral_input_missing",
                        BilateralMarkersReasonCodes.InputUnavailable,
                        new Dictionary<string, object>
                        {
                            { "elapsedSec", GetDurationSeconds() },
                        });
                }

                return;
            }

            _inputUnavailableLogged = false;
            var elapsed = GetDurationSeconds();

            if (_leftTraceTransform != null && hasLeft)
            {
                _leftTraceTransform.SetPositionAndRotation(leftPosition, leftRotation);
            }

            if (_rightTraceTransform != null && hasRight)
            {
                _rightTraceTransform.SetPositionAndRotation(rightPosition, rightRotation);
            }

            ProcessHandSample(
                _leftHandRuntime,
                hasLeft,
                leftPosition,
                IsHandActiveAtCurrentTime("LEFT"),
                elapsed,
                sampleIntervalSec);
            ProcessHandSample(
                _rightHandRuntime,
                hasRight,
                rightPosition,
                IsHandActiveAtCurrentTime("RIGHT"),
                elapsed,
                sampleIntervalSec);
        }

        private void ProcessHandSample(
            HandRuntime hand,
            bool hasPose,
            Vector3 worldPosition,
            bool handIsActive,
            float elapsedSec,
            float sampleIntervalSec)
        {
            if (hand == null || hand.points.Count <= 1)
            {
                return;
            }

            if (!hasPose)
            {
                return;
            }

            if (worldPosition.y < hand.motionRangeMinY)
            {
                hand.motionRangeMinY = worldPosition.y;
            }

            if (worldPosition.y > hand.motionRangeMaxY)
            {
                hand.motionRangeMaxY = worldPosition.y;
            }

            if (hand.hasPreviousPosition)
            {
                var speed = Vector3.Distance(hand.previousPosition, worldPosition) / Mathf.Max(0.001f, sampleIntervalSec);
                if (speed <= TempSimplePauseSpeedThresholdMps)
                {
                    hand.pauseSampleCount += 1f;
                }
            }

            hand.previousPosition = worldPosition;
            hand.hasPreviousPosition = true;

            var nearestIndex = FindNearestPathIndex(hand.points, worldPosition, hand.lastNearestIndex);
            var nearestPoint = hand.points[Mathf.Clamp(nearestIndex, 0, hand.points.Count - 1)];
            var deviationMeters = Vector3.Distance(worldPosition, nearestPoint);
            if (handIsActive && nearestIndex > hand.lastNearestIndex)
            {
                hand.lastNearestIndex = nearestIndex;
            }

            var progress01 = hand.points.Count <= 1
                ? 0f
                : (float)hand.lastNearestIndex / (hand.points.Count - 1);
            var targetValid = handIsActive && deviationMeters <= _effectiveTunnelWidthMeters;
            var reasonCode = targetValid
                ? "PATH_PROGRESS"
                : (handIsActive
                    ? "PATH_DEVIATION_EXCEEDED"
                    : BilateralMarkersReasonCodes.HandPhaseLocked);

            hand.progress01 = Mathf.Max(hand.progress01, progress01);
            hand.deviationMeters = deviationMeters;
            hand.sampleCount++;
            if (targetValid)
            {
                hand.validSampleCount++;
            }
            else
            {
                hand.invalidSampleCount++;
            }

            if (_emitDetailedSampleEvents && _posePathProbe != null)
            {
                var sample = new PosePathProbe.PosePathSample
                {
                    gameId = GameId,
                    inputHand = hand.handToken,
                    inputSource = "QUEST_CONTROLLER",
                    inputControl = "DEVICE_POSE",
                    sourceComponent = nameof(BilateralMarkersSceneGameController),
                    targetId = hand.targetId,
                    targetName = hand.targetName,
                    pathProgress01 = progress01,
                    pathCoverage01 = hand.progress01,
                    pathDeviation = deviationMeters,
                    tolerance = _effectiveTunnelWidthMeters,
                    inputValue = progress01,
                    targetValid = targetValid,
                    reasonCode = reasonCode,
                };

                if (targetValid)
                {
                    _posePathProbe.RecordFollowTick(sample);
                }
                else
                {
                    _posePathProbe.RecordFollowDeviation(sample);
                }

                if (targetValid &&
                    !hand.completionMarked &&
                    hand.progress01 >= 0.999f)
                {
                    hand.completionMarked = true;
                    sample.pathProgress01 = 1f;
                    sample.pathCoverage01 = 1f;
                    sample.reasonCode = "PATH_FOLLOW_COMPLETED";
                    _posePathProbe.RecordFollowCompleted(sample);
                }
            }

            if (!hand.startMarked &&
                handIsActive &&
                hand.progress01 >= TempSimpleStartProgressThreshold)
            {
                hand.startMarked = true;
                hand.firstMoveAtSec = elapsedSec;
                PublishDualHandStartProgress(hand);
            }
        }

        private void PublishDualHandStartProgress(HandRuntime markedHand)
        {
            if (_dualHandProbe == null || markedHand == null)
            {
                return;
            }

            var sample = BuildDualHandSample(markedHand.handToken, false, 0f);
            if (string.Equals(markedHand.handToken, "LEFT", StringComparison.Ordinal))
            {
                _dualHandProbe.RecordLeftMarked(sample);
            }
            else
            {
                _dualHandProbe.RecordRightMarked(sample);
            }

            if (!_leftHandRuntime.startMarked || !_rightHandRuntime.startMarked || _syncResolved)
            {
                return;
            }

            var rawDeltaMs = Mathf.Abs(_leftHandRuntime.firstMoveAtSec - _rightHandRuntime.firstMoveAtSec) * 1000f;
            _syncDeltaMs = rawDeltaMs;
            var expectedDeltaMs = ResolveExpectedSyncDeltaMs();
            var syncSatisfied = Mathf.Abs(rawDeltaMs - expectedDeltaMs) <= _settings.syncWindowMs;
            _syncResolved = true;
            _syncWithinWindow = syncSatisfied;

            var syncReasonCode = syncSatisfied
                ? (_settings.level == BilateralLevel.Alternating
                    ? BilateralMarkersReasonCodes.AlternationSyncConfirmed
                    : "DUAL_HAND_SYNC_CONFIRMED")
                : (_settings.level == BilateralLevel.Alternating
                    ? BilateralMarkersReasonCodes.AlternationOffsetMismatch
                    : "DUAL_HAND_SYNC_WINDOW_EXCEEDED");

            var syncSample = BuildDualHandSample("BOTH_HANDS", syncSatisfied, rawDeltaMs);
            syncSample.reasonCode = syncReasonCode;
            syncSample.syncWindowMs = _settings.syncWindowMs;
            syncSample.targetValid = syncSatisfied;

            if (syncSatisfied)
            {
                _dualHandProbe.RecordSynchronizedMarked(syncSample);
            }
            else
            {
                _syncOutOfWindowCount++;
                _dualHandProbe.RecordOutOfSync(syncSample);
            }

            TrackSceneLifecycle(
                "bilateral_sync_resolved",
                syncReasonCode,
                new Dictionary<string, object>
                {
                    { "syncDeltaMs", rawDeltaMs },
                    { "expectedSyncDeltaMs", expectedDeltaMs },
                    { "syncWindowMs", _settings.syncWindowMs },
                    { "syncSatisfied", syncSatisfied },
                });
        }

        private DualHandProbe.DualHandSample BuildDualHandSample(
            string inputHand,
            bool syncSatisfied,
            float syncDeltaMs)
        {
            var sample = new DualHandProbe.DualHandSample
            {
                gameId = GameId,
                inputHand = string.IsNullOrWhiteSpace(inputHand) ? "BOTH_HANDS" : inputHand,
                inputSource = "QUEST_CONTROLLER",
                inputControl = "DEVICE_POSE",
                sourceComponent = nameof(BilateralMarkersSceneGameController),
                leftTargetId = _leftHandRuntime.targetId,
                leftTargetName = _leftHandRuntime.targetName,
                rightTargetId = _rightHandRuntime.targetId,
                rightTargetName = _rightHandRuntime.targetName,
                leftMatched = _leftHandRuntime.startMarked,
                rightMatched = _rightHandRuntime.startMarked,
                syncDeltaMs = syncDeltaMs,
                syncWindowMs = _settings.syncWindowMs,
                inputValue = syncSatisfied ? 1f : 0f,
                targetValid = syncSatisfied,
                reasonCode = syncSatisfied ? "DUAL_HAND_SYNC_CONFIRMED" : "DUAL_HAND_WAITING_FOR_SECOND_HAND",
            };
            return sample;
        }

        private bool IsHandActiveAtCurrentTime(string handToken)
        {
            if (_settings.level != BilateralLevel.Alternating)
            {
                return true;
            }

            var dominantToken = _settings.dominantHandLeft ? "LEFT" : "RIGHT";
            if (string.Equals(handToken, dominantToken, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return GetDurationSeconds() >= _settings.alternationOffsetSec;
        }

        private void EmitTimelineBeatTicks()
        {
            if (_timelineProbe == null)
            {
                return;
            }

            var elapsedSec = GetDurationSeconds();
            var beatIntervalSec = 60f / Mathf.Max(1f, _settings.tempoBpm);
            while (_roundActive && elapsedSec >= _nextBeatAtSec)
            {
                _beatIndex++;
                var progress01 = _settings.durationSec <= 0f ? 0f : Mathf.Clamp01(elapsedSec / _settings.durationSec);
                var attentionScore = Mathf.Clamp01((ComputeAccuracy01(_leftHandRuntime) + ComputeAccuracy01(_rightHandRuntime)) * 0.5f);
                var sample = new TimelineProbe.TimelineSample
                {
                    gameId = GameId,
                    inputSource = "RHYTHM_GUIDE",
                    inputControl = "BPM",
                    sourceComponent = nameof(BilateralMarkersSceneGameController),
                    segmentId = "beat_" + _beatIndex.ToString(CultureInfo.InvariantCulture),
                    segmentName = "Beat " + _beatIndex.ToString(CultureInfo.InvariantCulture),
                    elapsedSec = elapsedSec,
                    requiredSec = _settings.durationSec,
                    progress01 = progress01,
                    attentionScore = attentionScore,
                    interrupted = false,
                    targetValid = true,
                    reasonCode = "TIMELINE_PROGRESS",
                };
                _timelineProbe.RecordSegmentTick(sample);
                _nextBeatAtSec += beatIntervalSec;
            }
        }

        private void TryApplyTemporaryAdaptiveAssist()
        {
            if (!_settings.adaptiveAssistEnabled || _tempAdaptiveApplied)
            {
                return;
            }

            if (GetDurationSeconds() < 4f)
            {
                return;
            }

            var totalSamples = _leftHandRuntime.sampleCount + _rightHandRuntime.sampleCount;
            var totalInvalid = _leftHandRuntime.invalidSampleCount + _rightHandRuntime.invalidSampleCount;
            var invalidRatio = totalSamples <= 0 ? 0f : (float)totalInvalid / totalSamples;

            if (invalidRatio < TempSimpleAdaptiveInvalidRatioThreshold)
            {
                return;
            }

            var previousTunnel = _effectiveTunnelWidthMeters;
            _effectiveTunnelWidthMeters = Mathf.Clamp(
                _effectiveTunnelWidthMeters * TempSimpleAdaptiveTunnelScale,
                _settings.tunnelWidthMeters,
                0.24f);
            _tempAdaptiveApplied = true;

            TrackSceneLifecycle(
                "bilateral_adaptive_temp_applied",
                BilateralMarkersReasonCodes.TempAdaptiveApplied,
                new Dictionary<string, object>
                {
                    { "previousTunnelWidthM", previousTunnel },
                    { "nextTunnelWidthM", _effectiveTunnelWidthMeters },
                    { "invalidRatio", invalidRatio },
                    { "tempSimpleAdaptive", true },
                });
        }

        private void TryFinalizeRoundByProgressOrTimeout()
        {
            if (!_roundActive || _roundFinalizing)
            {
                return;
            }

            var elapsedSec = GetDurationSeconds();
            if (elapsedSec >= _settings.durationSec)
            {
                FinalizeRound(
                    GameContracts.GameStopReason.Timeout,
                    BilateralMarkersReasonCodes.RoundTimeout,
                    completed: false);
                return;
            }

            if (_leftHandRuntime.progress01 >= 0.999f &&
                _rightHandRuntime.progress01 >= 0.999f &&
                _leftHandRuntime.startMarked &&
                _rightHandRuntime.startMarked)
            {
                FinalizeRound(
                    GameContracts.GameStopReason.Completed,
                    BilateralMarkersReasonCodes.RoundCompleted,
                    completed: true);
            }
        }

        private void FinalizeRound(
            GameContracts.GameStopReason stopReason,
            string reasonCode,
            bool completed)
        {
            if (_roundFinalizing)
            {
                return;
            }

            _roundFinalizing = true;
            _roundActive = false;
            EmitTimelineRoundTerminal(reasonCode, completed);
            CaptureRoundSummary(reasonCode, completed);
            PublishTraceIfNeeded(reasonCode);

            TrackEvent(
                "bilateral_round_summary",
                new Dictionary<string, object>
                {
                    { "reasonCode", reasonCode },
                    { "completed", completed },
                    { "durationSec", _lastRoundSummary.durationSec },
                    { "leftAccuracy01", _lastRoundSummary.leftAccuracy01 },
                    { "rightAccuracy01", _lastRoundSummary.rightAccuracy01 },
                    { "leftSmoothness01", _lastRoundSummary.leftSmoothness01 },
                    { "rightSmoothness01", _lastRoundSummary.rightSmoothness01 },
                    { "leftAmplitudeM", _lastRoundSummary.leftAmplitudeM },
                    { "rightAmplitudeM", _lastRoundSummary.rightAmplitudeM },
                    { "syncDeltaMs", _lastRoundSummary.syncDeltaMs },
                    { "targetSyncDeltaMs", _lastRoundSummary.targetSyncDeltaMs },
                    { "syncWithinWindow", _lastRoundSummary.syncWithinWindow },
                    { "syncOutOfWindowCount", _lastRoundSummary.syncOutOfWindowCount },
                    { "adaptiveApplied", _lastRoundSummary.adaptiveApplied },
                    { "finalTunnelWidthM", _lastRoundSummary.finalTunnelWidthM },
                    { "level", _lastRoundSummary.level },
                    { "tempoBpm", _lastRoundSummary.tempoBpm },
                    { "tempSimpleMetrics", true },
                });

            StopGame(stopReason);
        }

        private void CaptureRoundSummary(string reasonCode, bool completed)
        {
            _lastRoundSummary = new RoundSummary
            {
                completed = completed,
                reasonCode = string.IsNullOrWhiteSpace(reasonCode) ? string.Empty : reasonCode.Trim(),
                durationSec = GetDurationSeconds(),
                leftAccuracy01 = ComputeAccuracy01(_leftHandRuntime),
                rightAccuracy01 = ComputeAccuracy01(_rightHandRuntime),
                leftSmoothness01 = ComputeSmoothness01(_leftHandRuntime),
                rightSmoothness01 = ComputeSmoothness01(_rightHandRuntime),
                leftAmplitudeM = ComputeAmplitude(_leftHandRuntime),
                rightAmplitudeM = ComputeAmplitude(_rightHandRuntime),
                syncDeltaMs = _syncDeltaMs < 0f ? 0f : _syncDeltaMs,
                targetSyncDeltaMs = _targetSyncDeltaMs,
                syncWithinWindow = _syncWithinWindow,
                syncOutOfWindowCount = _syncOutOfWindowCount,
                adaptiveApplied = _tempAdaptiveApplied,
                finalTunnelWidthM = _effectiveTunnelWidthMeters,
                level = (int)_settings.level,
                tempoBpm = _settings.tempoBpm,
            };
        }

        private void EmitTimelineRoundTerminal(string reasonCode, bool completed)
        {
            if (_timelineProbe == null)
            {
                return;
            }

            var elapsedSec = GetDurationSeconds();
            var sample = new TimelineProbe.TimelineSample
            {
                gameId = GameId,
                inputSource = "RHYTHM_GUIDE",
                inputControl = "BPM",
                sourceComponent = nameof(BilateralMarkersSceneGameController),
                segmentId = "round",
                segmentName = "Bilateral Round",
                elapsedSec = elapsedSec,
                requiredSec = _settings.durationSec,
                progress01 = _settings.durationSec <= 0f ? 0f : Mathf.Clamp01(elapsedSec / _settings.durationSec),
                attentionScore = Mathf.Clamp01((ComputeAccuracy01(_leftHandRuntime) + ComputeAccuracy01(_rightHandRuntime)) * 0.5f),
                interrupted = !completed,
                targetValid = completed,
                reasonCode = string.IsNullOrWhiteSpace(reasonCode)
                    ? (completed ? "TIMELINE_SEGMENT_WATCHED" : "TIMELINE_INTERRUPTED")
                    : reasonCode,
            };

            if (completed)
            {
                _timelineProbe.RecordSegmentCompleted(sample);
            }
            else
            {
                _timelineProbe.RecordSegmentInterrupted(sample);
            }
        }

        private void PublishTraceIfNeeded(string reasonCode)
        {
            if (_tracePublished || !_enableTraceRecorder || _motionTraceRecorder == null)
            {
                return;
            }

            _tracePublished = _motionTraceRecorder.StopAndPublish(
                string.IsNullOrWhiteSpace(reasonCode) ? BilateralMarkersReasonCodes.TracePublished : reasonCode);
        }

        private void BeginTraceIfNeeded()
        {
            if (!_enableTraceRecorder || _motionTraceRecorder == null)
            {
                return;
            }

            EnsureAnchorCamera();
            EnsureTraceTargets();
            _motionTraceRecorder.ConfigureTrackedTransforms(
                _anchorCamera == null ? null : _anchorCamera.transform,
                _leftTraceTransform,
                _rightTraceTransform);
            _motionTraceRecorder.BeginTrace(
                GameId,
                "bilateral_round",
                Context?.Session?.SessionId ?? string.Empty);
        }

        private void EnsureTraceTargets()
        {
            if (_leftTraceTransform == null)
            {
                var leftTarget = new GameObject("BilateralTraceLeftHand");
                leftTarget.transform.SetParent(transform, worldPositionStays: false);
                _leftTraceTransform = leftTarget.transform;
            }

            if (_rightTraceTransform == null)
            {
                var rightTarget = new GameObject("BilateralTraceRightHand");
                rightTarget.transform.SetParent(transform, worldPositionStays: false);
                _rightTraceTransform = rightTarget.transform;
            }
        }

        private void CleanupTraceTargets()
        {
            if (_leftTraceTransform != null)
            {
                Destroy(_leftTraceTransform.gameObject);
                _leftTraceTransform = null;
            }

            if (_rightTraceTransform != null)
            {
                Destroy(_rightTraceTransform.gameObject);
                _rightTraceTransform = null;
            }
        }

        private void BuildPathsAndVisuals()
        {
            _leftHandRuntime.Reset();
            _rightHandRuntime.Reset();

            ResolveAnchorFrame(out var anchor, out var anchorRight, out var anchorUp);
            ResolvePathSpecs(out var leftSpec, out var rightSpec);

            var sampleCount = Mathf.Clamp(_pathSampleCount, 24, 240);
            var halfSeparation = Mathf.Max(0.08f, _pathSeparationMeters * 0.5f);
            var leftCenter = anchor - anchorRight * halfSeparation;
            var rightCenter = anchor + anchorRight * halfSeparation;
            AppendPathPoints(_leftHandRuntime.points, leftSpec, leftCenter, anchorRight, anchorUp, sampleCount);
            AppendPathPoints(_rightHandRuntime.points, rightSpec, rightCenter, anchorRight, anchorUp, sampleCount);
            EnsureVisuals();
            UpdatePathRenderer(_leftPathRenderer, _leftHandRuntime.points, _leftPathColor);
            UpdatePathRenderer(_rightPathRenderer, _rightHandRuntime.points, _rightPathColor);
        }

        private void ResolveAnchorFrame(out Vector3 anchor, out Vector3 anchorRight, out Vector3 anchorUp)
        {
            EnsureAnchorCamera();
            var cameraTransform = _anchorCamera == null ? transform : _anchorCamera.transform;
            var forwardFlat = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
            if (forwardFlat.sqrMagnitude <= 0.0001f)
            {
                forwardFlat = Vector3.forward;
            }

            forwardFlat.Normalize();
            anchorRight = Vector3.Cross(Vector3.up, forwardFlat).normalized;
            if (anchorRight.sqrMagnitude <= 0.0001f)
            {
                anchorRight = Vector3.right;
            }

            anchorUp = Vector3.up;
            anchor = cameraTransform.position +
                     forwardFlat * Mathf.Max(0.35f, _pathDistanceMeters) +
                     anchorUp * _pathVerticalOffsetMeters;
            _trackingOrigin = cameraTransform.parent;
        }

        private void ResolvePathSpecs(out PathSpec leftSpec, out PathSpec rightSpec)
        {
            leftSpec = default;
            rightSpec = default;

            switch (_settings.level)
            {
                case BilateralLevel.Symmetry:
                    leftSpec.pattern = _settings.primaryPattern;
                    rightSpec.pattern = _settings.primaryPattern;
                    break;
                case BilateralLevel.Mirror:
                    leftSpec.pattern = _settings.primaryPattern;
                    leftSpec.mirrorX = true;
                    rightSpec.pattern = _settings.primaryPattern;
                    rightSpec.reverse = true;
                    break;
                case BilateralLevel.Rotational:
                    leftSpec.pattern = _settings.primaryPattern;
                    rightSpec.pattern = _settings.primaryPattern;
                    rightSpec.reverse = true;
                    rightSpec.invertY = true;
                    break;
                case BilateralLevel.Asymmetry:
                    leftSpec.pattern = _settings.leftPattern;
                    rightSpec.pattern = _settings.rightPattern;
                    break;
                case BilateralLevel.Alternating:
                    leftSpec.pattern = _settings.primaryPattern;
                    leftSpec.mirrorX = true;
                    rightSpec.pattern = _settings.primaryPattern;
                    break;
                default:
                    leftSpec.pattern = _settings.primaryPattern;
                    rightSpec.pattern = _settings.primaryPattern;
                    break;
            }
        }

        private void AppendPathPoints(
            List<Vector3> target,
            PathSpec spec,
            Vector3 center,
            Vector3 anchorRight,
            Vector3 anchorUp,
            int sampleCount)
        {
            if (target == null)
            {
                return;
            }

            target.Clear();
            var safeSamples = Mathf.Clamp(sampleCount, 4, 512);
            for (var i = 0; i < safeSamples; i++)
            {
                var t = safeSamples <= 1 ? 0f : (float)i / (safeSamples - 1);
                if (spec.reverse)
                {
                    t = 1f - t;
                }

                var local = EvaluatePattern(spec.pattern, t);
                if (spec.mirrorX)
                {
                    local.x *= -1f;
                }

                if (spec.invertY)
                {
                    local.y *= -1f;
                }

                local *= _settings.pathScaleMeters;
                var worldPoint = center + anchorRight * local.x + anchorUp * local.y;
                target.Add(worldPoint);
            }
        }

        private static Vector2 EvaluatePattern(BilateralPattern pattern, float t)
        {
            var clamped = Mathf.Clamp01(t);
            switch (pattern)
            {
                case BilateralPattern.Spiral:
                {
                    var angle = clamped * Mathf.PI * 4f;
                    var radius = Mathf.Lerp(0.05f, 0.75f, clamped);
                    return new Vector2(
                        Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius);
                }
                case BilateralPattern.FigureEight:
                    return new Vector2(
                        Mathf.Sin(clamped * Mathf.PI * 2f),
                        Mathf.Sin(clamped * Mathf.PI * 4f) * 0.55f);
                case BilateralPattern.Zigzag:
                    return new Vector2(
                        Mathf.Lerp(-1f, 1f, clamped),
                        Mathf.Lerp(-0.65f, 0.65f, Mathf.PingPong(clamped * 4f, 1f)));
                case BilateralPattern.Arc:
                    return new Vector2(
                        Mathf.Lerp(-1f, 1f, clamped),
                        Mathf.Sin(clamped * Mathf.PI) * 0.75f);
                case BilateralPattern.Wave:
                default:
                    return new Vector2(
                        Mathf.Lerp(-1f, 1f, clamped),
                        Mathf.Sin(clamped * Mathf.PI * 2f) * 0.45f);
            }
        }

        private int FindNearestPathIndex(List<Vector3> pathPoints, Vector3 currentPosition, int previousIndex)
        {
            if (pathPoints == null || pathPoints.Count <= 0)
            {
                return 0;
            }

            var safePrevious = Mathf.Clamp(previousIndex, 0, pathPoints.Count - 1);
            var minIndex = Mathf.Max(0, safePrevious - 4);
            var maxIndex = Mathf.Min(pathPoints.Count - 1, safePrevious + 14);
            var bestIndex = safePrevious;
            var bestDistance = float.MaxValue;
            for (var i = minIndex; i <= maxIndex; i++)
            {
                var distance = Vector3.Distance(pathPoints[i], currentPosition);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private void EnsureVisuals()
        {
            if (_runtimeVisualRoot == null)
            {
                _runtimeVisualRoot = new GameObject("BilateralMarkersRuntimeVisuals");
                _runtimeVisualRoot.transform.SetParent(transform, worldPositionStays: false);
            }

            if (_pathMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    _pathMaterial = new Material(shader);
                }
            }

            if (_leftPathRenderer == null)
            {
                _leftPathRenderer = CreatePathRenderer("LeftPath");
            }

            if (_rightPathRenderer == null)
            {
                _rightPathRenderer = CreatePathRenderer("RightPath");
            }
        }

        private LineRenderer CreatePathRenderer(string objectName)
        {
            var lineObject = new GameObject(string.IsNullOrWhiteSpace(objectName) ? "Path" : objectName);
            lineObject.transform.SetParent(_runtimeVisualRoot.transform, worldPositionStays: false);
            var renderer = lineObject.AddComponent<LineRenderer>();
            renderer.useWorldSpace = true;
            renderer.loop = false;
            renderer.numCornerVertices = 6;
            renderer.numCapVertices = 6;
            renderer.alignment = LineAlignment.View;
            renderer.textureMode = LineTextureMode.Stretch;
            renderer.widthCurve = AnimationCurve.Constant(0f, 1f, Mathf.Max(0.01f, _settings.tunnelWidthMeters * 2f));
            renderer.material = _pathMaterial;
            return renderer;
        }

        private void UpdatePathRenderer(LineRenderer renderer, List<Vector3> points, Color color)
        {
            if (renderer == null || points == null || points.Count <= 0)
            {
                return;
            }

            renderer.positionCount = points.Count;
            renderer.SetPositions(points.ToArray());
            renderer.startColor = color;
            renderer.endColor = color;
            renderer.widthCurve = AnimationCurve.Constant(0f, 1f, Mathf.Max(0.01f, _effectiveTunnelWidthMeters * 2f));
        }

        private void UpdatePathVisualFeedback()
        {
            if (_leftPathRenderer == null || _rightPathRenderer == null)
            {
                return;
            }

            var leftAccuracy = ComputeAccuracy01(_leftHandRuntime);
            var rightAccuracy = ComputeAccuracy01(_rightHandRuntime);
            _leftPathRenderer.startColor = Color.Lerp(Color.red, _leftPathColor, leftAccuracy);
            _leftPathRenderer.endColor = Color.Lerp(Color.red, _leftPathColor, leftAccuracy);
            _rightPathRenderer.startColor = Color.Lerp(Color.red, _rightPathColor, rightAccuracy);
            _rightPathRenderer.endColor = Color.Lerp(Color.red, _rightPathColor, rightAccuracy);
            var width = Mathf.Max(0.01f, _effectiveTunnelWidthMeters * 2f);
            _leftPathRenderer.widthCurve = AnimationCurve.Constant(0f, 1f, width);
            _rightPathRenderer.widthCurve = AnimationCurve.Constant(0f, 1f, width);
        }

        private void CleanupVisuals()
        {
            if (_leftPathRenderer != null)
            {
                Destroy(_leftPathRenderer.gameObject);
                _leftPathRenderer = null;
            }

            if (_rightPathRenderer != null)
            {
                Destroy(_rightPathRenderer.gameObject);
                _rightPathRenderer = null;
            }

            if (_runtimeVisualRoot != null)
            {
                Destroy(_runtimeVisualRoot);
                _runtimeVisualRoot = null;
            }
        }

        private float ResolveExpectedSyncDeltaMs()
        {
            return _settings.level == BilateralLevel.Alternating
                ? Mathf.Max(0f, _settings.alternationOffsetSec * 1000f)
                : 0f;
        }

        private static float ComputeAccuracy01(HandRuntime hand)
        {
            if (hand == null || hand.sampleCount <= 0)
            {
                return 0f;
            }

            return Mathf.Clamp01((float)hand.validSampleCount / hand.sampleCount);
        }

        private static float ComputeSmoothness01(HandRuntime hand)
        {
            if (hand == null || hand.sampleCount <= 0)
            {
                return 0f;
            }

            var pauseRatio = hand.pauseSampleCount / Mathf.Max(1f, hand.sampleCount);
            return Mathf.Clamp01(1f - pauseRatio);
        }

        private static float ComputeAmplitude(HandRuntime hand)
        {
            if (hand == null ||
                hand.motionRangeMinY == float.MaxValue ||
                hand.motionRangeMaxY == float.MinValue)
            {
                return 0f;
            }

            return Mathf.Max(0f, hand.motionRangeMaxY - hand.motionRangeMinY);
        }

        private static BilateralSettings BuildSettings(
            BilateralConfigPayload payload,
            out string reasonCode)
        {
            var settings = BilateralSettings.CreateDefault();
            var clamped = false;

            if (payload == null)
            {
                reasonCode = BilateralMarkersReasonCodes.ConfigFallbackUsed;
                return settings;
            }

            var level = Mathf.Clamp(payload.level, 0, 4);
            if (level != payload.level)
            {
                clamped = true;
            }

            settings.level = (BilateralLevel)level;
            settings.primaryPattern = ParsePattern(payload.pattern, BilateralPattern.Wave);
            settings.leftPattern = ParsePattern(payload.leftPattern, BilateralPattern.Spiral);
            settings.rightPattern = ParsePattern(payload.rightPattern, BilateralPattern.Wave);
            settings.tempoBpm = ClampFloat(payload.tempoBpm, 40f, 120f, TempSimpleDefaultTempoBpm, ref clamped);
            settings.durationSec = ClampFloat(payload.durationSec, 15f, 90f, TempSimpleDefaultDurationSec, ref clamped);
            settings.tunnelWidthMeters = ClampFloat(payload.tunnelWidthM, 0.05f, 0.22f, TempSimpleDefaultTunnelWidthM, ref clamped);
            settings.pathScaleMeters = ClampFloat(payload.pathScaleM, 0.12f, 0.45f, TempSimpleDefaultPathScaleM, ref clamped);
            settings.syncWindowMs = ClampFloat(payload.syncWindowMs, 120f, 700f, TempSimpleDefaultSyncWindowMs, ref clamped);
            settings.alternationOffsetSec = ClampFloat(payload.alternationOffsetSec, 0.15f, 2f, TempSimpleDefaultAlternationOffsetSec, ref clamped);
            settings.dominantHandLeft = string.Equals(
                (payload.dominantHand ?? string.Empty).Trim(),
                "left",
                StringComparison.OrdinalIgnoreCase);
            settings.adaptiveAssistEnabled = payload.adaptiveAssistEnabled;

            reasonCode = clamped
                ? BilateralMarkersReasonCodes.ConfigClamped
                : BilateralMarkersReasonCodes.ConfigApplied;
            return settings;
        }

        private static BilateralPattern ParsePattern(string rawValue, BilateralPattern fallback)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return fallback;
            }

            switch (rawValue.Trim().ToLowerInvariant())
            {
                case "wave":
                    return BilateralPattern.Wave;
                case "spiral":
                    return BilateralPattern.Spiral;
                case "figure_eight":
                case "figure8":
                case "eight":
                    return BilateralPattern.FigureEight;
                case "zigzag":
                    return BilateralPattern.Zigzag;
                case "arc":
                    return BilateralPattern.Arc;
                default:
                    return fallback;
            }
        }

        private static float ClampFloat(float value, float min, float max, float fallback, ref bool clamped)
        {
            var input = value > 0f ? value : fallback;
            var result = Mathf.Clamp(input, min, max);
            if (Mathf.Abs(result - value) > 0.0001f && value > 0f)
            {
                clamped = true;
            }

            if (value <= 0f && Mathf.Abs(result - fallback) > 0.0001f)
            {
                clamped = true;
            }

            return result;
        }

        private static string ResolveStopReasonCode(GameContracts.GameStopReason reason)
        {
            switch (reason)
            {
                case GameContracts.GameStopReason.Completed:
                    return BilateralMarkersReasonCodes.RoundCompleted;
                case GameContracts.GameStopReason.Timeout:
                    return BilateralMarkersReasonCodes.RoundTimeout;
                default:
                    return BilateralMarkersReasonCodes.RoundStopped;
            }
        }

        private void OnGUI()
        {
            if (!_showDebugHud)
            {
                return;
            }

            var label = "Bilateral Markers\n" +
                        "Level: " + (int)_settings.level + "\n" +
                        "Elapsed: " + GetDurationSeconds().ToString("F2", CultureInfo.InvariantCulture) + " s\n" +
                        "L progress: " + (_leftHandRuntime.progress01 * 100f).ToString("F1", CultureInfo.InvariantCulture) + "%\n" +
                        "R progress: " + (_rightHandRuntime.progress01 * 100f).ToString("F1", CultureInfo.InvariantCulture) + "%\n" +
                        "Sync delta: " + (_syncDeltaMs < 0f ? "-" : _syncDeltaMs.ToString("F1", CultureInfo.InvariantCulture)) + " ms";
            var area = new Rect(12f, 12f, 320f, 138f);
            GUI.Box(area, GUIContent.none);
            GUI.Label(new Rect(20f, 18f, 305f, 122f), label);
        }
    }
}
