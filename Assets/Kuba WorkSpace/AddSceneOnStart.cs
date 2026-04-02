using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

public class AddSceneOnStart : MonoBehaviour
{
    public string daySceneAddress = "ISLAND_DAY";
    [SerializeField] private float initialRevealDelaySeconds = 2.0f;

    [SerializeField] private FadeOnStart fadeOnStart;
    private AsyncOperationHandle<SceneInstance> loadHandle;
    private bool _initialAdditiveSceneLoaded;
    private bool _initialAdditiveSceneRevealReleased;
    public bool IsInitialAdditiveSceneReady =>
        !isActiveAndEnabled ||
        string.IsNullOrWhiteSpace(daySceneAddress) ||
        (_initialAdditiveSceneLoaded && _initialAdditiveSceneRevealReleased);

    private void Awake()
    {
        // DontDestroyOnLoad(this);
    }
    
    
    void Start()
    {
        if (string.IsNullOrWhiteSpace(daySceneAddress))
        {
            _initialAdditiveSceneLoaded = true;
            _initialAdditiveSceneRevealReleased = true;
            return;
        }

        Addressables.LoadSceneAsync(daySceneAddress, LoadSceneMode.Additive).Completed += handle =>
        {
            loadHandle = handle;
            _initialAdditiveSceneLoaded = true;
            Debug.Log($"[AddSceneOnStart] Initial additive scene loaded: {daySceneAddress} (status={handle.Status})");
            StartCoroutine(ReleaseInitialRevealAfterHold());
        };
    }

    IEnumerator ReleaseInitialRevealAfterHold()
    {
        if (fadeOnStart == null)
        {
            fadeOnStart = FindFirstObjectByType<FadeOnStart>();
        }

        if (fadeOnStart == null || !fadeOnStart.isActiveAndEnabled)
        {
            fadeOnStart = GetFadeOnStart();
        }

        if (initialRevealDelaySeconds > 0f)
        {
            yield return new WaitForSecondsRealtime(initialRevealDelaySeconds);
        }

        _initialAdditiveSceneRevealReleased = true;
        Debug.Log($"[AddSceneOnStart] Initial additive scene reveal released after hold: {daySceneAddress}");

        if (fadeOnStart == null || !fadeOnStart.HasVisibleOverlay)
        {
            Debug.Log("[AddSceneOnStart] Skipping local FadeIn because overlay is already clear.");
            yield break;
        }

        fadeOnStart.FadeIn();
    }

    FadeOnStart GetFadeOnStart()
    {
        var temp = GameObject.FindObjectsByType<FadeOnStart>(FindObjectsSortMode.None);

        foreach (var fos in temp)
        {
            if (fos.isActiveAndEnabled)
                return fos;
        }

        return null;
    }

    void OnDestroy()
    {
        Addressables.UnloadSceneAsync(loadHandle);
    }
}
