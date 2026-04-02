using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlaySkrzyniaAudio : MonoBehaviour
{
    private AudioSource skrzyniaAudio;
    [SerializeField]
    private AudioClip close;
    [SerializeField]
    private AudioClip open;
    [SerializeField]

    public Animator animator;

    public bool isOpen = false;

    void Start()
    {
        skrzyniaAudio = GetComponent<AudioSource>();
    }

    public void _Open()
    {
        skrzyniaAudio.clip = open;
        skrzyniaAudio.Play();
        isOpen = true;
    }

    public void _Close()
    {
        skrzyniaAudio.clip = close;
        skrzyniaAudio.Play();
        isOpen = false;
    }

    public void Open()
    {
        animator.SetTrigger("OPEN");
    }

    public void Close()
    {
        animator.SetTrigger("CLOSE");
    }
}
