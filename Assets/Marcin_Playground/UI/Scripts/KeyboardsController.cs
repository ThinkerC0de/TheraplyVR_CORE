using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class KeyboardsController : MonoBehaviour
{
    public bool iluminateButtons = false;
    public Color colorToIluminate;
    public float iluminationDuration = 1;
    public List<GameObject> buttons;

    private bool isIluminatig = false;
    
    
    void Start()
    {
        iluminationDuration = UILevelController.Instance.pauseTime;
        RestoreMaterial();
    }

    private void OnEnable()
    {
        if (UILevelController.Instance)
            iluminationDuration = UILevelController.Instance.pauseTime;
        RestoreMaterial();
    }

    public void LigthUpButton(int buttonIndex)
    {
        if (!iluminateButtons) return;
        if (isIluminatig) return;
        isIluminatig = true;
        StartCoroutine(LightUpButtonCoroutine(buttonIndex));
    }

    IEnumerator LightUpButtonCoroutine(int buttonIndex)
    {
        if (buttons[buttonIndex].GetComponent<Image>() == null)
        {
            GameObject key = buttons[buttonIndex].transform.GetChild(0).transform.GetChild(0).gameObject;
            Material material = key.GetComponent<Renderer>().material;
            Color tempColor = key.GetComponent<Renderer>().material.color;

            float time = 0;

            /*
            while (time < iluminationDuration/4)
            {
                material.color = Color.Lerp(tempColor, colorToIluminate, time / iluminationDuration/2);
                time += Time.deltaTime;
                yield return null;
            }
            */

            material.color = colorToIluminate;

            time = 0;

            while (time < iluminationDuration)
            {
                material.color = Color.Lerp(colorToIluminate, tempColor, time / iluminationDuration / 2);
                time += Time.deltaTime;
                yield return null;
            }

            material.color = tempColor;
            isIluminatig = false;
            yield return null;
        }
        else
        {
            Image button = buttons[buttonIndex].GetComponent<Image>();
            Color tempColor = button.color;

            float time = 0;

            /*
            while (time < iluminationDuration/4)
            {
                material.color = Color.Lerp(tempColor, colorToIluminate, time / iluminationDuration/2);
                time += Time.deltaTime;
                yield return null;
            }
            */

            button.color = colorToIluminate;

            time = 0;

            while (time < iluminationDuration)
            {
                button.color = Color.Lerp(colorToIluminate, tempColor, time / iluminationDuration / 2);
                time += Time.deltaTime;
                yield return null;
            }

            button.color = tempColor;
            isIluminatig = false;
            yield return null;
        }
    }

    public void Answer(string key)
    {
        UILevelController.Instance.CheckAnswer(key);
    }

    public void PlayPiano(int key)
    {
        UILevelController.Instance.PlayPianoSound(key);
    }
    
    public void PlayMorse(int key)
    {
        UILevelController.Instance.PlayMorseSound(key);
    }

    public void PlayKeyboard()
    {
        UILevelController.Instance.PlayKeyboardSound();
    }

    void RestoreMaterial()
    {
        for (int buttonIndex=0; buttonIndex<buttons.Count; buttonIndex++)
        if (buttons[buttonIndex].GetComponent<Image>() == null)
        {
            GameObject key = buttons[buttonIndex].transform.GetChild(0).transform.GetChild(0).gameObject;
            Material material = key.GetComponent<Renderer>().material;

            material.color = Color.white;
            isIluminatig = false;
        }
        else
        {
            Image button = buttons[buttonIndex].GetComponent<Image>();

            button.color = Color.white;
            isIluminatig = false;
        }
    }
}
