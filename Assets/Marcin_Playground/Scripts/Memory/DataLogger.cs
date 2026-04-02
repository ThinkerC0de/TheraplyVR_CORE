using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class DataLogger : MonoBehaviour
{
    private List<float> reactionTimes = new List<float>();
    private int touchCount = 0;

    public void LogTouchData(float reactionTime)
    {
        reactionTimes.Add(reactionTime);
        touchCount++;
    }

    public void SaveSessionData()
    {
        float averageTime = 0;
        if (reactionTimes.Count > 0)
        {
            averageTime = Mathf.Clamp01(reactionTimes.Average());
        }

        Debug.Log($"Total Touches: {touchCount}");
        Debug.Log($"Average Reaction Time: {averageTime} seconds");

        // Save the data to a file or database as needed.
    }
}