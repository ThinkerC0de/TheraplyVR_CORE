using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MoonController : MonoBehaviour
{
    private Animator _animator;
    void Start()
    {
        _animator = GetComponent<Animator>();
    }

    public void Smile()
    {
        _animator.SetTrigger("TrSmile");
    }

    public void Sad()
    {
        _animator.SetTrigger("TrSad");
    }

    public void Idle()
    {
        _animator.SetTrigger("TrIdle");
    }
}
