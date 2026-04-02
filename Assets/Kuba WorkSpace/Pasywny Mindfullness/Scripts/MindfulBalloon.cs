using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class MindfulBalloon : MonoBehaviour
{
    [Range(0,100)][SerializeField] private float enlargePercent;
    [SerializeField] private Animator mainAnimator;
    
    public void EnlargeBalloon()
    {
        float newScale = transform.localScale.x + transform.localScale.x * (enlargePercent / 100);
        transform.DOScale(newScale, 2.9f);  
    }

    public void FlyToTheSky()
    {
        mainAnimator.SetTrigger("FlyAway");
    }
    
    public void Connect()
    {
        mainAnimator.SetTrigger("Connect");
    }
}
