using Proyecto26;
using UnityEngine;

public class GeneralDataManager : MonoBehaviour
{
    public static GeneralDataManager Instance;

    [SerializeField] private string kidID;
    [SerializeField] private string therapistsID;
    [SerializeField] private string apiURL = "http://localhost:1337/api/"; // https://theraply-research.herokuapp.com/ , https://theraply-research.herokuapp.com/

    [SerializeField] private string serverIP;
        
    public string KidID
    {
        set => kidID = value;
        get => kidID;
    }
    public string TherapistsID
    {
        set => therapistsID = value;
        get => therapistsID;
    }
    public string ServerIP
    {
        set => serverIP = value;
        get => serverIP;
    }

   
    [SerializeField] private int currentKid;
    [SerializeField] private int currentTherapist;
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        else
        {
            DontDestroyOnLoad(this);
            Instance = this;
        }
    }

    public void SaveDataToServer<T>(T data)
    {
    }
    
}
