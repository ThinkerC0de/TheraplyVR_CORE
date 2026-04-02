using System.Collections;
using UnityEngine;

public class CampFirePrefab : MonoBehaviour
{
    private Coroutine _coroutine;

    public float startValue = 0f;
    public float minValue = 0.2f;
    public float maxValue = 0.3f;

    private float _scale = 0.0f;
    private bool _playSequence = false;
    private bool isPlaying = false;

    private void Start()
    {
        gameObject.transform.localScale = Vector3.zero;
    }

    public void LightUp()
    {
        _coroutine = StartCoroutine(LightUpCoroutine());
    }

    IEnumerator LightUpCoroutine()
    {
        isPlaying = true;
        float scaleModifier = 1;
        float time = 0;
        Vector3 startScale = transform.lossyScale;
        if (isPlaying || transform.lossyScale == new Vector3(minValue, minValue, minValue))
        {
            while (time < 3)
            {
                scaleModifier = Mathf.Lerp(startScale.x, minValue, time / 3);
                transform.localScale = new Vector3(scaleModifier, scaleModifier, scaleModifier);
                time += Time.deltaTime;
                yield return null;
            }
        }

        transform.localScale = new Vector3(minValue, minValue, minValue);

        if (_playSequence)
            BreathIn();
    }

    public void LightDown()
    {
        _playSequence = false;
        if (_coroutine != null)
        {
            StopCoroutine(_coroutine);
            _coroutine = StartCoroutine(LightDownCoroutine());
        }
        else _coroutine = StartCoroutine(LightDownCoroutine());
    }

    IEnumerator LightDownCoroutine()
    {
        float scaleModifier = 1;

        float time = 0;
        float startValue = gameObject.transform.localScale.x;
        Vector3 startScale = transform.lossyScale;
        while (time < 3)
        {
            scaleModifier = Mathf.Lerp(startValue, 0, time / 3);
            transform.localScale = new Vector3(scaleModifier, scaleModifier, scaleModifier);
            time += Time.deltaTime;
            yield return null;
        }

        transform.localScale = new Vector3(0, 0, 0);
    }

    public void BreathIn()
    {
        if (_coroutine != null)
        {
            StopCoroutine(_coroutine);
            _coroutine = StartCoroutine(BreathInCoroutine());
        }
        else _coroutine = StartCoroutine(BreathInCoroutine());
    }

    IEnumerator BreathInCoroutine()
    {
        float scaleModifier = 1;

        float time = 0;
        Vector3 startScale = transform.lossyScale;
        while (time < 3)
        {
            scaleModifier = Mathf.Lerp(startScale.x, maxValue, time / 3);
            transform.localScale = new Vector3(scaleModifier, scaleModifier, scaleModifier);
            time += Time.deltaTime;
            yield return null;
        }

        transform.localScale = new Vector3(maxValue, maxValue, maxValue);

        if (_playSequence)
        {
            yield return new WaitForSeconds(3);
            BreathOut();
        }
    }

    public void BreathOut()
    {
        if (_coroutine != null)
        {
            StopCoroutine(_coroutine);
            _coroutine = StartCoroutine(BreathOutCoroutine());
        }
        else _coroutine = StartCoroutine(BreathOutCoroutine());
    }

    IEnumerator BreathOutCoroutine()
    {
        float scaleModifier = 1;

        float time = 0;
        Vector3 startScale = transform.lossyScale;
        while (time < 6)
        {
            scaleModifier = Mathf.Lerp(startScale.x, minValue, time / 6);
            transform.localScale = new Vector3(scaleModifier, scaleModifier, scaleModifier);
            time += Time.deltaTime;
            yield return null;
        }

        transform.localScale = new Vector3(minValue, minValue, minValue);
        if (_playSequence)
            BreathIn();
    }

    public void PlaySequence()
    {
        _playSequence = true;
        BreathIn();
    }
}
