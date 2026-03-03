using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using TheraplyCore.Interactions;

namespace TheraplyExamples
{
    public enum DemoCubeLevelMode
    {
        Basic = 0,
        AlternateTwoColors = 1,
        RandomTargetColor = 2,
    }

    public enum DemoCubeColor
    {
        Any = -1,
        Red = 0,
        Blue = 1,
        Green = 2,
        Yellow = 3,
        Magenta = 4,
        Cyan = 5,
    }

    [DisallowMultipleComponent]
    public sealed class DemoCubeGameModule : GameModuleBase,
        GameContracts.IDefaultGameConfigProvider,
        GameContracts.IStartCommandConfigProvider
    {
        [Header("Runtime")]
        [SerializeField] private Camera _targetCamera;
        [SerializeField] private float _spawnDepth = 3.5f;
        [SerializeField] private float _viewportPadding = 0.08f;
        [SerializeField] private float _cubeScale = 0.35f;
        [SerializeField] private bool _showOverlayHud = true;
        [SerializeField] private bool _anchorPlayfieldToSessionStart = true;

        [Header("Visuals")]
        [SerializeField] private Shader _cubeFallbackShader;

        [Header("Save/Resume")]
        [SerializeField] private bool _enableSaveResume = true;
        [SerializeField] private string _saveFolder = "session_resilience";
        [SerializeField] private string _saveFilePrefix = "demo_cube_state_";
        [SerializeField] private float _periodicSaveIntervalSec = 0.5f;
        [SerializeField] private bool _logSaveResume = false;

        private readonly List<CubeRuntimeData> _cubes = new List<CubeRuntimeData>();
        private readonly DemoCubeColor[] _randomPalette =
        {
            DemoCubeColor.Red,
            DemoCubeColor.Blue,
            DemoCubeColor.Green,
            DemoCubeColor.Yellow,
            DemoCubeColor.Magenta,
            DemoCubeColor.Cyan,
        };

        private DemoCubeGameConfig _activeConfig;
        private int _spawnedCount;
        private int _clickedCount;
        private int _wrongClicks;
        private int _totalClickAttempts;
        private int _targetColorChanges;
        private int _nextCubeId;
        private int _alternateExpectedIndex;
        private DemoCubeColor _randomTargetColor = DemoCubeColor.Red;
        private bool _completionHandled;
        private float _lastHitElapsedSec = -1f;
        private float _bestTimeSec = -1f;
        private string _bestTimeKey = string.Empty;
        private string _saveStatePath = string.Empty;
        private DemoCubeSavedState _pendingRestoreState;
        private float _lastSavedAtRealtime = -1f;
        private float _restoredDurationOffsetSec = 0f;
        private bool _playfieldAnchorReady;
        private Vector3 _playfieldCenterWorld;
        private Vector3 _playfieldRightWorld;
        private Vector3 _playfieldUpWorld;
        private float _playfieldHalfWidthWorld;
        private float _playfieldHalfHeightWorld;
        private Material _runtimeCubeMaterial;
        private bool _gameplayFloorMaterialEnsured;
        private bool _loggedRuntimeShaderSelection;

        public override string GameId => DemoCubeGameConfig.DefaultGameId;

        public GameContracts.IGameConfig CreateDefaultConfig()
        {
            return DemoCubeGameConfig.CreateDefault(_cubeScale);
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

            var previous = previousConfig as DemoCubeGameConfig;
            resolvedConfig = DemoCubeGameConfig.CreateFromStartCommand(command, previous, _cubeScale);
            reasonCode = string.Empty;
            return true;
        }

        public override void Initialize(GameContracts.IGameConfig config, GameContracts.IGameContext context)
        {
            _activeConfig = config as DemoCubeGameConfig ?? DemoCubeGameConfig.CreateDefault(_cubeScale);
            EnsureCamera();
            ClearActiveCubes();
            _saveStatePath = BuildSaveStatePath(context == null ? string.Empty : context.Session?.SessionId);

            _spawnedCount = 0;
            _clickedCount = 0;
            _wrongClicks = 0;
            _totalClickAttempts = 0;
            _targetColorChanges = 0;
            _nextCubeId = 1;
            _alternateExpectedIndex = 0;
            _randomTargetColor = DemoCubeColor.Red;
            _completionHandled = false;
            _lastHitElapsedSec = -1f;
            _lastSavedAtRealtime = -1f;
            _restoredDurationOffsetSec = 0f;
            _pendingRestoreState = null;
            _playfieldAnchorReady = false;
            _gameplayFloorMaterialEnsured = false;

            if (_enableSaveResume && !_activeConfig.ResumeFromSaved)
            {
                TryDeleteSavedState();
            }

            _bestTimeKey = BuildBestTimeKey(_activeConfig.LevelMode);
            _bestTimeSec = PlayerPrefs.HasKey(_bestTimeKey)
                ? Mathf.Max(0f, PlayerPrefs.GetFloat(_bestTimeKey, 0f))
                : -1f;

            if (_enableSaveResume && _activeConfig.ResumeFromSaved)
            {
                if (!TryLoadSavedState(out _pendingRestoreState))
                {
                    _pendingRestoreState = null;
                }
            }

            base.Initialize(_activeConfig, context);

            TrackEvent("demo_cube_config", new Dictionary<string, object>
            {
                { "cubeCount", _activeConfig.CubeCount },
                { "cubeSpeed", _activeConfig.CubeSpeed },
                { "levelMode", _activeConfig.LevelMode.ToString() },
                { "resumeFromSaved", _activeConfig.ResumeFromSaved },
                { "saveStateLoaded", _pendingRestoreState != null },
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

            var wasPaused = State == GameContracts.GameState.Paused;
            base.StartGame();

            if (State != GameContracts.GameState.Playing)
            {
                return;
            }

            if (!wasPaused)
            {
                CapturePlayfieldAnchor();

                if (!TryRestoreSavedStateIfRequested())
                {
                    SpawnInitialCubes();
                    RefreshTargetColor(forceRandomRefresh: true);
                }

                UpdatePointerTargetIndicator();
                PersistSavedState("START_GAME");
            }
        }

        public override void PauseGame()
        {
            var wasPlaying = State == GameContracts.GameState.Playing;
            base.PauseGame();

            if (wasPlaying && State == GameContracts.GameState.Paused)
            {
                PersistSavedState("PAUSE_GAME");
            }
        }

        public override void ResumeGame()
        {
            var wasPaused = State == GameContracts.GameState.Paused;
            base.ResumeGame();

            if (wasPaused && State == GameContracts.GameState.Playing)
            {
                PersistSavedState("RESUME_GAME");
            }
        }

        public override void StopGame(GameContracts.GameStopReason reason)
        {
            if (State == GameContracts.GameState.Completed || State == GameContracts.GameState.Failed)
            {
                return;
            }

            if (reason == GameContracts.GameStopReason.Completed)
            {
                TryDeleteSavedState();
            }
            else
            {
                PersistSavedState("STOP_GAME:" + reason);
            }

            base.StopGame(reason);
            ClearActiveCubes();
            _pendingRestoreState = null;
            ClearPointerTargetIndicator();
        }

        public override GameContracts.IGameResult BuildResult()
        {
            var metrics = new Dictionary<string, object>
            {
                { "cubeCount", _spawnedCount },
                { "clickedCount", _clickedCount },
                { "wrongClicks", _wrongClicks },
                { "totalClickAttempts", _totalClickAttempts },
                { "levelMode", _activeConfig == null ? string.Empty : _activeConfig.LevelMode.ToString() },
                { "targetColorChanges", _targetColorChanges },
                { "bestTimeSec", _bestTimeSec },
            };

            return new GameResult(
                GameId,
                State == GameContracts.GameState.Completed,
                GetSessionDurationSeconds(),
                metrics);
        }

        private void Update()
        {
            if (State != GameContracts.GameState.Playing)
            {
                return;
            }

            EnsureCamera();
            if (_activeConfig == null)
            {
                return;
            }

            if (!_gameplayFloorMaterialEnsured)
            {
                _gameplayFloorMaterialEnsured = EnsureGameplayFloorMaterial();
            }

            if (_anchorPlayfieldToSessionStart && !_playfieldAnchorReady)
            {
                CapturePlayfieldAnchor();
            }

            var dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            var minBound = Mathf.Clamp01(_viewportPadding);
            var maxBound = Mathf.Clamp01(1f - _viewportPadding);

            for (var i = 0; i < _cubes.Count; i++)
            {
                var cube = _cubes[i];
                if (cube == null || cube.clicked || cube.instance == null)
                {
                    continue;
                }

                cube.viewportPos += cube.direction * (_activeConfig.CubeSpeed * dt);

                if (cube.viewportPos.x <= minBound)
                {
                    cube.viewportPos.x = minBound;
                    cube.direction.x = Mathf.Abs(cube.direction.x);
                }
                else if (cube.viewportPos.x >= maxBound)
                {
                    cube.viewportPos.x = maxBound;
                    cube.direction.x = -Mathf.Abs(cube.direction.x);
                }

                if (cube.viewportPos.y <= minBound)
                {
                    cube.viewportPos.y = minBound;
                    cube.direction.y = Mathf.Abs(cube.direction.y);
                }
                else if (cube.viewportPos.y >= maxBound)
                {
                    cube.viewportPos.y = maxBound;
                    cube.direction.y = -Mathf.Abs(cube.direction.y);
                }

                cube.instance.transform.position = ViewportToWorld(cube.viewportPos);
                cube.instance.transform.Rotate(28f * dt, 42f * dt, 16f * dt, Space.Self);
            }

            if (_enableSaveResume &&
                _periodicSaveIntervalSec > 0f &&
                (_lastSavedAtRealtime < 0f || Time.realtimeSinceStartup - _lastSavedAtRealtime >= _periodicSaveIntervalSec))
            {
                PersistSavedState("PERIODIC");
            }
        }

        private void OnDisable()
        {
            if (State == GameContracts.GameState.Initialized ||
                State == GameContracts.GameState.Playing ||
                State == GameContracts.GameState.Paused)
            {
                PersistSavedState("ON_DISABLE");
            }

            ClearActiveCubes();
            ClearPointerTargetIndicator();
            if (_runtimeCubeMaterial != null)
            {
                Destroy(_runtimeCubeMaterial);
                _runtimeCubeMaterial = null;
            }
        }

        private void OnGUI()
        {
            if (!_showOverlayHud || _activeConfig == null)
            {
                return;
            }

            var remaining = Mathf.Max(0, _spawnedCount - _clickedCount);
            var bestText = _bestTimeSec < 0f ? "--" : _bestTimeSec.ToString("F2") + "s";
            var expectedColor = GetExpectedColor();

            var label =
                "Demo Cube Clicker\n" +
                "Mode: " + _activeConfig.LevelMode + "\n" +
                "Remaining: " + remaining + "/" + _spawnedCount + "\n" +
                "Time: " + GetSessionDurationSeconds().ToString("F2") + "s\n" +
                "Best: " + bestText + "\n" +
                "Target: " + FormatColorLabel(expectedColor);

            var area = new Rect(12f, 12f, 320f, 130f);
            GUI.Box(area, GUIContent.none);
            GUI.Label(new Rect(22f, 20f, 300f, 110f), label);
        }

        private void SpawnInitialCubes()
        {
            if (_activeConfig == null)
            {
                return;
            }

            ClearActiveCubes();

            var count = Mathf.Max(1, _activeConfig.CubeCount);
            for (var i = 0; i < count; i++)
            {
                var color = ResolveSpawnColor(i);
                SpawnCube(color);
            }

            _spawnedCount = _cubes.Count;
            _clickedCount = 0;
            _wrongClicks = 0;
            _totalClickAttempts = 0;
            _targetColorChanges = 0;
            _alternateExpectedIndex = 0;
            _completionHandled = false;
            _lastHitElapsedSec = -1f;
            _restoredDurationOffsetSec = 0f;

            TrackEvent("demo_cube_spawned", new Dictionary<string, object>
            {
                { "cubeCount", _spawnedCount },
                { "levelMode", _activeConfig.LevelMode.ToString() },
            });
        }

        private void SpawnCube(DemoCubeColor color)
        {
            var cubeId = _nextCubeId;
            var direction = UnityEngine.Random.insideUnitCircle;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector2.right;
            }

            direction.Normalize();

            var cubeState = new CubeRuntimeData
            {
                cubeId = cubeId,
                color = color,
                direction = direction,
                viewportPos = new Vector2(
                    UnityEngine.Random.Range(_viewportPadding, 1f - _viewportPadding),
                    UnityEngine.Random.Range(_viewportPadding, 1f - _viewportPadding)),
                clicked = false,
            };

            CreateCubeVisual(cubeState);
            _cubes.Add(cubeState);
            _nextCubeId++;
        }

        private void SpawnCubeFromSavedState(SavedCubeState savedCube)
        {
            if (savedCube == null)
            {
                return;
            }

            var parsedColor = ParseColor(savedCube.color, DemoCubeColor.Cyan);
            var viewport = new Vector2(
                Mathf.Clamp01(savedCube.viewportX),
                Mathf.Clamp01(savedCube.viewportY));
            var direction = new Vector2(savedCube.directionX, savedCube.directionY);
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector2.right;
            }

            direction.Normalize();

            var cubeState = new CubeRuntimeData
            {
                cubeId = Mathf.Max(1, savedCube.cubeId),
                color = parsedColor,
                direction = direction,
                viewportPos = viewport,
                clicked = savedCube.clicked,
            };

            if (!cubeState.clicked)
            {
                CreateCubeVisual(cubeState);
            }

            _cubes.Add(cubeState);
        }

        private void CreateCubeVisual(CubeRuntimeData cubeState)
        {
            if (cubeState == null || cubeState.clicked)
            {
                return;
            }

            var cubeObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cubeObject.name = "DemoCube_" + cubeState.cubeId;
            cubeObject.transform.SetParent(transform, worldPositionStays: true);
            cubeObject.transform.localScale = Vector3.one * _activeConfig.CubeScale;
            cubeObject.transform.position = ViewportToWorld(cubeState.viewportPos);

            var renderer = cubeObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                var runtimeMaterial = ResolveRuntimeCubeMaterial();
                if (runtimeMaterial != null)
                {
                    renderer.sharedMaterial = runtimeMaterial;
                }

                var cubeMaterial = renderer.material;
                if (cubeMaterial != null && cubeMaterial.HasProperty("_Color"))
                {
                    cubeMaterial.color = ToUnityColor(cubeState.color);
                }
            }

            var clickable = cubeObject.AddComponent<DemoCubeClickTarget>();
            clickable.Configure(cubeState.cubeId, HandleCubeClicked, ResolveCubeHoverFeedback);

            var validationZone = cubeObject.AddComponent<TargetValidationZone>();
            validationZone.Configure(
                cubeState.cubeId.ToString(CultureInfo.InvariantCulture),
                "DEMO_CUBE_TARGET",
                defaultValidTarget: true);

            cubeState.instance = cubeObject;
        }

        private void HandleCubeClicked(int cubeId, string inputSource)
        {
            if (State != GameContracts.GameState.Playing)
            {
                return;
            }

            var cube = FindCube(cubeId);
            if (cube == null || cube.clicked)
            {
                return;
            }

            _totalClickAttempts++;

            var expected = GetExpectedColor();
            if (!CanAcceptColor(cube.color, expected))
            {
                _wrongClicks++;
                PointerIndicatorService.Instance?.ReportHit(false);
                TrackEvent("demo_cube_wrong_click", new Dictionary<string, object>
                {
                    { "cubeId", cube.cubeId },
                    { "clickedColor", cube.color.ToString() },
                    { "expectedColor", FormatColorLabel(expected) },
                    { "wrongClicks", _wrongClicks },
                    { "inputSource", string.IsNullOrWhiteSpace(inputSource) ? "UNKNOWN" : inputSource },
                    { "targetId", cube.cubeId.ToString() },
                    { "targetName", cube.instance == null ? string.Empty : cube.instance.name },
                    { "targetValid", false },
                });
                PersistSavedState("WRONG_CLICK");
                return;
            }

            cube.clicked = true;
            _clickedCount++;
            PointerIndicatorService.Instance?.ReportHit(true);

            var elapsedSec = GetSessionDurationSeconds();
            var reactionSec = _lastHitElapsedSec < 0f
                ? elapsedSec
                : Mathf.Max(0f, elapsedSec - _lastHitElapsedSec);
            _lastHitElapsedSec = elapsedSec;

            TrackEvent("demo_cube_clicked", new Dictionary<string, object>
            {
                { "cubeId", cube.cubeId },
                { "clickedColor", cube.color.ToString() },
                { "reactionSec", reactionSec },
                { "clickedCount", _clickedCount },
                { "inputSource", string.IsNullOrWhiteSpace(inputSource) ? "UNKNOWN" : inputSource },
                { "targetId", cube.cubeId.ToString() },
                { "targetName", cube.instance == null ? string.Empty : cube.instance.name },
                { "targetValid", true },
            });

            if (cube.instance != null)
            {
                Destroy(cube.instance);
            }

            AdvanceTargetAfterCorrectClick();
            UpdatePointerTargetIndicator();
            PersistSavedState("CORRECT_CLICK");

            if (!_completionHandled && _clickedCount >= _spawnedCount)
            {
                CompleteRound();
            }
        }

        private void CompleteRound()
        {
            _completionHandled = true;

            var duration = GetSessionDurationSeconds();
            var previousBest = _bestTimeSec;
            var isBestImproved = _bestTimeSec < 0f || duration < _bestTimeSec;

            if (isBestImproved)
            {
                _bestTimeSec = duration;
                if (!string.IsNullOrWhiteSpace(_bestTimeKey))
                {
                    PlayerPrefs.SetFloat(_bestTimeKey, duration);
                    PlayerPrefs.Save();
                }
            }

            TrackEvent("demo_cube_completed", new Dictionary<string, object>
            {
                { "durationSec", duration },
                { "wrongClicks", _wrongClicks },
                { "attempts", _totalClickAttempts },
                { "bestImproved", isBestImproved },
                { "previousBestSec", previousBest },
                { "newBestSec", _bestTimeSec },
            });

            StopGame(GameContracts.GameStopReason.Completed);
        }

        private void AdvanceTargetAfterCorrectClick()
        {
            if (_activeConfig == null)
            {
                return;
            }

            if (_activeConfig.LevelMode == DemoCubeLevelMode.AlternateTwoColors)
            {
                _alternateExpectedIndex = (_alternateExpectedIndex + 1) % 2;
                return;
            }

            if (_activeConfig.LevelMode == DemoCubeLevelMode.RandomTargetColor)
            {
                RefreshTargetColor(forceRandomRefresh: true);
            }
        }

        private void RefreshTargetColor(bool forceRandomRefresh)
        {
            if (_activeConfig == null || _activeConfig.LevelMode != DemoCubeLevelMode.RandomTargetColor)
            {
                return;
            }

            var available = CollectRemainingColors();
            if (available.Count == 0)
            {
                return;
            }

            var previous = _randomTargetColor;
            var selected = available[UnityEngine.Random.Range(0, available.Count)];

            if (forceRandomRefresh && available.Count > 1 && selected == previous)
            {
                selected = available[(available.IndexOf(selected) + 1) % available.Count];
            }

            _randomTargetColor = selected;

            if (previous != _randomTargetColor)
            {
                _targetColorChanges++;
            }

            UpdatePointerTargetIndicator();
        }

        private List<DemoCubeColor> CollectRemainingColors()
        {
            var values = new List<DemoCubeColor>();
            for (var i = 0; i < _cubes.Count; i++)
            {
                var cube = _cubes[i];
                if (cube == null || cube.clicked || cube.instance == null)
                {
                    continue;
                }

                if (!values.Contains(cube.color))
                {
                    values.Add(cube.color);
                }
            }

            return values;
        }

        private DemoCubeColor ResolveSpawnColor(int index)
        {
            if (_activeConfig == null)
            {
                return DemoCubeColor.Red;
            }

            switch (_activeConfig.LevelMode)
            {
                case DemoCubeLevelMode.Basic:
                    return DemoCubeColor.Cyan;
                case DemoCubeLevelMode.AlternateTwoColors:
                    // Keep deterministic parity so alternate mode cannot become unsatisfiable.
                    return index % 2 == 0 ? DemoCubeColor.Red : DemoCubeColor.Blue;
                case DemoCubeLevelMode.RandomTargetColor:
                    return _randomPalette[UnityEngine.Random.Range(0, _randomPalette.Length)];
                default:
                    return index % 2 == 0 ? DemoCubeColor.Red : DemoCubeColor.Blue;
            }
        }

        private DemoCubeColor GetExpectedColor()
        {
            if (_activeConfig == null)
            {
                return DemoCubeColor.Any;
            }

            switch (_activeConfig.LevelMode)
            {
                case DemoCubeLevelMode.Basic:
                    return DemoCubeColor.Any;
                case DemoCubeLevelMode.AlternateTwoColors:
                    var preferred = _alternateExpectedIndex == 0
                        ? DemoCubeColor.Red
                        : DemoCubeColor.Blue;
                    if (HasRemainingColor(preferred))
                    {
                        return preferred;
                    }

                    var fallback = preferred == DemoCubeColor.Red
                        ? DemoCubeColor.Blue
                        : DemoCubeColor.Red;
                    return HasRemainingColor(fallback)
                        ? fallback
                        : DemoCubeColor.Any;
                case DemoCubeLevelMode.RandomTargetColor:
                    return _randomTargetColor;
                default:
                    return DemoCubeColor.Any;
            }
        }

        private bool HasRemainingColor(DemoCubeColor color)
        {
            for (var i = 0; i < _cubes.Count; i++)
            {
                var cube = _cubes[i];
                if (cube == null || cube.clicked || cube.instance == null)
                {
                    continue;
                }

                if (cube.color == color)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CanAcceptColor(DemoCubeColor clickedColor, DemoCubeColor expectedColor)
        {
            return expectedColor == DemoCubeColor.Any || clickedColor == expectedColor;
        }

        private PointerHoverFeedback ResolveCubeHoverFeedback(int cubeId)
        {
            var cube = FindCube(cubeId);
            if (cube == null || cube.clicked || cube.instance == null)
            {
                return new PointerHoverFeedback(new Color(0.45f, 0.45f, 0.45f, 1f), false);
            }

            var expected = GetExpectedColor();
            var isValid = CanAcceptColor(cube.color, expected);
            if (expected == DemoCubeColor.Any)
            {
                var anyColor = ToUnityColor(cube.color);
                return new PointerHoverFeedback(anyColor, true);
            }

            var targetColor = ToUnityColor(expected);
            if (!isValid)
            {
                targetColor = new Color(1f, 0.28f, 0.28f, 1f);
            }

            return new PointerHoverFeedback(targetColor, isValid);
        }

        private static string FormatColorLabel(DemoCubeColor color)
        {
            if (color == DemoCubeColor.Any)
            {
                return "Any";
            }

            return color.ToString();
        }

        private static Color ToUnityColor(DemoCubeColor color)
        {
            switch (color)
            {
                case DemoCubeColor.Red:
                    return new Color(0.92f, 0.24f, 0.24f);
                case DemoCubeColor.Blue:
                    return new Color(0.24f, 0.44f, 0.92f);
                case DemoCubeColor.Green:
                    return new Color(0.24f, 0.78f, 0.34f);
                case DemoCubeColor.Yellow:
                    return new Color(0.92f, 0.84f, 0.24f);
                case DemoCubeColor.Magenta:
                    return new Color(0.84f, 0.24f, 0.92f);
                case DemoCubeColor.Cyan:
                    return new Color(0.24f, 0.86f, 0.92f);
                default:
                    return Color.white;
            }
        }

        private Vector3 ViewportToWorld(Vector2 viewport)
        {
            if (_anchorPlayfieldToSessionStart && _playfieldAnchorReady)
            {
                var x = (Mathf.Clamp01(viewport.x) - 0.5f) * 2f * _playfieldHalfWidthWorld;
                var y = (Mathf.Clamp01(viewport.y) - 0.5f) * 2f * _playfieldHalfHeightWorld;
                return _playfieldCenterWorld + (_playfieldRightWorld * x) + (_playfieldUpWorld * y);
            }

            if (_targetCamera == null)
            {
                var x = (viewport.x - 0.5f) * 8f;
                var y = (viewport.y - 0.5f) * 4.5f;
                return new Vector3(x, y, 0f);
            }

            return _targetCamera.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, Mathf.Max(0.2f, _spawnDepth)));
        }

        private void UpdatePointerTargetIndicator()
        {
            var service = PointerIndicatorService.Instance;
            if (service == null)
            {
                return;
            }

            var expected = GetExpectedColor();
            if (expected == DemoCubeColor.Any)
            {
                service.SetTargetColor(new Color(0.9f, 0.9f, 0.95f, 1f));
                return;
            }

            service.SetTargetColor(ToUnityColor(expected));
        }

        private static void ClearPointerTargetIndicator()
        {
            PointerIndicatorService.Instance?.ClearTargetColor();
        }

        private void CapturePlayfieldAnchor()
        {
            if (!_anchorPlayfieldToSessionStart)
            {
                return;
            }

            EnsureCamera();
            if (_targetCamera == null)
            {
                _playfieldAnchorReady = false;
                return;
            }

            var depth = Mathf.Max(0.2f, _spawnDepth);
            var cameraTransform = _targetCamera.transform;
            var halfHeight = Mathf.Tan(Mathf.Deg2Rad * Mathf.Clamp(_targetCamera.fieldOfView, 1f, 179f) * 0.5f) * depth;
            var aspect = Mathf.Max(0.1f, _targetCamera.aspect);

            _playfieldCenterWorld = cameraTransform.position + (cameraTransform.forward * depth);
            _playfieldRightWorld = cameraTransform.right.normalized;
            _playfieldUpWorld = cameraTransform.up.normalized;
            _playfieldHalfHeightWorld = Mathf.Max(0.05f, halfHeight);
            _playfieldHalfWidthWorld = Mathf.Max(0.05f, halfHeight * aspect);
            _playfieldAnchorReady = true;
        }

        private CubeRuntimeData FindCube(int cubeId)
        {
            for (var i = 0; i < _cubes.Count; i++)
            {
                var cube = _cubes[i];
                if (cube != null && cube.cubeId == cubeId)
                {
                    return cube;
                }
            }

            return null;
        }

        private void EnsureCamera()
        {
            if (_targetCamera != null && _targetCamera.isActiveAndEnabled)
            {
                return;
            }

            _targetCamera = ResolvePreferredCamera();
        }

        private Camera ResolvePreferredCamera()
        {
            var centerEyeAnchor = GameObject.Find("OVRCameraRig/TrackingSpace/CenterEyeAnchor");
            if (centerEyeAnchor != null)
            {
                var centerEyeCamera = centerEyeAnchor.GetComponent<Camera>();
                if (centerEyeCamera != null && centerEyeCamera.isActiveAndEnabled)
                {
                    return centerEyeCamera;
                }

                centerEyeCamera = centerEyeAnchor.GetComponentInChildren<Camera>(true);
                if (centerEyeCamera != null && centerEyeCamera.isActiveAndEnabled)
                {
                    return centerEyeCamera;
                }
            }

            var leftEyeAnchor = GameObject.Find("OVRCameraRig/TrackingSpace/LeftEyeAnchor");
            if (leftEyeAnchor != null)
            {
                var leftEyeCamera = leftEyeAnchor.GetComponent<Camera>();
                if (leftEyeCamera != null && leftEyeCamera.isActiveAndEnabled)
                {
                    return leftEyeCamera;
                }

                leftEyeCamera = leftEyeAnchor.GetComponentInChildren<Camera>(true);
                if (leftEyeCamera != null && leftEyeCamera.isActiveAndEnabled)
                {
                    return leftEyeCamera;
                }
            }

            var rightEyeAnchor = GameObject.Find("OVRCameraRig/TrackingSpace/RightEyeAnchor");
            if (rightEyeAnchor != null)
            {
                var rightEyeCamera = rightEyeAnchor.GetComponent<Camera>();
                if (rightEyeCamera != null && rightEyeCamera.isActiveAndEnabled)
                {
                    return rightEyeCamera;
                }

                rightEyeCamera = rightEyeAnchor.GetComponentInChildren<Camera>(true);
                if (rightEyeCamera != null && rightEyeCamera.isActiveAndEnabled)
                {
                    return rightEyeCamera;
                }
            }

            var allCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            if (allCameras != null)
            {
                for (var i = 0; i < allCameras.Length; i++)
                {
                    var camera = allCameras[i];
                    if (camera == null || !camera.isActiveAndEnabled)
                    {
                        continue;
                    }

                    if (camera.stereoTargetEye == StereoTargetEyeMask.None)
                    {
                        continue;
                    }

                    return camera;
                }
            }

            var taggedMainCamera = Camera.main;
            if (taggedMainCamera != null && taggedMainCamera.isActiveAndEnabled)
            {
                return taggedMainCamera;
            }

            if (allCameras != null)
            {
                for (var i = 0; i < allCameras.Length; i++)
                {
                    var camera = allCameras[i];
                    if (camera == null || !camera.isActiveAndEnabled)
                    {
                        continue;
                    }

                    if (camera.name.IndexOf("CenterEye", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return camera;
                    }
                }
            }

            return FindFirstObjectByType<Camera>();
        }

        private bool EnsureGameplayFloorMaterial()
        {
            var floorObject = GameObject.Find("GameplayFloor");
            if (floorObject == null)
            {
                return false;
            }

            var floorRenderer = floorObject.GetComponent<Renderer>();
            if (floorRenderer == null)
            {
                return false;
            }

            var sharedMaterial = floorRenderer.sharedMaterial;
            var shaderBroken = sharedMaterial == null ||
                               sharedMaterial.shader == null ||
                               sharedMaterial.shader.name == "Hidden/InternalErrorShader" ||
                               !sharedMaterial.shader.isSupported;
            if (!shaderBroken)
            {
                return true;
            }

            var fallbackMaterial = ResolveRuntimeCubeMaterial();
            if (fallbackMaterial == null)
            {
                return false;
            }

            floorRenderer.sharedMaterial = fallbackMaterial;
            var floorMaterial = floorRenderer.material;
            if (floorMaterial != null && floorMaterial.HasProperty("_Color"))
            {
                floorMaterial.color = new Color(0.18f, 0.18f, 0.2f, 1f);
            }

            return true;
        }

        private Material ResolveRuntimeCubeMaterial()
        {
            if (_runtimeCubeMaterial != null && _runtimeCubeMaterial.shader != null)
            {
                return _runtimeCubeMaterial;
            }

            var shader = ResolveRuntimeCubeShader();
            if (shader == null)
            {
                return null;
            }

            _runtimeCubeMaterial = new Material(shader)
            {
                name = "DemoCubeRuntimeMaterial",
            };
            if (_runtimeCubeMaterial.HasProperty("_Color"))
            {
                _runtimeCubeMaterial.color = Color.white;
            }

            return _runtimeCubeMaterial;
        }

        private Shader ResolveRuntimeCubeShader()
        {
            if (_cubeFallbackShader != null && _cubeFallbackShader.isSupported)
            {
                LogResolvedRuntimeShader(_cubeFallbackShader, "serialized_fallback");
                return _cubeFallbackShader;
            }

            var shaderNames = new[]
            {
                "Standard",
                "Legacy Shaders/Diffuse",
                "Legacy Shaders/Transparent/Diffuse",
                "Sprites/Default",
                "Unlit/Color",
                "Mobile/Diffuse",
                "Universal Render Pipeline/Unlit",
                "Universal Render Pipeline/Simple Lit",
                "Universal Render Pipeline/Lit",
                "UI/Default",
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

            if (!_loggedRuntimeShaderSelection)
            {
                _loggedRuntimeShaderSelection = true;
                Debug.LogWarning(
                    "[DemoCube] No supported fallback shader found for runtime cube material. " +
                    "Generated cubes may render pink/invisible on Quest.");
            }

            return null;
        }

        private void LogResolvedRuntimeShader(Shader shader, string source)
        {
            if (_loggedRuntimeShaderSelection || shader == null)
            {
                return;
            }

            _loggedRuntimeShaderSelection = true;
            Debug.Log(
                "[DemoCube] Runtime fallback shader selected: " +
                shader.name +
                " (supported=" +
                shader.isSupported +
                ", source=" +
                source +
                ")");
        }

        private bool TryRestoreSavedStateIfRequested()
        {
            if (!_enableSaveResume || _pendingRestoreState == null || _activeConfig == null || !_activeConfig.ResumeFromSaved)
            {
                return false;
            }

            ApplySavedState(_pendingRestoreState);
            _pendingRestoreState = null;
            return true;
        }

        private void ApplySavedState(DemoCubeSavedState savedState)
        {
            if (savedState == null)
            {
                return;
            }

            if (_activeConfig != null)
            {
                var restoreCommand = new StartGameCommand
                {
                    correlationId = string.Empty,
                    gameId = GameId,
                    resumeFromSaved = true,
                    gameConfigType = "demo_cube_config_v1",
                    gameConfigVersion = Math.Max(1, _activeConfig.Version),
                    gameConfigJson = BuildSavedStateConfigJson(savedState),
                };

                _activeConfig = DemoCubeGameConfig.CreateFromStartCommand(restoreCommand, _activeConfig, _cubeScale);
                base.UpdateConfig(_activeConfig);
            }

            ClearActiveCubes();
            _cubes.Clear();

            if (savedState.cubes != null)
            {
                for (var i = 0; i < savedState.cubes.Count; i++)
                {
                    SpawnCubeFromSavedState(savedState.cubes[i]);
                }
            }

            _spawnedCount = Mathf.Max(1, savedState.spawnedCount);
            _clickedCount = Mathf.Clamp(savedState.clickedCount, 0, _spawnedCount);
            _wrongClicks = Mathf.Max(0, savedState.wrongClicks);
            _totalClickAttempts = Mathf.Max(_clickedCount, savedState.totalClickAttempts);
            _targetColorChanges = Mathf.Max(0, savedState.targetColorChanges);
            _alternateExpectedIndex = Mathf.Abs(savedState.alternateExpectedIndex % 2);
            _randomTargetColor = ParseColor(savedState.randomTargetColor, DemoCubeColor.Red);
            _nextCubeId = Mathf.Max(1, savedState.nextCubeId);
            _completionHandled = _clickedCount >= _spawnedCount;
            _restoredDurationOffsetSec = Mathf.Max(0f, savedState.elapsedSeconds);
            _lastHitElapsedSec = savedState.lastHitElapsedSeconds;

            if (_nextCubeId <= 1)
            {
                var maxCubeId = 0;
                for (var i = 0; i < _cubes.Count; i++)
                {
                    var cube = _cubes[i];
                    if (cube != null && cube.cubeId > maxCubeId)
                    {
                        maxCubeId = cube.cubeId;
                    }
                }

                _nextCubeId = Math.Max(1, maxCubeId + 1);
            }

            TrackEvent("demo_cube_state_restored", new Dictionary<string, object>
            {
                { "spawnedCount", _spawnedCount },
                { "clickedCount", _clickedCount },
                { "remainingCount", Mathf.Max(0, _spawnedCount - _clickedCount) },
                { "elapsedSec", _restoredDurationOffsetSec },
            });
        }

        private void PersistSavedState(string reasonCode)
        {
            if (!_enableSaveResume || _activeConfig == null || string.IsNullOrWhiteSpace(_saveStatePath))
            {
                return;
            }

            if (State != GameContracts.GameState.Initialized &&
                State != GameContracts.GameState.Playing &&
                State != GameContracts.GameState.Paused)
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(_saveStatePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var savedState = BuildSavedState(reasonCode);
                var json = JsonUtility.ToJson(savedState);
                var tempPath = _saveStatePath + ".tmp";
                File.WriteAllText(tempPath, json);

                if (File.Exists(_saveStatePath))
                {
                    File.Delete(_saveStatePath);
                }

                File.Move(tempPath, _saveStatePath);
                _lastSavedAtRealtime = Time.realtimeSinceStartup;

                if (_logSaveResume)
                {
                    Debug.Log($"[DemoCubeGame] Saved state ({reasonCode}) -> {_saveStatePath}");
                }
            }
            catch (Exception e)
            {
                if (_logSaveResume)
                {
                    Debug.LogWarning($"[DemoCubeGame] Save state failed ({reasonCode}): {e.Message}");
                }
            }
        }

        private DemoCubeSavedState BuildSavedState(string reasonCode)
        {
            var cubes = new List<SavedCubeState>(_cubes.Count);
            for (var i = 0; i < _cubes.Count; i++)
            {
                var cube = _cubes[i];
                if (cube == null)
                {
                    continue;
                }

                cubes.Add(new SavedCubeState
                {
                    cubeId = cube.cubeId,
                    color = cube.color.ToString(),
                    clicked = cube.clicked,
                    viewportX = cube.viewportPos.x,
                    viewportY = cube.viewportPos.y,
                    directionX = cube.direction.x,
                    directionY = cube.direction.y,
                });
            }

            var sessionId = Context == null || Context.Session == null ? string.Empty : Context.Session.SessionId;
            var activeLevel = _activeConfig == null ? string.Empty : _activeConfig.LevelMode.ToString();

            return new DemoCubeSavedState
            {
                version = 1,
                sessionId = sessionId ?? string.Empty,
                gameId = GameId,
                savedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                reasonCode = reasonCode ?? string.Empty,
                levelMode = activeLevel,
                cubeCount = _activeConfig == null ? 0 : _activeConfig.CubeCount,
                cubeSpeed = _activeConfig == null ? 0f : _activeConfig.CubeSpeed,
                cubeScale = _activeConfig == null ? 0f : _activeConfig.CubeScale,
                spawnedCount = _spawnedCount,
                clickedCount = _clickedCount,
                wrongClicks = _wrongClicks,
                totalClickAttempts = _totalClickAttempts,
                targetColorChanges = _targetColorChanges,
                nextCubeId = _nextCubeId,
                alternateExpectedIndex = _alternateExpectedIndex,
                randomTargetColor = _randomTargetColor.ToString(),
                elapsedSeconds = GetSessionDurationSeconds(),
                lastHitElapsedSeconds = _lastHitElapsedSec,
                cubes = cubes,
            };
        }

        private static string BuildSavedStateConfigJson(DemoCubeSavedState savedState)
        {
            if (savedState == null)
            {
                return "{}";
            }

            var cubeCount = Mathf.Clamp(savedState.cubeCount, 1, 64);
            var cubeSpeed = Mathf.Clamp(savedState.cubeSpeed, 0.1f, 3f);
            var levelMode = string.IsNullOrWhiteSpace(savedState.levelMode) ? "basic" : savedState.levelMode.Trim();

            return
                "{" +
                "\"cubeCount\":" + cubeCount + "," +
                "\"cubeSpeed\":" + cubeSpeed.ToString("F3", CultureInfo.InvariantCulture) + "," +
                "\"levelMode\":\"" + levelMode + "\"," +
                "\"version\":1," +
                "\"resumeFromSaved\":true" +
                "}";
        }

        private bool TryLoadSavedState(out DemoCubeSavedState savedState)
        {
            savedState = null;

            if (!_enableSaveResume || string.IsNullOrWhiteSpace(_saveStatePath) || !File.Exists(_saveStatePath))
            {
                return false;
            }

            try
            {
                var json = File.ReadAllText(_saveStatePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return false;
                }

                var parsed = JsonUtility.FromJson<DemoCubeSavedState>(json);
                if (parsed == null || parsed.cubes == null)
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(parsed.gameId) &&
                    !string.Equals(parsed.gameId, GameId, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var currentSessionId = Context == null || Context.Session == null ? string.Empty : Context.Session.SessionId;
                if (!string.IsNullOrWhiteSpace(currentSessionId) &&
                    !string.IsNullOrWhiteSpace(parsed.sessionId) &&
                    !string.Equals(parsed.sessionId, currentSessionId, StringComparison.Ordinal))
                {
                    return false;
                }

                savedState = parsed;
                return true;
            }
            catch (Exception e)
            {
                if (_logSaveResume)
                {
                    Debug.LogWarning($"[DemoCubeGame] Load state failed: {e.Message}");
                }

                return false;
            }
        }

        private void TryDeleteSavedState()
        {
            if (string.IsNullOrWhiteSpace(_saveStatePath))
            {
                return;
            }

            try
            {
                if (File.Exists(_saveStatePath))
                {
                    File.Delete(_saveStatePath);
                    if (_logSaveResume)
                    {
                        Debug.Log($"[DemoCubeGame] Deleted saved state: {_saveStatePath}");
                    }
                }
            }
            catch (Exception e)
            {
                if (_logSaveResume)
                {
                    Debug.LogWarning($"[DemoCubeGame] Delete state failed: {e.Message}");
                }
            }
        }

        private string BuildSaveStatePath(string sessionId)
        {
            var folder = string.IsNullOrWhiteSpace(_saveFolder) ? "session_resilience" : _saveFolder.Trim();
            var prefix = string.IsNullOrWhiteSpace(_saveFilePrefix) ? "demo_cube_state_" : _saveFilePrefix.Trim();
            var safeSessionId = SanitizeForFileName(string.IsNullOrWhiteSpace(sessionId) ? "unknown_session" : sessionId);
            return Path.Combine(Application.persistentDataPath, folder, prefix + safeSessionId + ".json");
        }

        private static string SanitizeForFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            var chars = value.ToCharArray();
            var invalid = Path.GetInvalidFileNameChars();
            for (var i = 0; i < chars.Length; i++)
            {
                for (var j = 0; j < invalid.Length; j++)
                {
                    if (chars[i] == invalid[j])
                    {
                        chars[i] = '_';
                        break;
                    }
                }
            }

            return new string(chars);
        }

        private void ClearActiveCubes()
        {
            for (var i = 0; i < _cubes.Count; i++)
            {
                var cube = _cubes[i];
                if (cube != null && cube.instance != null)
                {
                    Destroy(cube.instance);
                }
            }

            _cubes.Clear();
        }

        private static string BuildBestTimeKey(DemoCubeLevelMode levelMode)
        {
            return "demo_cube_best_time_" + levelMode.ToString();
        }

        private float GetSessionDurationSeconds()
        {
            return _restoredDurationOffsetSec + GetDurationSeconds();
        }

        private static DemoCubeColor ParseColor(string rawColor, DemoCubeColor fallback)
        {
            if (string.IsNullOrWhiteSpace(rawColor))
            {
                return fallback;
            }

            return Enum.TryParse(rawColor.Trim(), true, out DemoCubeColor parsed)
                ? parsed
                : fallback;
        }

        private sealed class CubeRuntimeData
        {
            public int cubeId;
            public DemoCubeColor color;
            public GameObject instance;
            public Vector2 direction;
            public Vector2 viewportPos;
            public bool clicked;
        }

        [Serializable]
        private sealed class SavedCubeState
        {
            public int cubeId;
            public string color;
            public bool clicked;
            public float viewportX;
            public float viewportY;
            public float directionX;
            public float directionY;
        }

        [Serializable]
        private sealed class DemoCubeSavedState
        {
            public int version;
            public string sessionId;
            public string gameId;
            public string savedAtUtc;
            public string reasonCode;
            public string levelMode;
            public int cubeCount;
            public float cubeSpeed;
            public float cubeScale;
            public int spawnedCount;
            public int clickedCount;
            public int wrongClicks;
            public int totalClickAttempts;
            public int targetColorChanges;
            public int nextCubeId;
            public int alternateExpectedIndex;
            public string randomTargetColor;
            public float elapsedSeconds;
            public float lastHitElapsedSeconds;
            public List<SavedCubeState> cubes;
        }
    }

    [Serializable]
    public sealed class DemoCubeGameConfig : GameContracts.IGameConfig
    {
        public const string DefaultGameId = "demo_cube_clicker";

        [SerializeField] private string _gameId = DefaultGameId;
        [SerializeField] private int _version = 1;
        [SerializeField] private int _cubeCount = 12;
        [SerializeField] private float _cubeSpeed = 0.7f;
        [SerializeField] private DemoCubeLevelMode _levelMode = DemoCubeLevelMode.Basic;
        [SerializeField] private float _cubeScale = 0.35f;
        [SerializeField] private bool _resumeFromSaved = false;

        public string GameId => string.IsNullOrWhiteSpace(_gameId) ? DefaultGameId : _gameId;
        public int Version => _version <= 0 ? 1 : _version;
        public int CubeCount => Mathf.Clamp(_cubeCount, 1, 64);
        public float CubeSpeed => Mathf.Clamp(_cubeSpeed, 0.1f, 3f);
        public DemoCubeLevelMode LevelMode => _levelMode;
        public float CubeScale => Mathf.Clamp(_cubeScale, 0.12f, 1.2f);
        public bool ResumeFromSaved => _resumeFromSaved;

        public static DemoCubeGameConfig CreateDefault(float cubeScale)
        {
            return new DemoCubeGameConfig
            {
                _gameId = DefaultGameId,
                _version = 1,
                _cubeCount = 12,
                _cubeSpeed = 0.7f,
                _levelMode = DemoCubeLevelMode.Basic,
                _cubeScale = Mathf.Clamp(cubeScale, 0.12f, 1.2f),
                _resumeFromSaved = false,
            };
        }

        public static DemoCubeGameConfig CreateFromStartCommand(
            StartGameCommand command,
            DemoCubeGameConfig previous,
            float fallbackCubeScale)
        {
            var baseline = previous ?? CreateDefault(fallbackCubeScale);
            var settings = ParseStartSettings(command);

            var nextCubeCount = settings != null && settings.cubeCount > 0
                ? settings.cubeCount
                : baseline.CubeCount;
            var nextCubeSpeed = settings != null && settings.cubeSpeed > 0f
                ? settings.cubeSpeed
                : baseline.CubeSpeed;

            var nextVersion = baseline.Version;
            if (command != null && command.gameConfigVersion > 0)
            {
                nextVersion = command.gameConfigVersion;
            }
            else if (settings != null && settings.version > 0)
            {
                nextVersion = settings.version;
            }

            var nextLevelMode = ParseLevelMode(
                settings == null ? string.Empty : settings.levelMode,
                baseline.LevelMode);
            var nextResumeFromSaved =
                (command != null && command.resumeFromSaved) ||
                (settings != null && settings.resumeFromSaved);

            return new DemoCubeGameConfig
            {
                _gameId = DefaultGameId,
                _version = Mathf.Max(1, nextVersion),
                _cubeCount = Mathf.Clamp(nextCubeCount, 1, 64),
                _cubeSpeed = Mathf.Clamp(nextCubeSpeed, 0.1f, 3f),
                _levelMode = nextLevelMode,
                _cubeScale = baseline.CubeScale,
                _resumeFromSaved = nextResumeFromSaved,
            };
        }

        private static DemoCubeStartSettings ParseStartSettings(StartGameCommand command)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.gameConfigJson))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<DemoCubeStartSettings>(command.gameConfigJson);
            }
            catch
            {
                return null;
            }
        }

        private static DemoCubeLevelMode ParseLevelMode(string wireValue, DemoCubeLevelMode fallback)
        {
            if (string.IsNullOrWhiteSpace(wireValue))
            {
                return fallback;
            }

            var normalized = wireValue.Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "basic":
                    return DemoCubeLevelMode.Basic;
                case "alternate":
                case "alternate_colors":
                case "alternate_two_colors":
                    return DemoCubeLevelMode.AlternateTwoColors;
                case "random":
                case "random_target":
                case "random_target_color":
                    return DemoCubeLevelMode.RandomTargetColor;
                default:
                    return fallback;
            }
        }

        [Serializable]
        private sealed class DemoCubeStartSettings
        {
            public int cubeCount;
            public float cubeSpeed;
            public string levelMode;
            public int version;
            public bool resumeFromSaved;
        }
    }

    public sealed class DemoCubeClickTarget : MonoBehaviour, IQuestPointerTarget, IPointerHoverFeedbackTarget
    {
        private int _cubeId;
        private Action<int, string> _onClicked;
        private Func<int, PointerHoverFeedback> _onHoverFeedback;

        public void Configure(
            int cubeId,
            Action<int, string> onClicked,
            Func<int, PointerHoverFeedback> onHoverFeedback)
        {
            _cubeId = cubeId;
            _onClicked = onClicked;
            _onHoverFeedback = onHoverFeedback;
        }

#if UNITY_EDITOR
        private void OnMouseDown()
        {
            ActivateFromPointer("MOUSE");
        }
#endif

        private void OnTriggerEnter(Collider other)
        {
            if (other != null && other.CompareTag("Controller"))
            {
                ActivateFromPointer("CONTROLLER_TRIGGER");
            }
        }

        public void ActivateFromPointer(string source)
        {
            _onClicked?.Invoke(_cubeId, string.IsNullOrWhiteSpace(source) ? "UNKNOWN" : source.Trim());
        }

        public bool TryGetHoverFeedback(out PointerHoverFeedback feedback)
        {
            if (_onHoverFeedback != null)
            {
                feedback = _onHoverFeedback(_cubeId);
                return true;
            }

            feedback = default;
            return false;
        }
    }
}
