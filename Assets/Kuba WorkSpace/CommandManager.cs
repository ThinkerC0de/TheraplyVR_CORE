using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class CommandManager : MonoBehaviour
{
    [SerializeField] private List<CommandAction> commands;

    public void OnMessageReceived(string msg)
    {
        Debug.Log(msg);

        if (msg == "Welcome") Welcome();

        if (commands.Count == 0) return;
        foreach (CommandAction action in commands)
        {
            if (msg.Contains(action.command)) action.onCommand?.Invoke();

            //if (action.command.Contains("|Set")) action.onCommand?.Invoke(); else
            //if (action.command == msg) action.onCommand?.Invoke();
        }
    }

    public void Welcome()
    {
        // FMNetworkManager.instance.SendToServerReliable("Game:"+SceneManager.GetActiveScene().name);
    }
}



[Serializable]
public class CommandAction
{
    public string command;
    public UnityEvent onCommand;
}