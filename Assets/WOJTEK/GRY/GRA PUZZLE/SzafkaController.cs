using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Events;

public class SzafkaController : MonoBehaviour


{
    // --------------------- ANIMATION STATES -------------------------------
    const string POSE0 = "POSE0";
    const string OPEN = "OPEN";
    const string CLOSE = "CLOSE";
    // --------------------- ---------------- -------------------------------
    private string currentState;
    [SerializeField] Animator animator;
    [SerializeField] private AudioSource audioSource;


    // Start is called before the first frame update
    void Start()
    {
        
    }

    public void ChangeAnimationState(string newState)
    
    {
        

        //stop the same animation from interrupting itself
        if (currentState == newState) return;

        // Play the animation
        animator.CrossFade(newState, 0.1f);

        currentState = newState;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
