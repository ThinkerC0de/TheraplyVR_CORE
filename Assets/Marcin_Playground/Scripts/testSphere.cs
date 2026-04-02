using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class testSphere : MonoBehaviour
{
    public GameObject gameObjectToStick;
    private Vector3 _localPos;
    private Vector3 pos;
    public bool _updatePosition = false;

    // Start is called before the first frame update
    void Start()
    {
        _localPos = gameObjectToStick.GetComponent<SphereCollider>().center;
        pos = gameObjectToStick.transform.position;
    }

    // Update is called once per frame
    void Update()
    {
        if (_updatePosition)
        {
            this.transform.position = new Vector3(pos.x, pos.y + _localPos.y, pos.z);
        }
    }
}
