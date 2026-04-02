using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CommandDispatcher : MonoBehaviour
{
    //public static CommandDispatcher Instance;
    
    public List<NetworkCommandListener> allCommands;

    [SerializeField] private Dictionary<string, NetworkCommand> lookup;

    private void Awake()
    {
        /*
        if (Instance == null) Instance = this;
        else
        {
            Destroy(this);
        }
        */
        lookup = new Dictionary<string, NetworkCommand>();
        foreach (var cmd in allCommands)
            if (!lookup.ContainsKey(cmd.commandSO.commandId))
                lookup.Add(cmd.commandSO.commandId, cmd.commandSO);
    }

    private void Start()
    {
        WebSocketClientV6.Instance.commandDispather = this;
        //WebSocketClientV7.Instance.commandDispather = this;
    }

    public void OnServerMessage(string raw)
    {
        Debug.Log("!!!!!!!!!!!!!!!!!!!!! ON SERVER MESSAGE: raw: " + raw);

        foreach (NetworkCommandListener cmd in allCommands)
        {
            if(raw.Contains(cmd.commandSO.commandId)){
                Debug.Log("----------------------> HAVE A MATCH!!!!!!!!!!!!!!!!!");
                cmd.HandleCommand(raw);
            }
        }

        if (lookup.TryGetValue(raw, out var fullCmd))
        {
            Debug.Log("!!!!!!!!!!!!!!!!!!!!! CommandDispatcher: " + fullCmd.commandId);
            Debug.Log(fullCmd.commandId);
            fullCmd.Raise(raw);
            return;
        }
    }

    public void LoadScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }
}
