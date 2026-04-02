using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class ShrinkAfterSomeTime : MonoBehaviour
{
    private float waitTime = 0.5f;
    IEnumerator Start()
    {
        yield return new WaitForSeconds(waitTime);
        transform.DOScale(0.0f, 0.80f).OnComplete((() => gameObject.SetActive(false)));
    }
}
