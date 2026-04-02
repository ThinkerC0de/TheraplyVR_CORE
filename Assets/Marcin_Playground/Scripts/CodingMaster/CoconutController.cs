using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CoconutController : MonoBehaviour
{
    private bool isPressed = false;

    public void OnPressed(int i)
    {
        //if (NumbersController.Instance.actualCollisionGameObject == gameObject)
        {
            Debug.Log("OnPressed: " + gameObject.name);
            NumbersController.Instance.OnButtonPressed(i);
            isPressed = true;
        }
    }
}
