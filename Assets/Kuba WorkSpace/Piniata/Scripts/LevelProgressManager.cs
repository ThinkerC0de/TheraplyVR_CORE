using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LevelProgressManager : MonoBehaviour
{

    [SerializeField] private List<Toggle> chooseMenuToggles;
    [SerializeField] private int currentLevel = 1;
    
    public static LevelProgressManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        else
        {
            Instance = this;
        }

        foreach (Toggle toggle in chooseMenuToggles)
        {
            toggle.interactable = false;
        }

        string saveKey = GeneralDataManager.Instance.KidID + "_PinataCurrentLevel";
        currentLevel = PlayerPrefs.GetInt(saveKey, 0);
        // SetNewCurrentLevel(currentLevel);
    }

    public int LastLevelIndex()
    {
        return chooseMenuToggles.Count;
    }

    [ContextMenu("ostatni")]
    public void Last()
    {
        SetNewCurrentLevel(14);
    }

    [ContextMenu("Reset Player Prefs")]
    public void ResetPrefs()
    {
        PlayerPrefs.DeleteAll();
    }

    public int CurrentLevel()
    {
        return currentLevel;
    }
    
    public void SetNewCurrentLevel(int lvl)
    {
        if (lvl > currentLevel)
        {
            currentLevel = lvl;
            
            string saveKey = GeneralDataManager.Instance.KidID + "_PinataCurrentLevel";
            PlayerPrefs.SetInt(saveKey, currentLevel);
            PlayerPrefs.Save();
        }
        int index = currentLevel;
        
        for (int i = 0; i <= index; i++)
        {
            
            if(i < chooseMenuToggles.Count) chooseMenuToggles[i].interactable = true;

            if (currentLevel == 0)
            {
                // tutaj nic się nie zaznacza że jest done
            }

            if (currentLevel > 0 && i != index)
            {
                chooseMenuToggles[i].GetComponent<PiniataLevelPanelUI>().SetDone();
            }
        }
    }
    
}
