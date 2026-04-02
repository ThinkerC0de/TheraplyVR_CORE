using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class FireController : MonoBehaviour
{
    public float minScale = 0.1f;
    public float maxScale = 0.6f;

    private Vector3 _minScale;
    private Vector3 _maxScale;

    private bool isBurning = false;
    private Coroutine fireCoroutine;
    // Start is called before the first frame update

    public void LightUpFlames()
    {
        fireCoroutine = StartCoroutine(AnimateFire());
    }

    public void ExtinguishFire()
    {
        isBurning = false;
    }

    void Start()
    {
        _maxScale = new Vector3(maxScale, maxScale, maxScale);
        _minScale = new Vector3(minScale, minScale, minScale);
    }

    IEnumerator AnimateFire()
    {
        /*
        transform.parent.gameObject.GetComponent<ParticleSystem>().Play();
        transform.DOScale(_maxScale, 2);
        yield return new WaitForSeconds(2);
        isBurning = true;
        transform.DOScale(_maxScale, 3);
        do
        {
            transform.DOScale(_maxScale, 3);
            yield return new WaitForSeconds(6);
            transform.DOScale(_minScale, 6);
            yield return new WaitForSeconds(6);
        } while (isBurning);

        transform.DOScale(Vector3.zero, 10);
        */
        yield return null;
    }

    public void Restart()
    {
        StopCoroutine(fireCoroutine);
        fireCoroutine = StartCoroutine(AnimateFire());
    }
}
