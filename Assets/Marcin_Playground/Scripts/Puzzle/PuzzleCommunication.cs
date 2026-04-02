using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PuzzleCommunication : CommunicationAbstractClass
{
    public static PuzzleCommunication Instance;

    public List<GameObject> puzzles;

    public GameObject spawnPoint;
    public BoxCollider tableVolume;
    public PuzzleGameData data;
    public GameObject confetti;
    public GameObject confettiSpawnPoint;

    private GameObject puzzlesRoot;
    private ClossetOfHints _clossetOfHints;
    private int _actualPuzzleIndex;

    private IEnumerator Start()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
        }

        _clossetOfHints = GameObject.FindFirstObjectByType<ClossetOfHints>();

        yield return new WaitForSeconds(6f);
        OnSessionStarted("SceneLoaded:PuzzleGame");
    }

    private void Update()
    {
        /*
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            SpawnPuzzle(0);
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            SpawnPuzzle(1);
        }

        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            SpawnPuzzle(2);
        }

        if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            SpawnPuzzle(3);
        }

        if (Input.GetKeyDown(KeyCode.Alpha5))
        {
            SpawnPuzzle(4);
        }

        if (Input.GetKeyDown(KeyCode.Alpha6))
        {
            SpawnPuzzle(5);
        }

        if (Input.GetKeyDown(KeyCode.Alpha7))
        {
            SpawnPuzzle(6);
        }

        if (Input.GetKeyDown(KeyCode.Alpha8))
        {
            SpawnPuzzle(7);
        }

        if (Input.GetKeyDown(KeyCode.Alpha9))
        {
            SpawnPuzzle(8);
        }

        if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            SpawnPuzzle(9);
        }

        if (Input.GetKeyDown(KeyCode.S))
        {
            CollectedPuzzlesManager.Instance.SaveData();
        }

        if (Input.GetKeyDown(KeyCode.L))
        {
            CollectedPuzzlesManager.Instance.LoadData();
        }
        */
    }

    public override void GetJsonFromPreviewApp(string _json)
    {
        Debug.Log("MAM JSONa z MOBILKI");
        Debug.Log(_json);
        if (_json.Contains("Puzzle"))
        {
            data = JsonUtility.FromJson<PuzzleGameData>(_json);
            switch (data.puzzleIndex)
            {
                case 0:
                    // 3x4 v1
                    SpawnPuzzle(0);
                    break;
                case 1:
                    // 3x4 v2
                    SpawnPuzzle(1);
                    break;
                case 2:
                    // 4x5 v1
                    SpawnPuzzle(2);
                    break;
                case 3:
                    // 4x5 v2
                    SpawnPuzzle(3);
                    break;
                case 4:
                    // 5x5 v1
                    SpawnPuzzle(4);
                    break;
                case 5:
                    // 5x5 v2
                    SpawnPuzzle(5);
                    break;
                case 6:
                    // 6x5 v1
                    SpawnPuzzle(6);
                    break;
                case 7:
                    // 6x5 v2
                    SpawnPuzzle(7);
                    break;
                case 8:
                    // 6x6 v1
                    SpawnPuzzle(8);
                    break;
                case 9:
                    // 6x6 v2
                    SpawnPuzzle(9);
                    break;
            }

            WebSocketClientV6.Instance.SendMessage("TheGameIsStarted");

        }
        else
        {
            base.GetJsonFromPreviewApp(_json);
        }
    }

    public void SetData(double sessionSecondsTime, int currentLevel, int levelsProgress)
    {
        /*
        data.sessionSecondsTime = sessionSecondsTime;
        data.currentLevel = currentLevel;
        data.levelsProgress = levelsProgress;
        */
    }

    public override void OnGameFinished(string msg)
    {
        ReturnData returnData = new ReturnData
        {
            sessionState = msg,
            data = data
        };

        Debug.Log("data count:");
        Debug.Log(data.hints.Count);

        base.OnGameFinished(JsonUtility.ToJson(returnData));
    }

    void SpawnPuzzle(int i)
    {
        _clossetOfHints = GameObject.FindFirstObjectByType<ClossetOfHints>();

        _actualPuzzleIndex = i;

        VirtualFriend.Instance.Talk("Intro");
        UpdatePuzzleBox();

        StartCoroutine(UnlockBox());
    }

    IEnumerator UnlockBox()
    {
        _clossetOfHints.puzzleBox.GetComponent<BoxCollider>().enabled = false;

        yield return null;

        while (VirtualFriend.Instance.IsTalking)
        {
            yield return null;
        }

        _clossetOfHints.puzzleBox.GetComponent<BoxCollider>().enabled = true;
    }

    public void OpenBox()
    {
        GetPuzzleAddOns();

        DestroyFromList(GetPuzzlesGameObjects());
        GameObject temp = Instantiate(puzzles[_actualPuzzleIndex], spawnPoint.transform.position, spawnPoint.transform.rotation);

        temp.GetComponent<PuzzleManager>().tableVolume = tableVolume;

        _clossetOfHints.PlayAudio(_clossetOfHints.scatterPuzzle);

        UpdatePicture(temp);
    }

    void UpdatePuzzleBox()
    {
        if (_clossetOfHints == null) return;
        if (_clossetOfHints.puzzleBoxImageMaterial == null) return;

        _clossetOfHints.puzzleBoxImageMaterial.mainTexture = puzzles[_actualPuzzleIndex].GetComponent<PuzzleManager>().puzzleImage;

        ShowPuzzleBox();
    }

    void ShowPuzzleBox()
    {
        if (_clossetOfHints == null) return;
        if (_clossetOfHints.puzzleBox == null) return;

        for (int i = 0; i < _clossetOfHints.puzzleBox.transform.childCount; i++)
            _clossetOfHints.puzzleBox.transform.GetChild(i).gameObject.SetActive(true);
    }

    public void HidePuzzleBox()
    {
        if (_clossetOfHints == null) return;
        if (_clossetOfHints.puzzleBox == null) return;

        for (int i = 0; i < _clossetOfHints.puzzleBox.transform.childCount; i++)
            _clossetOfHints.puzzleBox.transform.GetChild(i).gameObject.SetActive(false);
    }

    public void SpawnConfetti()
    {
        Instantiate(confetti, confettiSpawnPoint.transform.position, Quaternion.identity);
        _clossetOfHints.squareImage.enabled = false;
        _clossetOfHints.rectangleImage.enabled = false;
    }

    void UpdatePicture(GameObject puzzle)
    {
        if (puzzle.GetComponent<PuzzleManager>().isSquare)
        {
            _clossetOfHints.squareImage.enabled = true;
            _clossetOfHints.rectangleImage.enabled = false;
        }
        else
        {
            _clossetOfHints.squareImage.enabled = false;
            _clossetOfHints.rectangleImage.enabled = true;
        }

        var puzzlesPicture = GameObject.FindGameObjectsWithTag("PuzzlePicture");
        foreach (var picture in puzzlesPicture)
        {
            picture.GetComponent<Renderer>().material.mainTexture = puzzle.GetComponent<PuzzleManager>().puzzleImage;
        }
    }

    List<GameObject> GetPuzzlesGameObjects()
    {
        return GameObject.FindGameObjectsWithTag("Puzzle").ToList();
    }

    void DestroyFromList(List<GameObject> list)
    {
        foreach (var go in list)
        {
            Destroy(go);
        }
    }

    void GetPuzzleAddOns()
    {
        spawnPoint = GameObject.FindGameObjectWithTag("PuzzleSpawnPoint");
        tableVolume = GameObject.FindGameObjectWithTag("PuzzleBoxVolume").GetComponent<BoxCollider>();
        confettiSpawnPoint = GameObject.FindGameObjectWithTag("ConfettiSpawnPoint");
    }

    [System.Serializable]
    private class ReturnData
    {
        public string sessionState;
        public PuzzleGameData data;
    }
}