using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BellController : MonoBehaviour
{
    public int i = 0;
    public bool demoMode = false;
    
    private CodingMasterGameManager _codingManager;
    private AudioSource _audioSource;
     
    void Start()
    {
        _codingManager = CodingMasterGameManager.Instance;
        if (demoMode)
        {
            _audioSource = GetComponent<AudioSource>();
            _audioSource.clip = TubularBellController.Instance.sound[i];
        }
    }
    
    private void OnCollisionEnter(Collision collision)
    {
        if (demoMode)
        {
            _audioSource.Play();
            return;
        }
        
        if (_codingManager.canInteract == false) return;
        if (TubularBellController.Instance.isPlaying == false)
        {
            CodingMasterGameManager.Instance.CheckAnswer(i.ToString());
            TubularBellController.Instance.PlaySound(i);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (demoMode)
        {
            _audioSource.Play();
            return;
        }
        
        if (_codingManager.canInteract == false) return;
        if (TubularBellController.Instance.isPlaying == false)
        {
            CodingMasterGameManager.Instance.CheckAnswer(i.ToString());
            TubularBellController.Instance.PlaySound(i);
        }
    }
}
