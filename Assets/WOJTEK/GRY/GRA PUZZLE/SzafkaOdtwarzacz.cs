using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlaySzafkaAudio : MonoBehaviour
{
    private AudioSource szafkaAudio;
    [SerializeField]
    private AudioClip close;
    [SerializeField]
    private AudioClip open;
    [SerializeField]
    public Animator animator;
  
    void Start()
    {
        szafkaAudio = GetComponent<AudioSource>();
    }

    public void _SzafkaOpen()
    {
        szafkaAudio.clip = open;
        szafkaAudio.Play();
    }

    public void _SzafkaClose()
    {
        szafkaAudio.clip = close;
        szafkaAudio.Play();
    }
   
}
