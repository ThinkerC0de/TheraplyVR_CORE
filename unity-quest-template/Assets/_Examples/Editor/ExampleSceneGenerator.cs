using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheraplyExamples.Editor
{
    public static class ExampleSceneGenerator
    {
        private const string MainTemplateScenePath = "Assets/_Examples/Scenes/DemoCubeScene.unity";
        private const string MainScenePath = "Assets/_Examples/Scenes/MainScene.unity";
        private const string CubeClickerVrScenePath = "Assets/_Examples/Scenes/CubeClickerVR.unity";
        private const string PulseTargetsScenePath = "Assets/_Examples/Scenes/PulseTargetsScene.unity";

        [MenuItem("Theraply/Examples/Generate Main + Game Scenes")]
        public static void GenerateFromMenu()
        {
            GenerateAll();
        }

        public static void GenerateForCli()
        {
            try
            {
                GenerateAll();
                UnityEditor.EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"[ExampleSceneGenerator] Generation failed: {exception}");
                UnityEditor.EditorApplication.Exit(1);
            }
        }

        private static void GenerateAll()
        {
            GenerateMainScene();
            GenerateGameplayScene(
                CubeClickerVrScenePath,
                "CubeClickerVRRoot",
                new Color(0.17f, 0.30f, 0.46f, 1f));
            GenerateGameplayScene(
                PulseTargetsScenePath,
                "PulseTargetsSceneRoot",
                new Color(0.22f, 0.16f, 0.12f, 1f));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ExampleSceneGenerator] Generated MainScene + gameplay scenes.");
        }

        private static void GenerateMainScene()
        {
            if (!System.IO.File.Exists(MainTemplateScenePath))
            {
                throw new System.InvalidOperationException(
                    $"Missing template scene: {MainTemplateScenePath}");
            }

            var templateScene = EditorSceneManager.OpenScene(MainTemplateScenePath, OpenSceneMode.Single);
            if (!templateScene.IsValid())
            {
                throw new System.InvalidOperationException(
                    $"Failed to open template scene: {MainTemplateScenePath}");
            }

            EditorSceneManager.SaveScene(templateScene, MainScenePath, saveAsCopy: true);
        }

        private static void GenerateGameplayScene(string outputPath, string rootName, Color floorColor)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!scene.IsValid())
            {
                throw new System.InvalidOperationException($"Failed to create scene: {outputPath}");
            }

            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.95f, 0.84f, 1f);
            light.intensity = 1f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var root = new GameObject(rootName);
            root.transform.position = Vector3.zero;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "GameplayFloor";
            floor.transform.SetParent(root.transform, worldPositionStays: false);
            floor.transform.position = new Vector3(0f, 0f, 4f);
            floor.transform.localScale = new Vector3(0.35f, 1f, 0.35f);
            var floorRenderer = floor.GetComponent<Renderer>();
            if (floorRenderer != null)
            {
                var mat = floorRenderer.sharedMaterial;
                if (mat != null)
                {
                    mat.color = floorColor;
                }
            }

            var playerAnchor = new GameObject("PlayerSpawnAnchor");
            playerAnchor.transform.SetParent(root.transform, worldPositionStays: false);
            playerAnchor.transform.position = new Vector3(0f, 1.2f, -1.8f);

            var guideAnchor = new GameObject("GuideSpawnAnchor");
            guideAnchor.transform.SetParent(root.transform, worldPositionStays: false);
            guideAnchor.transform.position = new Vector3(-1.2f, 1f, 0.2f);

            EditorSceneManager.SaveScene(scene, outputPath, saveAsCopy: false);
        }
    }
}
