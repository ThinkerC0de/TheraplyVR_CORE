using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VirtualFriendTummy : MonoBehaviour
{
    [SerializeField] public Animator mainAnimator;
    private bool canTrigger = true;
    public new AudioSource audio;

    private void OnTriggerEnter(Collider other)
    {
        if (!canTrigger) return;
        //if (other.gameObject.layer != 0)
            StartCoroutine(Fart());
    }

    IEnumerator Fart()
    {
        canTrigger = false;
        mainAnimator.SetTrigger("Farting");
        audio.Play();
        yield return new WaitForSeconds(5);
        canTrigger = true;
    }
}
