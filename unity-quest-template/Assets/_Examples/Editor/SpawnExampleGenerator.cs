using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TheraplyCore.Games.Contracts;
using TheraplyCore.Games.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheraplyExamples.Editor
{
    public static class SpawnExampleGenerator
    {
        private const string SpawnExampleScenePath = "Assets/_Examples/Scenes/SpawnExample.unity";
        private const string SpawnExampleDefinitionPath =
            "Assets/_YourGames/Samples/SessionFlow/SpawnExampleFlowDefinition.asset";
        private const string SpawnCubePrefabPath = "Assets/_Examples/Prefabs/SpawnExampleCube.prefab";

        [MenuItem("Theraply/Examples/Generate Spawn Example")]
        public static void GenerateFromMenu()
        {
            GenerateAll();
        }

        public static void GenerateForCli()
        {
            try
            {
                GenerateAll();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[SpawnExampleGenerator] Generation failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        private static void GenerateAll()
        {
            EnsureDirectoryForAsset(SpawnExampleDefinitionPath);
            EnsureDirectoryForAsset(SpawnExampleScenePath);
            EnsureDirectoryForAsset(SpawnCubePrefabPath);

            var definitionAsset = UpsertSpawnDefinitionAsset();
            var spawnCubePrefab = EnsureSpawnCubePrefab();
            GenerateSpawnExampleScene(definitionAsset, spawnCubePrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SpawnExampleGenerator] Generated SpawnExample scene + flow asset.");
        }

        private static GameDefinitionAsset UpsertSpawnDefinitionAsset()
        {
            var definitionAsset = AssetDatabase.LoadAssetAtPath<GameDefinitionAsset>(SpawnExampleDefinitionPath);
            if (definitionAsset == null)
            {
                definitionAsset = ScriptableObject.CreateInstance<GameDefinitionAsset>();
                AssetDatabase.CreateAsset(definitionAsset, SpawnExampleDefinitionPath);
            }

            definitionAsset.definition = CreateSpawnExampleDefinition();
            EditorUtility.SetDirty(definitionAsset);
            return definitionAsset;
        }

        private static GameDefinition CreateSpawnExampleDefinition()
        {
            var definition = GameDefinition.CreateSample();
            definition.gameId = "spawn_example";
            definition.displayName = "Spawn Example";
            definition.commentVersion = "2026-02-25 spawn example scene";
            definition.controlMode = SessionFlowControlModes.LocalOnly;
            definition.config = new GameDefinitionConfig
            {
                difficulty = "basic",
                timeLimitSec = 30,
                targetCount = 5,
                extras = new List<KeyValuePairString>
                {
                    new KeyValuePairString { key = "sample", value = "spawn_example" },
                },
            };
            definition.channels = SessionFlowDefaults.CreateDefaultChannels();
            definition.policies = SessionFlowPolicies.CreateDefault();
            definition.policies.controlPolicy.mode = SessionFlowControlModes.LocalOnly;

            definition.taskGraph = new TaskGraphDefinition
            {
                entryNodeId = "start",
                nodes = new List<TaskGraphNodeDefinition>
                {
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "start",
                        nodeType = TaskGraphNodeTypes.Action,
                        allowedActions = new List<AllowedActionDefinition>(),
                        conditions = new List<ConditionDefinition>(),
                        timeoutSec = 0.1f,
                        onEnterEffects = new List<EffectDefinition>
                        {
                            new EffectDefinition
                            {
                                effectId = "emit_hint",
                                parameters = new List<KeyValuePairString>
                                {
                                    new KeyValuePairString { key = "message", value = "Starting SpawnExample..." },
                                },
                            },
                        },
                        onExitEffects = new List<EffectDefinition>(),
                        onAcceptedEffects = new List<EffectDefinition>(),
                        onRejectedEffects = new List<EffectDefinition>(),
                        onTimeoutEffects = new List<EffectDefinition>(),
                        nextOnSuccess = "spawn_wave",
                        nextOnFail = "fail",
                        nextOnTimeout = "spawn_wave",
                    },
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "spawn_wave",
                        nodeType = TaskGraphNodeTypes.Timer,
                        allowedActions = new List<AllowedActionDefinition>(),
                        conditions = new List<ConditionDefinition>(),
                        timeoutSec = 4f,
                        onEnterEffects = new List<EffectDefinition>
                        {
                            new EffectDefinition
                            {
                                effectId = "spawn_prefab_wave",
                                parameters = new List<KeyValuePairString>
                                {
                                    new KeyValuePairString { key = "prefabKey", value = "target_prefab" },
                                    new KeyValuePairString { key = "bindingKeyPrefix", value = "targets" },
                                    new KeyValuePairString { key = "spawnPointKey", value = "spawn_center" },
                                    new KeyValuePairString { key = "count", value = "8" },
                                    new KeyValuePairString { key = "durationSec", value = "3.0" },
                                    new KeyValuePairString { key = "areaSizeX", value = "2.8" },
                                    new KeyValuePairString { key = "areaSizeY", value = "1.2" },
                                    new KeyValuePairString { key = "areaSizeZ", value = "2.8" },
                                    new KeyValuePairString { key = "randomYaw", value = "true" },
                                    new KeyValuePairString { key = "randomColorOnSpawn", value = "true" },
                                    new KeyValuePairString { key = "colorIncludeInactive", value = "true" },
                                    new KeyValuePairString { key = "minHue", value = "0.0" },
                                    new KeyValuePairString { key = "maxHue", value = "1.0" },
                                    new KeyValuePairString { key = "minSaturation", value = "0.55" },
                                    new KeyValuePairString { key = "maxSaturation", value = "0.95" },
                                    new KeyValuePairString { key = "minValue", value = "0.60" },
                                    new KeyValuePairString { key = "maxValue", value = "1.00" },
                                    new KeyValuePairString { key = "alpha", value = "1.00" },
                                    new KeyValuePairString { key = "rotationOnSpawn", value = "true" },
                                    new KeyValuePairString { key = "rotationRandom", value = "true" },
                                    new KeyValuePairString { key = "rotationSpeed", value = "1.0" },
                                    new KeyValuePairString { key = "rotationAngleX", value = "0.0" },
                                    new KeyValuePairString { key = "rotationAngleY", value = "90.0" },
                                    new KeyValuePairString { key = "rotationAngleZ", value = "0.0" },
                                    new KeyValuePairString { key = "rotationMinSpeed", value = "0.6" },
                                    new KeyValuePairString { key = "rotationMaxSpeed", value = "1.4" },
                                    new KeyValuePairString { key = "rotationMinAngleX", value = "-120.0" },
                                    new KeyValuePairString { key = "rotationMaxAngleX", value = "120.0" },
                                    new KeyValuePairString { key = "rotationMinAngleY", value = "-120.0" },
                                    new KeyValuePairString { key = "rotationMaxAngleY", value = "120.0" },
                                    new KeyValuePairString { key = "rotationMinAngleZ", value = "-120.0" },
                                    new KeyValuePairString { key = "rotationMaxAngleZ", value = "120.0" },
                                },
                            },
                        },
                        onExitEffects = new List<EffectDefinition>(),
                        onAcceptedEffects = new List<EffectDefinition>(),
                        onRejectedEffects = new List<EffectDefinition>(),
                        onTimeoutEffects = new List<EffectDefinition>(),
                        nextOnSuccess = "complete",
                        nextOnFail = "fail",
                        nextOnTimeout = "complete",
                    },
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "complete",
                        nodeType = TaskGraphNodeTypes.Complete,
                        allowedActions = new List<AllowedActionDefinition>(),
                        conditions = new List<ConditionDefinition>(),
                        timeoutSec = 0f,
                        onEnterEffects = new List<EffectDefinition>(),
                        onExitEffects = new List<EffectDefinition>(),
                        onAcceptedEffects = new List<EffectDefinition>(),
                        onRejectedEffects = new List<EffectDefinition>(),
                        onTimeoutEffects = new List<EffectDefinition>(),
                        nextOnSuccess = string.Empty,
                        nextOnFail = string.Empty,
                        nextOnTimeout = string.Empty,
                    },
                    new TaskGraphNodeDefinition
                    {
                        nodeId = "fail",
                        nodeType = TaskGraphNodeTypes.Fail,
                        allowedActions = new List<AllowedActionDefinition>(),
                        conditions = new List<ConditionDefinition>(),
                        timeoutSec = 0f,
                        onEnterEffects = new List<EffectDefinition>(),
                        onExitEffects = new List<EffectDefinition>(),
                        onAcceptedEffects = new List<EffectDefinition>(),
                        onRejectedEffects = new List<EffectDefinition>(),
                        onTimeoutEffects = new List<EffectDefinition>(),
                        nextOnSuccess = string.Empty,
                        nextOnFail = string.Empty,
                        nextOnTimeout = string.Empty,
                    },
                },
            };

            definition.mobileControlSchema = null;
            return definition;
        }

        private static GameObject EnsureSpawnCubePrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SpawnCubePrefabPath);
            if (prefab != null)
            {
                return prefab;
            }

            var tempCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tempCube.name = "SpawnExampleCube";
            tempCube.transform.localScale = new Vector3(0.28f, 0.28f, 0.28f);
            var renderer = tempCube.GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                renderer.sharedMaterial.color = new Color(0.85f, 0.31f, 0.28f, 1f);
            }

            prefab = PrefabUtility.SaveAsPrefabAsset(tempCube, SpawnCubePrefabPath);
            UnityEngine.Object.DestroyImmediate(tempCube);

            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Failed to create SpawnExample prefab at path: " + SpawnCubePrefabPath);
            }

            return prefab;
        }

        private static void GenerateSpawnExampleScene(
            GameDefinitionAsset definitionAsset,
            GameObject spawnCubePrefab)
        {
            if (definitionAsset == null)
            {
                throw new InvalidOperationException("SpawnExample definition asset is required.");
            }

            if (spawnCubePrefab == null)
            {
                throw new InvalidOperationException(
                    "SpawnExample prefab is required: " + SpawnCubePrefabPath);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            if (!scene.IsValid())
            {
                throw new InvalidOperationException("Failed to create SpawnExample scene.");
            }

            ConfigureMainCamera();

            var root = new GameObject("SpawnExampleRoot");
            root.transform.position = Vector3.zero;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "SpawnExampleFloor";
            floor.transform.SetParent(root.transform, worldPositionStays: false);
            floor.transform.position = new Vector3(0f, 0f, 3.8f);
            floor.transform.localScale = new Vector3(0.55f, 1f, 0.55f);

            var floorRenderer = floor.GetComponent<Renderer>();
            if (floorRenderer != null && floorRenderer.sharedMaterial != null)
            {
                floorRenderer.sharedMaterial.color = new Color(0.17f, 0.26f, 0.18f, 1f);
            }

            var spawnCenter = new GameObject("SpawnCenter");
            spawnCenter.transform.SetParent(root.transform, worldPositionStays: false);
            spawnCenter.transform.position = new Vector3(0f, 1.25f, 3.8f);

            var runtimeHost = new GameObject("SpawnExampleRuntime");
            var flowBindings = runtimeHost.AddComponent<FlowBindingRegistry>();
            var sceneRuntime = runtimeHost.AddComponent<SceneRuntimeController>();
            var effectRunner = runtimeHost.AddComponent<EffectRunner>();
            var actionRegistry = runtimeHost.AddComponent<ActionAdapterRegistry>();
            var configProvider = runtimeHost.AddComponent<FlowConfigProvider>();
            var flowRunner = runtimeHost.AddComponent<SessionFlowRunner>();

            SetNonPublicField(configProvider, "_definitionAsset", definitionAsset);
            SetNonPublicField(configProvider, "_fallbackToSampleWhenMissing", false);
            SetNonPublicField(configProvider, "_preferJsonSource", false);
            SetNonPublicField(configProvider, "_validateOnAwake", true);

            SetNonPublicField(effectRunner, "_flowBindingRegistry", flowBindings);
            SetNonPublicField(effectRunner, "_sceneRuntimeController", sceneRuntime);
            SetNonPublicField(effectRunner, "_registerBuiltInsOnAwake", true);
            SetNonPublicField(effectRunner, "_emitEffectTelemetry", false);

            SetNonPublicField(actionRegistry, "_registerBuiltInPluginsOnAwake", true);
            SetNonPublicField(actionRegistry, "_emitIntentEvents", false);

            SetNonPublicField(flowRunner, "_flowConfigProvider", configProvider);
            SetNonPublicField(flowRunner, "_effectRunner", effectRunner);
            SetNonPublicField(flowRunner, "_actionAdapterRegistry", actionRegistry);
            SetNonPublicField(flowRunner, "_autoStartOnEnable", true);
            SetNonPublicField(flowRunner, "_emitFlowTelemetry", false);
            SetNonPublicField(flowRunner, "_interruptSessionOnDisable", true);

            var prefabBindings = new List<SceneRuntimeController.PrefabBindingEntry>
            {
                new SceneRuntimeController.PrefabBindingEntry
                {
                    key = "target_prefab",
                    prefab = spawnCubePrefab,
                },
            };
            var spawnPointBindings = new List<SceneRuntimeController.TransformBindingEntry>
            {
                new SceneRuntimeController.TransformBindingEntry
                {
                    key = "spawn_center",
                    value = spawnCenter.transform,
                },
            };
            SetNonPublicField(sceneRuntime, "_prefabBindings", prefabBindings);
            SetNonPublicField(sceneRuntime, "_spawnPointBindings", spawnPointBindings);
            SetNonPublicField(sceneRuntime, "_emitSceneTelemetry", false);

            EditorSceneManager.SaveScene(scene, SpawnExampleScenePath, saveAsCopy: false);
        }

        private static void ConfigureMainCamera()
        {
            var mainCamera = Camera.main;
            if (mainCamera == null)
            {
                mainCamera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            }

            if (mainCamera == null)
            {
                return;
            }

            mainCamera.transform.position = new Vector3(0f, 1.6f, -1.9f);
            mainCamera.transform.rotation = Quaternion.Euler(8f, 0f, 0f);
        }

        private static void SetNonPublicField(object target, string fieldName, object value)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(target.GetType().FullName, fieldName);
            }

            field.SetValue(target, value);
        }

        private static void EnsureDirectoryForAsset(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            var full = Path.Combine(Directory.GetCurrentDirectory(), directory);
            Directory.CreateDirectory(full);
        }
    }
}
