using UnityEngine;

public class SetRefreshRate : MonoBehaviour
{
    private void Awake()
    {
        OVRPlugin.systemDisplayFrequency = 90.0f;
    }
}
