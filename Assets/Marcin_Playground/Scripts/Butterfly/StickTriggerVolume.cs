using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StickTriggerVolume : MonoBehaviour
{
    public bool canCloseAviary = true;
    private void OnTriggerEnter(Collider other)
    {
        //Debug.Log("On trigger enter: " + other.tag);
        if (other.tag == "Stick_Blue" || other.tag == "Stick_Red" ||
            (other.tag == "Stick_Blue" && other.tag == "Stick_Red"))
        {
            canCloseAviary = false;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.tag == "Stick_Blue" || other.tag == "Stick_Red" ||
            (other.tag == "Stick_Blue" && other.tag == "Stick_Red"))
        {
            if (other.transform.GetComponentInChildren<MagicStickPoint>().haveButterfly) return;
            canCloseAviary = true;
            AviaryController.Instance.CloseDoor();
        }
    }
}
