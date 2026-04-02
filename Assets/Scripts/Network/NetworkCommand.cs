using UnityEngine;
using System;

[CreateAssetMenu(fileName = "New Network Command", menuName = "WebSocket/Network Command")]
public class NetworkCommand : ScriptableObject
{
    [Tooltip("The command ID to listen for (e.g., 'MOVE', 'JUMP', 'SESSION_DATA')")]
    public string commandId;

    [Tooltip("Description of what this command does")]
    [TextArea(3, 10)]
    public string description;

    // Event that passes the raw message to listeners
    public event Action<string> OnReceived;

    // Method to trigger the event with raw message
    public void Raise(string rawMessage)
    {
        OnReceived?.Invoke(rawMessage);
    }
}