using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class animationStateController : MonoBehaviour
{
    Animator animator;
    int isWalkingHash;
    int isRunningHash;
    int VelocityHash;
    float velocity = 0.0f;
    public float acceleration = 0.5f;
    public float deceleration = 0.5f;

    bool isRunning;
    bool isWalking;
    bool forwardPressed;
    bool runPressed;


    // Start is called before the first frame update
    void Start()
    {
        animator = GetComponent<Animator>();
        //Debug.Log(animator);
        //isWalkingHash = Animator.StringToHash("isWalking");
        //isRunningHash = Animator.StringToHash("isRunning");
        //Debug.Log(animator);
        VelocityHash = Animator.StringToHash("Velocity");
    }

    // Update is called once per frame
    void Update()
    {
        isRunning = animator.GetBool("isRunning");
        isWalking = animator.GetBool("isWalking");
        forwardPressed = Input.GetKey(KeyCode.W);
        runPressed = Input.GetKey("left shift");
        //if player presses w key

        if (forwardPressed && velocity < 10.0f)
        {
            velocity += Time.deltaTime * acceleration;
        }

        if (!forwardPressed && velocity > 0.0f)
        {
            velocity -= Time.deltaTime * deceleration;
        }

        if (!forwardPressed && velocity < 0.0f)
        {
            velocity = 0.0f;
        }

        animator.SetFloat("Velocity", velocity);
        animator.speed = velocity;
    }
}
