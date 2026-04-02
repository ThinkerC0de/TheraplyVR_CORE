using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TreeController : MonoBehaviour
{
    public void OnTarget()
    {
        GetComponent<Renderer>().material.color = Color.red;
    }

    public void OutTarget()
    {
        GetComponent<Renderer>().material.color = Color.white;
    }

    public void OnGrab()
    {
        Debug.Log(HideAndSeekController.Instance.gameObject.name);
        Debug.Log(gameObject.name);
        HideAndSeekController.Instance.CheckAnswer(gameObject);//(gameObject.transform.parent.gameObject);
    }
}
