using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

[Serializable]
public class ReliableCommandEnvelope
{
    public string type = ReliableProtocolConstants.CommandMessageType;
    public string version = ReliableProtocolConstants.ProtocolVersion;
    public string messageId;
    public string commandId;
    public string sessionId;
    public string issuedAtUtc;
    public string expiresAtUtc;
    public string payloadJson;
    public bool requiresAck = true;
}

[Serializable]
public class ReliableCommandAck
{
    public string type = ReliableProtocolConstants.AckMessageType;
    public string version = ReliableProtocolConstants.ProtocolVersion;
    public string messageId;
    public string commandId;
    public string sessionId;
    public string status = ReliableProtocolConstants.AckStatusAccepted;
    public string reasonCode = ReliableProtocolConstants.ReasonNone;
    public string processedAtUtc;
}

[Serializable]
public class ReliableResumeRequest
{
    public string type = ReliableProtocolConstants.ResumeRequestMessageType;
    public string version = ReliableProtocolConstants.ProtocolVersion;
    public string sessionId;
    public string teacherId;
    public string studentId;
    public string deviceId;
    public string lastAckedMessageId;
    public string requestedAtUtc;
}

[Serializable]
public class ReliableResumeResult
{
    public string type = ReliableProtocolConstants.ResumeResultMessageType;
    public string version = ReliableProtocolConstants.ProtocolVersion;
    public bool accepted;
    public string sessionId;
    public string studentId;
    public string reasonCode = ReliableProtocolConstants.ReasonNone;
    public bool shouldReplayPending;
}

[Serializable]
public class ReliableHeartbeat
{
    public string type = ReliableProtocolConstants.HeartbeatMessageType;
    public string version = ReliableProtocolConstants.ProtocolVersion;
    public string sessionId;
    public string connectionState;
    public int queuedCriticalCount;
    public string sentAtUtc;
}

public static class ReliableProtocolConstants
{
    public const string ProtocolVersion = "1.0";
    public const string CommandMessageType = "RELIABLE_COMMAND";
    public const string AckMessageType = "RELIABLE_ACK";
    public const string ResumeRequestMessageType = "RELIABLE_RESUME_REQUEST";
    public const string ResumeResultMessageType = "RELIABLE_RESUME_RESULT";
    public const string HeartbeatMessageType = "RELIABLE_HEARTBEAT";
    public const string AckStatusAccepted = "ACK";
    public const string AckStatusRejected = "NACK";
    public const string ReasonNone = "NONE";
    public const string ReasonDuplicateMessage = "DUPLICATE_MESSAGE";
    public const string ReasonExecutionFailed = "EXECUTION_FAILED";
    public const string ReasonSessionExpired = "SESSION_EXPIRED";
    public const string ReasonTransportFailure = "TRANSPORT_FAILURE";
}

public static class ReliableProtocolUtility
{
    public static string CreateMessageId()
    {
        return Guid.NewGuid().ToString("N");
    }

    public static string ToUtcString(DateTime utcTime)
    {
        return utcTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }

    public static DateTime ParseUtcOrDefault(string utcValue, DateTime fallbackUtc)
    {
        if (DateTime.TryParse(
                utcValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out DateTime parsed))
        {
            return parsed.ToUniversalTime();
        }

        return fallbackUtc;
    }

    public static bool IsJsonObject(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return false;
        }

        string trimmed = rawMessage.Trim();
        return trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal);
    }

    public static bool TryDeserialize<T>(string json, out T result) where T : class
    {
        result = null;

        if (!IsJsonObject(json))
        {
            return false;
        }

        try
        {
            result = JsonUtility.FromJson<T>(json);
            return result != null;
        }
        catch
        {
            return false;
        }
    }

    public static string ResolveMessageType(string rawMessage)
    {
        if (!IsJsonObject(rawMessage))
        {
            return rawMessage ?? string.Empty;
        }

        if (TryGetJsonString(rawMessage, "type", out string messageType))
        {
            return messageType;
        }

        return string.Empty;
    }

    public static string ResolveCommandId(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return string.Empty;
        }

        if (!IsJsonObject(rawMessage))
        {
            return rawMessage.Trim();
        }

        if (TryGetJsonString(rawMessage, "commandId", out string commandId))
        {
            return commandId;
        }

        if (TryGetJsonString(rawMessage, "command", out string nestedCommand))
        {
            return nestedCommand;
        }

        if (TryGetJsonString(rawMessage, "type", out string messageType))
        {
            return messageType;
        }

        return string.Empty;
    }

    public static bool IsReliableProtocolMessage(string rawMessage)
    {
        string messageType = ResolveMessageType(rawMessage);
        return messageType == ReliableProtocolConstants.CommandMessageType ||
               messageType == ReliableProtocolConstants.AckMessageType ||
               messageType == ReliableProtocolConstants.ResumeRequestMessageType ||
               messageType == ReliableProtocolConstants.ResumeResultMessageType ||
               messageType == ReliableProtocolConstants.HeartbeatMessageType;
    }

    public static bool TryGetJsonString(string json, string key, out string value)
    {
        value = string.Empty;

        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
        {
            return false;
        }

        string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"";
        Match match = Regex.Match(json, pattern);
        if (!match.Success)
        {
            return false;
        }

        value = Regex.Unescape(match.Groups[1].Value);
        return true;
    }

    public static bool TryGetJsonBool(string json, string key, out bool value)
    {
        value = false;

        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
        {
            return false;
        }

        string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*(true|false)";
        Match match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        return bool.TryParse(match.Groups[1].Value, out value);
    }

    public static ReliableCommandAck BuildAck(
        ReliableCommandEnvelope envelope,
        string status,
        string reasonCode)
    {
        return new ReliableCommandAck
        {
            messageId = envelope != null ? envelope.messageId : string.Empty,
            commandId = envelope != null ? envelope.commandId : string.Empty,
            sessionId = envelope != null ? envelope.sessionId : string.Empty,
            status = string.IsNullOrEmpty(status) ? ReliableProtocolConstants.AckStatusAccepted : status,
            reasonCode = string.IsNullOrEmpty(reasonCode) ? ReliableProtocolConstants.ReasonNone : reasonCode,
            processedAtUtc = ToUtcString(DateTime.UtcNow)
        };
    }
}
