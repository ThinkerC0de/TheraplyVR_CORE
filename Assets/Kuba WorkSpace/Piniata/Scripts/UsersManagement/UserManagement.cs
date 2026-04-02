using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UserManagement : MonoBehaviour
{
    [SerializeField] private TMP_InputField childInput;
    [SerializeField] private TMP_InputField therapistInput;



    [SerializeField] private GameObject nameListItem;
    
    [SerializeField] private Transform childrenListParent;
    [SerializeField] private Transform therapistsListParent;

    [SerializeField] private ToggleGroup _toggleGroupKids;
    [SerializeField] private ToggleGroup _toggleGroupTherapists;


    [SerializeField] private SaveUsersData _saveUsersData;
    
    private void Awake()
    {
        LoadData();
        GenerateTherapistsList();
        GenerateChildrenList();
    }

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(4.0f);
        if (! string.IsNullOrEmpty(_saveUsersData.kidId) && ! string.IsNullOrEmpty(_saveUsersData.therapistsID))
        {
            StartCoroutine(VirtualFriend.Instance.FriendTalking("on_start"));
        }
    }

    [ContextMenu("Create Child")]
    public void AddChild()
    {
        if(string.IsNullOrEmpty(childInput.text)) return;
        
        ChildClass kid = new ChildClass
        {
            id = childInput.text
        };

        _saveUsersData.children.Add(kid);
        childInput.text = null;
        GenerateChildrenList();
        SaveData();
    }

    [ContextMenu("Create Therapist")]
    public void AddTherapist()
    {
        if(string.IsNullOrEmpty(therapistInput.text)) return;
        
        TherapistClass teacher = new TherapistClass
        {
            id = therapistInput.text
        };
        
        _saveUsersData.therapists.Add(teacher);
        therapistInput.text = null;
        GenerateTherapistsList();
        SaveData();
    }

    private void GenerateChildrenList()
    {
        foreach (Transform ch in childrenListParent)
        {
            Destroy(ch.gameObject);
        }

        int i = 0;
        foreach (ChildClass child in _saveUsersData.children)
        {
            GameObject gm = Instantiate(nameListItem, childrenListParent);
            gm.GetComponentInChildren<TextMeshProUGUI>().text = child.id;
            Toggle tg = gm.GetComponent<Toggle>();
           
            var i1 = i;
            tg.onValueChanged.AddListener((b)=>OnButtonClickedKid(i1));
           
            i++;
        }

        int j = 0;
        foreach (Transform child in childrenListParent)
        {
            Toggle tg = child.GetComponent<Toggle>();
            tg.group = _toggleGroupKids;
            
            tg.isOn = j == _saveUsersData.kidIdIndex;
            j++;
        }
    }
    
    private void GenerateTherapistsList()
    {
        foreach (Transform ch in therapistsListParent)
        {
            Destroy(ch.gameObject);
        }

        int i = 0;
        foreach (TherapistClass th in _saveUsersData.therapists)
        {
            GameObject gm = Instantiate(nameListItem, therapistsListParent);
            gm.GetComponentInChildren<TextMeshProUGUI>().text = th.id;
            Toggle tg = gm.GetComponent<Toggle>();
            
            var i1 = i;
            tg.onValueChanged.AddListener((b)=>OnButtonClickedTherapists(i1));
            i++;
        }

        int j = 0;
        
        
        foreach (Transform child in therapistsListParent)
        {
            Toggle tg = child.GetComponent<Toggle>();
            tg.group = _toggleGroupTherapists;
            
            tg.isOn = j == _saveUsersData.therapistsIDIndex;
            j++;
        }
    }

    public bool CheckIfCanStart()
    {
        return true;
    }

    void OnButtonClickedTherapists(int i)
    {
        _saveUsersData.therapistsIDIndex = i;
        GeneralDataManager.Instance.TherapistsID = _saveUsersData.therapists[i].id;
        _saveUsersData.therapistsID = _saveUsersData.therapists[i].id;
        SaveData();
    }
    void OnButtonClickedKid(int i)
    {
        _saveUsersData.kidIdIndex = i;
        GeneralDataManager.Instance.KidID = _saveUsersData.children[i].id;
        _saveUsersData.kidId = _saveUsersData.children[i].id;
        SaveData();
    }

    void SaveData()
    {
        string json = JsonUtility.ToJson(_saveUsersData);
        Debug.Log(json);
        
        PlayerPrefs.SetString("MainData", json);
    }

    void LoadData()
    {
        string json = PlayerPrefs.GetString("MainData", "NONE");
        if (json != "NONE")
        {
            _saveUsersData = JsonUtility.FromJson<SaveUsersData>(json);
            GeneralDataManager.Instance.KidID = _saveUsersData.kidId;
            GeneralDataManager.Instance.TherapistsID = _saveUsersData.therapistsID; 
                
            GenerateTherapistsList();
            GenerateChildrenList();
        }
    }

    [ContextMenu("Delete Save")]
    public void DeleteSave()
    {
        PlayerPrefs.DeleteKey("MainData");
        _saveUsersData = new SaveUsersData
        {
            therapists = new List<TherapistClass>(),
            children = new List<ChildClass>()
        };
        
        GenerateTherapistsList();
        GenerateChildrenList();
    }
}

[Serializable]
public class SaveUsersData
{
    public List<ChildClass> children;
    public List<TherapistClass> therapists;
    public string kidId;
    public string therapistsID; 
    public int kidIdIndex;
    public int therapistsIDIndex; 
}

[Serializable]
public class ChildClass
{
    public string id;
}

[Serializable]
public class TherapistClass
{
    public string id;
}