using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Manages top-3 best times (lower is better) per level using PlayerPrefs and JSON.
/// Levels are referenced by zero-based index: 0, 1, 2
/// </summary>
public static class HighscoreManager
{
    private const string PlayerPrefsKey = "PiorkowskiAparatus_Highscores";
    public static event Action HighscoresUpdated;

    [Serializable]
    private class LevelScores
    {
        public List<float> times = new List<float>();
    }

    [Serializable]
    private class HighscoresData
    {
        public LevelScores[] levels = new LevelScores[3];
    }

    private static HighscoresData Load()
    {
        string json = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
        HighscoresData data;
        if (string.IsNullOrEmpty(json))
        {
            data = CreateEmpty();
        }
        else
        {
            try
            {
                data = JsonUtility.FromJson<HighscoresData>(json);
                if (data == null || data.levels == null || data.levels.Length != 3)
                {
                    data = CreateEmpty();
                }
                for (int i = 0; i < 3; i++)
                {
                    if (data.levels[i] == null) data.levels[i] = new LevelScores();
                    if (data.levels[i].times == null) data.levels[i].times = new List<float>();
                }
            }
            catch (Exception)
            {
                data = CreateEmpty();
            }
        }
        return data;
    }

    private static HighscoresData CreateEmpty()
    {
        var data = new HighscoresData();
        for (int i = 0; i < 3; i++)
        {
            data.levels[i] = new LevelScores();
        }
        return data;
    }

    private static void Save(HighscoresData data)
    {
        string json = JsonUtility.ToJson(data);
        PlayerPrefs.SetString(PlayerPrefsKey, json);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Submit a new completion time in seconds for the given level index (0-2).
    /// Keeps only the best 3 times (ascending) per level.
    /// Returns the 0-based placement index if the score is within top 3; otherwise -1.
    /// </summary>
    public static int SubmitScore(int levelIndex, float timeSeconds)
    {
        if (levelIndex < 0 || levelIndex > 2) return -1;
        if (timeSeconds <= 0) return -1;

        HighscoresData data = Load();
        List<float> times = data.levels[levelIndex].times;
        times.Add(timeSeconds);
        times.Sort();
        int placementIndex = times.IndexOf(timeSeconds);
        if (times.Count > 3)
        {
            times.RemoveRange(3, times.Count - 3);
        }
        Save(data);
        try { HighscoresUpdated?.Invoke(); } catch { }
        return placementIndex >= 0 && placementIndex < 3 ? placementIndex : -1;
    }

    /// <summary>
    /// Returns a copy of top-3 times for the given level (may be less if not enough scores).
    /// </summary>
    public static List<float> GetTopThree(int levelIndex)
    {
        if (levelIndex < 0 || levelIndex > 2) return new List<float>();
        HighscoresData data = Load();
        return new List<float>(data.levels[levelIndex].times);
    }

    /// <summary>
    /// Formats time as seconds:hundredths (e.g., 10:15 for 10.15s).
    /// </summary>
    public static string FormatTime(float timeSeconds)
    {
        if (timeSeconds <= 0f) return "-";
        int wholeSeconds = Mathf.FloorToInt(timeSeconds);
        int hundredths = Mathf.Clamp(Mathf.FloorToInt((timeSeconds - wholeSeconds) * 100f), 0, 99);
        return string.Format("{0}:{1:D2}", wholeSeconds, hundredths);
    }
}


