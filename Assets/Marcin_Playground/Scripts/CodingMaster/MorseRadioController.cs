using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MorseRadioController : MonoBehaviour
{
    public static MorseRadioController Instance;
    public GameObject[] leds;

    public Material ledOff;
    public Material ledOnBad;
    public Material ledOnGood;

    public GameObject LongBtn;
    public GameObject ShortBtn;

    private CodingMasterGameManager _codingManager;
    private int ledIndex = 0;
    public bool canPress = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        _codingManager = CodingMasterGameManager.Instance;
    }

    public void ResetRadio()
    {
        StartCoroutine(ResetRadioCoroutine());
    }

    IEnumerator ResetRadioCoroutine()
    {
        yield return new WaitForSeconds(1);
        for (int i = 0; i < leds.Length; i++)
        {
            leds[i].GetComponent<Renderer>().material = ledOff;
        }

        ledIndex = 0;
    }

    void LightOnLed(int i, bool good)
    {
        if (good)
        {
            leds[i].GetComponent<Renderer>().material = ledOnGood;
        }
        else
        {
            Debug.Log("bad on led: " + i);
            leds[i].GetComponent<Renderer>().material = ledOnBad;
        }
    }

    void OnButtonPressed(bool shortBtn)
    {
        if (shortBtn) CodingMasterGameManager.Instance.CheckAnswer("0");
        else CodingMasterGameManager.Instance.CheckAnswer("1");
    }

    public void OnLongButtonPressed()
    {
        if (_codingManager.canInteract == false) return;
        OnButtonPressed(false);
        //_codingManager.PlayMorseSound(1);
    }

    public void OnShortButtonPressed()
    {
        if (_codingManager.canInteract == false) return;
        OnButtonPressed(true);
        //_codingManager.PlayMorseSound(0);
    }

    public void PlaySound(int i)
    {
        _codingManager.PlayMorseSound(i);
    }

    public void Answer(bool goodAnswer)
    {
        SetKinematic(true);
        Debug.Log("ledIndex:" + ledIndex + " " + goodAnswer);
        /*
        if (ledIndex == 4)
        {
            ResetRadio();
        }
        */

        if (ledIndex < 3)
        {
            if (goodAnswer)
            {
                LightOnLed(ledIndex, true);
            }
            else
            {
                LightOnLed(ledIndex, false);
            }
        }
        ledIndex++;
        if (ledIndex == 3 && _codingManager.actualQuestion == 1)
        {
            ResetRadio();
        }

    }

    public void SetKinematic(bool setKinematic)
    {
        LongBtn.GetComponent<Rigidbody>().isKinematic = setKinematic;
        ShortBtn.GetComponent<Rigidbody>().isKinematic = setKinematic;
    }
}
