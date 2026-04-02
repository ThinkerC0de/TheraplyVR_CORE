using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class randomTester : MonoBehaviour
{
    public List<byte> list;
    void Start()
    {
        list = new List<byte>();

        list = TheraplyHelpers.GenerateNumbers(0, 10);
        
        Debug.Log(TheraplyHelpers.GetIntFromList(list).ToString());
    }
}
