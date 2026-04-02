using System;
using System.Collections;
using UnityEngine;

public class PiniataHeight : MonoBehaviour
{
    [SerializeField] private Transform playerHead;
    [SerializeField] private Transform hangingPoint;
    [SerializeField] private float heightOffset = 2.8f;

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(2.0f);
        SetPiniataHeight();
    }

    [ContextMenu("Set Height")]
    public void SetPiniataHeight()
    {
        float finalHeight = Mathf.Clamp(heightOffset + playerHead.position.y - 0.5f, 2.75f, 10.5f);
        Vector3 locPos = hangingPoint.localPosition;
        locPos.y = finalHeight;
        hangingPoint.localPosition = locPos;
    }
}
