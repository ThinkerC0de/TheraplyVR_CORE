using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PassiveMindfulnessCommunication : CommunicationAbstractClass
{
    public PmData data;
    public PassiveMindfulnessController pmController;
    public DaytimeSceneLoader daytimeSceneLoader;
    
    private IEnumerator Start()
    {
        yield return new WaitForSeconds(6f);
        OnSessionStarted("SceneLoaded:PassiveMindfulness");
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
                        ParsePassiveMindfulnessData(structuredMessage.data);
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
                // Fallback to old format - parse directly as PmData
                print("Parsing as old format");
                ParsePassiveMindfulnessData(_json);
            }
        }
        catch (Exception e)
        {
            print("Failed to parse as structured message, trying old format: " + e.Message);
            // If parsing as structured message fails, try old format
            ParsePassiveMindfulnessData(_json);
        }
    }
    
    private void ParsePassiveMindfulnessData(string jsonData)
    {
        try
        {
            PmData pmData = JsonUtility.FromJson<PmData>(jsonData);
            print("Parsed PmData: " + jsonData);
            
            if (pmData != null && pmData.code != null && pmData.code.Equals("PM"))
            {
                data = pmData;
                
                print("Starting Passive Mindfulness session with level: " + data.level);
                
                // Choose session based on level
                pmController.ChooseSession(data.level);
                
                // Start the game sequence
                // StartCoroutine(StartGame());
                
                WebSocketClientV6.Instance.SendMessage("TheGameIsStarted");
                
                print("Passive Mindfulness session started successfully");
            }
            else
            {
                print("Not a Passive Mindfulness session or invalid data");
                base.GetJsonFromPreviewApp(jsonData);
            }
        }
        catch (Exception e)
        {
            print("Error parsing PmData: " + e.Message);
            base.GetJsonFromPreviewApp(jsonData);
        }
    }

    IEnumerator StartGame()
    {
        print("Starting game sequence for level: " + data.level);
        
        if (data.level <= 5)
        {
            print("Loading day scene for level: " + data.level);
            yield return StartCoroutine(daytimeSceneLoader.LoadDaySceneEnum());
        }
        else
        {
            print("Loading night scene for level: " + data.level);
            yield return StartCoroutine(daytimeSceneLoader.LoadNightSceneEnum());
        }
        
        print("Starting timeline");
        pmController.StartTheTimeline();
    }
    
    public void OnSceneLoaded()
    {
        print("Scene loaded - sending notification to mobile app");
        WebSocketClientV6.Instance.SendMessage("SceneLoaded:PassiveMindfulness");
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
    
    [System.Serializable]
    private class ReturnData
    {
        public string sessionState;
        public PmData data;
    }
    
    [System.Serializable]
    private class StructuredMessage
    {
        public string type;
        public string data;
    }
}

[System.Serializable]
public class PmData
{
    public string name;
    public string code;
    public int level;
    public string locale;
}