using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RespawnDropped : MonoBehaviour
{
    [SerializeField] private float offset = 0.3f;
    private Vector3 startPos;

    private Rigidbody _rb;

    private void Awake()
    {
        transform.parent = null;
    }

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        ResetPosition();
    }

    public void ResetPosition()
    {
        startPos = transform.position;
    }

    public void Respawn()
    {
        StartCoroutine(RespawnCoroutine());
    }

    IEnumerator RespawnCoroutine()
    {
        transform.position = startPos;
        _rb.isKinematic = true;
        yield return null;
        _rb.isKinematic = false;
    }

    private void Update()
    {
        if (transform.position.y <= offset)
            Respawn();
    }
}
