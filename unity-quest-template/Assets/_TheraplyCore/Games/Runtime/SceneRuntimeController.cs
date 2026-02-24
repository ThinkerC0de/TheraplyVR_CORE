using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
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

        private void Awake()
        {
            RebuildRegistryIndex();
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
            reasonCode = string.Empty;
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                reasonCode = "SCENE_NAME_REQUIRED";
                return false;
            }

            var normalizedSceneName = sceneName.Trim();
            var mode = additive ? LoadSceneMode.Additive : LoadSceneMode.Single;
            var operation = SceneManager.LoadSceneAsync(normalizedSceneName, mode);
            if (operation == null)
            {
                reasonCode = "SCENE_LOAD_REQUEST_FAILED";
                return false;
            }

            MaybeLog("SCENE_LOAD_REQUEST:" + normalizedSceneName + ":" + mode);
            return true;
        }

        public bool TryUnloadScene(string sceneName, out string reasonCode)
        {
            reasonCode = string.Empty;
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                reasonCode = "SCENE_NAME_REQUIRED";
                return false;
            }

            var normalizedSceneName = sceneName.Trim();
            var scene = SceneManager.GetSceneByName(normalizedSceneName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                reasonCode = "SCENE_NOT_LOADED";
                return false;
            }

            var operation = SceneManager.UnloadSceneAsync(scene);
            if (operation == null)
            {
                reasonCode = "SCENE_UNLOAD_REQUEST_FAILED";
                return false;
            }

            MaybeLog("SCENE_UNLOAD_REQUEST:" + normalizedSceneName);
            return true;
        }

        public bool TryResetScene(string sceneName, bool additive, out string reasonCode)
        {
            reasonCode = string.Empty;
            if (!TryUnloadScene(sceneName, out var unloadReason) && !string.Equals(unloadReason, "SCENE_NOT_LOADED", StringComparison.Ordinal))
            {
                reasonCode = unloadReason;
                return false;
            }

            if (!TryLoadScene(sceneName, additive, out var loadReason))
            {
                reasonCode = loadReason;
                return false;
            }

            reasonCode = string.Empty;
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
    }
}
