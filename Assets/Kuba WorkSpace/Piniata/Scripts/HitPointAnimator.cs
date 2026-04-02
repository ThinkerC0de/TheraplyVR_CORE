using System;
using DG.Tweening;
using UnityEngine;

public class HitPointAnimator : MonoBehaviour
{
    
    private Tweener tweener;
    private Sequence sequence;
    private void Awake()
    {
        sequence = DOTween.Sequence();
 
        sequence.Append(transform.DOScale( 0.45f, 1.0f ).SetEase( Ease.InOutSine )).SetLoops( -1, LoopType.Yoyo );
        sequence.Join(transform.DOLocalRotate( new Vector3(0, 360, 0), 2.0f, RotateMode.FastBeyond360));
        
        // tweener = sequence.SetEase( Ease.InOutSine ).SetLoops( -1, LoopType.Yoyo );
    }
    private void Kill() {
        sequence.Kill();
    }

    private void OnDestroy()
    {
        Kill();
    }
}

// 0.3 -> 0.45