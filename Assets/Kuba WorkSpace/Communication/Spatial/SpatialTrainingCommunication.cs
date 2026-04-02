using System;
using System.Collections;
using TheraplyVR.Pairs.Basic;
using UnityEngine;
using static SpatialManager;

public class SpatialTrainingCommunication : CommunicationAbstractClass
{
    public SpatialData data;
    public SpatialManager levelManager;

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(6f);
        OnSessionStarted("SceneLoaded:SpatialTraining");
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
                        ParseSpatialData(structuredMessage.data);
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
                ParseSpatialData(_json);
            }
        }
        catch (Exception e)
        {
            print("Failed to parse as structured message, trying old format: " + e.Message);
            // If parsing as structured message fails, try old format
            ParseSpatialData(_json);
        }
    }
    
    private void ParseSpatialData(string jsonData)
    {
        StartCoroutine(ParseSpatialDataRoutine(jsonData));
    }
    
    
    private IEnumerator ParseSpatialDataRoutine(string jsonData)
    {
        SpatialData sd = null;
        try
        {
            sd = JsonUtility.FromJson<SpatialData>(jsonData);
            print("Parsed SpatialData: " + jsonData);
        }
        catch (Exception e)
        {
            print("Error parsing SpatialData: " + e.Message);
        }

        if (sd != null)
        {
            data = sd;
            levelManager.LoadStudentData(sd);
            yield return null; // Wait a frame if needed
            levelManager.StartSession();

            // levelManager.StartTheGame();
            WebSocketClientV6.Instance.SendMessage("TheGameIsStarted");
        }
        else
        {
            print("Not a Spatial session or invalid data");
            base.GetJsonFromPreviewApp(jsonData);
        }

        base.GetJsonFromPreviewApp(jsonData);
    }
    

    public void SetResult(SpatialData result)
    {
        data = result;
        OnGameFinished("GameFinished:Spatial");
    }

    public override void OnGameFinished(string msg)
    {
        ReturnData returnData = new ReturnData
        {
            sessionState = msg,
            data = data
            // data = null
        };

        base.OnGameFinished(JsonUtility.ToJson(returnData));
    }

    [System.Serializable]
    private class ReturnData
    {
        public string sessionState;
        public SpatialData data;
    }

    [System.Serializable]
    private class StructuredMessage
    {
        public string type;
        public string data;
    }
}

