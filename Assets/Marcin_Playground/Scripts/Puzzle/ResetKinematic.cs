using System;
using System.Collections;
using System.Collections.Generic;
using Autohand;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Rigidbody))]
public class ResetKinematic : MonoBehaviour
{
    private Rigidbody _rb;

    private void Start()
    {
        _rb = GetComponent<Rigidbody>();
    }

    public void Reset()
    {
        StartCoroutine(ResetCoroutine());
    }

    IEnumerator ResetCoroutine()
    {
        if (_rb == null) yield break;
        _rb.isKinematic = true;
        yield return null;
        if (_rb == null) yield break;
        _rb.isKinematic = false;
        
    }
}
