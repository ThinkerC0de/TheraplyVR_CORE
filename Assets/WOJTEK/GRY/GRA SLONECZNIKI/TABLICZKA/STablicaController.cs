using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Events;
using UnityEngine.VFX;

public class STablicaController : MonoBehaviour


{
    // --------------------- ANIMATION STATES -------------------------------
    const string IDLE = "T_IDLE";
    const string SHOW = "T_SHOW";
    const string HIDE = "T_HIDE";
    // --------------------- ---------------- -------------------------------
    private string currentState;
    [SerializeField] Animator animator;
    //[SerializeField] private AudioSource audioSource;
    private AudioSource szafkaAudio;
    [SerializeField]
    private AudioClip hide;
    [SerializeField]
    private AudioClip show;
    [SerializeField]
    public VisualEffect vfx;
     


    // Start is called before the first frame update
    void Start()
    {
        szafkaAudio = GetComponent<AudioSource>();
    }

    // public void ChangeAnimationState(string newState)
    
    // {
        

    //     //stop the same animation from interrupting itself
    //     if (currentState == newState) return;

    //     // Play the animation
    //     animator.CrossFade(newState, 0.1f);

    //     currentState = newState;
    // }

    void Update()
    {
        
    }

 
    public void _Show()
    {
        szafkaAudio.clip = show;
        szafkaAudio.Play();
        vfx.SendEvent("Show");
    }

    public void _Hide()
    {
        szafkaAudio.clip = hide;
        szafkaAudio.Play();
        vfx.SendEvent("Hide");
    }
}
