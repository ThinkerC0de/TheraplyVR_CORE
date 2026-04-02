using UnityEditor;
using UnityEngine;
using UnityEngine.Events;


public class NetworkCommandListener : MonoBehaviour
{
    [Tooltip("Wybrany ScriptableObject komendy (MOVE, JUMP itp.)")]
    public NetworkCommand commandSO;

    [Tooltip("Tu przeciągnij obiekty/ich metody, które mają reagować")]
    [System.Serializable]
    public class StringEvent : UnityEvent<string> { }

    public StringEvent OnCommand; 

    // private void OnEnable()
    // {
    //     if (commandSO != null)
    //         commandSO.OnReceived += HandleCommand;
    // }

    // private void OnDisable()
    // {
    //     if (commandSO != null)
    //         commandSO.OnReceived -= HandleCommand;
    // }

    public void HandleCommand(string payload)
    {
        OnCommand.Invoke(payload);
    }
    
    private void OnValidate()
    {
        // wymusza odświeżenie Inspektora po każdej zmianie
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}