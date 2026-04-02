using System.Collections;
using UnityEngine;

public class MainSceneCommunication : MonoBehaviour
{
    public MainMenuController menuController;
    public ChooseSceneData chosenSceneData;
    private static bool introPlayed = false;
    [SerializeField] private LocController locController;
    [SerializeField] private BlinkersActivator blinkersActivator;
    public void GetJsonFromPreviewApp(string _json)
    {
        if (_json.Contains("selectedSession"))
        {
            chosenSceneData = JsonUtility.FromJson<ChooseSceneData>(_json);
            GeneralDataManager.Instance.KidID = chosenSceneData.kidID;
            blinkersActivator.SetBlinkersActivated(chosenSceneData.blinkersActive);

            StartCoroutine(SetLocaleAndPlayAudio(chosenSceneData.locale));
        }
        else
        {
            // Debug.Log("Coś nie halo!");  
        }
    }

    IEnumerator SetLocaleAndPlayAudio(string locale)
    {
        yield return StartCoroutine(locController.ChangeLocaleString(locale));
        yield return StartCoroutine(PlayDialogAndLoadScene());
    }

    private void Start()
    {
        if (!introPlayed)
        {
            PlayIntroAudio();
            introPlayed = true;
        }
    }

    /*
    IEnumerator PlayDialogAndLoadScene()
    {
        yield return VirtualFriend.Instance.FriendTalking("start_scene_on_session_started");
        menuController.ChangeScene(chosenSceneData.index);
        menuController.OpenScene();
    }
    */

    IEnumerator PlayDialogAndLoadScene(int _index = -1)
    {
        int sceneIndex = (_index == -1) ? chosenSceneData.index : _index;

        string scenePath = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(sceneIndex);
        string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);

        Debug.Log($"Loading scene: {sceneName} (index: {sceneIndex})");

        var transitionAudioManager = SceneTransitionAudioManager.Instance;
        var virtualFriend = VirtualFriend.Instance;

        if (transitionAudioManager != null && virtualFriend != null)
        {
            var (tableReference, dialogKey, waitTime) = transitionAudioManager.GetAudioParamsBySceneName(sceneName);
            if (!string.IsNullOrEmpty(tableReference) && !string.IsNullOrEmpty(dialogKey))
            {
                Debug.Log($"Playing audio: {tableReference}/{dialogKey}");
                yield return virtualFriend.FriendTalkingOtherTable(tableReference, dialogKey, waitTime);
            }
            else
            {
                Debug.Log("Using fallback audio");
                yield return virtualFriend.FriendTalking("start_scene_on_session_started");
            }
        }
        else if (virtualFriend != null)
        {
            Debug.Log("Using fallback audio");
            yield return virtualFriend.FriendTalking("start_scene_on_session_started");
        }

        menuController.ChangeScene(sceneIndex);
        menuController.OpenScene();
    }

    public void PlayDialogAndOpenScene(int _index)
    {
        StartCoroutine(PlayDialogAndLoadScene(_index));
    }


    private void PlayIntroAudio()
    {
        StartCoroutine(PlayIntroAudioEnum());
    }

    IEnumerator PlayIntroAudioEnum()
    {
        yield return new WaitForSeconds(1.5f);
        yield return VirtualFriend.Instance.FriendTalking("start_scene_on_start", 2.0f);
        yield return new WaitForSeconds(1.5f);
        StartCoroutine(VirtualFriend.Instance.FingerPoint());
        yield return VirtualFriend.Instance.FriendTalking("General_door_number");
    }

    public void PlayConnectedAudio()
    {
        // StartCoroutine(PlayConnectedAudioEnum());
    }

    IEnumerator PlayConnectedAudioEnum()
    {
        yield return VirtualFriend.Instance.FriendTalking("General_connected");
    }
}

[System.Serializable]
public class ChooseSceneData
{
    public string name;
    public int index;
    public string kidID;
    public string locale;
    public bool blinkersActive = true;
}
