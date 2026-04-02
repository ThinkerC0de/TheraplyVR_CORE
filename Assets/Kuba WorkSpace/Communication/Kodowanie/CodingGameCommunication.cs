using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class CodingGameCommunication : CommunicationAbstractClass
{
    public CodingGameData data;
    public UnityEvent onLiczby;
    public UnityEvent onMorse;
    public UnityEvent onPiano;

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(6f);
        OnSessionStarted("SceneLoaded:Coding");
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
                        ParseCodingData(structuredMessage.data);
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
                // Fallback to old format - parse directly as CodingGameData
                print("Parsing as old format");
                ParseCodingData(_json);
            }
        }
        catch (Exception e)
        {
            print("Failed to parse as structured message, trying old format: " + e.Message);
            // If parsing as structured message fails, try old format
            ParseCodingData(_json);
        }
    }

    private void ParseCodingData(string jsonData)
    {
        try
        {
            CodingGameData cd = JsonUtility.FromJson<CodingGameData>(jsonData);
            print("Parsed CodingGameData: " + jsonData);

            if (cd != null && cd.code != null && cd.code.Equals("Coding"))
            {
                data = cd;

                switch (data.level)
                {
                    case 0:
                        // Gra liczbowa
                        print("Starting Numbers game (level 0)");
                        onLiczby?.Invoke();
                        break;
                    case 1:
                        // Morse
                        print("Starting Morse game (level 1)");
                        onMorse?.Invoke();
                        break;
                    case 2:
                        // Gra dźwiękowa
                        print("Starting Piano game (level 2)");
                        onPiano?.Invoke();
                        break;
                    default:
                        print("Unknown coding game level: " + data.level);
                        break;
                }

                WebSocketClientV6.Instance.SendMessage("TheGameIsStarted");

                print("Coding session started with level: " + data.level);
            }
            else
            {
                print("Not a Coding session or invalid data");
                base.GetJsonFromPreviewApp(jsonData);
            }
        }
        catch (Exception e)
        {
            print("Error parsing CodingGameData: " + e.Message);
            base.GetJsonFromPreviewApp(jsonData);
        }
    }

    public void SetData(double sessionSecondsTime, int currentLevel, int levelsProgress)
    {
        data.sessionSecondsTime = sessionSecondsTime;
        data.currentLevel = currentLevel;
        data.levelsProgress = levelsProgress;
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
        public CodingGameData data;
    }

    [System.Serializable]
    private class StructuredMessage
    {
        public string type;
        public string data;
    }
}

[System.Serializable]
public class CodingGameData
{
    public string name;
    public string code;
    public int level;
    public double sessionSecondsTime;
    public int currentLevel;
    public int levelsProgress;
    public string locale;
}