using System;
using System.Collections;
using UnityEngine;
using TheraplyVR.Pairs.Basic;

public class BothHandsTrainingCommunication : CommunicationAbstractClass
{
    public BasicSessionConfig data;
    public BasicSessionManager levelManager;

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(6f);
        OnSessionStarted("SceneLoaded:BothHandsTraining");
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
                        ParseBasicSessionConfig(structuredMessage.data);
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
                ParseBasicSessionConfig(_json);
            }
        }
        catch (Exception e)
        {
            print("Failed to parse as structured message, trying old format: " + e.Message);
            // If parsing as structured message fails, try old format
            ParseBasicSessionConfig(_json);
        }
    }

    private void ParseBasicSessionConfig(string jsonData)
    {
        StartCoroutine(ParseBasicSessionConfigRoutine(jsonData));
    }

    private IEnumerator ParseBasicSessionConfigRoutine(string jsonData)
    {
        BasicSessionConfig pd = null;
        try
        {
            pd = JsonUtility.FromJson<BasicSessionConfig>(jsonData);
            print("Parsed TwoHandsData: " + jsonData);
        }
        catch (Exception e)
        {
            print("Error parsing TwoHandsData: " + e.Message);
        }

        if (pd != null && pd.code != null && pd.code.Equals("two_hand_manipulation"))
        {
            data = pd;
            levelManager.activeConfig = pd;
            yield return null; // Wait a frame if needed
            levelManager.StartSession();

            // levelManager.StartTheGame();
            WebSocketClientV6.Instance.SendMessage("TheGameIsStarted");
        }
        else
        {
            print("Not a TwoHands session or invalid data");
            base.GetJsonFromPreviewApp(jsonData);
        }
        
        base.GetJsonFromPreviewApp(jsonData);
    }

    public void SetResult(BasicSessionResult result)
    {
        data.result = result;
        OnGameFinished("GameFinished:two_hand_manipulation");
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
        public BasicSessionConfig data;
    }

    [System.Serializable]
    private class StructuredMessage
    {
        public string type;
        public string data;
    }
}

