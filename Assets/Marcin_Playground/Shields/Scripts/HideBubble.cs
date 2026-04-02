using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HideBubble : MonoBehaviour
{
    public Shield _shield;

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("Wieksza sfera " + other.gameObject.name);
        if (other.gameObject.layer == 11)
        {
            if (other.GetComponent<ButterflyController>().holdingStick != null)
                _shield.OpenShield();
        }
    }
    
    private void OnTriggerExit(Collider other)
    {
        /*if (other.gameObject.layer == 11)
        {
            if (other.GetComponent<MagicStickPoint>().haveButterfly == null)
                _shield.OpenShield();
        }*/
    }
}
