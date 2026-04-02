using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CoconutControllerOld : MonoBehaviour
{
    public List<Transform> coconutList;
    public List<Vector3> coconutPosList;
    public List<Quaternion> coconutRotList;
    void Start()
    {
        for (int i = 0; i < coconutList.Count; i++)
        {
            coconutPosList.Add(coconutList[i].position);
            coconutRotList.Add(coconutList[i].rotation);
        }
    }

    void Restore()
    {
        Debug.Log("restore");
        for (int i = 0; i < transform.childCount; i++)
        {
            coconutList[i].GetComponent<Rigidbody>().isKinematic = true;
            coconutList[i].GetComponent<Rigidbody>().useGravity = false;
            coconutList[i].transform.position = coconutPosList[i];
            coconutList[i].transform.rotation = coconutRotList[i];
        }

        foreach (var coconut in coconutList)
        {
            coconut.GetComponent<Rigidbody>().isKinematic = false;
            coconut.GetComponent<Rigidbody>().useGravity = true;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.U))
            Restore();
    }
}
