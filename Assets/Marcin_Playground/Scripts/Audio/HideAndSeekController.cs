using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;

public class HideAndSeekController : MonoBehaviour
{
    public static HideAndSeekController Instance;
    
    public GameObject stages;
    private List<GameObject> stagesList;
    public GameObject cuckooPrefab;
    public GameObject puppetPrefab;

    public float timeForReaction = 15f;
    private bool timeTicking = false;
    public int stageIndex = 0;
    public int subStageIndex = 0;
    private Stopwatch stopwatch;
    private float timer;
    private int correctTreeIndex;
    private GameObject correctTreeGameObject;
    private int oldTreeIndex = 0;

    private List<byte> _randomNumberTreeList;
    private List<byte> _randomNumberSoundList;
    
    private void Awake()
    {
        
    }

    private void Start()
    {
        for (int i = 0; i < stages.transform.childCount; i++)
        {
            stagesList.Add(stages.transform.GetChild(i).gameObject);
        }

        _randomNumberTreeList = new List<byte>();
        _randomNumberTreeList = TheraplyHelpers.GenerateNumbers(0, stagesList[stageIndex].transform.childCount - 1);
        
        _randomNumberSoundList = new List<byte>();
        _randomNumberSoundList = TheraplyHelpers.GenerateNumbers(0, 5);
        GenerateLevel();
    }

    public void StartStopwatch()
    {
        stopwatch = new Stopwatch();
        stopwatch.Start();
    }

    public void StopStopwatch()
    {
        stopwatch.Stop();
        UnityEngine.Debug.Log("Timer: " + stopwatch.ElapsedMilliseconds);
        stopwatch.Reset();
    }

    public void StartCuckoo()
    {
        timer = timeForReaction;
        timeTicking = true;

        correctTreeGameObject = stagesList[stageIndex].transform.GetChild(correctTreeIndex).gameObject;
        
        if (subStageIndex == 0)
        {
            StartCoroutine(StartCuckooCoroutine(3, correctTreeGameObject));
        }
        if (subStageIndex == 1)
        {
            StartCoroutine(StartCuckooCoroutine(2, correctTreeGameObject));
        }
        if (subStageIndex == 2)
        {
            StartCoroutine(StartCuckooCoroutine(1, correctTreeGameObject));
        }
    }

    IEnumerator StartCuckooCoroutine(int repeat, GameObject tree)
    {
        AudioSource treeAudio = tree.transform.GetChild(0).GetComponent<AudioSource>();
        StartStopwatch();
        for (int i = 0; i < repeat; i++)
        {
            treeAudio.Play();
            yield return new WaitWhile(() => treeAudio.isPlaying);
        }
    }
    
    void ColorizePole(GameObject go, Color newColor)
    {
        go.GetComponent<Renderer>().material.color = newColor;
    }

    void Update()
    {
        if (timeTicking)
        {
            if (timer > 0)
            {
                timer -= Time.deltaTime;
                if (timer < 0)
                {
                    StopStopwatch();
                    timeTicking = false;
                    timer = timeForReaction;
                }
            }
        }
    }

    public void GenerateLevel()
    {
        
        correctTreeIndex = TheraplyHelpers.GetIntFromList(_randomNumberTreeList);
        
        if (correctTreeIndex == oldTreeIndex)
        {
            correctTreeIndex = TheraplyHelpers.GetIntFromList(_randomNumberTreeList);
        }

        for (int i = 0; i < stagesList.Count; i++)
        {
            if (i == stageIndex)
            {
                stagesList[i].SetActive(true);
            }
            else
            {
                stagesList[i].SetActive(false);
            }
        }
        StartCuckoo();
        oldTreeIndex = correctTreeIndex;
    }

    public void CheckAnswer(GameObject tree)
    {
        StopStopwatch();
        if (tree == correctTreeGameObject)
        {
            UnityEngine.Debug.Log("Correct");
        }
        else
        {
            UnityEngine.Debug.Log("Bad answer");
        }
    }
}
