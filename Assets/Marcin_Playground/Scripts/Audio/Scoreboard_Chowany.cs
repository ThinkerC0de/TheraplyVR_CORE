using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class Scoreboard_Chowany : MonoBehaviour
{
    public static Scoreboard_Chowany Instance;
    
    public TMP_Text bestTime;
    public TMP_Text bestHits;
    public TMP_Text fastestReaction;
    public TMP_Text averageReaction;

    public TimeSpan currentTime;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
        }
    }

    /*
    void Start()
    {
        bestTime.text = "00:00:00";
        bestHits.text = "0";
        fastestReaction.text = "0";
        averageReaction.text = "0";
        
        SetCurrentTime("00:01:01","00:03:09");
    }
    */

    public void SetCurrentTime(string startTime, string endTime)
    {
        currentTime = DateTimeOffset.Parse(endTime).UtcDateTime - DateTimeOffset.Parse(startTime).UtcDateTime;
        Debug.Log(currentTime);
        SetBestTime(currentTime);
    }

    public void SetBestTime(TimeSpan time)
    {
        string minutes = "00";
        if (time.Minutes < 10)
            minutes = "0" + time.Minutes;

        string seconds = "00";
        if (time.Seconds < 10)
            seconds = "0" + time.Seconds;

        string miliseconds = "00";
        if (time.Milliseconds < 10)
            miliseconds = "0" + time.Milliseconds;

        bestTime.text = minutes + ":" + seconds + ":" + miliseconds;
    }

    void UpdateScoreboard(ChowanyScoreboard data)
    {
        
    }
    
}

public struct ChowanyScoreboard
{
    public string startTime;
    public string endTime;
    public int bestHits;
    public string fastestReaction;
    public string averageReaction;
}
