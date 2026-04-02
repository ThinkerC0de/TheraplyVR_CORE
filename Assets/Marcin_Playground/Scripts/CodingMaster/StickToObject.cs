using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StickToObject : MonoBehaviour
{
    public GameObject gameObjectToStick;
    private Vector3 _localPos;
    private bool _updatePosition = false;
    private GameObject _originalParent;
    
    void Start()
    {
        _localPos = transform.localPosition;
        _originalParent = this.gameObject.transform.parent.gameObject;
    }

    public void StickToGameObject(bool stick)
    {
//        _updatePosition = stick;
        if (stick)
        {
            this.gameObject.transform.parent = gameObjectToStick.transform;
            //GetComponent<Rigidbody>().isKinematic = true;
        }
        else
        {
            this.gameObject.transform.localPosition = _localPos;
            this.gameObject.transform.rotation = Quaternion.Euler(Vector3.zero);
            this.gameObject.transform.parent = _originalParent.transform;
            //GetComponent<Rigidbody>().isKinematic = false;
        }
    }
}
