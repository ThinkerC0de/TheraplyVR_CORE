using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class NumbersController : MonoBehaviour
{
    public static NumbersController Instance;
    public GameObject[] numberGameObjects;
    public bool isHandInCollision = false;
    public AudioClip[] soundsOnAction;
    public AudioClip[] numberSounds;
    public GameObject actualCollisionGameObject;
    private AudioSource _audioSource;
    public GameObject[] starfishs;
    private int starfishIndex = 0;
    private bool isPlaying = false;

    private List<byte> _randomSoundsOnActionList;

    private CodingMasterGameManager _codingManager;

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
        _audioSource = GetComponent<AudioSource>();
        _codingManager = CodingMasterGameManager.Instance;

        _randomSoundsOnActionList = new List<byte>();
        _randomSoundsOnActionList = TheraplyHelpers.GenerateNumbers(0, soundsOnAction.Length - 1);

        DisableCollisions();
    }

    void PlayRandomSound()
    {
        if (soundsOnAction.Length == 0) return;

        _audioSource.Stop();
        _audioSource.clip = soundsOnAction[TheraplyHelpers.GetIntFromList(_randomSoundsOnActionList)];
        _audioSource.Play();
    }

    public void OnButtonPressed(int i)
    {
        if (_codingManager.canInteract == false) return;

        PlayRandomSound();
        CheckAnswer(i);
    }

    void CheckAnswer(int i)
    {
        _codingManager.CheckAnswer(i.ToString());
    }

    public void Answer(bool goodAnswer)
    {
        DisableCollisions();

        if (starfishIndex == 3)
        {
            starfishIndex = 0;
        }

        if (goodAnswer)
        {
            starfishs[starfishIndex].GetComponent<MeshRenderer>().material.SetFloat("_GOOD", 1.0f);
        }
        else
        {
            starfishs[starfishIndex].GetComponent<MeshRenderer>().material.SetFloat("_BAD", 1.0f);
        }

        starfishIndex++;
    }

    public void ResetStarfishs()
    {
        for (int i = 0; i < 3; i++)
        {
            starfishs[i].GetComponent<MeshRenderer>().material.SetFloat("_BAD", 0);
            starfishs[i].GetComponent<MeshRenderer>().material.SetFloat("_GOOD", 0);
        }

        starfishIndex = 0;
    }

    public void SetHandInCollision(bool state)
    {
        if (state == true && actualCollisionGameObject == null)
        {
            isHandInCollision = true;
        }
        else if (state == true && actualCollisionGameObject != null)
        {
            return;
        }
        else if (state == false && actualCollisionGameObject != null)
        {
            isHandInCollision = false;
        }
        else
        {
            return;
        }
    }

    public void SetActualCollisionGameObject(GameObject go)
    {
        actualCollisionGameObject = go;
    }

    public void DisableKinematicsForAll()
    {
        /*
        foreach (GameObject go in numberGameObjects)
        {
            go.GetComponent<Rigidbody>().isKinematic = false;
        }
        */
        EnableCollisions();
    }

    public void EnableKinematicsExcept(GameObject go)
    {
        /*
        for (int i = 0; i < numberGameObjects.Length; i++)
        {
            if (numberGameObjects[i] != go)
            {
                numberGameObjects[i].GetComponent<Rigidbody>().isKinematic = true;
            }
        }
        */
        DisableCollisionsExcept(go);
    }

    public void DisableCollisionsExcept(GameObject go)
    {
        foreach (GameObject goBtn in numberGameObjects)
        {
            if (goBtn != go)
            {
                if (goBtn.GetComponent<BoxCollider>())
                    goBtn.GetComponent<BoxCollider>().enabled = false;
                if (goBtn.GetComponent<SphereCollider>())
                    goBtn.GetComponent<SphereCollider>().enabled = false;
                if (goBtn.GetComponent<MeshCollider>())
                    goBtn.GetComponent<MeshCollider>().enabled = false;
                if (goBtn.GetComponent<CapsuleCollider>())
                    goBtn.GetComponent<CapsuleCollider>().enabled = false;
            }
        }
    }

    public void EnableCollisions()
    {
        foreach (GameObject goBtn in numberGameObjects)
        {
            goBtn.GetComponent<Renderer>().material.color = Color.white;
            if (goBtn.GetComponent<BoxCollider>())
                goBtn.GetComponent<BoxCollider>().enabled = true;
            if (goBtn.GetComponent<SphereCollider>())
                goBtn.GetComponent<SphereCollider>().enabled = true;
            if (goBtn.GetComponent<MeshCollider>())
                goBtn.GetComponent<MeshCollider>().enabled = true;
            if (goBtn.GetComponent<CapsuleCollider>())
                goBtn.GetComponent<CapsuleCollider>().enabled = true;

            if (goBtn.GetComponent<Rigidbody>())
                goBtn.GetComponent<Rigidbody>().isKinematic = false;
        }
    }

    public void DisableCollisions()
    {
        foreach (GameObject goBtn in numberGameObjects)
        {
            goBtn.GetComponent<Renderer>().material.color = Color.red;
            if (goBtn.GetComponent<BoxCollider>())
                goBtn.GetComponent<BoxCollider>().enabled = true;
            if (goBtn.GetComponent<SphereCollider>())
                goBtn.GetComponent<SphereCollider>().enabled = true;
            if (goBtn.GetComponent<MeshCollider>())
                goBtn.GetComponent<MeshCollider>().enabled = true;
            if (goBtn.GetComponent<CapsuleCollider>())
                goBtn.GetComponent<CapsuleCollider>().enabled = true;
            //if (goBtn.GetComponent<Rigidbody>())
            //    goBtn.GetComponent<Rigidbody>().isKinematic = true;
        }
    }

    private void Update()
    {
        /*
        if (isHandInCollision && actualCollisionGameObject != null)
            DisableCollisionsExcept(actualCollisionGameObject);
        else
        {
            EnableCollisions();
        }
        */
    }

    public void PlaySound(int i)
    {
        //StartCoroutine(PlaySoundCoroutine(i));
    }

    IEnumerator PlaySoundCoroutine(int i)
    {
        isPlaying = true;
        //_audioSource.clip = numberSounds[i];
        //_audioSource.Play();
        yield return new WaitWhile(() => _audioSource.isPlaying);
        isPlaying = false;
    }
}
