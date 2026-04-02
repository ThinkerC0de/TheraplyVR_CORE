using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NetworkAdapterConfig", menuName = "Network/Network Adapter Config")]
public class NetworkAdapterConfig : ScriptableObject
{
    public CommunicationMode mode = CommunicationMode.Legacy;
    public bool enableReliableControl = true;
    public bool enableSessionResume = true;
    public bool enableCriticalAck = true;
    public bool enableCriticalJournal = true;
    public float reconnectRecoveryWindowSeconds = 30f;
    public float heartbeatIntervalSeconds = 5f;
    public float idleTimeoutSeconds = 10f;
    public float ackTimeoutSeconds = 2f;
    public int maxCriticalRetries = 3;
    public float retryBaseDelaySeconds = 1f;
    public float retryMaxDelaySeconds = 8f;
    public string journalFolderName = "network-adapter";
    public string journalFileName = "reliable-command-journal.ndjson";
    public string sessionContextFileName = "session-context.json";
    public bool logVerbose = false;
    public List<string> extraCriticalCommandIds = new List<string>();
}
