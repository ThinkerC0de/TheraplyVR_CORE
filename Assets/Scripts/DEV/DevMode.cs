using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DevMode : MonoBehaviour
{
    [SerializeField]
    private GameObject devModeUI;


    // Start is called before the first frame update
    void Start()
    {
        if (Debug.isDebugBuild)
        {
            devModeUI.SetActive(true);
        }
        else
        {
            devModeUI.SetActive(false);
        }
    }

    public void LoadLevel(int levelIndex)
    {
        GameObject.Find("COMMUNICATION").GetComponent<MainSceneCommunication>().PlayDialogAndOpenScene(levelIndex);
        //SceneManager.LoadScene(levelIndex);
    }
}
