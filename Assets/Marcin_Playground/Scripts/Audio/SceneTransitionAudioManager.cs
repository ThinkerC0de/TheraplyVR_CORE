using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[System.Serializable]
public class SceneData
{
    public string sceneName;
    public string assetTableName = "";
    public string localizationKey = "";
    [Range(0f, 10f)]
    public float delayInSeconds = 0f;
}

public class SceneTransitionAudioManager : MonoBehaviour
{
    public List<SceneData> sceneDataList = new List<SceneData>();
    
    private static SceneTransitionAudioManager _instance;
    public static SceneTransitionAudioManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<SceneTransitionAudioManager>();
            }
            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }
    
    public (string tableReference, string dialogKey, float waitTime) GetAudioParams(int buildIndex)
    {
        if (buildIndex >= 0 && buildIndex < sceneDataList.Count)
        {
            var data = sceneDataList[buildIndex];
            return (data.assetTableName, data.localizationKey, data.delayInSeconds);
        }
        
        return ("", "", 0f);
    }
    
    public bool HasAudioForIndex(int buildIndex)
    {
        if (buildIndex >= 0 && buildIndex < sceneDataList.Count)
        {
            var data = sceneDataList[buildIndex];
            return !string.IsNullOrEmpty(data.assetTableName) && !string.IsNullOrEmpty(data.localizationKey);
        }
        return false;
    }
    
    public (string tableReference, string dialogKey, float waitTime) GetAudioParamsBySceneName(string sceneName)
    {
        var data = sceneDataList.FirstOrDefault(d => d.sceneName == sceneName);
    
        if (data != null)
        {
            return (data.assetTableName, data.localizationKey, data.delayInSeconds);
        }
    
        return ("", "", 0f);
    }
}