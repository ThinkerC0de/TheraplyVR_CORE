# Simple Cube Game - Example

**A minimal example demonstrating the Theraply VR Framework API.**

⚠️ **This is NOT a production game** - it's a teaching tool to show you how to use the framework.

---

## 🎯 What This Example Demonstrates

### Framework Features Used

✅ **IGameModule Interface** - via BaseGame  
✅ **Network Commands** - ScriptableObject event system  
✅ **Configuration** - MessagePack serialization  
✅ **Data Collection** - Automatic batching & Firebase upload  
✅ **Dependency Injection** - VContainer services  
✅ **Lifecycle Management** - Start, pause, resume, end  
✅ **Result Reporting** - Metrics & completion status  

---

## 🎮 Gameplay

1. Game starts when therapist presses "Start" in controller app
2. A cube spawns at random position within spawn area
3. Player clicks cube with VR controller
4. Cube moves to new random position
5. Score increases by 1
6. Game ends when:
   - Target score reached (WIN)
   - Time limit expires (configurable)

---

## 📊 Data Collected

### Events Tracked

**cube_spawned:**
```json
{
  "position": {"x": 1.2, "y": 1.5, "z": -0.3},
  "scale": 0.8,
  "difficulty": 2
}
```

**cube_clicked:**
```json
{
  "score": 5,
  "reactionTime": 0.523,
  "totalClicks": 7,
  "cubeScale": 0.8,
  "cubePosition": {"x": 1.2, "y": 1.5, "z": -0.3}
}
```

### Final Results

```json
{
  "gameId": "example_cube_clicker",
  "score": 10,
  "completed": true,
  "metrics": {
    "totalClicks": 12,
    "avgReactionTime": 0.485,
    "accuracy": 0.833,
    "duration": 45.2,
    "difficulty": 2
  }
}
```

---

## 🏗️ File Structure

```
SimpleCubeGame/
├── Scripts/
│   └── SimpleCubeGame.cs          # Main game logic (200 lines)
├── Prefabs/
│   └── ClickableCube.prefab       # Cube with collider
├── Scenes/
│   └── SimpleCubeGame.unity       # Game scene
└── ScriptableObjects/
    ├── CMD_StartCubeGame.asset    # SESSION_START command
    ├── CMD_PauseCubeGame.asset    # GAME_PAUSE command
    ├── CMD_ResumeCubeGame.asset   # GAME_RESUME command
    └── CMD_UpdateCubeConfig.asset # CONFIG_UPDATE command
```

---

## 🔧 Configuration

```csharp
[MessagePackObject]
public class SimpleCubeConfig : GameConfig
{
    [Key(0)] public int targetScore = 10;      // Cubes to click
    [Key(1)] public float timeLimit = 0f;      // Seconds (0 = unlimited)
    [Key(2)] public float cubeSize = 1.0f;     // Size multiplier
}
```

### Difficulty System

Difficulty (1-5) affects cube size:
- **Level 1:** Scale = 1.0 (easy, large cubes)
- **Level 3:** Scale = 0.65 (medium)
- **Level 5:** Scale = 0.3 (hard, tiny cubes)

---

## 🚀 How to Use as Template

### Option 1: Copy & Modify

```bash
1. Copy SimpleCubeGame folder to _YourGames/
2. Rename to YourGame
3. Change GameId, DisplayName
4. Modify game logic in Update()
5. Keep data collection structure
```

### Option 2: Study & Rewrite

```bash
1. Read SimpleCubeGame.cs line by line
2. Note the patterns:
   - Command subscriptions in Awake()
   - Config deserialization
   - CollectDataPoint() calls
   - ReportResults() in EndGame()
3. Write your own from scratch
4. Use same patterns
```

---

## 📝 Key Code Patterns

### Pattern 1: Command Subscription

```csharp
[SerializeField] private NetworkCommand _onSessionStart;

void Awake()
{
    _onSessionStart.OnReceived += HandleSessionStart;
}

void HandleSessionStart(string payload)
{
    var config = MessagePackSerializer.Deserialize<SimpleCubeConfig>(
        Convert.FromBase64String(payload));
    
    Initialize(config);
    StartGame();
}
```

### Pattern 2: Data Collection

```csharp
void OnGameEvent()
{
    CollectDataPoint("event_type", new Dictionary<string, object>
    {
        { "metric1", value1 },
        { "metric2", value2 }
    });
}
```

### Pattern 3: Lifecycle Overrides

```csharp
public override void StartGame()
{
    base.StartGame(); // IMPORTANT: Call base first!
    
    // Your initialization here
}

public override void EndGame()
{
    base.EndGame(); // IMPORTANT: Call base first!
    
    // Report results
    ReportResults(score, completed, metrics);
}
```

---

## 🧪 Testing

### In Editor (No Controller)

```csharp
// Add to SimpleCubeGame.cs:

#if UNITY_EDITOR
[ContextMenu("Test Start Game")]
void TestStartGame()
{
    var config = new SimpleCubeConfig { targetScore = 5 };
    Initialize(config);
    StartGame();
}
#endif
```

Then: Right-click component → "Test Start Game"

### With Controller App

```
1. Build Quest app
2. Install on Quest 3
3. Start Flutter controller
4. Scan for device
5. Connect
6. Select "Cube Clicker (Example)"
7. Configure: Target Score = 5
8. Press Start
```

---

## 📊 Performance

- **90 FPS maintained** ✅ (simple geometry)
- **Data points:** ~1 per second (low frequency)
- **Memory:** <10 MB (minimal assets)
- **Network:** <1 KB per message

---

## ❓ Common Questions

### Q: Why so simple?

**A:** This is a TEACHING tool. Production games are more complex. This shows the minimum viable implementation of the framework API.

### Q: Can I ship this?

**A:** No. This is an example. Build your own game using these patterns.

### Q: Where's the scoring UI?

**A:** Not included. This example focuses on framework integration, not game polish.

### Q: How do I add more mechanics?

**A:** Study the patterns, then add your own logic in Update(), while keeping the same:
- Command handlers
- Data collection
- Result reporting

---

## 🎓 Next Steps

1. **Understand this example completely**
2. **Read:** [Creating Games Guide](../../../docs/02-Creating-Games.md)
3. **Build your own game** in `_YourGames/`
4. **Use same patterns** shown here

---

## 💡 Pro Tips

✅ **DO:**
- Call `base.StartGame()` first in overrides
- Unsubscribe from events in OnDestroy()
- Use CollectDataPoint() frequently
- Test with Context Menu buttons first

❌ **DON'T:**
- Skip calling base methods
- Access services in Awake() (they're null!)
- Collect PII (personally identifiable information)
- Force flush data buffer on every point

---

**This example took ~2 hours to build. Yours will too (after you understand the framework).** 🚀
