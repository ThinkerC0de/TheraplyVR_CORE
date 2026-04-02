using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UI;

public class PassiveMindfulnessController : MonoBehaviour
{
    [SerializeField] private List<GameObject> levelsToInitialize;
    [SerializeField] private List<Toggle> toggles;
    [SerializeField] private GameObject currentLevel;

    [Space(20)]
    [SerializeField] private PlayableDirector dir;
    [SerializeField] private TimelineAsset timeline;

    [Space(20)]
    [SerializeField] private Transform playerHead;

    [SerializeField] private Transform friendHead;
    public Transform PlayerHead => playerHead;
    public Transform FriendHead => friendHead;

    [Space(20)]
    [SerializeField] private Animator virtualFriendAnimator;
    [SerializeField] private AudioSource virtualFriendAudioSource;
    [SerializeField] private AudioSource backgroundAudioSource;

    [Space(20)][SerializeField] private GameObject menu;
    [Space(60)]
    [Range(0, 9)][SerializeField] private int helperSession = 0;


    [SerializeField] private SignalReceiver signalReceiver;

    [SerializeField] private BackToMenu backToMenu;
    [SerializeField] private DaytimeSceneLoader _daytimeSceneLoader;

    [SerializeField] private PassiveMindfulnessServerData serverData;

    [SerializeField] private PassiveMindfulnessLocalSave localSave;
    [SerializeField] private List<GameObject> uiIndicators;
    public void Play()
    {
        dir.Play();
    }

    public void Pause()
    {
        dir.Pause();
    }

    private void Awake()
    {
        localSave = new PassiveMindfulnessLocalSave
        {
            levelDone = new List<bool>(10)
        };

        for (int i = 0; i < 10; i++)
        {
            localSave.levelDone.Add(false);
        }

        serverData = new PassiveMindfulnessServerData();
        // PrepareUI();
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        LoadTheGame();
    }

    private void Start()
    {
        // ChooseSession(0);
        UpdateUIIndicators();
    }

    void PrepareUI()
    {
        for (int i = 0; i < toggles.Count; i++)
        {
            var i1 = i;
            toggles[i1].onValueChanged.AddListener(delegate (bool arg0)
            {
                ChooseSession(i1);
            });
        }
    }

    public void ChooseSession(int index)
    {
        serverData.chosenSession = index;

        if (index <= 6 && _daytimeSceneLoader.IsDaySceneLoaded)
        {
            StartCoroutine(ChooseSessionEnum(index));
        }
        else if (index > 6 && _daytimeSceneLoader.IsNightSceneLoaded)
        {
            StartCoroutine(ChooseSessionEnum(index));
        }
        else
        {
            StartCoroutine(ChooseSessionEnum(index, OVRScreenFade.instance.fadeTime + 0.2f));
        }
    }

    // AsyncOperationHandle<GameObject> opHandle;
    // private GameObject instantiatedObj;
    // [SerializeField] private List<string> levelKeys;
    // void OnDestroy()
    // {
    //     Addressables.Release(opHandle);
    //     Destroy(instantiatedObj);
    // }
    IEnumerator ChooseSessionEnum(int index, float fadeTime = 0.0f)
    {

        if (index <= 5)
        {
            yield return StartCoroutine(_daytimeSceneLoader.LoadDaySceneEnum());
        }
        else
        {
            yield return StartCoroutine(_daytimeSceneLoader.LoadNightSceneEnum());
        }

        yield return new WaitForSeconds(fadeTime);
        //
        //
        // if (opHandle.IsValid())
        // {
        //     Addressables.Release(opHandle);
        //     Destroy(instantiatedObj);
        // }
        //
        // opHandle = Addressables.LoadAssetAsync<GameObject>(levelKeys[index]);
        // yield return opHandle;
        //
        // if (opHandle.Status == AsyncOperationStatus.Succeeded)
        // {
        //     GameObject obj = opHandle.Result;
        //     instantiatedObj=Instantiate(obj, transform);
        // }
        //
        // dir = instantiatedObj.GetComponent<PlayableDirector>();
        // timeline = (TimelineAsset)dir.playableAsset;

        if (currentLevel != null)
        {
            Destroy(currentLevel);
        }


        currentLevel = Instantiate(levelsToInitialize[index], transform);


        dir = currentLevel.GetComponent<PlayableDirector>();
        timeline = (TimelineAsset)dir.playableAsset;


        // BINDINGS

        for (int i = 0; i < timeline.outputTrackCount; i++)
        {
            if (timeline.GetOutputTrack(i).name == "Virtual Friend")
            {
                dir.SetGenericBinding(timeline.GetOutputTrack(i), virtualFriendAudioSource);
            }
        }

        for (int i = 0; i < timeline.outputTrackCount; i++)
        {
            if (timeline.GetOutputTrack(i).name == "Background Music")
            {
                dir.SetGenericBinding(timeline.GetOutputTrack(i), backgroundAudioSource);
            }
        }

        for (int i = 0; i < timeline.outputTrackCount; i++)
        {
            if (timeline.GetOutputTrack(i).name == "GENERAL_SIGNALS")
            {
                dir.SetGenericBinding(timeline.GetOutputTrack(i), signalReceiver);
            }
        }

        StartTheTimeline();
    }

    public void StartTheTimeline()
    {
        dir.Play();
        menu.SetActive(false);
        serverData.gameStartDate = TheraplyHelpers.DateTimeNowToString();
    }

    [ContextMenu("TRY SESSION")]
    public void TrySession()
    {
        ChooseSession(helperSession);
        // StartTheTimeline();
    }

    [ContextMenu("PLAY TRY SESSION")]
    public void PlayTrySession()
    {
        // ChooseSession(helperSession);
        StartTheTimeline();
    }


    public void CurrentTimelineEnded()
    {
        // menu.SetActive(true);

        serverData.kidID = GeneralDataManager.Instance.KidID;
        serverData.therapistID = GeneralDataManager.Instance.TherapistsID;
        serverData.gameFinishedDate = TheraplyHelpers.DateTimeNowToString();

        GeneralDataManager.Instance.SaveDataToServer(serverData);

        backToMenu.GoBack();
    }

    public void ChangeTime(float t)
    {
        dir.Pause();
        dir.time += t;
        dir.Play();
    }

    public void SetTrigger(string trig)
    {
        virtualFriendAnimator.SetTrigger(trig);
    }

    [ContextMenu("DELETE SAVE")]
    public void DeleteSave()
    {
        string key = GeneralDataManager.Instance.KidID + "_PassiveMindfulness";
        PlayerPrefs.DeleteKey(key);
    }

    [ContextMenu("SAVE")]
    public void SaveTheGame()
    {
        int index = serverData.chosenSession;
        localSave.levelDone[serverData.chosenSession] = true;

        string key = GeneralDataManager.Instance.KidID + "_PassiveMindfulness";
        string json = JsonUtility.ToJson(localSave);
        Debug.Log(json);
        PlayerPrefs.SetString(key, json);
    }

    [ContextMenu("LOAD")]
    public void LoadTheGame()
    {
        string key = GeneralDataManager.Instance.KidID + "_PassiveMindfulness";
        string save = PlayerPrefs.GetString(key, "NONE");

        if (save != "NONE")
        {
            Debug.Log("MAMY SAVE'A!");
            localSave = JsonUtility.FromJson<PassiveMindfulnessLocalSave>(save);
        }
        else
        {
            Debug.Log("NIEEEEEEEE MAMY SAVE'A!");
        }
    }

    private void UpdateUIIndicators()
    {
        for (int i = 0; i < localSave.levelDone.Count; i++)
        {
            uiIndicators[i].SetActive(localSave.levelDone[i]);
        }
    }

}

[Serializable]
public class PassiveMindfulnessServerData
{
    public string kidID;
    public string therapistID;
    public int chosenSession;
    public string gameStartDate;
    public string gameFinishedDate;
}

[Serializable]
public class PassiveMindfulnessLocalSave
{
    public List<bool> levelDone;
}