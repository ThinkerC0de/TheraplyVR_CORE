using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayNikoAudio : MonoBehaviour
{
    [SerializeField]
    private AudioSource nikoAudio;
    [SerializeField]
    private AudioClip bekanie;
    [SerializeField]
    private AudioClip ziewanie;
    [SerializeField]
    private AudioClip pierdzenie;
    [SerializeField]
    private AudioClip obrzydzenie;
    [SerializeField]
    private AudioClip clap1;
    [SerializeField]
    private AudioClip clap2;
    [SerializeField]
    private AudioClip clap3;
    [SerializeField]
    private AudioClip wiwat;
    [SerializeField]
    private AudioClip sniff;
   
    public Animator nc;

            
    void Start()
    {
        if (nikoAudio == null)
            nikoAudio = GetComponent<AudioSource>();
    }

    [ContextMenu("TR")]
    public void TR()
    {
        nc.SetTrigger("GoodJob");
    }
   
    public void _Burp()
    {
        nikoAudio.clip = bekanie;
        nikoAudio.Play();
    }

    public void _Yawn()
    {
        nikoAudio.clip = ziewanie;
        nikoAudio.Play();
    }
    public void _Fart()
    {
        nikoAudio.clip = pierdzenie;
        nikoAudio.Play();
    }
    public void _Yuck()
    {
        nikoAudio.clip = obrzydzenie;
        nikoAudio.Play();
    }

    public void _clap1()
    {
        nikoAudio.clip = clap1;
        nikoAudio.Play();
    }

    public void _clap2()
    {
        nikoAudio.clip = clap2;
        nikoAudio.Play();
    }

    public void _clap3()
    {
        nikoAudio.clip = clap3;
        nikoAudio.Play();
    }

    public void _wiwat()
    {
        nikoAudio.clip = wiwat;
        nikoAudio.Play();
    }

    public void _sniff()
    {
        nikoAudio.clip = sniff;
        nikoAudio.Play();
    }

}
