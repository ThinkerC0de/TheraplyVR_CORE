using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LinePoints : MonoBehaviour
{
    [SerializeField] private LineRenderer _lineRenderer;
    [SerializeField] private List<Transform> points;

    void Start()
    {
        _lineRenderer.positionCount = points.Count;
    }

    private void Update()
    {

        for (int i = 0; i < points.Count; i++)
        {
            _lineRenderer.SetPosition(i, points[i].position);
        }
    }
}
