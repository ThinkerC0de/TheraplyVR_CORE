using System;
using System.Collections;
using UnityEngine;

public class ObeliskHeight : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 startPosition;
    [SerializeField] private Vector3 translation;
    IEnumerator Start()
    {
        startPosition = transform.localPosition;
        yield return new WaitForSeconds(0.03f);
        SetHeight();
    }

    [ContextMenu("SET H")]
    public void SetHeight()
    {
        Vector3 pos = startPosition;
        
        pos.y += Math.Clamp(target.position.y + translation.y, -0.215f, 5.0f);
        pos.x += translation.x;
        pos.z += translation.z;
        
        transform.localPosition = pos;
    }
}
