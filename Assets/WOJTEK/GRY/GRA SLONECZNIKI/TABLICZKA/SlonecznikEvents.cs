using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Events;
using UnityEngine.VFX;

public class SlonecznikEvents : MonoBehaviour


{
    // --------------------- ANIMATION STATES -------------------------------
    // const string IDLE = "IDLE";
    // const string SHOW = "T-SHOW";
    // const string HIDE = "T-HIDE";
    // --------------------- ---------------- -------------------------------
    private string currentState;
    [SerializeField] Animator animator;
    //[SerializeField] private AudioSource audioSource;
    private AudioSource slonecznikAudio;
    [SerializeField]
    private AudioClip hide;
    [SerializeField]
    private AudioClip show;
    [SerializeField]
    public VisualEffect vfx;
     


    // Start is called before the first frame update
    void Start()
    {
        slonecznikAudio = GetComponent<AudioSource>();
    }

    void Update()
    {
        
    }

 
    public void _Show()
    {
        slonecznikAudio.clip = show;
        slonecznikAudio.Play();
        vfx.SendEvent("Show");
    }

    public void _Hide()
    {
        slonecznikAudio.clip = hide;
        slonecznikAudio.Play();
        vfx.SendEvent("Hide");
    }

    public void _Stop()
    {
        vfx.SendEvent("Stop");
    }
}
