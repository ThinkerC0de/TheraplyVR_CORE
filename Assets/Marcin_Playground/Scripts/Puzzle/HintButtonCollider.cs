using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;

using UnityEngine;

public class HintButtonCollider : MonoBehaviour
{
    [SerializeField] private ClossetOfHints _closset;

    private void OnTriggerEnter(Collider other)
    {
        if (!_closset) return;
        if (other.gameObject.layer != 28) return;
        
        if (_closset.isClosed)
            _closset.OpenClosset();
        
        if (PuzzleManager.Instance != null) PuzzleManager.Instance.ShowHint();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!_closset) return;
        if (other.gameObject.layer != 28) return;
        
        if (!_closset.isClosed)
            _closset.CloseClosset();
        
        if (PuzzleManager.Instance != null) PuzzleManager.Instance.HideHint();
    }

    private void OnCollisionEnter(Collision other)
    {
        if (other.gameObject.layer != 28) _closset.canOpenClosset = false;
        _closset.canOpenClosset = true;
    }

    private void OnCollisionExit(Collision other)
    {
        if (other.gameObject.layer != 28) _closset.canOpenClosset = true;
        else _closset.canOpenClosset = true;
    }
}
