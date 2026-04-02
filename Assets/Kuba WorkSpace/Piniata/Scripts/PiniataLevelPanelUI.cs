using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PiniataLevelPanelUI : MonoBehaviour
{
    [SerializeField] private GameObject doneMark;
    
    
    public void SetDone()
    {
        doneMark.SetActive(true);
    }
}
