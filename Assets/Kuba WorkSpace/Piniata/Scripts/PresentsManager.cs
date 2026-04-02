using System;
using System.Collections.Generic;
using UnityEngine;

public class PresentsManager : MonoBehaviour
{
    [SerializeField] private LevelProgressManager _levelProgressManager;
    [SerializeField] private List<int> presentsIndexes;
    [SerializeField] private List<GameObject> presents;
    [SerializeField] private ParticleSystem _particleSystem;

    [SerializeField] private GameObject paperMessage;

    [SerializeField] private CollectedPresents _collectedPresents; 
    
    private static int test;
    private void Start()
    {
        string json = PlayerPrefs.GetString("CollectedPresents_" + GeneralDataManager.Instance.KidID, "null");
        
        if ( json != "null")
        {
            _collectedPresents = JsonUtility.FromJson<CollectedPresents>(json);
        }
        
        foreach (GameObject present in presents)
        {
            present.SetActive(false);
        }

        ShowPresent();
        // for (int i = 0; i <= _levelProgressManager.CurrentLevel(); i++)
        // {
        //     if (presentsIndexes.Contains(i-1))
        //     {
        //         paperMessage.SetActive(false);
        //         int index = presentsIndexes.IndexOf(i-1);
        //         presents[index].SetActive(true);
        //     }
        // }

        int last = _levelProgressManager.LastLevelIndex();
        test = last;

        if (_collectedPresents.collectedPresentsIndexes.Count == 0) //  _levelProgressManager.CurrentLevel()
        {
            StartCoroutine(VirtualFriend.Instance.FriendTalking("Intro_First"));
        }
        else if (_collectedPresents.collectedPresentsIndexes.Count >= _levelProgressManager.LastLevelIndex())
        {
            StartCoroutine(VirtualFriend.Instance.FriendTalking("Intro_Last"));
        }
        else
        {
            StartCoroutine(VirtualFriend.Instance.FriendTalking("Intro_General"));
        }
    }

    [ContextMenu("SHOW PRESENTS")]
    public void ShowPresent()
    {
        // if (presentsIndexes.Contains(_levelProgressManager.CurrentLevel()-1))
        // {
        //     paperMessage.SetActive(false);
        //     int i = presentsIndexes.IndexOf(_levelProgressManager.CurrentLevel()-1);
        //     
        //     _particleSystem.transform.localPosition = presents[i].transform.localPosition;
        //     _particleSystem.transform.localRotation = presents[i].transform.localRotation;
        //     
        //     if (!presents[i].activeSelf)
        //     {
        //         _particleSystem.Play();
        //         presents[i].SetActive(true);
        //     }
        // }

        _collectedPresents.collectedPresentsIndexes.Sort();
        foreach (int index in presentsIndexes)
        {
            bool canShow = true;

            int i = 0;
            if (!_collectedPresents.collectedPresentsIndexes.Contains(index))
            {
                canShow = false;
            }
            else
            {
                i = presentsIndexes.IndexOf(index);
            }
            
            if (canShow)
            {
                paperMessage.SetActive(false);
                var transform1 = _particleSystem.transform;
                transform1.localPosition = presents[i].transform.localPosition;
                transform1.localRotation = presents[i].transform.localRotation;
                if (!presents[i].activeSelf)
                {
                    _particleSystem.Play();
                    presents[i].SetActive(true);
                }
            }
        }
    }

    public void CollectedPresent(int presentID)
    {
        if (!_collectedPresents.collectedPresentsIndexes.Contains(presentID))
        {
            _collectedPresents.collectedPresentsIndexes.Add(presentID);
        }
        
        PlayerPrefs.SetString("CollectedPresents_" + GeneralDataManager.Instance.KidID, JsonUtility.ToJson(_collectedPresents) );
    }
}

[Serializable]
public class CollectedPresents
{
    public List<int> collectedPresentsIndexes;
}