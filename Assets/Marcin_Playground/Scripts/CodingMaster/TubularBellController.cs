using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
[RequireComponent(typeof(AudioSource))]
public class TubularBellController : MonoBehaviour
{
    public static TubularBellController Instance;
    public bool isPlaying = false;
    public AudioClip[] sound;
    private AudioSource _audioSource;
    public GameObject[] tube;
    public SeashellController[] seashells;
    public int seashellIndex = 0;
    public GameObject mainFrame;
    private Vector3 position;
    public bool endAnimateShells = true;
    public GameObject scoreboard;
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
        //_audioSource.clip = sound;
        position = mainFrame.transform.position;
    }

    public void PlaySound(int i)
    {
        StartCoroutine(PlaySoundCoroutine(i));
    }

    IEnumerator PlaySoundCoroutine(int i)
    {
        isPlaying = true;
        _audioSource.clip = sound[i];
        _audioSource.Play();
        LightOnTube(i);
        yield return new WaitWhile(() => _audioSource.isPlaying);
        LightOffTube(i);
        isPlaying = false;
    }

    public void LightOnTube(int i)
    {
        //Debug.Log("light tube:" + i);
        tube[i].GetComponent<MeshRenderer>().material.SetFloat("_GOOD", 1);
    }

    public void LightOffTube(int i)
    {
        tube[i].GetComponent<MeshRenderer>().material.SetFloat("_GOOD", 0);
    }

    public void Answer(bool goodAnswer)
    {
        StartCoroutine(AnswerCoroutine(goodAnswer));
    }

    IEnumerator AnswerCoroutine(bool goodAnswer)
    {
        endAnimateShells = false;
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
        //Debug.Log("udzielona odpowiedz dla shell_" + seashellIndex + ": " + goodAnswer);
        yield return new WaitForSeconds(0.5f);

        seashells[seashellIndex].Answer(goodAnswer);

        //Debug.Log(seashellIndex.ToString());

        if (seashellIndex == 0 && goodAnswer == true)
        {
            yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("FirstGoodPianoAnswer")));
        }
        else if (goodAnswer == true && seashellIndex != 0)
        {
            yield return new WaitWhile(() => CodingMasterGameManager.Instance.gameObject.GetComponent<AudioSource>().isPlaying);
            yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
            if (seashellIndex == 1)
            {
                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("NextGoodPianoAnswer")));
            }
            else if (seashellIndex == 2)
            {
                //if (CodingMasterGameManager.Instance.answers.Sum() == 0)
                    //yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("Piano_LevelCompleted"));
            }
        }
        if (goodAnswer == false && seashellIndex != 2)
        {
            yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("BadPianoAnswer")));
        }

        yield return null;
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);

        seashellIndex++;

        //yield return new WaitForSeconds(2);

        endAnimateShells = true;
    }

    public void ResetTubularBells()
    {
        if (seashellIndex == 3)
        {
            foreach (var shell in seashells)
            {
                shell.ResetShell();
            }

            seashellIndex = 0;
        }
    }

    public void ResetTubularBellsColors()
    {
        for (int i = 0; i < tube.Length; i++)
        {
            LightOffTube(i);
        }
    }

    public void SetHeight()
    {
        if (Camera.main != null)
        {
            Vector3 height = new Vector3(position.x, (Camera.main.transform.position.y / 2) - 0.2f, position.z);
            if (height.y > -0.2f)
                position = height;
            else
            {
                position = new Vector3(position.x, -.2f, position.z);
            }
        }

        mainFrame.transform.position = position;
    }

    public void DisableTubularBells()
    {
        CodingMasterGameManager.Instance.canInteract = false;
    }

    public void EnableTubularBells()
    {
        CodingMasterGameManager.Instance.canInteract = true;
    }
}
