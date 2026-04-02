using System;
using UnityEngine;

public class TouchInput : MonoBehaviour
{
    [SerializeField] private SunflowerController _controller;
    public Camera cam;

    private void OnEnable()
    {
        cam = Camera.main;
    }

    /*
         void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                Touch();
            }
        }
    */

    private void OnMouseDown()
    {
        //Debug.Log("naciśnięto " + gameObject.transform.root.name);
        _controller.OnTouch();
    }

    void Touch()
    {
        if (cam == null)
        {
            Debug.LogError("Main camera is not set or found in the scene!");
            return;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            if (_controller != null)
            {
                if (hit.collider.gameObject == gameObject)
                    _controller.OnTouch();
            }
            else
            {
                Debug.Log("The object hit does not have a SunflowerController component.");
            }
        }
        else
        {
            Debug.Log("Raycast did not hit any object.");
        }
    }

    private void OnCollisionEnter(Collision other)
    {
        if (MemoryGameController.Instance.canTurn)
        {
            _controller.OnTouch();
        }
    }
}