using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Events;

public class SlonecznikController : MonoBehaviour
{
    // --------------------- ANIMATION STATES -------------------------------
    const string IDLE = "IDLE";
    const string ODKRYCIE = "ODKRYCIE";
    const string ZAKRYCIE = "ZAKRYCIE";
    const string WAVE = "WAVE";
    // --------------------- ---------------- -------------------------------

    private string currentState;
    [SerializeField] Animator animator;

    public void ChangeAnimationState(string newState)
    {
        
        //stop the same animation from interrupting itself
        if (currentState == newState) return;

        // Play the animation
        animator.CrossFade(newState, 0.1f);

        currentState = newState;
    }
    // Start is called before the first frame update
    void Start()
    {
        
    }

   

    // Update is called once per frame
    void Update()
    {
        
    }
}
