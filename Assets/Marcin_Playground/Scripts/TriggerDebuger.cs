using System;
using UnityEngine;

public class TriggerDebuger : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        PrintCollider(other,"Enter");
    }

    private void OnTriggerStay(Collider other)
    {
        PrintCollider(other,"Stay");
    }

    private void OnTriggerExit(Collider other)
    {
        PrintCollider(other,"Exit");
    }
    
    void PrintCollider(Collider other, string state)
    {
        Debug.Log("OnTrigger" + state + ": " + other.gameObject.name);
    }
}
