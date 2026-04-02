using System;
using System.Collections;
using System.Collections.Generic;
using Autohand;
using UnityEngine;

public class HapticController : MonoBehaviour
{
    [SerializeField] private Hand rightHand;
    [SerializeField] private Hand leftHand;
    [Space(10)]
    [Header("Vibration Settings")]
    public float hapticAmp = 0.8f;
    public float velocityAmp = 0.5f;
    public float repeatDelay = 0.2f;
    public float maxDuration = 0.5f;
    public AnimationCurve velocityAmpCurve = AnimationCurve.Linear(0, 0, 1, 1);
    public AnimationCurve velocityDurationCurve = AnimationCurve.Linear(0, 0, 1, 1);
    
    public static HapticController Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        else
        {
            Instance = this;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private bool TryPlayHaptic(Hand hand, string handLabel)
    {
        if (hand == null)
        {
            Debug.LogWarning($"[HapticController] Skipping {handLabel} haptic because Hand reference is missing.");
            return false;
        }

        hand.PlayHapticVibration(
            0.2f,
            velocityAmpCurve.Evaluate(velocityAmp) * hapticAmp);
        return true;
    }

    public void LeftHandGentleVibrations()
    {
        TryPlayHaptic(leftHand, "left");
    }
    
    public void RightHandGentleVibrations()
    {
        TryPlayHaptic(rightHand, "right");
    }
    
}
