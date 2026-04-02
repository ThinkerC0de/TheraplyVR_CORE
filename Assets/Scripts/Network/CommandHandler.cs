using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(fileName = "New Command Handler", menuName = "WebSocket/Command Handler")]
public class CommandHandler : ScriptableObject
{
    [Tooltip("The command string to listen for")]
    public string command;

    [Tooltip("Action to execute when this command is received")]
    public UnityEvent onCommandReceived;

    // Optional description for editor clarity
    [TextArea(3, 10)]
    public string description;

    // Execute the action
    public void ExecuteAction()
    {
        if (onCommandReceived != null)
        {
            onCommandReceived.Invoke();
        }
    }
}