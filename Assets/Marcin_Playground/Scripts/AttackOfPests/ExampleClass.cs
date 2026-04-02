using UnityEngine;
using System.Collections;

public class ExampleClass : MonoBehaviour
{
    public void PrintEvent(string s)
    {
        Debug.Log("PrintEvent called at " + Time.time + " with a value of " + s);
    }
}