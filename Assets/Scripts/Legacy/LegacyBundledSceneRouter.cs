using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheraplyCore.Games.Runtime;
using TheraplyCore.Streaming;
using GameContracts = TheraplyCore.Games.Contracts;

/// <summary>
/// Handles scene loading/unloading for legacy bundled games (Build Settings scenes).
///
/// CORE's GameRuntimeService skips scene loading when installedBundlePath is empty.
/// This router fills that gap:
///   - Subscribes to PREPARE_GAME → loads the correct scene (non-additive)
///   - After scene load → calls GameRegistryService.RebuildRegistry()
///     so the game's XxxGameModule is discovered
///
/// Also handles returning to StartScene after a game ends.
/// </summary>
[DisallowMultipleComponent]
public class LegacyBundledSceneRouter : MonoBehaviour
{
    private static LegacyBundledSceneRouter _persistentInstance;

    [Serializable]
    public class GameSceneEntry
    {
        public string gameId;
        public string sceneName; // must match scene name in Build Settings
    }

    [SerializeField] private List<GameSceneEntry> _sceneMap = new List<GameSceneEntry>();
    [SerializeField] private string _menuSceneName = "StartScene";
    [SerializeField] private GameCommandBus _commandBus;
    [SerializeField] private GameRegistryService _registryService;
    [SerializeField] private GameRuntimeService _runtimeService;
    [SerializeField] private SceneTransitionFader _fader;
    [SerializeField] private SceneTransitionAudioManager _transitionAudioManager;

    private string _currentGameScene;
    private string _pendingPreparedScene;
    private Coroutine _finalizeSceneLoadCoroutine;
    private bool _subscriptionsAttached;
    private bool _initialSceneFinalizeRequested;

    private void Awake()
    {
        if (_persistentInstance != null && _persistentInstance != this)
        {
            Debug.LogWarning("[SceneRouter] Duplicate [TheraplyCore] detected after scene load. Destroying duplicate root.");
            Destroy(gameObject);
            return;
        }

        _persistentInstance = this;
        DontDestroyOnLoad(gameObject);

        if (_commandBus == null) _commandBus = GetComponent<GameCommandBus>();
        if (_registryService == null) _registryService = GetComponent<GameRegistryService>();
        if (_runtimeService == null) _runtimeService = GetComponent<GameRuntimeService>();
        if (_fader == null) _fader = GetComponent<SceneTransitionFader>();
        if (_transitionAudioManager == null) _transitionAudioManager = FindFirstObjectByType<SceneTransitionAudioManager>();

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (_subscriptionsAttached && _commandBus != null)
        {
            _commandBus.Unsubscribe<GameContracts.PrepareGameCommand>(OnPrepareGame);
            _commandBus.Unsubscribe<GameContracts.StopGameCommand>(OnStopGame);
            _commandBus.Unsubscribe<GameContracts.EndSessionCommand>(OnEndSession);
            _subscriptionsAttached = false;
        }

        if (_finalizeSceneLoadCoroutine != null)
        {
            StopCoroutine(_finalizeSceneLoadCoroutine);
            _finalizeSceneLoadCoroutine = null;
        }

        if (_persistentInstance == this)
            _persistentInstance = null;

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        if (_commandBus == null || _subscriptionsAttached)
            return;

        _commandBus.Subscribe<GameContracts.PrepareGameCommand>(OnPrepareGame);
        _commandBus.Subscribe<GameContracts.StopGameCommand>(OnStopGame);
        _commandBus.Subscribe<GameContracts.EndSessionCommand>(OnEndSession);
        _subscriptionsAttached = true;

        TryFinalizeInitialScene();
    }

    // ------------------------------------------------------------------

    private void OnPrepareGame(GameContracts.PrepareGameCommand cmd)
    {
        var entry = FindEntry(cmd.gameId);
        if (entry == null)
        {
            Debug.LogWarning($"[SceneRouter] No scene mapping for gameId: {cmd.gameId}");
            return;
        }

        StartSceneTransition(
            entry.sceneName,
            playTransitionNarration: true,
            markPreparedScene: true,
            allowFinalizeCurrentScene: true);
    }

    private void OnEndSession(GameContracts.EndSessionCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(_menuSceneName))
        {
            Debug.LogWarning("[SceneRouter] END_SESSION ignored because menu scene is not configured.");
            return;
        }

        ReturnToMenuScene("END_SESSION");
    }

    private void OnStopGame(GameContracts.StopGameCommand cmd)
    {
        if (!ShouldReturnToMenuForStopReason(cmd == null ? string.Empty : cmd.reason))
        {
            return;
        }

        ReturnToMenuScene(cmd == null ? "STOP_GAME" : $"STOP_GAME_{cmd.reason ?? string.Empty}");
    }

    public void LoadSceneByBuildIndex(int buildIndex, bool playTransitionNarration = false)
    {
        var scenePath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            Debug.LogWarning($"[SceneRouter] Cannot resolve scene path for build index: {buildIndex}");
            return;
        }

        var sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
        LoadSceneByName(sceneName, playTransitionNarration);
    }

    public void LoadSceneByName(string sceneName, bool playTransitionNarration = false)
    {
        StartSceneTransition(
            sceneName,
            playTransitionNarration: playTransitionNarration,
            markPreparedScene: false,
            allowFinalizeCurrentScene: false);
    }

    private IEnumerator LoadSceneWithFade(string sceneName, bool playTransitionNarration)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            yield break;
        }

        if (playTransitionNarration)
        {
            yield return PlaySceneTransitionNarration(sceneName);
        }

        if (_fader != null)
        {
            _fader.TryBindToCurrentCamera();
            bool fadeComplete = false;
            _fader.FadeToBlack(() => fadeComplete = true);
            yield return new WaitUntil(() => fadeComplete);
        }

        var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        if (op == null)
        {
            Debug.LogError($"[SceneRouter] Failed to load scene: {sceneName}");
            _fader?.FadeFromBlack();
            yield break;
        }
        yield return op;
        // OnSceneLoaded fires here — camera switched, registry rebuilt.
        // Fade-out is triggered from OnSceneLoaded.
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SuppressLegacySceneFade();
        _fader?.TryBindToCurrentCamera();

        if (mode == LoadSceneMode.Additive)
        {
            return;
        }

        StartFinalizeSceneLoad(scene);
    }

    private void StartFinalizeSceneLoad(Scene scene)
    {
        if (_finalizeSceneLoadCoroutine != null)
        {
            StopCoroutine(_finalizeSceneLoadCoroutine);
        }

        _finalizeSceneLoadCoroutine = StartCoroutine(FinalizeSceneLoad(scene));
    }

    private void TryFinalizeInitialScene()
    {
        if (_initialSceneFinalizeRequested)
        {
            return;
        }

        var activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !activeScene.isLoaded)
        {
            return;
        }

        _initialSceneFinalizeRequested = true;
        SuppressLegacySceneFade();
        _fader?.TryBindToCurrentCamera();
        _fader?.SnapToBlack();
        Debug.Log($"[SceneRouter] Finalizing initial scene bootstrap for '{activeScene.name}' from black.");
        StartFinalizeSceneLoad(activeScene);
    }

    private IEnumerator FinalizeSceneLoad(Scene scene)
    {
        yield return RebindSceneCameraTargets();
        SuppressLegacySceneFade();
        yield return null;
        _fader?.TryBindToCurrentCamera();

        ResolveRegistryService()?.RebuildRegistry();
        ResolveRuntimeService()?.RefreshSceneBindings();
        yield return WaitForLegacySceneReadiness(scene);
        SuppressLegacySceneFade();
        yield return null;
        _fader?.TryBindToCurrentCamera();

        Action onFadeCleared = null;
        if (!string.IsNullOrWhiteSpace(_pendingPreparedScene) &&
            string.Equals(scene.name, _pendingPreparedScene, StringComparison.OrdinalIgnoreCase))
        {
            var preparedSceneName = _pendingPreparedScene;
            onFadeCleared = () =>
            {
                Debug.Log($"[SceneRouter] Fade-out finished for prepared scene '{preparedSceneName}'. Notifying mobile.");
                ResolveRuntimeService()?.NotifyPreparedGameSceneReady("LEGACY_PREPARE_GAME_SCENE_READY");
                _pendingPreparedScene = string.Empty;
            };
        }

        if (_fader != null)
        {
            _fader.FadeFromBlack(onFadeCleared);
        }
        else
        {
            onFadeCleared?.Invoke();
        }
        _finalizeSceneLoadCoroutine = null;
    }

    private IEnumerator WaitForLegacySceneReadiness(Scene scene)
    {
        const float timeoutSeconds = 8f;
        float waitedSeconds = 0f;
        bool loggedWait = false;

        while (waitedSeconds < timeoutSeconds)
        {
            if (IsLegacySceneReady(scene))
            {
                if (loggedWait)
                {
                    Debug.Log($"[SceneRouter] Legacy scene '{scene.name}' finished deferred initialization after {waitedSeconds:F2}s.");
                }

                yield break;
            }

            if (!loggedWait)
            {
                Debug.Log($"[SceneRouter] Waiting for deferred scene initialization in '{scene.name}' before fade-out.");
                loggedWait = true;
            }

            waitedSeconds += Time.unscaledDeltaTime;
            yield return null;
        }

        Debug.LogWarning($"[SceneRouter] Timed out waiting for deferred scene initialization in '{scene.name}'. Continuing transition.");
    }

    private bool IsLegacySceneReady(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return false;
        }

        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == null)
            {
                continue;
            }

            foreach (var addSceneOnStart in root.GetComponentsInChildren<AddSceneOnStart>(true))
            {
                if (addSceneOnStart != null && !addSceneOnStart.IsInitialAdditiveSceneReady)
                {
                    return false;
                }
            }

            foreach (var daytimeSceneLoader in root.GetComponentsInChildren<DaytimeSceneLoader>(true))
            {
                if (daytimeSceneLoader != null && !daytimeSceneLoader.IsInitialTransitionReady)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void ReturnToMenuScene(string reasonCode)
    {
        if (string.IsNullOrWhiteSpace(_menuSceneName))
        {
            Debug.LogWarning($"[SceneRouter] {reasonCode} ignored because menu scene is not configured.");
            return;
        }

        StartSceneTransition(
            _menuSceneName,
            playTransitionNarration: false,
            markPreparedScene: false,
            allowFinalizeCurrentScene: true);
    }

    private bool ShouldReturnToMenuForStopReason(string reason)
    {
        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? string.Empty
            : reason.Trim().ToUpperInvariant();

        return normalizedReason == "RETURN_TO_MENU" ||
               normalizedReason == "RETURN_TO_CATALOG";
    }

    private IEnumerator RebindSceneCameraTargets()
    {
        const float timeoutSeconds = 2f;
        float remaining = timeoutSeconds;
        while (remaining > 0f)
        {
            var cam = FindActiveSceneCamera();
            if (cam != null)
            {
                _fader?.BindToCamera(cam);

                var streaming = ResolveMediaStreamService();
                if (streaming != null)
                {
                    streaming.SetSourceCamera(cam);
                }
                yield break;
            }

            remaining -= Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private Camera FindActiveSceneCamera()
    {
        var mainCamera = Camera.main;
        if (mainCamera != null && mainCamera.enabled && mainCamera.gameObject.scene.IsValid() &&
            mainCamera.gameObject.scene.isLoaded)
        {
            return mainCamera;
        }

        foreach (Camera candidate in Camera.allCameras)
        {
            if (candidate == null || !candidate.enabled)
            {
                continue;
            }

            var cameraScene = candidate.gameObject.scene;
            if (cameraScene.IsValid() && cameraScene.isLoaded)
            {
                return candidate;
            }
        }

        return null;
    }

    private void SuppressLegacySceneFade()
    {
        foreach (var fadeOnStart in FindObjectsByType<FadeOnStart>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            fadeOnStart.SuppressTransitionFade();
        }

        foreach (var ovrScreenFade in FindObjectsByType<OVRScreenFade>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            ovrScreenFade.fadeOnStart = false;
            ClearOvrScreenFade(ovrScreenFade);
        }
    }

    private IEnumerator PlaySceneTransitionNarration(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName) ||
            string.Equals(sceneName, _menuSceneName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(SceneManager.GetActiveScene().name, _menuSceneName, StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        _transitionAudioManager ??= FindFirstObjectByType<SceneTransitionAudioManager>();
        if (_transitionAudioManager == null || VirtualFriend.Instance == null)
        {
            yield break;
        }

        yield return new WaitWhile(() => VirtualFriend.Instance != null && VirtualFriend.Instance.IsTalking);

        var (tableReference, dialogKey, waitTime) = _transitionAudioManager.GetAudioParamsBySceneName(sceneName);
        if (string.IsNullOrWhiteSpace(tableReference) || string.IsNullOrWhiteSpace(dialogKey))
        {
            yield break;
        }

        yield return VirtualFriend.Instance.FriendTalkingOtherTable(tableReference, dialogKey, waitTime);
    }

    private static void ClearOvrScreenFade(OVRScreenFade fade)
    {
        if (fade == null)
        {
            return;
        }

        fade.StopAllCoroutines();
        var previousFadeTime = fade.fadeTime;
        fade.fadeTime = 0f;
        fade.SetExplicitFade(0f);
        fade.SetUIFade(0f);
        fade.FadeIn();
        fade.fadeTime = previousFadeTime;
    }

    private void StartSceneTransition(
        string sceneName,
        bool playTransitionNarration,
        bool markPreparedScene,
        bool allowFinalizeCurrentScene)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("[SceneRouter] Scene transition ignored because target scene name is empty.");
            return;
        }

        var isMenuScene = string.Equals(sceneName, _menuSceneName, StringComparison.OrdinalIgnoreCase);
        _currentGameScene = isMenuScene ? string.Empty : sceneName;
        _pendingPreparedScene = markPreparedScene ? sceneName : string.Empty;

        if (markPreparedScene && string.Equals(SceneManager.GetActiveScene().name, sceneName, StringComparison.OrdinalIgnoreCase))
        {
            StartFinalizeSceneLoad(SceneManager.GetActiveScene());
            return;
        }

        if (string.Equals(SceneManager.GetActiveScene().name, sceneName, StringComparison.OrdinalIgnoreCase))
        {
            if (allowFinalizeCurrentScene)
            {
                StartFinalizeSceneLoad(SceneManager.GetActiveScene());
            }

            return;
        }

        StartCoroutine(LoadSceneWithFade(sceneName, playTransitionNarration));
    }

    private GameRuntimeService ResolveRuntimeService()
    {
        if (_runtimeService == null && _persistentInstance != null)
        {
            _runtimeService = _persistentInstance.GetComponent<GameRuntimeService>();
        }

        _runtimeService ??= GetComponent<GameRuntimeService>();

        if (_runtimeService == null)
        {
            foreach (var candidate in FindObjectsByType<GameRuntimeService>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (candidate == null)
                {
                    continue;
                }

                if (_persistentInstance != null &&
                    candidate.gameObject == _persistentInstance.gameObject)
                {
                    _runtimeService = candidate;
                    break;
                }

                if (candidate.gameObject.scene.name == "DontDestroyOnLoad")
                {
                    _runtimeService = candidate;
                    break;
                }

                _runtimeService ??= candidate;
            }
        }

        return _runtimeService;
    }

    private GameRegistryService ResolveRegistryService()
    {
        if (_registryService == null && _persistentInstance != null)
        {
            _registryService = _persistentInstance.GetComponent<GameRegistryService>();
        }

        _registryService ??= GetComponent<GameRegistryService>();

        if (_registryService == null)
        {
            foreach (var candidate in FindObjectsByType<GameRegistryService>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (candidate == null)
                {
                    continue;
                }

                if (_persistentInstance != null &&
                    candidate.gameObject == _persistentInstance.gameObject)
                {
                    _registryService = candidate;
                    break;
                }

                if (candidate.gameObject.scene.name == "DontDestroyOnLoad")
                {
                    _registryService = candidate;
                    break;
                }

                _registryService ??= candidate;
            }
        }

        return _registryService;
    }

    private MediaStreamService ResolveMediaStreamService()
    {
        if (_persistentInstance != null)
        {
            var persistentStreaming = _persistentInstance.GetComponent<MediaStreamService>();
            if (persistentStreaming != null)
            {
                return persistentStreaming;
            }
        }

        var localStreaming = GetComponent<MediaStreamService>();
        if (localStreaming != null)
        {
            return localStreaming;
        }

        MediaStreamService fallback = null;
        foreach (var candidate in FindObjectsByType<MediaStreamService>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (candidate == null)
            {
                continue;
            }

            if (_persistentInstance != null &&
                candidate.gameObject == _persistentInstance.gameObject)
            {
                return candidate;
            }

            if (candidate.gameObject.scene.name == "DontDestroyOnLoad")
            {
                return candidate;
            }

            fallback ??= candidate;
        }

        return fallback;
    }

    private GameSceneEntry FindEntry(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId)) return null;
        foreach (var e in _sceneMap)
        {
            if (string.Equals(e.gameId, gameId, StringComparison.OrdinalIgnoreCase))
                return e;
        }
        return null;
    }
}
