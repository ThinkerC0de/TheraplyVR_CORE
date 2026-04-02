using System.Collections;
using System.Collections.Generic;
using MalbersAnimations.Controller;
using MalbersAnimations.Controller.Reactions;
using UnityEngine;

public class LisekMovement : LisekBrain
{
    private const int UPPERBODY = 0;
    private const int LOWERBODY = 1;
    private int currentIdle = 0;
    private int idle1PlayCount = 0;
    public static LisekMovement instance;
    private AnimatorStateInfo stateInfo;

    private readonly Animations [] idleAnimations =
    {
        Animations.IDLE1,
        Animations.IDLE2,
        Animations.IDLE3,
        Animations.IDLE4
    };

    // Start is called before the first frame update
    void Start()
    {
    Initialize(GetComponent<Animator>().layerCount, Animations.IDLE1, GetComponent<Animator>(), DefaultAnimation);
    StartCoroutine(ChangeIdle());
    }

    IEnumerator ChangeIdle()
    {
            while(true)
            {
                stateInfo = GetComponent<Animator>().GetCurrentAnimatorStateInfo(0);

                if (stateInfo.normalizedTime >= 1.0f)
                {
                    if (idleAnimations[currentIdle] == Animations.IDLE1)
                    {
                        idle1PlayCount++;
                        if (idle1PlayCount >= 5)
                        {
                            idle1PlayCount = 0;
                            currentIdle = (currentIdle + 1) % idleAnimations.Length;
                        }
                    }
                    else
                    {
                        currentIdle = (currentIdle + 1) % idleAnimations.Length;
                    }
                }
                yield return null;
            }
    }


    private void CheckTopAnimation()
    {
        CheckMovementAnimations(UPPERBODY);
    }

    private void CheckBottomAnimation()
    {
        CheckMovementAnimations(LOWERBODY);
    }

    private void CheckMovementAnimations(int layer)
    {
        Play(idleAnimations[currentIdle], layer, false, false); 
    }

    void DefaultAnimation(int layer)
    {
        if (layer == UPPERBODY)
            CheckTopAnimation();
        else
            CheckBottomAnimation();
    }

    void CheckDeath()
    {
        if (Input.GetKeyDown(KeyCode.LeftShift))
        {
            Play(Animations.DLUBANIE, UPPERBODY, true, true);
            Play(Animations.DLUBANIE, LOWERBODY, true, true);
            enabled = false;
        }
    }

    // Update is called once per frame
    void Update()
    {
        //CheckBottomAnimation();
        //CheckTopAnimation();
        CheckDeath();
        
        //Play(idleAnimations[currentIdle], UPPERBODY, false, false);
        //Play(idleAnimations[currentIdle], LOWERBODY, false, false);
    }
    
}
