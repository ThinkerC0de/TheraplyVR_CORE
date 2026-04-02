using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class HideAndSeekTree : MonoBehaviour
{
    public GameObject treeMesh;
    public AudioSource treeAudio;
    public AudioClip badAnswerAudio;
    public AudioClip cuckooAudio;
    public AudioClip magicSound;
    public Image UIActivator;
    public GameObject cuckooDummy;
    public GameObject puppetDummy;
    public GameObject magicPuf;
    private GameObject _cuckooSpawned;
    private GameObject _puppetSpawned;


    private HideAndSeekGameController gameController;
    private void Start()
    {
        gameController = HideAndSeekGameController.Instance;

        if (treeMesh == null)
        {
            treeMesh = transform.GetChild(0).gameObject;
        }

        if (treeAudio == null)
        {
            treeAudio = treeMesh.GetComponent<AudioSource>();
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
        if (gameController.treeSelected) return;
        if (!gameController.timeForAnswer) return;
        gameController.treeSelected = true;
        CheckAnswer();
    }
    public void CheckAnswer()
    {
        gameController.StopCuckooCoroutine();
        if (gameController.treesOnStage[gameController.actualTree] == this)
        {
            CorrectAnswer();
        }
        else
        {
            WrongAnswer();
        }

        gameController.timeForAnswer = false;

        gameController.UpdateStageCounter();
    }
    public void CorrectAnswer()
    {
        ColorizeTree(Color.green);
        StartCoroutine(ShowCuckoo());
        gameController.StopStopwatch();
        gameController.StopCuckooCoroutine();
        gameController.CorrectAnswer();
    }

    IEnumerator ShowCuckoo()
    {
        _cuckooSpawned = Instantiate(gameController.cuckooPrefab, cuckooDummy.transform.position, cuckooDummy.transform.rotation);
        yield return new WaitForSeconds(2);
        Destroy(_cuckooSpawned);
    }
    
    
    public void WrongAnswer()
    {
        ColorizeTree(Color.red);
        gameController.StopStopwatch();
        gameController.StopCuckooCoroutine();
        gameController.WrongAnswer();
    }
    public void PlayCuckoo()
    {
        treeAudio.clip = cuckooAudio;
        treeAudio.Play();

        RemoveOldCuckoo();
        //ColorizeTree(Color.blue);
    }

    void RemoveOldCuckoo()
    {
        var cuckoo = FindObjectsByType<Cuckoo>(FindObjectsSortMode.None);

        foreach (var c in cuckoo)
        {
            Destroy(c.gameObject);
        }
    }
    
    public void ColorizeTree(Color color)
    {
        treeMesh.GetComponent<Renderer>().material.color = color;
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

        ColorizeTree(Color.green);

        //StartCoroutine(ShowPuppetAndCuckoo());

        StartCoroutine(ShowAnswerCoroutine());

        yield return null;
    }
    IEnumerator ShowPuppetAndCuckoo()
    {
        Instantiate(magicPuf, puppetDummy.transform.position + new Vector3(0, .2f, 0), Quaternion.Euler(90, 0, 0));
        treeAudio.spatialize = false;
        treeAudio.volume = 1;
        treeAudio.clip = magicSound;
        treeAudio.Play();

        yield return new WaitForSeconds(0.4f);

        _cuckooSpawned = Instantiate(gameController.cuckooPrefab, cuckooDummy.transform.position, cuckooDummy.transform.rotation);
        _puppetSpawned = Instantiate(gameController.puppetPrefab, puppetDummy.transform.position, puppetDummy.transform.rotation);
        _puppetSpawned.GetComponent<Animator>().Play("LISEK-ARMATURE|POKWAZYWANIE-V2");
        yield return new WaitWhile(() => gameController.audioSource.isPlaying);

        gameController.audioSource.clip = badAnswerAudio;
        gameController.audioSource.Play();

        yield return new WaitForSeconds(3.3f);
        Instantiate(magicPuf, puppetDummy.transform.position + new Vector3(0, .2f, 0), Quaternion.Euler(90, 0, 0));

        treeAudio.clip = magicSound;
        treeAudio.Play();

        yield return new WaitForSeconds(0.4f);
        Destroy(_cuckooSpawned);
        Destroy(_puppetSpawned);
        treeAudio.spatialize = true;
        treeAudio.volume = 0.5f;

        //gameController.AnimateButton();
    }

    IEnumerator ShowAnswerCoroutine()
    {
        gameController.virtualFriendIsBusy = true;
        Transform origin = VirtualFriend.Instance.transform;//gameController.puppetPrefab.transform;
        Vector3 originPos = gameController.puppetPrefab.transform.position;
        Quaternion originRot = gameController.puppetPrefab.transform.rotation;

        Transform temp = origin;

        _cuckooSpawned = Instantiate(gameController.cuckooPrefab, cuckooDummy.transform.position, cuckooDummy.transform.rotation);
        yield return (StartCoroutine(VirtualFriend.Instance.Hide()));
        GenerateMagicPuf(originPos);
        VirtualFriend.Instance.Teleport(puppetDummy.transform);
        yield return new WaitForSeconds(1);

        GenerateMagicPuf(puppetDummy.transform.position);
        yield return (StartCoroutine(VirtualFriend.Instance.Show()));
        StartCoroutine(VirtualFriend.Instance.FingerPoint());
        StartCoroutine(VirtualFriend.Instance.FriendTalking("BadAnswer"));
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
        yield return new WaitForSeconds(3.3f);

        // disabled teleport to start position
        /*
        yield return (StartCoroutine(VirtualFriend.Instance.Hide()));
        GenerateMagicPuf(puppetDummy.transform.position);
        
        yield return new WaitForSeconds(1);
        
        VirtualFriend.Instance.transform.position = originPos;
        VirtualFriend.Instance.transform.rotation = originRot;
        
        GenerateMagicPuf(originPos);
        
        yield return (StartCoroutine(VirtualFriend.Instance.Show()));
        */

        Destroy(_cuckooSpawned);

        gameController.timeForAnswer = false;
        gameController.EnableButton();
        gameController.virtualFriendIsBusy = false;
    }

    void GenerateMagicPuf(Vector3 pos)
    {
        Instantiate(magicPuf, pos + new Vector3(0, .2f, 0), Quaternion.Euler(90, 0, 0));
        treeAudio.spatialize = false;
        treeAudio.volume = 1;
        treeAudio.clip = magicSound;
        treeAudio.Play();
    }

    bool AnimatorIsPlaying(Animator animator)
    {
        return animator.GetCurrentAnimatorStateInfo(0).length >
               animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
    }
}
