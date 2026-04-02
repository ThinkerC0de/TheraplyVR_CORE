using System;
using System.Collections;
using UnityEngine;

public class PiniataCommunication : CommunicationAbstractClass, ILegacyPauseHandler
{
    public PiniataData data;
    public PiniataLevelManager levelManager;
    public PiniataHeight piniataHeight;
    public SliderIndicator sweets;

    public PiniataGame piniataGame;
    
    private IEnumerator Start()
    {
        yield return new WaitForSeconds(6f);
        OnSessionStarted("SceneLoaded:Piniata");
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
                        ParsePiniataData(structuredMessage.data);
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
                // Fallback to old format - parse directly as PiniataData
                print("Parsing as old format");
                ParsePiniataData(_json);
            }
        }
        catch (Exception e)
        {
            print("Failed to parse as structured message, trying old format: " + e.Message);
            // If parsing as structured message fails, try old format
            ParsePiniataData(_json);
        }
    }
    
    private void ParsePiniataData(string jsonData)
    {
        try
        {
            PiniataData pd = JsonUtility.FromJson<PiniataData>(jsonData);
            print("Parsed PiniataData: " + jsonData);
            
            if (pd != null && pd.code != null && pd.code.Equals("Piniata"))
            {
                data = pd;
                levelManager.SetLevel(data.level);
                piniataHeight.SetPiniataHeight();
                sweets.SetSweet();
                int percent = 90 - data.difficultyLevel * 10;
                piniataGame.SetPassPercent(percent);
                
                levelManager.StartTheGame();
                WebSocketClientV6.Instance.SendMessage("TheGameIsStarted");
                
                print("Piniata session started with level: " + data.level + ", difficulty: " + data.difficultyLevel);
            }
            else
            {
                print("Not a Piniata session or invalid data");
                base.GetJsonFromPreviewApp(jsonData);
            }
        }
        catch (Exception e)
        {
            print("Error parsing PiniataData: " + e.Message);
        }
            base.GetJsonFromPreviewApp(jsonData);
    }

    public void SetPiniataData(double sessionSecondsTime, int wrongHits, float averageReactionTime, int maxPoints)
    {
        data.sessionSecondsTime = sessionSecondsTime;
        data.wrongHits = wrongHits;
        data.averageReactionTime = averageReactionTime;
        data.maxPoints = maxPoints;
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
        if (piniataGame == null)
        {
            return;
        }

        piniataGame.PauseGameplay();
    }

    public void ResumeLegacyGame()
    {
        if (piniataGame == null)
        {
            return;
        }

        piniataGame.ResumeGameplay();
    }
    
    [System.Serializable]
    private class ReturnData
    {
        public string sessionState;
        public PiniataData data;
    }
    
    [System.Serializable]
    private class StructuredMessage
    {
        public string type;
        public string data;
    }
}

[Serializable]
public class PiniataData
{
    public string name;
    public string code;
    public int level;
    public int difficultyLevel;
    public double sessionSecondsTime;
    public int wrongHits;
    public double averageReactionTime;
    public int maxPoints;
    public string locale;
}
