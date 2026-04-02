using UnityEngine;

/// <summary>
/// Example script showing how to send Unity-style messages to the Flutter app
/// This demonstrates how to use the new messaging API with your existing onUnityMessage logic
/// </summary>
public class UnityMessageExample : MonoBehaviour
{
    [Header("Example Usage")]
    [SerializeField] private bool _sendExampleMessages = false;
    
    void Start()
    {
        if (_sendExampleMessages)
        {
            // Example of sending messages after a delay
            Invoke(nameof(SendSceneLoadedMessage), 2f);
            Invoke(nameof(SendGameStartedMessage), 5f);
            Invoke(nameof(SendGameFinishedMessage), 10f);
        }
    }
    
    // Example: Send scene loaded message (Unity widget style)
    public void SendSceneLoadedMessage()
    {
        if (WebSocketClientV6.Instance != null && WebSocketClientV6.Instance.IsReadyToSend())
        {
            // Simple string message (old Unity widget style)
            WebSocketClientV6.Instance.SendMessage("SceneLoaded");
            
            // OR structured message (new style)
            WebSocketClientV6.Instance.SendCommand("UNITY_MESSAGE", new { 
                command = "SceneLoaded",
                sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
            });
        }
    }
    
    // Example: Send game started message
    public void SendGameStartedMessage()
    {
        if (WebSocketClientV6.Instance != null && WebSocketClientV6.Instance.IsReadyToSend())
        {
            // Simple string message
            WebSocketClientV6.Instance.SendMessage("TheGameIsStarted");
            
            // OR with additional data
            WebSocketClientV6.Instance.SendCommand("UNITY_MESSAGE", new {
                command = "TheGameIsStarted",
                level = 1,
                difficulty = "Easy"
            });
        }
    }
    
    // Example: Send game finished with results
    public void SendGameFinishedMessage()
    {
        if (WebSocketClientV6.Instance != null && WebSocketClientV6.Instance.IsReadyToSend())
        {
            // Create game results (matching your existing format)
            var gameResults = new {
                sessionState = "GameFinished",
                data = new {
                    score = 1250,
                    level = 3,
                    timeSpent = 180,
                    achievements = new[] { "first_win", "speed_demon" },
                    playerStats = new {
                        accuracy = 85.5f,
                        totalMoves = 42
                    }
                }
            };
            
            // Send as JSON (will be handled by onUnityMessage)
            WebSocketClientV6.Instance.SendJsonMessage(gameResults);
        }
    }
    
    // Example: Send session status updates
    public void SendSessionLoading()
    {
        WebSocketClientV6.Instance?.SendMessage("SessionIsLoading");
    }
    
    public void SendConnectionLost()
    {
        WebSocketClientV6.Instance?.SendMessage("ConnectionLost");
    }
    
    public void SendConnected()
    {
        WebSocketClientV6.Instance?.SendMessage("Connected");
    }
    
    // Example: Send custom session data
    public void SendCustomSessionData(string dataType, object data)
    {
        if (WebSocketClientV6.Instance != null && WebSocketClientV6.Instance.IsReadyToSend())
        {
            var message = new {
                type = "UNITY_MESSAGE",
                command = dataType,
                timestamp = System.DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                data = data
            };
            
            WebSocketClientV6.Instance.SendJsonMessage(message);
        }
    }
    
    // Example: Button methods for testing in Inspector
    [ContextMenu("Test Scene Loaded")]
    public void TestSceneLoaded() => SendSceneLoadedMessage();
    
    [ContextMenu("Test Game Started")]
    public void TestGameStarted() => SendGameStartedMessage();
    
    [ContextMenu("Test Game Finished")]
    public void TestGameFinished() => SendGameFinishedMessage();
} 