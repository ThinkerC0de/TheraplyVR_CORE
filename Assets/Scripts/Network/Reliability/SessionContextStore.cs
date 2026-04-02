using System;
using System.IO;
using UnityEngine;

public class SessionContextStore
{
    [Serializable]
    private class SessionContextSnapshot
    {
        public string teacherId;
        public string teacherIp;
        public string studentId;
        public string sessionId;
        public bool isAttached;
        public bool isRecovering;
        public string lastAckedMessageId;
        public string lastCriticalInboundMessageId;
        public string lastDisconnectReason;
        public string lastConnectionLostAtUtc;
        public string lastConnectedAtUtc;
        public string lastSessionDataRaw;
        public string lastSelectedSessionRaw;
    }

    private readonly string _filePath;
    private SessionContextSnapshot _snapshot = new SessionContextSnapshot();

    public SessionContextStore(string folderName, string fileName)
    {
        string safeFolder = string.IsNullOrWhiteSpace(folderName) ? "network-adapter" : folderName.Trim();
        string safeFileName = string.IsNullOrWhiteSpace(fileName) ? "session-context.json" : fileName.Trim();
        string root = Path.Combine(Application.persistentDataPath, safeFolder);
        _filePath = Path.Combine(root, safeFileName);
    }

    public string TeacherId => _snapshot.teacherId ?? string.Empty;
    public string TeacherIp => _snapshot.teacherIp ?? string.Empty;
    public string StudentId => _snapshot.studentId ?? string.Empty;
    public string SessionId => _snapshot.sessionId ?? string.Empty;
    public string LastAckedMessageId => _snapshot.lastAckedMessageId ?? string.Empty;
    public bool IsAttached => _snapshot.isAttached;
    public bool IsRecovering => _snapshot.isRecovering;
    public DateTime? LastConnectionLostAtUtc => ParseNullable(_snapshot.lastConnectionLostAtUtc);
    public DateTime? LastConnectedAtUtc => ParseNullable(_snapshot.lastConnectedAtUtc);
    public bool HasSessionContext => !string.IsNullOrWhiteSpace(_snapshot.sessionId);
    public bool HasBootstrapPayloads =>
        !string.IsNullOrWhiteSpace(_snapshot.lastSessionDataRaw) ||
        !string.IsNullOrWhiteSpace(_snapshot.lastSelectedSessionRaw);
    public string LastSessionDataRaw => _snapshot.lastSessionDataRaw ?? string.Empty;
    public string LastSelectedSessionRaw => _snapshot.lastSelectedSessionRaw ?? string.Empty;

    public void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            string json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            SessionContextSnapshot loaded = JsonUtility.FromJson<SessionContextSnapshot>(json);
            if (loaded != null)
            {
                _snapshot = loaded;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NetworkAdapter] Failed to load session context: {exception.Message}");
        }
    }

    public void Save()
    {
        try
        {
            string directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_filePath, JsonUtility.ToJson(_snapshot, true));
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NetworkAdapter] Failed to save session context: {exception.Message}");
        }
    }

    public void Clear()
    {
        _snapshot = new SessionContextSnapshot();
        Save();
    }

    public void BindTeacher(string teacherId, string teacherIp)
    {
        bool changed = false;

        if (!string.IsNullOrWhiteSpace(teacherId) && !string.Equals(_snapshot.teacherId, teacherId, StringComparison.Ordinal))
        {
            _snapshot.teacherId = teacherId;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(teacherIp) && !string.Equals(_snapshot.teacherIp, teacherIp, StringComparison.Ordinal))
        {
            _snapshot.teacherIp = teacherIp;
            changed = true;
        }

        if (changed)
        {
            Save();
        }
    }

    public void BindSession(string sessionId, string studentId)
    {
        bool changed = false;

        if (!string.IsNullOrWhiteSpace(sessionId) && !string.Equals(_snapshot.sessionId, sessionId, StringComparison.Ordinal))
        {
            _snapshot.sessionId = sessionId;
            _snapshot.isAttached = true;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(studentId) && !string.Equals(_snapshot.studentId, studentId, StringComparison.Ordinal))
        {
            _snapshot.studentId = studentId;
            changed = true;
        }

        if (changed)
        {
            Save();
        }
    }

    public void MarkConnected()
    {
        _snapshot.isRecovering = false;
        _snapshot.lastConnectedAtUtc = ReliableProtocolUtility.ToUtcString(DateTime.UtcNow);
        Save();
    }

    public void MarkDisconnected(string reason)
    {
        _snapshot.isRecovering = true;
        _snapshot.lastDisconnectReason = reason ?? string.Empty;
        _snapshot.lastConnectionLostAtUtc = ReliableProtocolUtility.ToUtcString(DateTime.UtcNow);
        Save();
    }

    public void MarkSessionEnded(string reason)
    {
        _snapshot.lastDisconnectReason = reason ?? string.Empty;
        _snapshot.sessionId = string.Empty;
        _snapshot.studentId = string.Empty;
        _snapshot.isAttached = false;
        _snapshot.isRecovering = false;
        _snapshot.lastSessionDataRaw = string.Empty;
        _snapshot.lastSelectedSessionRaw = string.Empty;
        Save();
    }

    public void UpdateLastAcked(string messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return;
        }

        _snapshot.lastAckedMessageId = messageId;
        Save();
    }

    public void UpdateLastCriticalInbound(string messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return;
        }

        _snapshot.lastCriticalInboundMessageId = messageId;
        Save();
    }

    public ReliableResumeRequest BuildResumeRequest(string deviceId)
    {
        return new ReliableResumeRequest
        {
            sessionId = SessionId,
            teacherId = TeacherId,
            studentId = StudentId,
            deviceId = deviceId ?? string.Empty,
            lastAckedMessageId = LastAckedMessageId,
            requestedAtUtc = ReliableProtocolUtility.ToUtcString(DateTime.UtcNow)
        };
    }

    public void CaptureFromRawMessage(string rawMessage)
    {
        if (!ReliableProtocolUtility.IsJsonObject(rawMessage))
        {
            return;
        }

        string commandId = ReliableProtocolUtility.ResolveCommandId(rawMessage);
        if (string.Equals(commandId, "END_SESSION", StringComparison.OrdinalIgnoreCase))
        {
            MarkSessionEnded("END_SESSION");
            return;
        }

        bool changed = false;

        if (ReliableProtocolUtility.TryGetJsonString(rawMessage, "type", out string messageType))
        {
            if (string.Equals(messageType, "SESSION_DATA", StringComparison.Ordinal) &&
                !string.Equals(_snapshot.lastSessionDataRaw, rawMessage, StringComparison.Ordinal))
            {
                _snapshot.lastSessionDataRaw = rawMessage;
                changed = true;
            }
            else if (string.Equals(messageType, "COMMAND", StringComparison.Ordinal) &&
                     ReliableProtocolUtility.TryGetJsonString(rawMessage, "name", out string commandName))
            {
                if (string.Equals(commandName, "selectedSession", StringComparison.Ordinal) &&
                    !string.Equals(_snapshot.lastSelectedSessionRaw, rawMessage, StringComparison.Ordinal))
                {
                    _snapshot.lastSelectedSessionRaw = rawMessage;
                    changed = true;
                }
                else if (string.Equals(commandName, "EndSessionCommand", StringComparison.Ordinal))
                {
                    MarkSessionEnded("EndSessionCommand");
                    return;
                }
            }
        }

        if (ReliableProtocolUtility.TryGetJsonString(rawMessage, "teacherId", out string teacherId) &&
            !string.IsNullOrWhiteSpace(teacherId) &&
            !string.Equals(_snapshot.teacherId, teacherId, StringComparison.Ordinal))
        {
            _snapshot.teacherId = teacherId;
            changed = true;
        }

        if ((ReliableProtocolUtility.TryGetJsonString(rawMessage, "teacherIP", out string teacherIp) ||
             ReliableProtocolUtility.TryGetJsonString(rawMessage, "teacherIp", out teacherIp)) &&
            !string.IsNullOrWhiteSpace(teacherIp) &&
            !string.Equals(_snapshot.teacherIp, teacherIp, StringComparison.Ordinal))
        {
            _snapshot.teacherIp = teacherIp;
            changed = true;
        }

        if (ReliableProtocolUtility.TryGetJsonString(rawMessage, "sessionId", out string sessionId) &&
            !string.IsNullOrWhiteSpace(sessionId) &&
            !string.Equals(_snapshot.sessionId, sessionId, StringComparison.Ordinal))
        {
            _snapshot.sessionId = sessionId;
            _snapshot.isAttached = true;
            changed = true;
        }

        if (ReliableProtocolUtility.TryGetJsonString(rawMessage, "studentId", out string studentId) &&
            !string.IsNullOrWhiteSpace(studentId) &&
            !string.Equals(_snapshot.studentId, studentId, StringComparison.Ordinal))
        {
            _snapshot.studentId = studentId;
            changed = true;
        }

        if (changed)
        {
            Save();
        }
    }

    private static DateTime? ParseNullable(string utcValue)
    {
        if (string.IsNullOrWhiteSpace(utcValue))
        {
            return null;
        }

        return ReliableProtocolUtility.ParseUtcOrDefault(utcValue, DateTime.UtcNow);
    }
}
