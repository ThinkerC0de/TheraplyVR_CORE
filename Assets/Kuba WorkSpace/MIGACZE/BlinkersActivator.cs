using UnityEngine;
using System.Collections.Generic;
public class BlinkersActivator : MonoBehaviour
{
    [SerializeField] bool activated = true;
    [SerializeField] private List<GameObject> blinkersObjects;
    private void Awake()
    {
        activated = PlayerPrefs.GetInt("BlinkersActivated", 1) == 1;
    }
    void Start()
    {
        foreach (var blinker in blinkersObjects)
        {
            blinker.SetActive(activated);
        }
    }

    public void SetBlinkersActivated(bool _activated)
    {
        activated = _activated;
        PlayerPrefs.SetInt("BlinkersActivated", activated ? 1 : 0);
        foreach (var blinker in blinkersObjects)
        {
            blinker.SetActive(activated);
        }
    }
}
