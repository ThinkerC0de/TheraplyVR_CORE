using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class JournalRecord
{
    public string messageId;
    public string commandId;
    public string sessionId;
    public string status;
    public string reasonCode;
    public string recordedAtUtc;
}

public class ReliableCommandJournal : IDisposable
{
    public const string StatusReceived = "RECEIVED";
    public const string StatusApplied = "APPLIED";
    public const string StatusRejected = "REJECTED";
    public const string StatusFailed = "FAILED";
    public const string StatusDuplicate = "DUPLICATE";

    private readonly object _sync = new object();
    private readonly string _filePath;
    private readonly Dictionary<string, JournalRecord> _latestByMessageId = new Dictionary<string, JournalRecord>(StringComparer.OrdinalIgnoreCase);

    public ReliableCommandJournal(string folderName, string fileName)
    {
        string safeFolder = string.IsNullOrWhiteSpace(folderName) ? "network-adapter" : folderName.Trim();
        string safeFileName = string.IsNullOrWhiteSpace(fileName) ? "reliable-command-journal.ndjson" : fileName.Trim();
        string root = Path.Combine(Application.persistentDataPath, safeFolder);
        _filePath = Path.Combine(root, safeFileName);
        LoadExisting();
    }

    public bool TryGetLatest(string messageId, out JournalRecord record)
    {
        lock (_sync)
        {
            return _latestByMessageId.TryGetValue(messageId ?? string.Empty, out record);
        }
    }

    public void RecordReceived(string messageId, string commandId, string sessionId)
    {
        AppendRecord(messageId, commandId, sessionId, StatusReceived, ReliableProtocolConstants.ReasonNone);
    }

    public void RecordApplied(string messageId, string commandId, string sessionId)
    {
        AppendRecord(messageId, commandId, sessionId, StatusApplied, ReliableProtocolConstants.ReasonNone);
    }

    public void RecordRejected(string messageId, string commandId, string sessionId, string reasonCode)
    {
        AppendRecord(messageId, commandId, sessionId, StatusRejected, reasonCode);
    }

    public void RecordFailed(string messageId, string commandId, string sessionId, string reasonCode)
    {
        AppendRecord(messageId, commandId, sessionId, StatusFailed, reasonCode);
    }

    public void RecordDuplicate(string messageId, string commandId, string sessionId)
    {
        AppendRecord(messageId, commandId, sessionId, StatusDuplicate, ReliableProtocolConstants.ReasonDuplicateMessage);
    }

    public void Flush()
    {
    }

    public void Dispose()
    {
        Flush();
    }

    private void LoadExisting()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            string[] lines = File.ReadAllLines(_filePath);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JournalRecord record = JsonUtility.FromJson<JournalRecord>(line);
                if (record != null && !string.IsNullOrWhiteSpace(record.messageId))
                {
                    _latestByMessageId[record.messageId] = record;
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NetworkAdapter] Failed to load reliable journal: {exception.Message}");
        }
    }

    private void AppendRecord(string messageId, string commandId, string sessionId, string status, string reasonCode)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return;
        }

        JournalRecord record = new JournalRecord
        {
            messageId = messageId,
            commandId = commandId ?? string.Empty,
            sessionId = sessionId ?? string.Empty,
            status = status ?? string.Empty,
            reasonCode = reasonCode ?? ReliableProtocolConstants.ReasonNone,
            recordedAtUtc = ReliableProtocolUtility.ToUtcString(DateTime.UtcNow)
        };

        lock (_sync)
        {
            try
            {
                string directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(_filePath, JsonUtility.ToJson(record) + Environment.NewLine);
                _latestByMessageId[messageId] = record;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[NetworkAdapter] Failed to append reliable journal: {exception.Message}");
            }
        }
    }
}
