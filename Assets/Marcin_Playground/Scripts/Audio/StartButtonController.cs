using System;
using System.Collections;
using System.Collections.Generic;
// using Oculus.Interaction.PoseDetection;
using UnityEngine;

public class StartButtonController : MonoBehaviour
{
    public GameObject button;

    public bool canBeAnimated = false;
    public float iluminationDuration = 1;
    public Color iluminationColor;
    public bool isPressed = false;

    private Material tempMat;

    private void Awake()
    {

    }

    private void Start()
    {
        tempMat = button.GetComponent<Renderer>().material;
        StartCoroutine(AnimateColorCoroutine());
    }

    IEnumerator AnimateColorCoroutine()
    {
        while (!isPressed)
        {
            if (canBeAnimated)
            {
                Color tempColor = Color.white;

                float time = 0;

                while (time < iluminationDuration)
                {
                    tempMat.color = Color.Lerp(tempColor, iluminationColor, time / iluminationDuration / 2);
                    time += Time.deltaTime;
                    yield return null;
                }

                tempMat.color = iluminationColor;
                time = 0;

                while (time < iluminationDuration)
                {
                    tempMat.color = Color.Lerp(iluminationColor, tempColor, time / iluminationDuration / 2);
                    time += Time.deltaTime;
                    yield return null;
                }

                tempMat.color = tempColor;
            }
        }
    }

    public void OnButtonPressed()
    {
        isPressed = true;
        tempMat.color = Color.white;

        //HideAndSeekController.Instance.stagesList[0].GetComponent<SoundGameController>().StartDemo();
        HideAndSeekController.Instance.GenerateLevel();
    }
}
