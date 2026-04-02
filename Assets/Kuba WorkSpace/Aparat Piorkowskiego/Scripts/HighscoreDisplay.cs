using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

/// <summary>
/// Attach to a TextMeshProUGUI to show top-3 times for the selected level of PiorkowskiAparatus.
/// You can call Refresh() manually, or toggle autoRefreshOnEnable.
/// </summary>
public class HighscoreDisplay : MonoBehaviour
{
    [SerializeField] private TMP_Text score1;
    [SerializeField] private TMP_Text score2;
    [SerializeField] private TMP_Text score3;
    [SerializeField] private TMP_Text headerLabel;
    [SerializeField] private PiorkowskiAparatus aparatus;
    [SerializeField] private bool autoRefreshOnEnable = true;
    [SerializeField] private string localizationTable = "UI";
    [SerializeField] private string[] gameLabelKeys = new string[] { "Game0Label", "Game1Label", "Game2Label" };
    [SerializeField] private int displayLevelOverride = -1; // -1 means follow aparatus selection

    private void Reset()
    {
        if (aparatus == null) aparatus = FindFirstObjectByType<PiorkowskiAparatus>();
    }

    private void OnEnable()
    {
        HighscoreManager.HighscoresUpdated += OnHighscoresUpdated;
        if (autoRefreshOnEnable)
        {
            Refresh();
        }
    }

    private void OnDisable()
    {
        HighscoreManager.HighscoresUpdated -= OnHighscoresUpdated;
    }

    private void OnHighscoresUpdated()
    {
        Refresh();
    }

    public void Refresh()
    {
        if (aparatus == null)
        {
            aparatus = FindFirstObjectByType<PiorkowskiAparatus>();
        }
        int levelIndex = (displayLevelOverride >= 0 && displayLevelOverride <= 2)
            ? displayLevelOverride
            : (aparatus != null ? aparatus.GetChosenGameIndex() : 0);
        List<float> top = HighscoreManager.GetTopThree(levelIndex);

        if (score1 != null) score1.text = top.Count > 0 ? HighscoreManager.FormatTime(top[0]) + "s." : "-";
        if (score2 != null) score2.text = top.Count > 1 ? HighscoreManager.FormatTime(top[1]) + "s." : "-";
        if (score3 != null) score3.text = top.Count > 2 ? HighscoreManager.FormatTime(top[2]) + "s." : "-";

        if (headerLabel != null)
        {
            string key = (levelIndex >= 0 && levelIndex < gameLabelKeys.Length) ? gameLabelKeys[levelIndex] : null;
            if (!string.IsNullOrEmpty(localizationTable) && !string.IsNullOrEmpty(key))
            {
                var handle = LocalizationSettings.StringDatabase.GetLocalizedStringAsync(localizationTable, key);
                if (handle.IsDone)
                {
                    headerLabel.text = handle.Result;
                }
                else
                {
                    handle.Completed += op => { headerLabel.text = op.Result; };
                }
            }
        }
    }

    public void SetDisplayLevel(int levelIndex)
    {
        if (levelIndex < 0 || levelIndex > 2) return;
        if (!aparatus.CanStart) return;

        displayLevelOverride = levelIndex;
        Refresh();
    }

    public void ClearDisplayLevelOverride()
    {
        displayLevelOverride = -1;
        Refresh();
    }
}


