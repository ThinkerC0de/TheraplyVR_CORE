using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MemoryManager : MonoBehaviour
{
    public static MemoryManager Instance;
    
    void Awake()
    {
        if (Instance != null) Instance = this;
        else
        {
            Debug.Log("There are more than one MemoryManager in the scene. Deleting one");
            Destroy(gameObject);
        }
    }
}
