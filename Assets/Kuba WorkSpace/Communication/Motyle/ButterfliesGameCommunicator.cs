using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class ButterfliesGameCommunicator : CommunicationAbstractClass, ILegacyPauseHandler
{
    public ButterFliesGameData data;

    public UnityEvent onStart;

    private ButterFliesGameData _pendingData;
    private bool _awaitingGameplayReady;
    
    private IEnumerator Start()
    {
        yield return new WaitForSeconds(6f);
        OnSessionStarted("SceneLoaded:Butterflies");
    }
    
    public override void GetJsonFromPreviewApp(string _json)
    {
        print("Received JSON: " + _json);
        
        try
        {
            // Try to parse as structured message first
            StructuredMessage structuredMessage = JsonUtility.FromJson<StructuredMessage>(_json);
            
            if (!string.IsNullOrEmpty(structuredMessage.type))
            {
                print("Structured message type: " + structuredMessage.type);
                
                switch (structuredMessage.type)
                {
                    case "SESSION_DATA":
                        // Extract the JSON string from data field and parse it
                        print("Session data received: " + structuredMessage.data);
                        ParseButterfliesData(structuredMessage.data);
                        break;
                    case "COMMAND":
                        // Handle command messages if needed
                        print("Command received: " + _json);
                        break;
                    default:
                        print("Unknown message type: " + structuredMessage.type);
                        break;
                }
            }
            else
            {
                // Fallback to old format - parse directly as ButterFliesGameData
                print("Parsing as old format");
                ParseButterfliesData(_json);
            }
        }
        catch (Exception e)
        {
            print("Failed to parse as structured message, trying old format: " + e.Message);
            // If parsing as structured message fails, try old format
            ParseButterfliesData(_json);
        }
    }
    
    private void ParseButterfliesData(string jsonData)
    {
        try
        {
            ButterFliesGameData bd = JsonUtility.FromJson<ButterFliesGameData>(jsonData);
            print("Parsed ButterFliesGameData: " + jsonData);

            if (bd != null && bd.code != null && bd.code.Equals("Butterflies"))
            {
                // Queue a new start while the scene is still finishing or rebuilding.
                // Flutter sends START_GAME only once, so we must not drop it.
                if (SceneManager_Butterflies.Instance != null &&
                    SceneManager_Butterflies.Instance.IsSceneTransitioning)
                {
                    print("[ButterfliesGameCommunicator] START_GAME queued — scene transition in progress.");
                    _pendingData = bd;
                    return;
                }

                ApplyGameData(bd);
            }
            else
            {
                print("Not a Butterflies session or invalid data");
                base.GetJsonFromPreviewApp(jsonData);
            }
        }
        catch (Exception e)
        {
            print("Error parsing ButterFliesGameData: " + e.Message);
            base.GetJsonFromPreviewApp(jsonData);
        }
    }

    /// Called by SceneManager_Butterflies at the END of the finish sequence.
    /// Applies any START_GAME that arrived while the scene was still transitioning.
    public void ApplyPendingDataIfAny()
    {
        if (_pendingData == null) return;
        var bd = _pendingData;
        _pendingData = null;
        print("[ButterfliesGameCommunicator] Applying queued START_GAME.");
        ApplyGameData(bd);
    }

    private void ApplyGameData(ButterFliesGameData bd)
    {
        data = bd;
        _awaitingGameplayReady = true;
        onStart?.Invoke();
        print("Butterflies session config applied with level: " + data.level + ", count: " + data.butterfliesCount + ", speed: " + data.butterfliesSpeed);
    }

    public void NotifyGameplayReady()
    {
        if (!_awaitingGameplayReady)
        {
            return;
        }

        _awaitingGameplayReady = false;
        WebSocketClientV6.Instance.SendMessage("TheGameIsStarted");
        print("Butterflies gameplay ready.");
    }

    public void SetData(double sessionSecondsTime, int wrongPoints)
    {
        data.sessionSecondsTime = sessionSecondsTime;
        data.wrongPoints = wrongPoints;
    }
    
    public override void OnGameFinished(string msg)
    {
        ReturnData returnData = new ReturnData
        {
            sessionState = msg,
            data = data
        };
        
        base.OnGameFinished(JsonUtility.ToJson(returnData));
    }

    public void PauseLegacyGame()
    {
        if (SceneManager_Butterflies.Instance == null)
        {
            return;
        }

        SceneManager_Butterflies.Instance.PauseGameplay();
    }

    public void ResumeLegacyGame()
    {
        if (SceneManager_Butterflies.Instance == null)
        {
            return;
        }

        SceneManager_Butterflies.Instance.ResumeGameplay();
    }
    
    [System.Serializable]
    private class ReturnData
    {
        public string sessionState;
        public ButterFliesGameData data;
    }
    
    [System.Serializable]
    private class StructuredMessage
    {
        public string type;
        public string data;
    }
}

[System.Serializable]
public class ButterFliesGameData
{
    public string name;
    public string code;
    public int level;
    public int butterfliesCount;
    public int butterfliesSpeed; // 0 1 2
    public double sessionSecondsTime;
    public int wrongPoints;
    public string locale;
}
