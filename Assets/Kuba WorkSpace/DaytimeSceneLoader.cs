using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

public class DaytimeSceneLoader : MonoBehaviour
{
    public string daySceneAddress;
    public string nightSceneAddress;
    public Material daySky;
    public Material nightSky;
    public OVRScreenFade screenFade;
    [SerializeField] private float initialRevealDelaySeconds = 1.0f;

    private AsyncOperationHandle<SceneInstance> sceneHandle;
    private AsyncOperationHandle<SceneInstance> nightSceneHandle;

    [SerializeField] private bool isDaySceneLoaded = false;
    public bool IsDaySceneLoaded => isDaySceneLoaded;
    [SerializeField] private bool isNightSceneLoaded = false;
    public bool IsNightSceneLoaded => isNightSceneLoaded;
    [SerializeField] private bool _initialSceneLoadCompleted = false;
    [SerializeField] private bool _initialSceneRevealReleased = false;
    public bool IsInitialTransitionReady =>
        !isActiveAndEnabled ||
        string.IsNullOrWhiteSpace(daySceneAddress) ||
        (_initialSceneLoadCompleted && _initialSceneRevealReleased);

    [SerializeField] private GameObject hider;

    [SerializeField] private Color dayFogColor;
    [SerializeField] private float dayFogDensity;
    [SerializeField] private Color nightFogColor;
    [SerializeField] private float nightFogDensity;

    private void Awake()
    {
        // screenFade.fadeTime = 0.0f;
        // screenFade.FadeOut();
        _initialSceneLoadCompleted = false;
        _initialSceneRevealReleased = false;
        LoadDayScene();
    }

    [ContextMenu("DAY")]
    public void LoadDayScene()
    {
        if (!isDaySceneLoaded)
        {
            UnloadNightScene();
            StartCoroutine(FadeAndLoadScene(daySceneAddress, daySky, dayFogColor, dayFogDensity));
            isDaySceneLoaded = true;
        }
    }

    public IEnumerator LoadDaySceneEnum()
    {
        if (!isDaySceneLoaded)
        {
            UnloadNightScene();
            yield return StartCoroutine(FadeAndLoadScene(daySceneAddress, daySky, dayFogColor, dayFogDensity));
            isDaySceneLoaded = true;
        }
    }

    [ContextMenu("NIGHT")]
    public void LoadNightScene()
    {
        if (!isNightSceneLoaded)
        {
            UnloadDayScene();
            StartCoroutine(FadeAndLoadScene(nightSceneAddress, nightSky, nightFogColor, nightFogDensity));
            isNightSceneLoaded = true;
        }
    }

    public IEnumerator LoadNightSceneEnum()
    {
        if (!isNightSceneLoaded)
        {
            UnloadDayScene();
            yield return StartCoroutine(FadeAndLoadScene(nightSceneAddress, nightSky, nightFogColor, nightFogDensity));
            isNightSceneLoaded = true;
        }
    }

    private IEnumerator FadeAndLoadScene(string sceneAddress, Material skybox, Color fogColor, float fogDensity)
    {
        // Fade out the screen
        screenFade.FadeOut();

        yield return new WaitForSeconds(screenFade.fadeTime + 0.1f);

        // Load the scene additively
        Addressables.LoadSceneAsync(sceneAddress, LoadSceneMode.Additive).Completed += handle =>
        {
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                if (isNightSceneLoaded)
                {
                    nightSceneHandle = handle;
                }
                else if (isDaySceneLoaded)
                {
                    sceneHandle = handle;
                }
                // Change the skybox
                RenderSettings.skybox = skybox;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogDensity = fogDensity;
                Debug.Log("Scene loaded successfully.");
                hider.SetActive(false);
            }
            else
            {
                Debug.LogError("Failed to load scene: " + handle.OperationException);
            }

            _initialSceneLoadCompleted = true;
            Debug.Log($"[DaytimeSceneLoader] Initial transition scene loaded: {sceneAddress} (status={handle.Status})");

            // Release scene visibility only after the same hold we use for the local fade.
            StartCoroutine(ReleaseInitialRevealAfterHold());
        };
    }

    IEnumerator ReleaseInitialRevealAfterHold()
    {
        if (initialRevealDelaySeconds > 0f)
        {
            yield return new WaitForSecondsRealtime(initialRevealDelaySeconds);
        }

        _initialSceneRevealReleased = true;
        Debug.Log("[DaytimeSceneLoader] Initial transition reveal released after hold.");

        if (screenFade == null || screenFade.currentAlpha <= 0.01f)
        {
            yield break;
        }

        screenFade.FadeIn();
    }

    private void UnloadDayScene()
    {
        if (isDaySceneLoaded)
        {
            StartCoroutine(UnloadDaySceneEnum());
        }
    }

    IEnumerator UnloadDaySceneEnum()
    {
        yield return new WaitForSeconds(OVRScreenFade.instance.fadeTime + 0.1f);
        Addressables.UnloadSceneAsync(sceneHandle);
        isDaySceneLoaded = false;
        Debug.Log("Day scene unloaded.");
    }

    private void UnloadNightScene()
    {
        if (isNightSceneLoaded)
        {
            StartCoroutine(UnloadNightSceneEnum());
        }
    }

    IEnumerator UnloadNightSceneEnum()
    {
        yield return new WaitForSeconds(OVRScreenFade.instance.fadeTime + 0.1f);
        Addressables.UnloadSceneAsync(nightSceneHandle);
        isNightSceneLoaded = false;
        Debug.Log("Night scene unloaded.");
    }
}
