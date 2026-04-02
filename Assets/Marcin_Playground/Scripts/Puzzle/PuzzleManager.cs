using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Autohand;
using UnityEngine;
using UnityEngine.Events;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

public class PuzzleManager : MonoBehaviour
{
    public static PuzzleManager Instance;
    [SerializeField] private int puzzleIndex = 0;
    [SerializeField] private int puzzleCount = 0;
    [SerializeField] private int puzzleCollected = 0;
    [SerializeField] private List<GameObject> puzzleParts;
    
    public Texture2D puzzleImage;
    public bool isSquare = true;
    
    public BoxCollider tableVolume;
    
    public UnityEvent onFinish;

    public CollectedPuzzlesManager collectedPuzzlesManager;
    
    private Stopwatch timeOfPuttingPuzzle;
    private List<Stopwatch> hints;
    private Stopwatch hintTime;
    
    private PuzzleGameData data;
    private ClossetOfHints _closset;
    
    
    private void Awake()
    {
        UpdateMaterial();
    }
    
    private void Start()
    {
        if (Instance == null) Instance = this;
        
        data = new PuzzleGameData();
        
        timeOfPuttingPuzzle = new Stopwatch();
        timeOfPuttingPuzzle.Start();

        data.puzzleIndex = puzzleIndex;

        hints = new List<Stopwatch>();

        _closset = GameObject.FindFirstObjectByType<ClossetOfHints>();
        
        MixPuzzle();
    }

    public void AddPuzzle()
    {
        puzzleCollected++;
        _closset.PlayAudio(_closset.correctPuzzleAudioClip);
        
        if (puzzleCollected == puzzleCount)
            PuzzleCollected();
    }

    void PuzzleCollected()
    {
        Debug.Log("Puzzle Finished");
        onFinish?.Invoke();
        
        PuzzleCommunication.Instance.SpawnConfetti();
        
        _closset.PlayAudio(_closset.onFinishPuzzleAudioClip);
        _closset.CloseClosset();
        _closset.canOpenClosset = false;
        
        VirtualFriend.Instance.Talk("End");
        
        //CollectedPuzzlesManager.Instance.MarkAsFinished(puzzleIndex);
        
        CollectedPuzzlesManager.Instance.completedPuzzles[puzzleIndex] = true;
        
        CollectDate();
    }

    public void MixPuzzle()
    {
        if (tableVolume == null) return;

        foreach (var puzzle in puzzleParts)
        {
            puzzle.transform.parent = null;
            puzzle.transform.position = GetPointInVolume(tableVolume, puzzle.GetComponent<Collider>().bounds.size);
        }
    }

    Vector3 GetPointInVolume(BoxCollider volume, Vector3 puzzleSize)
    {
        if (volume == null) return Vector3.zero;

        float xMin = volume.transform.position.x - volume.size.x / 2 + puzzleSize.x / 2;
        float xMax = volume.transform.position.x + volume.size.x / 2 - puzzleSize.x / 2;
    
        float yMin = volume.transform.position.y - volume.size.y / 2 + puzzleSize.y / 2;
        float yMax = volume.transform.position.y + volume.size.y / 2 - puzzleSize.y / 2;
    
        float zMin = volume.transform.position.z - volume.size.z / 2 + puzzleSize.z / 2;
        float zMax = volume.transform.position.z + volume.size.z / 2 - puzzleSize.z / 2;

        return new Vector3(
            Random.Range(xMin, xMax),
            Random.Range(yMin, yMax),
            Random.Range(zMin, zMax)
        );
    }

    void UpdateMaterial()
    {
        if (puzzleImage == null) return;
        
        foreach (var puzzle in puzzleParts)
        {
            puzzle.GetComponent<Renderer>().materials[1].mainTexture = puzzleImage;
        }
    }
    
    void CollectDate()
    {
        timeOfPuttingPuzzle.Stop();
        data.puzzleCompletionTime = ConvertMillisecondsToSeconds(timeOfPuttingPuzzle.Elapsed.TotalMilliseconds);
        
        List<double> temp = new List<double>();
        
        foreach (var hint in hints)
        {
            temp.Add(ConvertMillisecondsToSeconds(hint.Elapsed.TotalMilliseconds));
        }
        data.hints = temp;
        
        Debug.Log(data.puzzleCompletionTime);
        Debug.Log("---");
        foreach (var hint in data.hints)
        {
            Debug.Log(hint);
        }

        collectedPuzzlesManager = CollectedPuzzlesManager.Instance;
        
        data.completedPuzzles = collectedPuzzlesManager.completedPuzzles;

        PuzzleCommunication.Instance.data = data;
        PuzzleCommunication.Instance.OnGameFinished("GameFinished:Puzzle");
        
        CollectedPuzzlesManager.Instance.SaveData();
        
        Destroy(gameObject);
    }
    
    public void ShowHint()
    {
        hintTime = new Stopwatch();
        hintTime.Start();
    }

    public void HideHint()
    {
        hintTime.Stop();
        hints.Add(hintTime);
    }

    public void OnPlaced(PlacePoint placePoint)
    {
        var obj = placePoint.placedObject.gameObject;
        Destroy(obj.GetComponent<MeshCollider>());
        Destroy(obj.GetComponent<Grabbable>());
        Destroy(obj.GetComponent<RespawnDropped>());
        Destroy(obj.GetComponent<Rigidbody>());
    }
    
    double ConvertMillisecondsToSeconds(double milliseconds)
    {
        double seconds = milliseconds / 1000.0;
        return Math.Round(seconds, 2);
    }
}

[Serializable]
public class PuzzleGameData
{
    public string name;
    public string code;
    public int puzzleIndex;
    public double puzzleCompletionTime;
    public List<double> hints;
    public List<bool> completedPuzzles;
    public string locale;
}