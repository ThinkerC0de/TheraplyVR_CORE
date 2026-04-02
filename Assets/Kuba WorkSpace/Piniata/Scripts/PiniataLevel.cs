using System;
using System.Collections.Generic;
using UnityEngine;

public class PiniataLevel : MonoBehaviour
{
    [SerializeField] private Transform piniataModel;
    [SerializeField] private GameObject brokenPiniata;
    
    [SerializeField] private LevelTypeEnum levelType;
    public LevelTypeEnum LevelType => levelType;

    [SerializeField] private float startIntervalTime = 4.0f;
    [SerializeField] private float intervalTime = 4.0f;
    public int touchIndex = 0;
    [SerializeField] private bool resetOnWrong = true;

    [SerializeField] private List<GameObject> distractingInsects;
    [SerializeField] private List<GameObject> distractingInsectsReference;

    [SerializeField] private bool redStick = true;
    [SerializeField] private bool blueStick = true;
    public bool RedStick => redStick;
    public bool BlueStick => blueStick;
    

    public bool levelFinished = false;
    public bool ResetOnWrong => resetOnWrong;

    private void OnDestroy()
    {
        DeleteInsects();
    }

    public void DeleteInsects()
    {
        if (distractingInsectsReference.Count > 0)
        {
            foreach (GameObject o in distractingInsectsReference)
            {
                Destroy(o);
            }
        }

        distractingInsectsReference.Clear();
        distractingInsectsReference = new List<GameObject>();
    }

    [ContextMenu("BROKE")]
    public void BrokePiniata()
    {
        if (brokenPiniata == null) return;
        if(brokenPiniata.activeSelf) return;
        
        Debug.Log("Single Game Piniata Data z PINIATA LEVELS BROKEN PINIATA");
        SingleGameFinalData();

        StartCoroutine(VirtualFriend.Instance.FriendTalking("Piniata_Done", 1.7f));
        piniataModel.gameObject.SetActive(false);
        brokenPiniata.SetActive(true);
    }

    public void GenerateInsects()
    {
        intervalTime = startIntervalTime;
        
        if (distractingInsectsReference.Count > 0) return;
        
        foreach (GameObject distractingInsect in distractingInsects)
        {
            GameObject piniata = Instantiate(distractingInsect, piniataModel);
            distractingInsectsReference.Add(piniata);
        }
    }
    
    public float IntervalTime
    {
        get => intervalTime;
        set => intervalTime = value;
    }

    [SerializeField] List<SingleRound> gameSeries;

    public List<SingleRound> GameSeries => gameSeries;
    [SerializeField] private int maxPoints = 0;
    
    [SerializeField] private int bugsHitted = 0;
    [SerializeField] private string gameStartDate;
    [SerializeField] private string gameFinishedDate;
    [SerializeField] private PiniataGame piniataGame;

    public void SaveStartTime()
    {
        gameStartDate = TheraplyHelpers.DateTimeNowToString();
    }
    
    public void SaveFinishTime()
    {
        gameStartDate = TheraplyHelpers.DateTimeNowToString();
    }
    
    public void BugHitted()
    {
        bugsHitted++;
    }
    
    public int MaxPoints => maxPoints;

    [SerializeField] private int currentSequence = 0;

    private void Awake()
    {
        piniataGame = FindFirstObjectByType<PiniataGame>();

        foreach (SingleRound sequence in gameSeries)
        {
            foreach (RoundPoints roundPoints in sequence.roundPoints)
            {
                foreach (HitPoint hitPoint in roundPoints.points)
                {
                    maxPoints++;
                    hitPoint.Piniata = piniataGame;
                    hitPoint.SetRigidbody = piniataGame.Rb;
                }
            }
        }

        startIntervalTime = intervalTime;
    }

    public enum LevelTypeEnum
    {
        PointTouched,
        FixedTime,
        AverageTime
    }


    public ServerSaveData serverData;
    
    public void SingleGameFinalData()
    {
        serverData.kidID = GeneralDataManager.Instance.KidID;
        serverData.therapistID = GeneralDataManager.Instance.TherapistsID;
        serverData.levelIndex = FindFirstObjectByType<PiniataLevelManager>().GetCurrentLevelIndex();
        serverData.bugsHitted = bugsHitted;
        serverData.levelType = (int)levelType;
        serverData.gameStartDate = gameStartDate;
        serverData.gameFinishedDate = TheraplyHelpers.DateTimeNowToString();
        
        // public string gameStartDate;
        // public string gameFinishedDate;

        DateTime dtStart = TheraplyHelpers.StringToDateTime(serverData.gameStartDate);
        DateTime dtEnd = TheraplyHelpers.StringToDateTime(serverData.gameFinishedDate);

        TimeSpan difference = dtEnd - dtStart;

        GameSeriesData gsd = new GameSeriesData
        {
            points = new List<GamePointsData>()
        };

        float finalAVG = 0.0f;
        float singleRoundAVG = 0.0f;
        float roundPointAVG = 0.0f;
        foreach (SingleRound singleRound in gameSeries)
        {
            gsd.intervalAdvantage = singleRound.intervalAdvantage;
            gsd.averageRoundTime = singleRound.averageRoundTime;
            // generalAv += gsd.averageRoundTime;
            foreach (RoundPoints roundPoint in singleRound.roundPoints)
            {
                GamePointsData gpd = new GamePointsData
                {
                    averageTime = roundPoint.averageTime,
                    pointColor = new List<int>(),
                    reactionTime = new List<float>()
                };

                foreach (int i in roundPoint.pointColor)
                {
                    gpd.pointColor.Add(i);
                }

                // roundPoint.pointColor = new List<int>();
                
                if (roundPoint.reactionTime.Count > 0)
                {
                    Debug.Log("ILOŚĆ CZASÓW REAKCJI: " + roundPoint.reactionTime.Count);
                
                    foreach (float f in roundPoint.reactionTime)
                    {
                        roundPointAVG += f;
                        Debug.Log("Czas punktu:" + f);
                        gpd.reactionTime.Add(f);
                    }
                    
                    roundPointAVG /= roundPoint.reactionTime.Count;
                    Debug.Log("Średnia punktów:" + roundPointAVG);
                }
                // roundPoint.reactionTime = new List<float>();
                
                singleRoundAVG += roundPointAVG;
                gsd.points.Add(gpd);
                roundPointAVG = 0.0f;
            }


            singleRoundAVG /= singleRound.roundPoints.Count;
            finalAVG += singleRoundAVG;
            singleRoundAVG = 0.0f;
            // generalAv /= gameSeries.Count;
            
            serverData.gameSeries.Add(gsd);
        }
        
        finalAVG /= gameSeries.Count;
        finalAVG = (float)Math.Round(finalAVG, 2);
        
        Debug.Log("FINALNA ŚREDNIA: " + finalAVG);
        
        
        // GeneralDataManager.Instance.SaveDataToServer(serverData);
        PiniataCommunication pc = FindFirstObjectByType<PiniataCommunication>();
        pc.SetPiniataData(difference.TotalSeconds, piniataGame.WrongAnswerPoint, finalAVG, maxPoints);
        
        StrapiData<ServerSaveData> sd = new StrapiData<ServerSaveData>()
        {
            data = serverData
        };
        
        string json = JsonUtility.ToJson(sd);
        
        Debug.Log(json);
    }
}

[Serializable]
public class SingleRound
{
    public List<RoundPoints> roundPoints;
    public float averageRoundTime;
    public float intervalAdvantage; // np 20%
}

[Serializable]
public class RoundPoints
{
    public List<HitPoint> points;
    public List<float> reactionTime;
    public List<int> pointColor;
    public float averageTime;
}

[Serializable]
public class StrapiData<T>
{
    public T data;
}

[Serializable]
public class ServerSaveData
{
    public string kidID;
    public string therapistID;
    public int levelIndex;
    public int bugsHitted;
    public string gameStartDate;
    public string gameFinishedDate;
    public int levelType; // to będzie z enuma

    public List<GameSeriesData> gameSeries;
}

[Serializable]
public class GameSeriesData
{
    public List<GamePointsData> points;
    public float averageRoundTime;
    public float intervalAdvantage; // np 20%
}

[Serializable]
public class GamePointsData
{
    public List<float> reactionTime;
    public List<int> pointColor;
    public float averageTime;
}