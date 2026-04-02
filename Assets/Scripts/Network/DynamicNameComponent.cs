using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

// Ten atrybut pozwala na zmianę nazwy komponentu w inspektorze
[System.Serializable]
public class DynamicNameComponent : MonoBehaviour
{
    [SerializeField] 
    private string componentDisplayName = "My Component";
    
    // Pole tekstowe widoczne w inspektorze
    [Space(10)]
    [Header("Component Settings")]
    public string textField = "Default Text";
    
    // Przykładowe dodatkowe właściwości
    public float value = 1.0f;
    public bool isActive = true;

    private void OnValidate()
    {
        // Aktualizuj nazwę komponentu gdy wartość się zmieni
        if (!string.IsNullOrEmpty(textField))
        {
            componentDisplayName = textField;
        }
    }
}