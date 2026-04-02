using System;
using UnityEngine;

public abstract class CommunicationAbstractClass : MonoBehaviour
{
    public BackToMenu backToMenu;

    /// <summary>
    /// Registered by LegacyGameModuleBase adapter.
    /// Called in addition to WebSocketClientV6.SendMessage until FAZA 5.X.
    /// </summary>
    public Action<string> OnGameFinishedCallback;

    public virtual void GetJsonFromPreviewApp(string _json)
    {
        Debug.Log("--------------------------------");
        Debug.Log(_json);
        Debug.Log("--------------------------------");
        if (_json.Contains("ResetPositionCommand"))
        {
            Debug.Log("RESETUJĘ POZYCJĘ!");
            OVRManager.display.RecenterPose();
        }
        else if (_json.Contains("EndSessionCommand"))
        {
            // backToMenu.GoBack();
        }
    }

    protected virtual void OnSessionStarted(string msg)
    {
        WebSocketClientV6.Instance.SendMessage(msg);
    }
    public virtual void OnGameFinished(string msg)
    {
        OnGameFinishedCallback?.Invoke(msg);
        WebSocketClientV6.Instance.SendMessage(msg);
    }
}