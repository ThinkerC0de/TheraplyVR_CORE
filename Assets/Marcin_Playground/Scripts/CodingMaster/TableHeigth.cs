using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TableHeigth : MonoBehaviour
{
    public float offset = 0.0f;
    private Vector3 _pos;

    private void Start()
    {
        _pos = transform.position;
    }

    public void SetHeight()
    {
        if (Camera.main != null)
        {
            Vector3 height = new Vector3(_pos.x,(Camera.main.transform.position.y / 2) + offset, _pos.z);
            if (height.y > 0.2f)
                transform.position = height;
            else
            {
                transform.position = new Vector3(_pos.x, .2f + offset, _pos.z);
            }
        }
    }
}
