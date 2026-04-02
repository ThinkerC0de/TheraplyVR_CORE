using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class MaterialPulse : MonoBehaviour
{
    public new Renderer renderer;

    [Header("Pulse Settings")]
    [SerializeField] private Color pulseColor = Color.white;
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField] private float maxIntensity = 1f;

    [Header("Fade Settings")]
    [SerializeField] private float fadeSpeed = 1f;

    private Material[] material;
    private List<Color> originalColor = new List<Color>();
    private bool isPulsing = false;
    private Coroutine pulseCoroutine;

    private static readonly string emissionColor = "_EmissionColor";
    private static readonly string emission = "_EMISSION";

    private void Awake()
    {

        // Pobierz materia� i w��cz emisj�
        if (renderer)
            material = renderer.materials;
        else
        {
            material = GetComponent<Renderer>().materials;
        }

        for (int i = 0; i < material.Length; i++)
        {
            material[i].EnableKeyword(emission);
            originalColor.Add(material[i].GetColor(emissionColor));
        }
    }

    public void StartPulsing()
    {
        //Debug.Log("Start Pulsing");
        if (pulseCoroutine != null)
        {
            StopCoroutine(pulseCoroutine);
        }
        isPulsing = true;
        pulseCoroutine = StartCoroutine(PulseRoutine());
    }

    public void StopPulsing()
    {
        //Debug.Log("Stop Pulsing");
        /*if (pulseCoroutine != null)
        {
            StopCoroutine(pulseCoroutine);
        }*/
        isPulsing = false;
        pulseCoroutine = StartCoroutine(FadeOutRoutine());
    }

    private IEnumerator PulseRoutine()
    {
        while (isPulsing)
        {
            // Moduluj intensywno�� w czasie
            float intensity = (Mathf.Sin(Time.time * pulseSpeed) + 1f) / 2f * maxIntensity;

            for (int i = 0; i < material.Length; i++)
            {
                material[i].SetColor(emissionColor, pulseColor * intensity);
            }
            yield return null;
        }
    }

    private IEnumerator FadeOutRoutine()
    {
        List<Color> currentColor = new List<Color>();

        for (int i = 0; i < material.Length; i++)
        {
            currentColor.Add(material[i].GetColor(emissionColor));
        }

        float elapsedTime = 0f;

        while (elapsedTime < 1f)
        {
            elapsedTime += Time.deltaTime * fadeSpeed;
            for (int i = 0; i < material.Length; i++)
            {
                currentColor[i] = material[i].GetColor(emissionColor);
                material[i].SetColor(emissionColor, Color.Lerp(currentColor[i], originalColor[i], elapsedTime));
            }
            yield return null;
        }

        for (int i = 0; i < material.Length; i++)
        {
            material[i].SetColor(emissionColor, originalColor[i]);
        }
    }

    public bool IsPulsing()
    {
        return isPulsing;
    }

    private void OnDestroy()
    {
        // Przywr�� oryginalny kolor przy zniszczeniu
        if (material != null)
        {
            for (int i = 0; i < material.Length; i++)
            {
                material[i].SetColor(emissionColor, originalColor[i]);
            }
        }
    }
}