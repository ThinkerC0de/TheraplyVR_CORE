using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class AmbientButton : MonoBehaviour
{
    public AudioClip audioClip;
    public AudioSource audioSource;
    public Image volumeBtn;
    public Sprite volumeOn;
    public Sprite volumeLoud;
    public Sprite volumeOff;
    public bool isOn = false;
    public byte state = 0;

    private void Awake()
    {
        if (audioClip)
        {
            if (audioSource)
            {
                audioSource.clip = audioClip;
            }
            else
            {
                gameObject.AddComponent<AudioSource>();
                audioSource = gameObject.GetComponent<AudioSource>();
                audioSource.clip = audioClip;
            }

            audioSource.volume = 0.3f;
            audioSource.playOnAwake = false;
            audioSource.loop = true;
        }
    }

    public void ToggleAudio()
    {
        /*
        isOn = !isOn;

        if (isOn)
        {
            volumeBtn.sprite = volumeOn;
            audioSource.Play();
        }
        else
        {
            volumeBtn.sprite = volumeOff;
            audioSource.Pause();
        }
        */
        if (state == 0)
        {
            state = 1;
            volumeBtn.sprite = volumeOn;
            audioSource.volume = 0.05f;
            audioSource.Play();
        }
        else if(state == 1)
        {
            state = 2;
            volumeBtn.sprite = volumeLoud;
            audioSource.volume = 0.1f;
        }
        else if (state == 2)
        {
            state = 0;
            volumeBtn.sprite = volumeOff;
            audioSource.Pause();
        }
    }
}
