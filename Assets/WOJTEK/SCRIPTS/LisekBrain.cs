using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class LisekBrain : MonoBehaviour
{
    private readonly static int [] animations =
    {
        Animator.StringToHash("IDLE1"),
        Animator.StringToHash("IDLE2"),
        Animator.StringToHash("IDLE3"),
        Animator.StringToHash("IDLE4"),
        Animator.StringToHash("KLASKANIE"),
        Animator.StringToHash("MACHANIE"),
        Animator.StringToHash("ZIEWANIE"),
        Animator.StringToHash("DLUBANIE"),
        Animator.StringToHash("PIERDZENIE"),
        Animator.StringToHash("BEKANIE"),
        Animator.StringToHash("POKAZYWANIE"),
        Animator.StringToHash("ZNIKANIE"),
        Animator.StringToHash("POJAWIANIE_SIE"),
        Animator.StringToHash("PIERDZENIE2"),
        Animator.StringToHash("GESTYKULOWANIE"),
        Animator.StringToHash("GLUPIA_MINA"),
        Animator.StringToHash("GRANIE_NA_NOSIE"),
        Animator.StringToHash("SIADANIE"),
        Animator.StringToHash("SIEDZENIE"),
        Animator.StringToHash("WSTAWANIE"),
    };

    private Animator animator;
    private Animations [] currentAnimation;
    private bool[] layerLocked;
    private Action<int> DefaultAnimation;
    private string currentState;


    protected void Initialize(int layers, Animations startingAnimation, Animator animator, Action<int> DefaultAnimation)
    {
        layerLocked = new bool[layers];
        currentAnimation = new Animations[layers];
        this.animator = animator;
        this.DefaultAnimation = DefaultAnimation;

        for (int i = 0; i < layers; i++)
        {
            layerLocked[i] = false;
            currentAnimation[i] = startingAnimation;
        }
    }

    public Animations GetCurrentAnimation(int layer)
    {
        return currentAnimation[layer];
    }

    public void SetLocked(bool lockLayer, int layer)
    {
        layerLocked[layer] = lockLayer;
    }

    public void Play(Animations animation, int layer, bool lockLayer, bool bypassLock, float crossfade = 0.2f)
    {
        if(animation == Animations.NONE)
        {
            DefaultAnimation(layer);
            return;
        }
        
        if (layerLocked[layer] && !bypassLock) return;
        layerLocked[layer] = lockLayer;

        if (currentAnimation[layer] == animation) return;

        currentAnimation[layer] = animation;

        animator.CrossFade(animations[(int)currentAnimation[layer]], crossfade, layer);

    }


    public enum Animations
    {
        IDLE1,
        IDLE2,
        IDLE3,
        IDLE4,
        KLASKANIE,
        MACHANIE,
        ZIEWANIE,
        DLUBANIE,
        PIERDZENIE,
        BEKANIE,
        POKAZYWANIE,
        ZNIKANIE,
        POJAWIANIE_SIE,
        PIERDZENIE2,
        GESTYKULOWANIE,
        GLUPIA_MINA,
        GRA_NA_NOSIE,
        SIADANIE,
        SIEDZENIE,
        WSTAWANIE,
        NONE
    }
//     public void ChangeAnimationState(string newState)
//     {
//         //stop the same animation from interrupting itself
//         if (currentState == newState) return;

//         // Play the animation
//         animator.CrossFade(newState, 0.1f);

//         currentState = newState;
//     }
}

