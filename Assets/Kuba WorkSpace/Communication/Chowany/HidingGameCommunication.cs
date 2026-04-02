using System;
using System.Collections;
using UnityEngine;

public class HidingGameCommunication : CommunicationAbstractClass
{
    public HidingGameData data;
    public HideAndSeekGameController hideAndSeekGameController;

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(6f);
        OnSessionStarted("SceneLoaded:HidingGame");
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
                        ParseHidingGameData(structuredMessage.data);
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
                // Fallback to old format - parse directly as HidingGameData
                print("Parsing as old format");
                ParseHidingGameData(_json);
            }
        }
        catch (Exception e)
        {
            print("Failed to parse as structured message, trying old format: " + e.Message);
            // If parsing as structured message fails, try old format
            ParseHidingGameData(_json);
        }
    }
    
    private void ParseHidingGameData(string jsonData)
    {
        try
        {
            HidingGameData hidingData = JsonUtility.FromJson<HidingGameData>(jsonData);
            print("Parsed HidingGameData: " + jsonData);
            
            if (hidingData != null && hidingData.code != null && hidingData.code.Equals("Hiding"))
            {
                data = hidingData;
                
                print("Starting Hiding Game session with level: " + data.level);
                
                switch (data.level)
                {
                    case 0:
                        // Pre Test
                        print("Running Pre-test (level 0)");
                        hideAndSeekGameController.RunPretest();
                        break;
                    case 1:
                        // Trening
                        print("Running Training game (level 1)");
                        hideAndSeekGameController.RunGame();
                        hideAndSeekGameController.startBtnMain.SetActive(true);
                        hideAndSeekGameController.EnableButton();
                        hideAndSeekGameController.captionLevelInfo.SetActive(true);
                        hideAndSeekGameController.actualLevelInfo.SetActive(true);
                        break;
                    case 2:
                        // Post Test
                        print("Running Post-test (level 2)");
                        hideAndSeekGameController.RunPosttest();
                        break;
                    default:
                        print("Unknown hiding game level: " + data.level);
                        break;
                }
                
                WebSocketClientV6.Instance.SendMessage("TheGameIsStarted");
                
                print("Hiding Game session started successfully");
            }
            else
            {
                print("Not a Hiding Game session or invalid data");
                base.GetJsonFromPreviewApp(jsonData);
            }
        }
        catch (Exception e)
        {
            print("Error parsing HidingGameData: " + e.Message);
            base.GetJsonFromPreviewApp(jsonData);
        }
    }
    
    public void SetData(double sessionSecondsTime, int wrongAnswers)
    {
        data.sessionSecondsTime = sessionSecondsTime;
        data.wrongAnswers = wrongAnswers;
        
        print("Updated session data - Time: " + sessionSecondsTime + ", Wrong Answers: " + wrongAnswers);
    }
    
    public override void OnGameFinished(string msg)
    {
        ReturnData returnData = new ReturnData
        {
            sessionState = msg,
            data = data
        };

        base.OnGameFinished(JsonUtility.ToJson(returnData));
        
        print("Hiding Game session finished: " + msg);
    }
    
    [System.Serializable]
    private class ReturnData
    {
        public string sessionState;
        public HidingGameData data;
    }
    
    [System.Serializable]
    private class StructuredMessage
    {
        public string type;
        public string data;
    }
}

[System.Serializable]
public class HidingGameData
{
    public string name;
    public string code;
    public int level;
    public double sessionSecondsTime;
    public int wrongAnswers;
    public string locale;
}
