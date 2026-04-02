using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TargetShield : MonoBehaviour
{
    public GameObject targetShieldMesh;
    public AudioSource targetShieldAudio;
    public AudioClip badAnswerAudio;
    public AudioClip goodAnswerAudio;
    public AudioClip crowAudio;
    public AudioClip magicSound;
    public Image UIActivator;
    public GameObject crowGameObject;
    public GameObject puppetDummy;
    public GameObject magicPuf;
    private GameObject _crowSpawned;
    private GameObject _puppetSpawned;
    

    private TargetShieldsController gameController;
    private void Start()
    {
        gameController = TargetShieldsController.Instance;

        if (targetShieldMesh == null)
        {
            targetShieldMesh = transform.GetChild(0).gameObject;
        }

        if (targetShieldAudio == null)
        {
            targetShieldAudio = targetShieldMesh.GetComponent<AudioSource>();
        }
    }

    private void Update()
    {
        UIActivator.transform.LookAt(Camera.main.transform.position, Vector3.up);
        UIActivator.transform.rotation *= Quaternion.Euler(0, 180, 0);
        //UIActivator.transform.rotation = Quaternion.LookRotation(Camera.main.transform.position) * Quaternion.Euler(180, 0, 0);
    }

    public void OnStartTargeting()
    {
        ColorizeTree(Color.yellow);
    }

    public void OnStopTargeting()
    {
        ColorizeTree(Color.white);
    }

    public void OnSelect()
    {
        if (gameController.targetSelected) return;

        gameController.StopCrowCoroutine();
        gameController.targetSelected = true;
        CheckAnswer();
    }

    public void CheckAnswer()
    {
        if (gameController.targetShields[gameController.targetIndex] == this)
        {
            CorrectAnswer();
        }
        else
        {
            WrongAnswer();
        }
        gameController.DisableAllTargets();
    }

    public void CorrectAnswer()
    {
        ColorizeTree(Color.green);
        targetShieldAudio.clip = goodAnswerAudio;
        targetShieldAudio.Play();
        gameController.StopStopwatch();
        gameController.StopCuckooCoroutine();
        gameController.CorrectAnswer();
    }

    public void WrongAnswer()
    {
        ColorizeTree(Color.red);
        targetShieldAudio.clip = badAnswerAudio;
        targetShieldAudio.Play();
        gameController.StopStopwatch();
        gameController.StopCuckooCoroutine();
        gameController.WrongAnswer();
    }

    public void PlayCrow()
    {
        targetShieldAudio.clip = crowAudio;
        targetShieldAudio.Play();
        //ColorizeTree(Color.blue);
    }

    public void StopPlay()
    {
        targetShieldAudio.Stop();
    }

    public void ColorizeTree(Color color)
    {
        targetShieldMesh.GetComponent<Renderer>().material.color = color;
    }

    public void ShowAnswer()
    {
        StartCoroutine(ShowPropperAnswerCoroutine());
    }

    IEnumerator ShowPropperAnswerCoroutine()
    {
        //var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        //sphere.transform.position = this.gameObject.transform.GetChild(0).gameObject.transform.position;
        //yield return new WaitForSeconds(2);
        //Destroy(sphere);
        
        //ColorizeTree(Color.green);

        StartCoroutine(ShowPuppetAndCuckoo());
        
        yield return null;
    }

    IEnumerator ShowPuppetAndCuckoo()
    {
        /*
        Instantiate(magicPuf, puppetDummy.transform.position + new Vector3(0,.2f,0), Quaternion.Euler(90,0,0));
        targetShieldAudio.spatialize = false;
        targetShieldAudio.volume = 1;
        targetShieldAudio.clip = magicSound;
        targetShieldAudio.Play();
        
        yield return new WaitForSeconds(0.4f); 
        
        //_crowSpawned = Instantiate(gameController.cuckooPrefab, crowDummy.transform.position, crowDummy.transform.rotation);
        crowGameObject.gameObject.SetActive(true);
        _puppetSpawned = Instantiate(gameController.puppetPrefab, puppetDummy.transform.position, puppetDummy.transform.rotation);
        _puppetSpawned.GetComponent<Animator>().Play("LISEK-ARMATURE|POKWAZYWANIE-V2");
        yield return new WaitWhile(() => gameController.audioSource.isPlaying);
        
        gameController.audioSource.clip = badAnswerAudio;
        gameController.audioSource.Play();
        
        yield return new WaitForSeconds(3.3f);
        Instantiate(magicPuf, puppetDummy.transform.position + new Vector3(0,.2f,0), Quaternion.Euler(90,0,0));
        
        targetShieldAudio.clip = magicSound;
        targetShieldAudio.Play();
        
        yield return new WaitForSeconds(0.4f);
        crowGameObject.gameObject.SetActive(false);

        Destroy(_puppetSpawned);
        targetShieldAudio.spatialize = true;
        targetShieldAudio.volume = 0.5f;
        yield return new WaitWhile(() => targetShieldAudio.isPlaying);
        */
        gameController.canAnimate = true;
        gameController.AnimateButton();
        yield return null;
    }
}
