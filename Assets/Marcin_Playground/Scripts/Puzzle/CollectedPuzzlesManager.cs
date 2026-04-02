using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization.Tables;

public class CollectedPuzzlesManager : MonoBehaviour
{
    public static CollectedPuzzlesManager Instance;
    public List<GameObject> completedPuzzlesImages;
    public List<bool> completedPuzzles;
    private List<bool> _savedPuzzles;

    private void Start()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
        }
        
        LoadData();
    }

    public void MarkAsFinished(int i)
    {
        completedPuzzles[i] = true;
        completedPuzzlesImages[i].GetComponent<Renderer>().enabled = false;
    }

    public void UnhideCompleted()
    {
        if (completedPuzzles.Count == completedPuzzlesImages.Count)
        {
            for (int i = 0; i < completedPuzzles.Count; i++)
            {
                if (completedPuzzles[i] == true)
                {
                    completedPuzzlesImages[i].GetComponent<Renderer>().enabled = false;
                }
            }
        }
    }

    public void SaveData()
    {
        BoolListWrapper wrapper = new BoolListWrapper();
        wrapper.boolList = completedPuzzles;
        //PlayerPrefs.SetString(GeneralDataManager.Instance.KidID+"_Puzzle", JsonUtility.ToJson(this));
        string json = JsonUtility.ToJson(wrapper);
        Debug.Log(json);
        PlayerPrefs.SetString(GeneralDataManager.Instance.KidID+"_Puzzle", json);
        PlayerPrefs.Save();
        
        UnhideCompleted();
    }

    public void LoadData()
    {
        StartCoroutine(LoadDataCoroutine());
    }

    IEnumerator LoadDataCoroutine()
    {
        if (PlayerPrefs.HasKey(GeneralDataManager.Instance.KidID+"_Puzzle"))
        {
            string json = PlayerPrefs.GetString(GeneralDataManager.Instance.KidID+"_Puzzle");
            BoolListWrapper temp = JsonUtility.FromJson<BoolListWrapper>(json);
            if (temp.boolList.Count > 0)
                completedPuzzles = temp.boolList;
        }

        yield return null;
        
        UnhideCompleted();
    }
}

public class BoolListWrapper
{
    public List<bool> boolList;
}
