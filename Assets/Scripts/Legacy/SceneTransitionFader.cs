using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Persistent full-screen black fader for scene transitions.
/// Lives on [TheraplyCore] (DontDestroyOnLoad) so it survives scene changes.
///
/// Usage:
///   FadeToBlack(onBlack) → LoadScene → SetSourceCamera → FadeFromBlack()
///
/// Compatible with the existing per-scene FadeOnStart pattern:
/// per-scene FadeOnStart scripts should be REMOVED from game scenes
/// (or their FadeIn call can be left — this fader will override via render queue).
/// </summary>
[DisallowMultipleComponent]
public class SceneTransitionFader : MonoBehaviour
{
    [SerializeField] private float _fadeDuration = 0.5f;
    [SerializeField] private Color _fadeColor = new Color(0f, 0f, 0f, 1f);
    [SerializeField] private int _renderQueue = 5000;
    [SerializeField] private bool _startFullyBlack = true;
    [SerializeField] private float _overscanMultiplier = 1.15f;

    private Material _fadeMaterial;
    private MeshRenderer _fadeRenderer;
    private float _currentAlpha;
    private Coroutine _activeCoroutine;
    private Camera _boundCamera;

    public bool IsFullyBlack => _currentAlpha >= 0.99f;
    public bool IsFullyClear => _currentAlpha <= 0.01f;

    private void Awake()
    {
        _fadeMaterial = new Material(Shader.Find("Oculus/Unlit Transparent Color"));
        _fadeMaterial.renderQueue = _renderQueue;

        var mf = gameObject.AddComponent<MeshFilter>();
        _fadeRenderer = gameObject.AddComponent<MeshRenderer>();
        _fadeRenderer.material = _fadeMaterial;
        _fadeRenderer.enabled = false;

        var mesh = new Mesh();
        mf.mesh = mesh;
        mesh.vertices = new Vector3[]
        {
            new Vector3(-2f, -2f, 1f),
            new Vector3( 2f, -2f, 1f),
            new Vector3(-2f,  2f, 1f),
            new Vector3( 2f,  2f, 1f),
        };
        mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
        mesh.normals = new Vector3[]
        {
            -Vector3.forward, -Vector3.forward,
            -Vector3.forward, -Vector3.forward,
        };
        mesh.uv = new Vector2[]
        {
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(0, 1), new Vector2(1, 1),
        };

        TryBindToCurrentCamera();
        SetAlpha(_startFullyBlack ? 1f : 0f);
    }

    private void LateUpdate()
    {
        SyncToBoundCamera();
    }

    // ------------------------------------------------------------------

    /// <summary>Fade to full black, then call onBlack.</summary>
    public void FadeToBlack(Action onBlack = null)
    {
        if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
        _activeCoroutine = StartCoroutine(FadeRoutine(_currentAlpha, 1f, onBlack));
    }

    /// <summary>Fade from current alpha back to transparent.</summary>
    public void FadeFromBlack(Action onClear = null)
    {
        if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
        _activeCoroutine = StartCoroutine(FadeRoutine(_currentAlpha, 0f, onClear));
    }

    /// <summary>Instantly go black (no animation), then call onBlack.</summary>
    public void SnapToBlack(Action onBlack = null)
    {
        if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
        SetAlpha(1f);
        onBlack?.Invoke();
    }

    public void BindToCamera(Camera camera)
    {
        _boundCamera = camera;
        SyncToBoundCamera();
    }

    public bool TryBindToCurrentCamera()
    {
        var camera = ResolveActiveCamera();
        if (camera == null)
        {
            return false;
        }

        BindToCamera(camera);
        return true;
    }

    // ------------------------------------------------------------------

    private IEnumerator FadeRoutine(float from, float to, Action onDone)
    {
        float elapsed = 0f;
        SetAlpha(from);
        while (elapsed < _fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / _fadeDuration)));
            yield return null;
        }
        SetAlpha(to);
        _activeCoroutine = null;
        onDone?.Invoke();
    }

    private void SetAlpha(float alpha)
    {
        SyncToBoundCamera();
        _currentAlpha = alpha;
        var c = _fadeColor;
        c.a = alpha;
        _fadeMaterial.color = c;
        _fadeRenderer.enabled = alpha > 0.01f;
    }

    private void SyncToBoundCamera()
    {
        if (!IsUsableCamera(_boundCamera))
        {
            _boundCamera = ResolveActiveCamera();
        }

        if (!IsUsableCamera(_boundCamera))
        {
            return;
        }

        var cameraTransform = _boundCamera.transform;
        transform.SetPositionAndRotation(cameraTransform.position, cameraTransform.rotation);
        transform.localScale = ResolveCoverageScale(_boundCamera);
    }

    private Vector3 ResolveCoverageScale(Camera camera)
    {
        if (camera == null)
        {
            return Vector3.one;
        }

        const float meshHalfExtent = 2f;
        const float quadDistance = 1f;
        const float minScale = 1f;

        var verticalHalfExtent = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * quadDistance;
        var horizontalHalfExtent = verticalHalfExtent * Mathf.Max(camera.aspect, 1f);

        var requiredScaleX = Mathf.Max(minScale, (horizontalHalfExtent * _overscanMultiplier) / meshHalfExtent);
        var requiredScaleY = Mathf.Max(minScale, (verticalHalfExtent * _overscanMultiplier) / meshHalfExtent);

        return new Vector3(requiredScaleX, requiredScaleY, 1f);
    }

    private static Camera ResolveActiveCamera()
    {
        if (IsUsableCamera(Camera.main))
        {
            return Camera.main;
        }

        foreach (Camera candidate in Camera.allCameras)
        {
            if (IsUsableCamera(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsUsableCamera(Camera camera)
    {
        if (camera == null || !camera.enabled || !camera.gameObject.activeInHierarchy)
        {
            return false;
        }

        var scene = camera.gameObject.scene;
        return scene.IsValid() && scene.isLoaded;
    }
}
