using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ClossetOfHints : MonoBehaviour
{
    public bool isClosed = true;
    public bool canOpenClosset = false;
    private Animator _animator;
    private string _currentState;

    public Renderer squareImage;
    public Renderer rectangleImage;

    public AudioClip closeDoorsAudioClip;
    public AudioClip openDoorsAudioClip;
    public AudioClip scatterPuzzle;
    public AudioClip correctPuzzleAudioClip;
    public AudioClip onFinishPuzzleAudioClip;
    
    public GameObject puzzleBox;
    public Material puzzleBoxImageMaterial;

    public AudioSource _audio;
    
    private void Start()
    {
        if (GetComponent<Animator>())
            _animator = GetComponent<Animator>();

        var table = GameObject.FindFirstObjectByType<TableHeigth>();
        if (table)
            table.SetHeight();

        if (squareImage)
            squareImage.enabled = false;
        if (rectangleImage)
            rectangleImage.enabled = false;
    }

    public void PlayAudio(AudioClip clip)
    {
        if (clip == null) return;
        
        _audio.loop = false;
        _audio.clip = clip;
        _audio.Play();
    }

    public void CloseClosset()
    {
        Debug.Log("close closset");
        
        if (canOpenClosset == false) return;
        if (_animator == null) return;
        if (isClosed) return;
        
        PlayAudio(closeDoorsAudioClip);
         
        ChangeAnimationState("CLOSE");
        isClosed = true;
    }
    
    public void OpenClosset()
    {
        Debug.Log("open closset");
        if (canOpenClosset == false) return;
        if (_animator == null) return;
        if (isClosed == false) return;
        
        PlayAudio(openDoorsAudioClip);
        
        ChangeAnimationState("OPEN");
        isClosed = false;
    }
    
    public void ChangeAnimationState(string newState)
    {
        if (_currentState == newState) return;
        
        _animator.CrossFade(newState, 0.1f);

        _currentState = newState;
    }

    private void Update()
    {
        /*
        if (Input.GetKeyDown(KeyCode.C))
            CloseClosset();
        if (Input.GetKeyDown(KeyCode.O))
            OpenClosset();
            */
    }
}
