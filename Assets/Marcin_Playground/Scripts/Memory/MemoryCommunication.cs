using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MemoryCommunication : MonoBehaviour
{
    public static MemoryCommunication Instance;

    public List<GameObject> memoryElements;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }
}
