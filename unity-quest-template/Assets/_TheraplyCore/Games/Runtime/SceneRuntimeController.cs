using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheraplyCore.Interactions;
using GameContracts = TheraplyCore.Games.Contracts;
using Logger = TheraplyCore.Logging.Logger;

namespace TheraplyCore.Games.Runtime
{
    /// <summary>
    /// Scene runtime operations for flow-driven sessions.
    /// Supports load/unload/reset/spawn/despawn/teleport using keyed registries.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneRuntimeController : MonoBehaviour
    {
        [Serializable]
        public sealed class TransformBindingEntry
        {
            public string key = string.Empty;
            public Transform value;
        }

        [Serializable]
        public sealed class PrefabBindingEntry
        {
            public string key = string.Empty;
            public GameObject prefab;
        }

        private sealed class SpawnRecord
        {
            public string instanceId = string.Empty;
            public string bindingKey = string.Empty;
            public GameObject instance;
        }

        [Header("Registries")]
        [SerializeField] private List<PrefabBindingEntry> _prefabBindings = new List<PrefabBindingEntry>();
        [SerializeField] private List<TransformBindingEntry> _spawnPointBindings = new List<TransformBindingEntry>();
        [SerializeField] private List<TransformBindingEntry> _anchorBindings = new List<TransformBindingEntry>();
        [SerializeField] private List<TransformBindingEntry> _poseBindings = new List<TransformBindingEntry>();

        [Header("Behavior")]
        [SerializeField] private SessionRuntimeBridge _sessionRuntimeBridge;
        [SerializeField] private InteractionEventBridge _interactionEventBridge;
        [SerializeField] private bool _emitSceneTelemetry = true;
        [SerializeField] private bool _logLifecycle;

        private readonly Dictionary<string, GameObject> _prefabByKey =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Transform> _spawnPointsByKey =
            new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Transform> _anchorsByKey =
            new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Transform> _posesByKey =
            new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SpawnRecord> _spawnById =
            new Dictionary<string, SpawnRecord>(StringComparer.Ordinal);
        private string _runtimeGameId = string.Empty;
        private string _runtimeFlowId = string.Empty;
        private string _runtimeSessionId = string.Empty;

        private void Awake()
        {
            ResolveDependencies();
            RebuildRegistryIndex();
        }

        public void SetRuntimeContext(string gameId, string flowId, string sessionId)
        {
            _runtimeGameId = NormalizeOrFallback(gameId, string.Empty);
            _runtimeFlowId = NormalizeOrFallback(flowId, string.Empty);
            _runtimeSessionId = NormalizeOrFallback(sessionId, string.Empty);
            ResolveDependencies();
        }

        public void RebuildRegistryIndex()
        {
            _prefabByKey.Clear();
            _spawnPointsByKey.Clear();
            _anchorsByKey.Clear();
            _posesByKey.Clear();

            IndexPrefabs(_prefabBindings, _prefabByKey);
            IndexTransforms(_spawnPointBindings, _spawnPointsByKey);
            IndexTransforms(_anchorBindings, _anchorsByKey);
            IndexTransforms(_poseBindings, _posesByKey);
        }

        public bool TryLoadScene(string sceneName, bool additive, out string reasonCode)
        {
            var loadMode = additive ? "additive" : "single";
            return TryLoadScene(sceneName, loadMode, out reasonCode);
        }

        public bool TryLoadScene(string sceneName, string loadMode, out string reasonCode)
        {
            reasonCode = string.Empty;
            if (!IsSceneOperationAllowed(out reasonCode))
            {
                EmitSceneRequestTelemetry(
                    "scene_load_requested",
                    sceneName,
                    loadMode,
                    accepted: false,
                    reasonCode: reasonCode);
                return false;
            }

            if (string.IsNullOrWhiteSpace(sceneName))
            {
                reasonCode = "SCENE_NAME_REQUIRED";
                EmitSceneRequestTelemetry(
                    "scene_load_requested",
                    sceneName,
                    loadMode,
                    accepted: false,
                    reasonCode: reasonCode);
                return false;
            }

            var normalizedSceneName = sceneName.Trim();
            if (!TryResolveLoadMode(loadMode, out var mode, out reasonCode))
            {
                EmitSceneRequestTelemetry(
                    "scene_load_requested",
                    normalizedSceneName,
                    loadMode,
                    accepted: false,
                    reasonCode: reasonCode);
                return false;
            }

            var existingScene = SceneManager.GetSceneByName(normalizedSceneName);
            if (existingScene.IsValid() && existingScene.isLoaded)
            {
                if (mode == LoadSceneMode.Additive)
                {
                    reasonCode = "SCENE_ALREADY_LOADED";
                    EmitSceneRequestTelemetry(
                        "scene_load_requested",
                        normalizedSceneName,
                        loadMode,
                        accepted: true,
                        reasonCode: reasonCode);
                    return true;
                }

                var activeScene = SceneManager.GetActiveScene();
                if (activeScene.IsValid() &&
                    string.Equals(activeScene.name, normalizedSceneName, StringComparison.OrdinalIgnoreCase))
                {
                    reasonCode = "SCENE_ALREADY_ACTIVE";
                    EmitSceneRequestTelemetry(
                        "scene_load_requested",
                        normalizedSceneName,
                        loadMode,
                        accepted: true,
                        reasonCode: reasonCode);
                    return true;
                }
            }

            var operation = SceneManager.LoadSceneAsync(normalizedSceneName, mode);
            if (operation == null)
            {
                reasonCode = "SCENE_LOAD_REQUEST_FAILED";
                EmitSceneRequestTelemetry(
                    "scene_load_requested",
                    normalizedSceneName,
                    loadMode,
                    accepted: false,
                    reasonCode: reasonCode);
                return false;
            }

            reasonCode = "SCENE_LOAD_REQUEST_ACCEPTED";
            EmitSceneRequestTelemetry(
                "scene_load_requested",
                normalizedSceneName,
                loadMode,
                accepted: true,
                reasonCode: reasonCode);
            MaybeLog("SCENE_LOAD_REQUEST:" + normalizedSceneName + ":" + mode);
            return true;
        }

        public bool TryUnloadScene(string sceneName, out string reasonCode)
        {
            reasonCode = string.Empty;
            if (!IsSceneOperationAllowed(out reasonCode))
            {
                EmitSceneRequestTelemetry(
                    "scene_unload_requested",
                    sceneName,
                    mode: string.Empty,
                    accepted: false,
                    reasonCode: reasonCode);
                return false;
            }

            if (string.IsNullOrWhiteSpace(sceneName))
            {
                reasonCode = "SCENE_NAME_REQUIRED";
                EmitSceneRequestTelemetry(
                    "scene_unload_requested",
                    sceneName,
                    mode: string.Empty,
                    accepted: false,
                    reasonCode: reasonCode);
                return false;
            }

            var normalizedSceneName = sceneName.Trim();
            var scene = SceneManager.GetSceneByName(normalizedSceneName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                reasonCode = "SCENE_ALREADY_UNLOADED";
                EmitSceneRequestTelemetry(
                    "scene_unload_requested",
                    normalizedSceneName,
                    mode: string.Empty,
                    accepted: true,
                    reasonCode: reasonCode);
                return true;
            }

            if (SceneManager.sceneCount <= 1)
            {
                reasonCode = "SCENE_UNLOAD_LAST_SCENE_BLOCKED";
                EmitSceneRequestTelemetry(
                    "scene_unload_requested",
                    normalizedSceneName,
                    mode: string.Empty,
                    accepted: false,
                    reasonCode: reasonCode);
                return false;
            }

            var operation = SceneManager.UnloadSceneAsync(scene);
            if (operation == null)
            {
                reasonCode = "SCENE_UNLOAD_REQUEST_FAILED";
                EmitSceneRequestTelemetry(
                    "scene_unload_requested",
                    normalizedSceneName,
                    mode: string.Empty,
                    accepted: false,
                    reasonCode: reasonCode);
                return false;
            }

            reasonCode = "SCENE_UNLOAD_REQUEST_ACCEPTED";
            EmitSceneRequestTelemetry(
                "scene_unload_requested",
                normalizedSceneName,
                mode: string.Empty,
                accepted: true,
                reasonCode: reasonCode);
            MaybeLog("SCENE_UNLOAD_REQUEST:" + normalizedSceneName);
            return true;
        }

        public bool TryResetScene(string sceneName, bool additive, out string reasonCode)
        {
            var loadMode = additive ? "additive" : "single";
            return TryResetScene(sceneName, loadMode, out reasonCode);
        }

        public bool TryResetScene(string sceneName, string loadMode, out string reasonCode)
        {
            reasonCode = string.Empty;
            if (!IsSceneOperationAllowed(out reasonCode))
            {
                EmitSceneRequestTelemetry(
                    "scene_reset_requested",
                    sceneName,
                    loadMode,
                    accepted: false,
                    reasonCode: reasonCode);
                return false;
            }

            if (string.IsNullOrWhiteSpace(sceneName))
            {
                reasonCode = "SCENE_NAME_REQUIRED";
                EmitSceneRequestTelemetry(
                    "scene_reset_requested",
                    sceneName,
                    loadMode,
                    accepted: false,
                    reasonCode: reasonCode);
                return false;
            }

            if (!TryUnloadScene(sceneName, out var unloadReason))
            {
                reasonCode = unloadReason;
                EmitSceneRequestTelemetry(
                    "scene_reset_requested",
                    sceneName,
                    loadMode,
                    accepted: false,
                    reasonCode: reasonCode);
                return false;
            }

            if (!TryLoadScene(sceneName, loadMode, out var loadReason))
            {
                reasonCode = loadReason;
                EmitSceneRequestTelemetry(
                    "scene_reset_requested",
                    sceneName,
                    loadMode,
                    accepted: false,
                    reasonCode: reasonCode);
                return false;
            }

            reasonCode = "SCENE_RESET_REQUEST_ACCEPTED";
            EmitSceneRequestTelemetry(
                "scene_reset_requested",
                sceneName,
                loadMode,
                accepted: true,
                reasonCode: reasonCode);
            return true;
        }

        public bool TrySpawn(
            string bindingKey,
            string prefabKey,
            string spawnPointKey,
            out string instanceId,
            out string reasonCode)
        {
            instanceId = string.Empty;
            reasonCode = string.Empty;

            RebuildRegistryIndex();

            if (string.IsNullOrWhiteSpace(prefabKey))
            {
                reasonCode = "PREFAB_KEY_REQUIRED";
                return false;
            }

            if (!_prefabByKey.TryGetValue(prefabKey.Trim(), out var prefab) || prefab == null)
            {
                reasonCode = "PREFAB_KEY_NOT_FOUND";
                return false;
            }

            Transform spawnPoint = null;
            if (!string.IsNullOrWhiteSpace(spawnPointKey))
            {
                _spawnPointsByKey.TryGetValue(spawnPointKey.Trim(), out spawnPoint);
            }

            var position = spawnPoint == null ? Vector3.zero : spawnPoint.position;
            var rotation = spawnPoint == null ? Quaternion.identity : spawnPoint.rotation;

            var instance = Instantiate(prefab, position, rotation);
            if (instance == null)
            {
                reasonCode = "SPAWN_FAILED";
                return false;
            }

            instanceId = Guid.NewGuid().ToString();
            _spawnById[instanceId] = new SpawnRecord
            {
                instanceId = instanceId,
                bindingKey = string.IsNullOrWhiteSpace(bindingKey) ? string.Empty : bindingKey.Trim(),
                instance = instance,
            };

            MaybeLog("SPAWN:" + instanceId);
            return true;
        }

        public bool TrySpawnWave(
            string bindingKeyPrefix,
            string prefabKey,
            string spawnPointKey,
            Vector3 areaSize,
            int count,
            float durationSec,
            bool randomYaw,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            RebuildRegistryIndex();

            if (string.IsNullOrWhiteSpace(prefabKey))
            {
                reasonCode = "PREFAB_KEY_REQUIRED";
                return false;
            }

            if (!_prefabByKey.TryGetValue(prefabKey.Trim(), out var prefab) || prefab == null)
            {
                reasonCode = "PREFAB_KEY_NOT_FOUND";
                return false;
            }

            if (count <= 0)
            {
                reasonCode = "SPAWN_COUNT_INVALID";
                return false;
            }

            if (durationSec < 0f)
            {
                reasonCode = "SPAWN_DURATION_INVALID";
                return false;
            }

            Transform spawnPoint = null;
            if (!string.IsNullOrWhiteSpace(spawnPointKey))
            {
                _spawnPointsByKey.TryGetValue(spawnPointKey.Trim(), out spawnPoint);
            }

            var safeAreaSize = new Vector3(
                Mathf.Max(0f, areaSize.x),
                Mathf.Max(0f, areaSize.y),
                Mathf.Max(0f, areaSize.z));
            var normalizedBindingPrefix = NormalizeOrFallback(bindingKeyPrefix, string.Empty);

            if (count == 1 || durationSec <= 0f)
            {
                for (var i = 0; i < count; i++)
                {
                    if (!TrySpawnResolvedPrefab(
                            prefab,
                            normalizedBindingPrefix,
                            i,
                            count,
                            spawnPoint,
                            safeAreaSize,
                            randomYaw,
                            out reasonCode))
                    {
                        return false;
                    }
                }

                reasonCode = "SPAWN_WAVE_APPLIED";
                return true;
            }

            if (!isActiveAndEnabled)
            {
                reasonCode = "SCENE_RUNTIME_INACTIVE";
                return false;
            }

            StartCoroutine(
                SpawnWaveRoutine(
                    prefab,
                    normalizedBindingPrefix,
                    count,
                    durationSec,
                    spawnPoint,
                    safeAreaSize,
                    randomYaw));

            reasonCode = "SPAWN_WAVE_SCHEDULED";
            return true;
        }

        public bool TryDespawnByInstanceId(string instanceId, out string reasonCode)
        {
            reasonCode = string.Empty;
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                reasonCode = "INSTANCE_ID_REQUIRED";
                return false;
            }

            if (!_spawnById.TryGetValue(instanceId.Trim(), out var record) || record == null)
            {
                reasonCode = "INSTANCE_NOT_FOUND";
                return false;
            }

            if (record.instance != null)
            {
                Destroy(record.instance);
            }

            _spawnById.Remove(instanceId.Trim());
            MaybeLog("DESPAWN:" + instanceId.Trim());
            return true;
        }

        public bool TryDespawnByBindingKey(string bindingKey, out string reasonCode)
        {
            reasonCode = string.Empty;
            if (string.IsNullOrWhiteSpace(bindingKey))
            {
                reasonCode = "BINDING_KEY_REQUIRED";
                return false;
            }

            var normalized = bindingKey.Trim();
            string matchedInstanceId = null;

            foreach (var pair in _spawnById)
            {
                var record = pair.Value;
                if (record == null)
                {
                    continue;
                }

                if (string.Equals(record.bindingKey, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    matchedInstanceId = pair.Key;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(matchedInstanceId))
            {
                reasonCode = "BINDING_INSTANCE_NOT_FOUND";
                return false;
            }

            return TryDespawnByInstanceId(matchedInstanceId, out reasonCode);
        }

        public bool TryTeleportAnchor(string anchorKey, string poseKey, out string reasonCode)
        {
            reasonCode = string.Empty;
            RebuildRegistryIndex();

            if (string.IsNullOrWhiteSpace(anchorKey) || string.IsNullOrWhiteSpace(poseKey))
            {
                reasonCode = "ANCHOR_OR_POSE_KEY_REQUIRED";
                return false;
            }

            if (!_anchorsByKey.TryGetValue(anchorKey.Trim(), out var anchor) || anchor == null)
            {
                reasonCode = "ANCHOR_KEY_NOT_FOUND";
                return false;
            }

            if (!_posesByKey.TryGetValue(poseKey.Trim(), out var pose) || pose == null)
            {
                reasonCode = "POSE_KEY_NOT_FOUND";
                return false;
            }

            anchor.position = pose.position;
            anchor.rotation = pose.rotation;

            MaybeLog("TELEPORT:" + anchorKey.Trim() + "->" + poseKey.Trim());
            return true;
        }

        public void DespawnAll()
        {
            var ids = new List<string>(_spawnById.Keys);
            for (var i = 0; i < ids.Count; i++)
            {
                TryDespawnByInstanceId(ids[i], out _);
            }
        }

        private IEnumerator SpawnWaveRoutine(
            GameObject prefab,
            string bindingKeyPrefix,
            int count,
            float durationSec,
            Transform spawnPoint,
            Vector3 areaSize,
            bool randomYaw)
        {
            var intervalSec = count <= 1 ? 0f : durationSec / (count - 1);
            for (var i = 0; i < count; i++)
            {
                if (!TrySpawnResolvedPrefab(
                        prefab,
                        bindingKeyPrefix,
                        i,
                        count,
                        spawnPoint,
                        areaSize,
                        randomYaw,
                        out _))
                {
                    yield break;
                }

                if (i < count - 1 && intervalSec > 0f)
                {
                    yield return new WaitForSeconds(intervalSec);
                }
            }
        }

        private bool TrySpawnResolvedPrefab(
            GameObject prefab,
            string bindingKeyPrefix,
            int index,
            int totalCount,
            Transform spawnPoint,
            Vector3 areaSize,
            bool randomYaw,
            out string reasonCode)
        {
            reasonCode = string.Empty;
            if (prefab == null)
            {
                reasonCode = "PREFAB_KEY_NOT_FOUND";
                return false;
            }

            var position = BuildSpawnPosition(spawnPoint, areaSize);
            var rotation = BuildSpawnRotation(spawnPoint, randomYaw);
            var instance = Instantiate(prefab, position, rotation);
            if (instance == null)
            {
                reasonCode = "SPAWN_FAILED";
                return false;
            }

            var instanceId = Guid.NewGuid().ToString();
            _spawnById[instanceId] = new SpawnRecord
            {
                instanceId = instanceId,
                bindingKey = BuildSpawnBindingKey(bindingKeyPrefix, index, totalCount),
                instance = instance,
            };

            MaybeLog("SPAWN:" + instanceId);
            return true;
        }

        private static string BuildSpawnBindingKey(string bindingKeyPrefix, int index, int totalCount)
        {
            var normalizedPrefix = NormalizeOrFallback(bindingKeyPrefix, string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedPrefix))
            {
                return string.Empty;
            }

            if (totalCount <= 1)
            {
                return normalizedPrefix;
            }

            return normalizedPrefix + "_" + (index + 1);
        }

        private static Vector3 BuildSpawnPosition(Transform spawnPoint, Vector3 areaSize)
        {
            var basePosition = spawnPoint == null ? Vector3.zero : spawnPoint.position;
            var half = areaSize * 0.5f;
            if (half.sqrMagnitude <= 0f)
            {
                return basePosition;
            }

            var localOffset = new Vector3(
                UnityEngine.Random.Range(-half.x, half.x),
                UnityEngine.Random.Range(-half.y, half.y),
                UnityEngine.Random.Range(-half.z, half.z));
            if (spawnPoint == null)
            {
                return basePosition + localOffset;
            }

            return basePosition + spawnPoint.TransformVector(localOffset);
        }

        private static Quaternion BuildSpawnRotation(Transform spawnPoint, bool randomYaw)
        {
            var baseRotation = spawnPoint == null ? Quaternion.identity : spawnPoint.rotation;
            if (!randomYaw)
            {
                return baseRotation;
            }

            var yawRotation = Quaternion.AngleAxis(UnityEngine.Random.Range(0f, 360f), Vector3.up);
            return yawRotation * baseRotation;
        }

        private static void IndexPrefabs(
            List<PrefabBindingEntry> source,
            Dictionary<string, GameObject> target)
        {
            if (source == null)
            {
                return;
            }

            for (var i = 0; i < source.Count; i++)
            {
                var entry = source[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.key) || entry.prefab == null)
                {
                    continue;
                }

                var key = entry.key.Trim();
                if (!target.ContainsKey(key))
                {
                    target[key] = entry.prefab;
                }
            }
        }

        private static void IndexTransforms(
            List<TransformBindingEntry> source,
            Dictionary<string, Transform> target)
        {
            if (source == null)
            {
                return;
            }

            for (var i = 0; i < source.Count; i++)
            {
                var entry = source[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.key) || entry.value == null)
                {
                    continue;
                }

                var key = entry.key.Trim();
                if (!target.ContainsKey(key))
                {
                    target[key] = entry.value;
                }
            }
        }

        private void MaybeLog(string marker)
        {
            if (_logLifecycle)
            {
                Logger.Info("[SceneRuntimeController] " + marker);
            }
        }

        private bool IsSceneOperationAllowed(out string reasonCode)
        {
            reasonCode = string.Empty;
            ResolveDependencies();

            if (_sessionRuntimeBridge == null)
            {
                return true;
            }

            var state = _sessionRuntimeBridge.SessionState;
            switch (state)
            {
                case GameContracts.SessionLifecycleState.COMPLETED:
                case GameContracts.SessionLifecycleState.ABORTED_BY_THERAPIST:
                case GameContracts.SessionLifecycleState.FAILED_TECHNICAL:
                    reasonCode = "SCENE_OPERATION_NOT_ALLOWED_IN_SESSION_STATE";
                    return false;
                default:
                    return true;
            }
        }

        private static bool TryResolveLoadMode(string loadMode, out LoadSceneMode mode, out string reasonCode)
        {
            mode = LoadSceneMode.Single;
            reasonCode = string.Empty;

            var normalizedLoadMode = NormalizeOrFallback(loadMode, "single");
            if (string.Equals(normalizedLoadMode, "single", StringComparison.OrdinalIgnoreCase))
            {
                mode = LoadSceneMode.Single;
                return true;
            }

            if (string.Equals(normalizedLoadMode, "additive", StringComparison.OrdinalIgnoreCase))
            {
                mode = LoadSceneMode.Additive;
                return true;
            }

            reasonCode = "SCENE_LOAD_MODE_INVALID";
            return false;
        }

        private void ResolveDependencies()
        {
            if (_sessionRuntimeBridge == null)
            {
                _sessionRuntimeBridge = GetComponent<SessionRuntimeBridge>();
                if (_sessionRuntimeBridge == null)
                {
                    _sessionRuntimeBridge = FindFirstObjectByType<SessionRuntimeBridge>();
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

        private void EmitSceneRequestTelemetry(
            string eventName,
            string sceneName,
            string mode,
            bool accepted,
            string reasonCode)
        {
            if (!_emitSceneTelemetry || _interactionEventBridge == null)
            {
                return;
            }

            var payload = new Dictionary<string, object>
            {
                { "flowId", NormalizeOrFallback(_runtimeFlowId, "session_flow") },
                { "sessionId", NormalizeOrFallback(_runtimeSessionId, string.Empty) },
                { "sceneName", NormalizeOrFallback(sceneName, string.Empty) },
                { "mode", NormalizeOrFallback(mode, string.Empty) },
                { "accepted", accepted },
                { "reasonCode", NormalizeOrFallback(reasonCode, accepted ? "SCENE_REQUEST_ACCEPTED" : "SCENE_REQUEST_REJECTED") },
                { "sessionState", _sessionRuntimeBridge == null ? string.Empty : _sessionRuntimeBridge.SessionState.ToString() },
                { "eventType", NormalizeOrFallback(eventName, "scene_request") },
                { "payloadVersion", 1 },
                { "monotonicSec", Time.realtimeSinceStartup },
                { "actionOutcome", accepted ? "CORRECT" : "INCORRECT" },
            };

            _interactionEventBridge.RecordGameplayEvent(
                NormalizeOrFallback(_runtimeGameId, "session_flow"),
                eventName,
                string.Empty,
                payload,
                nameof(SceneRuntimeController));
        }

        private static string NormalizeOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? (fallback ?? string.Empty)
                : value.Trim();
        }
    }
}
