using System.Collections;
using UnityEngine;

public class FadeOnStart : MonoBehaviour
{
    private MeshRenderer fadeRenderer;
    private MeshFilter fadeMesh;
    [SerializeField] Material fadeMaterial;
    [SerializeField] private float overscanMultiplier = 1.15f;
    public float fadeTime = 2.0f;
    private float animatedFadeAlpha = 0.0f;
    private float explicitFadeAlpha = 0.0f;
    public Color fadeColor = new Color(0.01f, 0.01f, 0.01f, 1.0f);
    private float uiFadeAlpha = 0.0f;
    public int renderQueue = 5000;
    private bool isFading = false;
    private bool suppressInitialFade = false;
    public float currentAlpha { get { return Mathf.Max(explicitFadeAlpha, animatedFadeAlpha, uiFadeAlpha); } }
    public bool HasVisibleOverlay => GetDisplayedAlpha() > 0.01f;
    // Start is called before the first frame update
    void Start()
    {
        if (fadeMaterial == null)
        {
            fadeMaterial = new Material(Shader.Find("Oculus/Unlit Transparent Color"));
        }

        fadeMaterial.color = fadeColor;
        fadeMesh = gameObject.AddComponent<MeshFilter>();
        fadeRenderer = gameObject.AddComponent<MeshRenderer>();
        fadeRenderer.material = fadeMaterial;

        var mesh = new Mesh();
        fadeMesh.mesh = mesh;
        // fadeRenderer.material = fadeMaterial;

        Vector3[] vertices = new Vector3[4];

        float width = 2f;
        float height = 2f;
        float depth = 1f;

        vertices[0] = new Vector3(-width, -height, depth);
        vertices[1] = new Vector3(width, -height, depth);
        vertices[2] = new Vector3(-width, height, depth);
        vertices[3] = new Vector3(width, height, depth);

        mesh.vertices = vertices;

        int[] tri = new int[6];

        tri[0] = 0;
        tri[1] = 2;
        tri[2] = 1;

        tri[3] = 2;
        tri[4] = 3;
        tri[5] = 1;

        mesh.triangles = tri;

        Vector3[] normals = new Vector3[4];

        normals[0] = -Vector3.forward;
        normals[1] = -Vector3.forward;
        normals[2] = -Vector3.forward;
        normals[3] = -Vector3.forward;

        mesh.normals = normals;

        Vector2[] uv = new Vector2[4];

        uv[0] = new Vector2(0, 0);
        uv[1] = new Vector2(1, 0);
        uv[2] = new Vector2(0, 1);
        uv[3] = new Vector2(1, 1);

        mesh.uv = uv;

        if (suppressInitialFade)
        {
            ClearImmediately();
        }

        UpdateOverlayCoverage();
    }

    void LateUpdate()
    {
        UpdateOverlayCoverage();
    }

    [ContextMenu("SHOW!")]
    public void FadeIn()
    {
        var startAlpha = GetDisplayedAlpha();
        if (startAlpha <= 0.01f)
        {
            ClearImmediately();
            return;
        }

        StartCoroutine(Fade(startAlpha, 0.0f));
    }

    public void SuppressTransitionFade()
    {
        suppressInitialFade = true;

        if (fadeRenderer != null)
        {
            ClearImmediately();
        }
    }

    public void ClearImmediately()
    {
        StopAllCoroutines();
        animatedFadeAlpha = 0.0f;
        explicitFadeAlpha = 0.0f;
        uiFadeAlpha = 0.0f;
        SetMaterialAlpha();
    }


    IEnumerator Fade(float startAlpha, float endAlpha)
    {
        float elapsedTime = 0.0f;
        while (elapsedTime < fadeTime)
        {
            elapsedTime += Time.unscaledDeltaTime;
            animatedFadeAlpha = Mathf.Lerp(startAlpha, endAlpha, Mathf.Clamp01(elapsedTime / fadeTime));
            SetMaterialAlpha();
            yield return new WaitForEndOfFrame();
        }
        animatedFadeAlpha = endAlpha;
        SetMaterialAlpha();
    }

    private void SetMaterialAlpha()
    {
        Color color = fadeColor;
        color.a = currentAlpha;
        isFading = color.a > 0;
        if (fadeMaterial != null)
        {
            fadeMaterial.color = color;
            fadeMaterial.renderQueue = renderQueue;
            fadeRenderer.material = fadeMaterial;
            fadeRenderer.enabled = isFading;
        }
    }

    private float GetDisplayedAlpha()
    {
        if (fadeRenderer != null && fadeRenderer.enabled && fadeMaterial != null)
        {
            return fadeMaterial.color.a;
        }

        return currentAlpha;
    }

    private void UpdateOverlayCoverage()
    {
        var cameraComponent = GetComponent<Camera>();
        if (cameraComponent == null)
        {
            cameraComponent = Camera.main;
        }

        if (cameraComponent == null)
        {
            return;
        }

        const float meshHalfExtent = 2f;
        const float quadDistance = 1f;
        const float minScale = 1f;

        var verticalHalfExtent =
            Mathf.Tan(cameraComponent.fieldOfView * 0.5f * Mathf.Deg2Rad) *
            quadDistance;
        var horizontalHalfExtent =
            verticalHalfExtent * Mathf.Max(cameraComponent.aspect, 1f);

        var scaleX =
            Mathf.Max(
                minScale,
                (horizontalHalfExtent * overscanMultiplier) / meshHalfExtent);
        var scaleY =
            Mathf.Max(
                minScale,
                (verticalHalfExtent * overscanMultiplier) / meshHalfExtent);

        transform.localScale = new Vector3(scaleX, scaleY, 1f);
    }

}
