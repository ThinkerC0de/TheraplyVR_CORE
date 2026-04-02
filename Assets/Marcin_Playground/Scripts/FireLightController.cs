using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class FireLightController : MonoBehaviour
{
    public float minValue = 0.75f;
    public float maxValue = 1.65f;

    private bool isBurning = false;
    private Light _light;
    public float t;
    // Start is called before the first frame update

    private void Start()
    {
        _light = GetComponent<Light>();
    }

    public void LightUp()
    {
        StartCoroutine(LightUpCoroutine());
    }

    IEnumerator LightUpCoroutine()
    {
        float duration = 5f;
        t = 0f;

        while (t < duration)
        {
            _light.intensity = Mathf.Lerp(0, 1, t / duration);
            t += Time.deltaTime;

            yield return null;
        }

        yield return null;
        AnimateFire();
    }

    public void ExtinguishFire()
    {
        isBurning = false;
        StartCoroutine(ExtinguishFireCoroutine());
    }

    IEnumerator ExtinguishFireCoroutine()
    {
        float duration = 10f;
        t = 0f;
        do
        {
            _light.intensity = Mathf.Lerp(_light.intensity, 0, t / duration);
            t += Time.deltaTime;
            yield return null;
        }
        while (t < duration);

        yield return null;
    }

    public void AnimateFire()
    {
        StartCoroutine(AnimateFireCoroutine());
    }

    IEnumerator AnimateFireCoroutine()
    {
        isBurning = true;
        do
        {
            _light.intensity = Random.Range(minValue, maxValue);
            yield return new WaitForSeconds(Random.Range(0.01f, 0.1f));
        } while (isBurning);
    }


}
