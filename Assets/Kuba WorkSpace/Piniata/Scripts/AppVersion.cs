using TMPro;
using UnityEngine;

public class AppVersion : MonoBehaviour
{
    private void Awake()
    {
        TextMeshProUGUI txt = GetComponent<TextMeshProUGUI>();
        txt.text = "V." + Application.version + ".";
    }
}
