using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MagicSphereMessage : MonoBehaviour
{
    public MagicSphereMessageData messageData;
    public string key;
    void Start()
    {
        ChangeMaterialColor();
    }

    public void ChangeMaterialColor()
    {
        if (this.GetComponent<Renderer>().material != null)
        {
            this.GetComponent<Renderer>().material.color = messageData.colorPostIt;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.anyKeyDown)
        {
            foreach (KeyCode keyCode in System.Enum.GetValues(typeof(KeyCode)))
            {
                if (Input.GetKeyDown(keyCode))
                {
                    if (keyCode.ToString() == key)
                    {
                        Debug.Log("Wciśnięty przycisk: " + keyCode.ToString());
                        Debug.Log(messageData.contentKey);
                        VirtualFriend.Instance.Talk(messageData.contentKey);
                        break;
                    }
                }
            }
        }
    }
}
