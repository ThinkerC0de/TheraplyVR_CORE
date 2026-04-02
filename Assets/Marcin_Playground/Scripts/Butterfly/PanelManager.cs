using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PanelManager : MonoBehaviour
{
    //public static PanelManager Instance;
    public Scrollbar catchedButerflies;
    public Scrollbar catchedLadybugs;
    public Scrollbar catchedBubbles;

    public GameObject updateBtn;
    public GameObject startBtn;
    public GameObject backBtn;
    public GameObject nextBtn;
    
    public TMP_Dropdown speed;
    public Slider levelSlider;
    public TMP_Text levelIndicator;
    public Slider butterflySlider;
    public TMP_Text butterflyIndicator;
    public TMP_Text desription;
    public bool isTherapist = true;
    
    [System.Serializable]
    public enum butterflySpeed
    {
        slow,
        medium,
        fast
    }

    public butterflySpeed speedPreset;
    
    private void Awake()
    {
        /*
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
        }
        */
        UpdateButterflyNr();
        UpdateLevelNr();
        PopulateDropDownWithEnum(speed, speedPreset);
        if (levelSlider == null) return;
        levelSlider.onValueChanged.AddListener(ChangeDescription);
    }

    private void OnEnable()
    {
        gameObject.SetActive(false);
    }

    private void ChangeDescription(float value)
    {
        if (isTherapist == false) return;
        SceneManager_Butterflies.Instance.levelIndex = (int)value;
        
        switch ((int)levelSlider.value)
        {
            case 1:
            {
                desription.text = "Poziom 1 - pusta przestrzeń, wielokolorowe motyle, jedna różdżka.";
                break;
            }
            case 2:
            {
                desription.text = "Poziom 2 - pusta przestrzeń, dwa kolory motyli, dwie różdżki.";
                break;
            }
            case 3:
            {
                desription.text = "Poziom 3 - pusta przestrzeń, motyle, biedronki, dwie różdżki.";
                break;
            }
            case 4:
            {
                desription.text = "Poziom 4 - pusta przestrzeń, motyle, bańki mydlane, dwie różdżki.";
                break;
            }
            case 5:
            {
                desription.text = "Poziom 5 - przestrzeń z drzewem, wielokolorowe motyle, jedna różdżka.";
                break;
            }
            case 6:
            {
                desription.text = "Poziom 6 - przestrzeń z drzewem, dwa kolory motyli, dwie różdżki.";
                break;
            }
            case 7:
            {
                desription.text = "Poziom 7 - przestrzeń z drzewem, motyle, biedronki, dwie różdżki.";
                break;
            }
            case 8:
            {
                desription.text = "Poziom 8 - przestrzeń z drzewem, motyle, bańki mydlane, dwie różdżki.";
                break;
            }
            case 9:
            {
                desription.text = "Poziom 9 - przestrzeń z balonami, wielokolorowe motyle, jedna różdżka.";
                break;
            }
            case 10:
            {
                desription.text = "Poziom 10 - przestrzeń z balonami, dwa kolory motyli, dwie różdżki.";
                break;
            }
            case 11:
            {
                desription.text = "Poziom 11 - przestrzeń z balonami, motyle, biedronki, dwie różdżki";
                break;
            }
            case 12:
            {
                desription.text = "Poziom 12 - przestrzeń z balonami, motyle, bańki mydlane, dwie różdżki.";
                break;
            }
            }
    }

    public string GetDescription()
    {
        switch (SceneManager_Butterflies.Instance.levelIndex)
        {
            case 1:
            {
                return "Poziom 1 - pusta przestrzeń, wielokolorowe motyle, jedna różdżka.";
            }
            case 2:
            {
                return "Poziom 2 - pusta przestrzeń, dwa kolory motyli, dwie różdżki.";
            }
            case 3:
            {
                return "Poziom 3 - pusta przestrzeń, motyle, biedronki, dwie różdżki.";
            }
            case 4:
            {
                return "Poziom 4 - pusta przestrzeń, motyle, bańki mydlane, dwie różdżki.";
            }
            case 5:
            {
                return "Poziom 5 - przestrzeń z drzewem, wielokolorowe motyle, jedna różdżka.";
            }
            case 6:
            {
                return "Poziom 6 - przestrzeń z drzewem, dwa kolory motyli, dwie różdżki.";
            }
            case 7:
            {
                return "Poziom 7 - przestrzeń z drzewem, motyle, biedronki, dwie różdżki.";
            }
            case 8:
            {
                return "Poziom 8 - przestrzeń z drzewem, motyle, bańki mydlane, dwie różdżki.";
            }
            case 9:
            {
                return "Poziom 9 - przestrzeń z balonami, wielokolorowe motyle, jedna różdżka.";
            }
            case 10:
            {
                return "Poziom 10 - przestrzeń z balonami, dwa kolory motyli, dwie różdżki.";
            }
            case 11:
            {
                return "Poziom 11 - przestrzeń z balonami, motyle, biedronki, dwie różdżki";
            }
            case 12:
            {
                return "Poziom 12 - przestrzeń z balonami, motyle, bańki mydlane, dwie różdżki.";
            }
            default:
            {
                return "You should't be here";
            }
        }
    }

    public void UpdateLevelNr()
    {
        if (isTherapist == false) return;
        
        levelIndicator.text = levelSlider.value.ToString();
    }

    public void UpdateButterflyNr()
    {
        if (isTherapist == false) return;
        
        butterflyIndicator.text = butterflySlider.value.ToString();
    }
    
    public static void PopulateDropDownWithEnum(TMP_Dropdown dropdown, butterflySpeed targetEnum) 
    {
        Type enumType = targetEnum.GetType();//Type of enum(FormatPresetType in my example)
        List<TMP_Dropdown.OptionData> newOptions = new List<TMP_Dropdown.OptionData>();
 
        for(int i = 0; i < Enum.GetNames(enumType).Length; i++)//Populate new Options
        {
            newOptions.Add(new TMP_Dropdown.OptionData(Enum.GetName(enumType, i)));
        }

        if (dropdown == null) return;
        dropdown.ClearOptions();//Clear old options
        dropdown.AddOptions(newOptions);//Add new options
    }

    public bool IsParentActive()
    {
        return gameObject.activeSelf;
    }
}
