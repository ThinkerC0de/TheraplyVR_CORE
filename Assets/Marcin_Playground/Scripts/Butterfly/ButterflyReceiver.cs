using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class ButterflyReceiver : MonoBehaviour
{
    [SerializeField] private SceneManager_Butterflies manager;
    [SerializeField] private PanelManager therapistPanel;
    // [SerializeField] private GameViewEncoder encoder;

    public UnityEvent OnStart;
    public UnityEvent OnStop;
    public UnityEvent OnRestore;

    public GameInfo_Motyle info;

    UnityEvent m_MyEvent = new UnityEvent();

    private void Start()
    {
        info = new GameInfo_Motyle();
        manager.updateEvent.AddListener(SendInfoToServer);
    }

    public void Action_ProcessStringData(string _string)
    {
        Debug.Log(_string);
        //if (_string == "StartLevel") StartPlay();
        if (_string == "StartLevel")
        {
            OnStart?.Invoke();
        }

        if (_string == "Welcome") Welcome();

        if (_string == "NextLevel") NextLevel();
        if (_string == "StopLevel") StopPlay();
        if (_string == "Save") Save();

        if (_string.Contains("SetSpeed:")) SetSpeed(_string);
        if (_string.Contains("SetLevel:")) SetLevel(_string);
        if (_string.Contains("SetCount:")) SetCount(_string);

        // if (_string.Contains("SetResolution:")) SetResolution(_string);
        // if (_string.Contains("SetFPS:")) SetFPS(_string);


    }

    // void SetResolution(string msg)
    // {
    //     if (msg == "SetResolution:0")
    //     {
    //         encoder.Resolution = new Vector2(320, 212);
    //     }
    //     if (msg == "SetResolution:1")
    //     {
    //         encoder.Resolution = new Vector2(480, 320);
    //     }
    //     if (msg == "SetResolution:2")
    //     {
    //         encoder.Resolution = new Vector2(720, 480);
    //     }
    //     if (msg == "SetResolution:3")
    //     {
    //         encoder.Resolution = new Vector2(1440, 960);
    //     }
    // }

    // void SetFPS(string msg)
    // {
    //     encoder.StreamFPS = GetIntValue(msg);
    // }

    void SetSpeed(string msg)
    {
        therapistPanel.speed.value = GetIntValue(msg);
        therapistPanel.speedPreset = (PanelManager.butterflySpeed)GetIntValue(msg);

        SendInfoToServer();
    }

    void SetLevel(string msg)
    {
        therapistPanel.levelSlider.value = GetIntValue(msg);
        SendInfoToServer();
    }

    void SetCount(string msg)
    {
        therapistPanel.butterflySlider.value = GetIntValue(msg);
        SendInfoToServer();
    }

    void StartPlay()
    {
        manager.RunOnStart();
        manager.UpdateLevel();
        SendInfoToServer();
    }

    void NextLevel()
    {
        manager.NextLevel();
        SendInfoToServer();
    }

    void StopPlay()
    {
        manager.ClearScene();
    }

    public void Save()
    {
        manager.UpdateKidPanel();
    }

    int GetIntValue(string msg)
    {
        string[] split = msg.Split(":");
        return int.Parse(split[1]);
    }

    void Welcome()
    {
        // FMNetworkManager.instance.SendToServerReliable("Game:" + SceneManager.GetActiveScene().name);
        SendInfoToServer();
    }

    public void SendInfoToServer()
    {
        info.poziom = manager.levelIndex.ToString();
        info.opis = GetLevelDescription(manager.levelIndex);
        info.motyle = manager.collectedButterfliesCount + "/" + manager.butterfliesCount;
        info.biedronki = manager.collectedLadybugsCount + "/" + manager.ladybugsCountToCollect;
        info.banki = manager.collectedBubblesCount + "/" + manager.bubblesCountToCollect;
        info.proby = UI_LifeCounter.Instance.uiSlider.value + "/" + UI_LifeCounter.Instance.uiSlider.maxValue;

        // FMNetworkManager.instance.SendToServerReliable("Info|" + JsonUtility.ToJson(info));
    }

    string GetLevelDescription(int i)
    {
        switch (i)
        {
            case 1:
                {
                    return "Wielokolorowe motyle, jedna różdżka.";
                }
            case 2:
                {
                    return "Dwa kolory motyli, dwie różdżki.";
                }
            case 3:
                {
                    return "Motyle, biedronki, dwie różdżki.";
                }
            case 4:
                {
                    return "Motyle, bańki mydlane, dwie różdżki.";
                }
            case 5:
                {
                    return "Drzewo, wielokolorowe motyle, jedna różdżka.";
                }
            case 6:
                {
                    return "Drzewo, dwa kolory motyli, dwie różdżki.";
                }
            case 7:
                {
                    return "Drzewo, motyle, biedronki, dwie różdżki.";
                }
            case 8:
                {
                    return "Drzewo, motyle, bańki mydlane, dwie różdżki.";
                }
            case 9:
                {
                    return "Balony, wielokolorowe motyle, jedna różdżka.";
                }
            case 10:
                {
                    return "Balony, dwa kolory motyli, dwie różdżki.";
                }
            case 11:
                {
                    return "Balony, motyle, biedronki, dwie różdżki";
                }
            case 12:
                {
                    return "Balony, motyle, bańki mydlane, dwie różdżki.";
                }
        }

        return "";
    }
}

public struct GameInfo_Motyle
{
    public string poziom;
    public string opis;
    public string motyle;
    public string biedronki;
    public string banki;
    public string proby;
}
