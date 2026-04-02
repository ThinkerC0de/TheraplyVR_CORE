using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameController : MonoBehaviour
{
    public UILevelController MorseGame;
    public GameObject morseFade;
    public UILevelController NumbersGame;
    public GameObject numbersFade;
    public UILevelController PianoGame;
    public GameObject pianoFade;

    private void Start()
    {
        MorseGame.enabled = false;
        morseFade.SetActive(true);
        NumbersGame.enabled = false;
        numbersFade.SetActive(true);
        PianoGame.enabled = false;
        pianoFade.SetActive(true);
    }

    public void MorseOn()
    {
        NumbersGame.enabled = false;
        numbersFade.SetActive(true);
        PianoGame.enabled = false;
        pianoFade.SetActive(true);
        MorseGame.enabled = true;
        morseFade.SetActive(false);
    }

    public void NumberOn()
    {
        MorseGame.enabled = false;
        morseFade.SetActive(true);
        PianoGame.enabled = false;
        pianoFade.SetActive(true);
        NumbersGame.enabled = true;
        numbersFade.SetActive(false);
    }
    
    public void PianoOn()
    {
        MorseGame.enabled = false;
        morseFade.SetActive(true);
        NumbersGame.enabled = false;
        numbersFade.SetActive(true);
        PianoGame.enabled = true;
        pianoFade.SetActive(false);
    }
}
