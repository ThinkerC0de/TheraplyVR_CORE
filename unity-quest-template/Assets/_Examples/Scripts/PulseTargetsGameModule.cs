using System;
using System.Collections.Generic;
using UnityEngine;
using GameContracts = TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;

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

        private PulseTargetsGameConfig _activeConfig;
        private TargetRuntime _activeTarget;
        private int _nextTargetId = 1;
        private int _hits;
        private int _misses;
        private float _lastHitElapsedSec = -1f;

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

            base.Initialize(_activeConfig, context);

            TrackEvent("pulse_targets_config", new Dictionary<string, object>
            {
                { "targetCount", _activeConfig.TargetCount },
                { "targetSpeed", _activeConfig.TargetSpeed },
                { "targetScale", _activeConfig.TargetScale },
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
            ClearActiveTarget();
        }

        public override GameContracts.IGameResult BuildResult()
        {
            return new GameResult(
                GameId,
                State == GameContracts.GameState.Completed,
                GetDurationSeconds(),
                new Dictionary<string, object>
                {
                    { "hits", _hits },
                    { "misses", _misses },
                    { "targetCount", _activeConfig == null ? 0 : _activeConfig.TargetCount },
                    { "targetSpeed", _activeConfig == null ? 0f : _activeConfig.TargetSpeed },
                    { "durationSec", GetDurationSeconds() },
                });
        }

        private void Update()
        {
            if (State != GameContracts.GameState.Playing || _activeTarget == null || _activeConfig == null)
            {
                return;
            }

            _activeTarget.viewportPos += _activeTarget.direction *
                                         Mathf.Max(0.1f, _activeConfig.TargetSpeed) *
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
            targetObject.transform.localScale = Vector3.one * _activeConfig.TargetScale;
            targetObject.transform.position = ViewportToWorld(viewportPos);

            var renderer = targetObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = Color.Lerp(Color.yellow, Color.red, 0.35f);
            }

            var clickTarget = targetObject.AddComponent<PulseTargetClickTarget>();
            clickTarget.Configure(_nextTargetId, HandleTargetClicked);

            _activeTarget = new TargetRuntime
            {
                targetId = _nextTargetId,
                viewportPos = viewportPos,
                direction = direction,
                instance = targetObject,
            };

            _nextTargetId++;

            TrackEvent("pulse_target_spawned", new Dictionary<string, object>
            {
                { "targetId", _activeTarget.targetId },
                { "hits", _hits },
                { "remaining", Mathf.Max(0, _activeConfig.TargetCount - _hits) },
            });
        }

        private void HandleTargetClicked(int targetId)
        {
            if (State != GameContracts.GameState.Playing || _activeTarget == null)
            {
                return;
            }

            if (targetId != _activeTarget.targetId)
            {
                _misses++;
                TrackEvent("pulse_target_miss", new Dictionary<string, object>
                {
                    { "targetId", targetId },
                    { "expectedTargetId", _activeTarget.targetId },
                    { "misses", _misses },
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

        [Serializable]
        private sealed class TargetRuntime
        {
            public int targetId;
            public Vector2 viewportPos;
            public Vector2 direction;
            public GameObject instance;
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

        public string GameId => string.IsNullOrWhiteSpace(_gameId) ? DefaultGameId : _gameId;
        public int Version => _version <= 0 ? 1 : _version;
        public int TargetCount => Mathf.Clamp(_targetCount, 3, 64);
        public float TargetSpeed => Mathf.Clamp(_targetSpeed, 0.1f, 3f);
        public float TargetScale => Mathf.Clamp(_targetScale, 0.12f, 1.2f);

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
            var nextTargetSpeed = settings != null && settings.targetSpeed > 0f
                ? settings.targetSpeed
                : baseline.TargetSpeed;
            var nextTargetScale = settings != null && settings.targetScale > 0f
                ? settings.targetScale
                : baseline.TargetScale;

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

        [Serializable]
        private sealed class PulseTargetsStartSettings
        {
            public int targetCount;
            public float targetSpeed;
            public float targetScale;
            public int version;
        }
    }

    public sealed class PulseTargetClickTarget : MonoBehaviour
    {
        private int _targetId;
        private Action<int> _onClicked;

        public void Configure(int targetId, Action<int> onClicked)
        {
            _targetId = targetId;
            _onClicked = onClicked;
        }

        private void OnMouseDown()
        {
            _onClicked?.Invoke(_targetId);
        }
    }
}
