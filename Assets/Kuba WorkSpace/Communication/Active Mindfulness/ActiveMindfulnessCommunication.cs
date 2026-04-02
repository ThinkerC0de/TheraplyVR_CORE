using System;
using System.Collections;
using UnityEngine;

public class ActiveMindfulnessCommunication : CommunicationAbstractClass
{
    public AmData data;
    public PhasesController phasesController;

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(6f);
        OnSessionStarted("SceneLoaded:ActiveMindfulness");
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
                        ParseActiveMindfulnessData(structuredMessage.data);
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
                // Fallback to old format - parse directly as AmData
                print("Parsing as old format");
                ParseActiveMindfulnessData(_json);
            }
        }
        catch (Exception e)
        {
            print("Failed to parse as structured message, trying old format: " + e.Message);
            // If parsing as structured message fails, try old format
            ParseActiveMindfulnessData(_json);
        }
    }
    
    private void ParseActiveMindfulnessData(string jsonData)
    {
        try
        {
            AmData amData = JsonUtility.FromJson<AmData>(jsonData);
            print("Parsed AmData: " + jsonData);
            
            if (amData != null && amData.code != null && amData.code.Equals("AM"))
            {
                data = amData;
                
                print("Starting Active Mindfulness session with level: " + data.level);
                
                // Start the first phase
                phasesController.StartPhase(0);
                
                WebSocketClientV6.Instance.SendMessage("TheGameIsStarted");
                
                print("Active Mindfulness session started successfully");
            }
            else
            {
                print("Not an Active Mindfulness session or invalid data");
                base.GetJsonFromPreviewApp(jsonData);
            }
        }
        catch (Exception e)
        {
            print("Error parsing AmData: " + e.Message);
            base.GetJsonFromPreviewApp(jsonData);
        }
    }
    
    public void SetData(double sessionSecondsTime, int currentLevel, int levelsProgress)
    {
        data.sessionSecondsTime = sessionSecondsTime;
        data.currentLevel = currentLevel;
        data.levelsProgress = levelsProgress;
        
        print("Updated session data - Time: " + sessionSecondsTime + ", Current Level: " + currentLevel + ", Progress: " + levelsProgress);
    }
    
    public override void OnGameFinished(string msg)
    {
        ReturnData returnData = new ReturnData
        {
            sessionState = msg,
            data = data
        };

        base.OnGameFinished(JsonUtility.ToJson(returnData));
        
        print("Active Mindfulness session finished: " + msg);
    }
    
    [System.Serializable]
    private class ReturnData
    {
        public string sessionState;
        public AmData data;
    }
    
    [System.Serializable]
    private class StructuredMessage
    {
        public string type;
        public string data;
    }
}

[System.Serializable]
public class AmData
{
    public string name;
    public string code;
    public int level;
    public double sessionSecondsTime;
    public int currentLevel;
    public int levelsProgress;
    public string locale;
}