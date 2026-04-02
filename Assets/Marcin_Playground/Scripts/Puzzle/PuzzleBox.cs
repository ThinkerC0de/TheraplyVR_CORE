using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PuzzleBox : MonoBehaviour
{
    private void OnCollisionEnter(Collision other)
    {
        if (other.gameObject.layer == 28)
        {
            OpenBox();
        }
    }

    void OpenBox()
    {
        GetComponent<BoxCollider>().enabled = false;
        PuzzleCommunication.Instance.OpenBox();
        PuzzleCommunication.Instance.HidePuzzleBox();
        GameObject.FindFirstObjectByType<ClossetOfHints>().canOpenClosset = true;
    }

    private void Update()
    {
        /*if (Input.GetKeyDown(KeyCode.O))
            OpenBox();*/
    }
}
