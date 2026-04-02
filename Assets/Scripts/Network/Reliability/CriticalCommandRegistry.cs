using System;
using System.Collections.Generic;

public class CriticalCommandRegistry
{
    private readonly HashSet<string> _criticalCommandIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public CriticalCommandRegistry()
    {
        RegisterDefaults();
    }

    public void ApplyOverrides(IEnumerable<string> extraCommandIds)
    {
        if (extraCommandIds == null)
        {
            return;
        }

        foreach (string commandId in extraCommandIds)
        {
            if (!string.IsNullOrWhiteSpace(commandId))
            {
                _criticalCommandIds.Add(commandId.Trim());
            }
        }
    }

    public bool IsCriticalCommandId(string commandId)
    {
        return !string.IsNullOrWhiteSpace(commandId) && _criticalCommandIds.Contains(commandId.Trim());
    }

    public string ResolveCommandId(string rawMessage)
    {
        string commandId = ReliableProtocolUtility.ResolveCommandId(rawMessage);
        if (string.Equals(commandId, "UNITY_MESSAGE", StringComparison.OrdinalIgnoreCase) &&
            ReliableProtocolUtility.TryGetJsonString(rawMessage, "command", out string nestedCommand))
        {
            return nestedCommand;
        }

        return commandId;
    }

    public bool IsCriticalRawMessage(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return false;
        }

        if (!ReliableProtocolUtility.IsJsonObject(rawMessage))
        {
            return IsCriticalCommandId(rawMessage.Trim());
        }

        string commandId = ResolveCommandId(rawMessage);
        if (IsCriticalCommandId(commandId))
        {
            return true;
        }

        if (ReliableProtocolUtility.TryGetJsonString(rawMessage, "sessionState", out string sessionState) &&
            !string.IsNullOrWhiteSpace(sessionState))
        {
            return true;
        }

        if (rawMessage.IndexOf("\"results\"", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return false;
    }

    private void RegisterDefaults()
    {
        string[] defaults =
        {
            "SESSION_ATTACH",
            "SESSION_RESUME",
            "START_SESSION",
            "PAUSE_SESSION",
            "RESUME_SESSION",
            "END_SESSION",
            "GAME_RESULTS",
            "SAVE_GAME_RESULTS",
            "SESSION_STATUS",
            "SESSION_STATE",
            "TheGameIsFinished",
            "TheGameIsStopped"
        };

        for (int i = 0; i < defaults.Length; i++)
        {
            _criticalCommandIds.Add(defaults[i]);
        }
    }
}
