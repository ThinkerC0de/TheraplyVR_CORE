using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheraplyCore.Games.Runtime;

namespace TheraplyExamples
{
    [Serializable]
    public sealed class ExampleGameSceneBinding
    {
        public string gameId;
        public string scenePath;
    }

    /// <summary>
    /// Example-only additive scene switching driven by currently active gameId.
    /// Keeps orchestration outside TheraplyCore.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ExampleAdditiveSceneRouter : MonoBehaviour
    {
        [SerializeField] private GameRuntimeService _runtimeService;
        [SerializeField] private bool _unloadWhenNoGameScene = false;
        [SerializeField] private bool _logSceneSwitches = true;
        [SerializeField] private List<ExampleGameSceneBinding> _sceneBindings =
            new List<ExampleGameSceneBinding>();

        private bool _sceneSwitchInProgress;
        private string _loadedScenePath = string.Empty;

        private void Awake()
        {
            if (_runtimeService == null)
            {
                _runtimeService = FindFirstObjectByType<GameRuntimeService>();
            }

            EnsureDefaultBindings();
        }

        private void Update()
        {
            if (_sceneSwitchInProgress)
            {
                return;
            }

            if (_runtimeService == null)
            {
                _runtimeService = FindFirstObjectByType<GameRuntimeService>();
                if (_runtimeService == null)
                {
                    return;
                }
            }

            var desiredScenePath = ResolveScenePath(_runtimeService.ActiveGameId);
            if (string.Equals(desiredScenePath, _loadedScenePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(desiredScenePath) && !_unloadWhenNoGameScene)
            {
                return;
            }

            StartCoroutine(SwitchSceneCoroutine(desiredScenePath));
        }

        private IEnumerator SwitchSceneCoroutine(string desiredScenePath)
        {
            _sceneSwitchInProgress = true;

            if (!string.IsNullOrWhiteSpace(_loadedScenePath))
            {
                var loadedName = ExtractSceneName(_loadedScenePath);
                if (!string.IsNullOrWhiteSpace(loadedName))
                {
                    var loadedScene = SceneManager.GetSceneByName(loadedName);
                    if (loadedScene.IsValid() && loadedScene.isLoaded)
                    {
                        var unloadOperation = SceneManager.UnloadSceneAsync(loadedScene);
                        if (unloadOperation != null)
                        {
                            yield return unloadOperation;
                        }
                    }
                }

                _loadedScenePath = string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(desiredScenePath))
            {
                var loadOperation = SceneManager.LoadSceneAsync(desiredScenePath, LoadSceneMode.Additive);
                if (loadOperation != null)
                {
                    yield return loadOperation;
                    _loadedScenePath = desiredScenePath;
                }
            }

            if (_logSceneSwitches)
            {
                Debug.Log(
                    $"[ExampleSceneRouter] activeGameId={_runtimeService.ActiveGameId}, loadedScene={_loadedScenePath}");
            }

            _sceneSwitchInProgress = false;
        }

        private string ResolveScenePath(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId) || _sceneBindings == null || _sceneBindings.Count == 0)
            {
                return string.Empty;
            }

            for (var i = 0; i < _sceneBindings.Count; i++)
            {
                var binding = _sceneBindings[i];
                if (binding == null ||
                    string.IsNullOrWhiteSpace(binding.gameId) ||
                    string.IsNullOrWhiteSpace(binding.scenePath))
                {
                    continue;
                }

                if (string.Equals(binding.gameId.Trim(), gameId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return binding.scenePath.Trim();
                }
            }

            return string.Empty;
        }

        private void EnsureDefaultBindings()
        {
            if (_sceneBindings == null)
            {
                _sceneBindings = new List<ExampleGameSceneBinding>();
            }

            EnsureBinding(
                DemoCubeGameConfig.DefaultGameId,
                "Assets/_Examples/Scenes/ExampleCubeScene.unity");
            EnsureBinding(
                PulseTargetsGameConfig.DefaultGameId,
                "Assets/_Examples/Scenes/PulseTargetsScene.unity");
        }

        private void EnsureBinding(string gameId, string scenePath)
        {
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(scenePath))
            {
                return;
            }

            for (var i = 0; i < _sceneBindings.Count; i++)
            {
                var existing = _sceneBindings[i];
                if (existing == null || string.IsNullOrWhiteSpace(existing.gameId))
                {
                    continue;
                }

                if (string.Equals(existing.gameId.Trim(), gameId, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(existing.scenePath))
                    {
                        existing.scenePath = scenePath;
                    }
                    return;
                }
            }

            _sceneBindings.Add(new ExampleGameSceneBinding
            {
                gameId = gameId,
                scenePath = scenePath,
            });
        }

        private static string ExtractSceneName(string scenePath)
        {
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                return string.Empty;
            }

            var normalized = scenePath.Replace('\\', '/');
            var fileName = System.IO.Path.GetFileNameWithoutExtension(normalized);
            return string.IsNullOrWhiteSpace(fileName) ? string.Empty : fileName.Trim();
        }
    }

    public static class ExampleSceneRouterBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRouter()
        {
            var existing = UnityEngine.Object.FindFirstObjectByType<ExampleAdditiveSceneRouter>();
            if (existing != null)
            {
                return;
            }

            var host = new GameObject("ExampleSceneRouter");
            host.AddComponent<ExampleAdditiveSceneRouter>();
        }
    }
}
