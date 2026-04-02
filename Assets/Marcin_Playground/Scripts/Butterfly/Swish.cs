using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Swish : MonoBehaviour
{
    public GameObject RightHandAnchor;
    private Vector3 lastPosition;
    public AudioClip sound;
    public float speed;
    public float speedActivator = 10;
    bool isPlaying = false;
    public AudioSource audioSource;

    private void FixedUpdate()
    {
        speed = Vector3.Distance(lastPosition, RightHandAnchor.transform.position) / Time.deltaTime;
        lastPosition = RightHandAnchor.transform.position;
        if (speed > speedActivator)
        {
            if (!isPlaying)
                StartCoroutine("PlaySound");
        }
    }

    IEnumerator PlaySound()
    {
        audioSource.clip = sound;
        isPlaying = true;
        yield return null;
        audioSource.Play();

        // while (audioSource.isPlaying)
        // {
        //     yield return null;
        // }

        isPlaying = false;
    }
}
