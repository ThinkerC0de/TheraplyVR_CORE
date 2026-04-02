using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class WordBoardController : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private TMP_Text text;
    public WordElementData elementData;
    public string word;
    private string currentState;
    private AudioSource _audioSource;

    private void OnEnable()
    {
        this.AddComponent<AudioSource>();
        _audioSource = GetComponent<AudioSource>();
        _audioSource.loop = false;
    }

    public void ChangeAnimationState(string newState)
    {
        //stop the same animation from interrupting itself
        if (currentState == newState) return;

        // Play the animation
        animator.CrossFade(newState, 0.1f);

        currentState = newState;
    }

    public void GrowUp()
    {
        ChangeAnimationState("T_SHOW");
    }

    public void ShrinkDown()
    {
        StartCoroutine(ShrinkDownCoroutine());
    }

    IEnumerator ShrinkDownCoroutine()
    {
        if (elementData && elementData.audioClip)
        {
            _audioSource.clip = elementData.audioClip;
            yield return null;
            _audioSource.Play();
        }

        yield return new WaitWhile(() => _audioSource.isPlaying);
        
        ChangeAnimationState("T_HIDE");
    }

    public void SetText(string value)
    {
        text.text = value;
        word = value;
    }
}
