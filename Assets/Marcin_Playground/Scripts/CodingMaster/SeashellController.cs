using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Dreamteck.Splines.Primitives;
using UnityEngine;

public class SeashellController : MonoBehaviour
{
    public GameObject pearl;
    public GameObject shellPartUp;
    public GameObject shellPartDown;

    private Material shellMaterial;

    private void Start()
    {
        shellMaterial = shellPartUp.GetComponent<MeshRenderer>().material;
        shellPartDown.GetComponent<MeshRenderer>().material = shellMaterial;
    }

    public void ResetShell()
    {
        CloseShell();
        shellMaterial.color = Color.white;
        //StartCoroutine(ResetCoroutine());
    }

    public void Answer(bool goodAnswer)
    {
        if (goodAnswer)
        {
            LightPearl(true);
            OpenShell();
        }
        else
        {
            LightPearl(false);
        }

        //StartCoroutine(ResetCoroutine());
    }

    void OpenShell()
    {
        var targetRotation = Quaternion.Euler(90,0,0);
        shellPartUp.transform.DOLocalRotate(targetRotation.eulerAngles,0.2f);
    }

    void CloseShell()
    {
        var targetRotation = Quaternion.Euler(0,0,0);
        shellPartUp.transform.DOLocalRotate(targetRotation.eulerAngles,0.2f);
    }

    void LightPearl(bool goodAnswer)
    {
        LightShell(goodAnswer);
        if (goodAnswer)
        {
            pearl.GetComponent<MeshRenderer>().material.SetFloat("_GOOD", 1);
            pearl.GetComponent<MeshRenderer>().material.SetFloat("_BAD", 0);
        }
        else
        {
            Debug.Log("Bad - red");
            pearl.GetComponent<MeshRenderer>().material.SetFloat("_BAD", 1);
            pearl.GetComponent<MeshRenderer>().material.SetFloat("_GOOD", 0);
        }
    }

    void LightShell(bool goodAnswer)
    {
        if (goodAnswer)
        {
            pearl.GetComponent<MeshRenderer>().material.SetFloat("_GOOD", 1);
            pearl.GetComponent<MeshRenderer>().material.SetFloat("_BAD", 0);
        }
        else
        {
            Debug.Log("Bad - red");
            shellMaterial.color = Color.red;
        }
    }

    IEnumerator ResetCoroutine()
    {
        yield return new WaitForSeconds(2);
        yield return new WaitWhile(()=>VirtualFriend.Instance.IsTalking);
        if (TubularBellController.Instance.seashellIndex == 3)
        {
            Debug.Log("była ostatnia odpowiedź... będzie reset");
            //yield return new WaitForSeconds(2);
            foreach (var shell in TubularBellController.Instance.seashells)
            {
                shell.ResetShell();
            }
            
            //TubularBellController.Instance.seashellIndex = 0;
        }
    }
}
