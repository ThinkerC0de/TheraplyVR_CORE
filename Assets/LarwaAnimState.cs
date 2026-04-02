using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Events;

public class LarwaAnimState : MonoBehaviour
{
    // --------------------- ANIMATION STATES -------------------------------
    const string IDLE = "IDLE";
    const string RISE = "RISE";
    const string WALK = "WALK";
    const string SMIERC = "SMIERC";
    // --------------------- ---------------- -------------------------------

    public GameObject beetle;

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

    public void ShowBeetle()
    {
        beetle.SetActive(true);
        beetle.GetComponent<Animator>().CrossFade("WALK", 0.1f);
    }

    public void SelfHide()
    {
        gameObject.SetActive(false);
    }
}
