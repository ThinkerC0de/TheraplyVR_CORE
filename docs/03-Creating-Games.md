# Creating Games with Theraply VR Framework

**A complete guide to building therapeutic mini-games using the Theraply VR Framework.**

---

## 🎯 Overview

This framework provides all the infrastructure you need:
- ✅ Network communication (handled)
- ✅ Session management (handled)
- ✅ Data collection (handled)
- ✅ Streaming (handled)

**You only focus on:**
- 🎮 Game mechanics
- 🎨 Visual design
- 📊 What data to collect

---

## 🏗️ Architecture

### The Game Module Pattern

```
┌─────────────────────────────────────────┐
│ YOUR GAME                               │
│ └── Implements IGameModule              │
│     └── Extends BaseGame (optional)     │
└─────────────────────────────────────────┘
              ↕ (uses)
┌─────────────────────────────────────────┐
│ FRAMEWORK SERVICES                      │
│ ├── Network (TCP/UDP)                   │
│ ├── Session Management                  │
│ ├── Data Collection                     │
│ └── Firebase Integration                │
└─────────────────────────────────────────┘
```

**Key Principle:** Your game is a **plugin**. The framework handles all the plumbing.

---

## 🚀 Quick Start: Your First Game

### Step 1: Create Game Script

Create `Assets/_YourGames/MyFirstGame/MyFirstGame.cs`:

```csharp
using UnityEngine;
using TheraplyCore.Games;
using VContainer;
using MessagePack;
using System;

public class MyFirstGame : BaseGame
{
    // ============================================
    // REQUIRED: Game Metadata
    // ============================================
    
    public override string GameId => "my_first_game";
    public override string DisplayName => "My First Game";
    public override string Description => "A simple tutorial game";
    
    // ============================================
    // CONFIGURATION: Network Commands
    // ============================================
    
    [SerializeField] private NetworkCommand _onSessionStart;
    [SerializeField] private NetworkCommand _onGamePause;
    
    // ============================================
    // GAME STATE
    // ============================================
    
    private int _score = 0;
    private float _startTime;
    
    // ============================================
    // UNITY LIFECYCLE
    // ============================================
    
    void Awake()
    {
        // Subscribe to network commands
        _onSessionStart.OnReceived += HandleSessionStart;
        _onGamePause.OnReceived += HandleGamePause;
    }
    
    void OnDestroy()
    {
        // Unsubscribe
        _onSessionStart.OnReceived -= HandleSessionStart;
        _onGamePause.OnReceived -= HandleGamePause;
    }
    
    // ============================================
    // COMMAND HANDLERS
    // ============================================
    
    void HandleSessionStart(string payload)
    {
        // Deserialize config
        MyGameConfig config = MessagePackSerializer.Deserialize<MyGameConfig>(
            Convert.FromBase64String(payload));
        
        // Initialize and start
        Initialize(config);
        StartGame();
    }
    
    void HandleGamePause(string payload)
    {
        PauseGame();
    }
    
    // ============================================
    // GAME LIFECYCLE (Override BaseGame methods)
    // ============================================
    
    public override void StartGame()
    {
        base.StartGame(); // IMPORTANT: Call base first
        
        _score = 0;
        _startTime = Time.time;
        
        Debug.Log("Game started!");
    }
    
    public override void EndGame()
    {
        base.EndGame(); // IMPORTANT: Call base first
        
        // Report results
        ReportResults(_score, true, new Dictionary<string, object>
        {
            { "finalScore", _score },
            { "duration", Time.time - _startTime }
        });
        
        Debug.Log($"Game ended! Score: {_score}");
    }
    
    // ============================================
    // GAME LOGIC
    // ============================================
    
    void Update()
    {
        if (CurrentState != GameState.Playing) return;
        
        // Your game update logic here
    }
    
    public void OnPlayerAction()
    {
        _score += 10;
        
        // ✅ Collect data point (automatically batched & uploaded)
        CollectDataPoint("player_action", new Dictionary<string, object>
        {
            { "score", _score },
            { "timeElapsed", Time.time - _startTime },
            { "timestamp", DateTime.UtcNow }
        });
    }
}

// ============================================
// GAME CONFIGURATION
// ============================================

[MessagePackObject]
public class MyGameConfig : GameConfig
{
    [Key(0)] public int targetScore = 100;
    [Key(1)] public float difficulty = 1.0f;
}
```

### Step 2: Create ScriptableObject Commands

In Unity Editor:

```
1. Right-click in Project → Create → Theraply → Network Command
2. Name: "CMD_StartMyFirstGame"
3. Set commandId: "SESSION_START"
4. Description: "Starts the game session"

Repeat for other commands:
- CMD_PauseMyFirstGame (commandId: "GAME_PAUSE")
- CMD_ResumeMyFirstGame (commandId: "GAME_RESUME")
```

### Step 3: Create Scene

```
1. Create new scene: MyFirstGame.unity
2. Add empty GameObject: "MyFirstGameManager"
3. Add MyFirstGame.cs component
4. Drag ScriptableObjects to Inspector:
   - _onSessionStart → CMD_StartMyFirstGame
   - _onGamePause → CMD_PauseMyFirstGame
```

### Step 4: Test

```
1. Enter Play Mode
2. Manually trigger command (for testing):
   - Select CMD_StartMyFirstGame in Project
   - Right-click → "Test Raise (Sample Payload)"
3. Your game should start!
```

---

## 📚 Core Concepts

### 1. IGameModule Interface

**What:** Contract that all games must implement.

**Why:** Framework knows how to communicate with your game.

**Methods:**
```csharp
void Initialize(GameConfig config);  // Parse settings
void StartGame();                    // Begin gameplay
void PauseGame();                    // Freeze game
void ResumeGame();                   // Unpause
void RestartGame();                  // Reset to start
void EndGame();                      // Clean up & report
```

**Properties:**
```csharp
string GameId { get; }         // Unique ID
string DisplayName { get; }    // UI display name
GameState CurrentState { get; } // Current state
```

**Events:**
```csharp
event Action<GameDataPoint> OnDataPoint;     // Data collected
event Action<GameResult> OnGameComplete;     // Game finished
```

---

### 2. BaseGame Helper Class

**What:** Abstract class with common functionality.

**Benefits:**
- ✅ Automatic data buffering & upload
- ✅ Service injection (VContainer)
- ✅ State management
- ✅ Lifecycle helpers

**Usage:**
```csharp
public class MyGame : BaseGame // Inherit from BaseGame
{
    // Access injected services
    void SomeMethod()
    {
        SessionService.DoSomething();
        ConnectionService.SendCommand("TEST");
    }
    
    // Collect data (automatic batching)
    void OnEvent()
    {
        CollectDataPoint("event_type", data);
    }
    
    // Report final results
    public override void EndGame()
    {
        base.EndGame();
        ReportResults(score, completed, metrics);
    }
}
```

---

### 3. NetworkCommand ScriptableObjects

**What:** Designer-friendly event system.

**Why:** Decouple network layer from game logic.

**Flow:**
```
Network Message Arrives
    ↓
CommandDispatcher finds matching NetworkCommand
    ↓
NetworkCommand.Raise(payload) fires event
    ↓
Your game's handler receives payload
    ↓
Deserialize & process
```

**Best Practices:**
- One command per action (e.g., START, PAUSE, RESUME)
- Use descriptive names (e.g., CMD_StartPiniataGame)
- Set clear commandId in Inspector
- Enable logging during development

---

### 4. Data Collection

**What:** Structured data points for ML analysis.

**Format:**
```csharp
{
    "timestamp": "2025-02-11T14:30:00Z",
    "dataType": "hit_target",
    "payload": {
        "reactionTime": 0.523,
        "accuracy": 0.92,
        "targetId": 5
    }
}
```

**Usage:**
```csharp
CollectDataPoint("event_type", new Dictionary<string, object>
{
    { "metric1", value1 },
    { "metric2", value2 }
});
```

**Framework handles:**
- ✅ Buffering (10 points/batch)
- ✅ Firebase upload
- ✅ Retry on failure
- ✅ Compression

**You decide:**
- What events to track
- What metrics to include
- How to structure payload

---

## 🎨 Design Patterns

### Pattern 1: Simple State Machine

```csharp
public class MyGame : BaseGame
{
    private enum InternalState { Intro, Playing, Results }
    private InternalState _internalState;
    
    public override void StartGame()
    {
        base.StartGame();
        _internalState = InternalState.Intro;
        StartCoroutine(IntroSequence());
    }
    
    IEnumerator IntroSequence()
    {
        // Show instructions
        yield return new WaitForSeconds(3);
        
        _internalState = InternalState.Playing;
        SpawnTargets();
    }
}
```

### Pattern 2: Configuration-Driven Difficulty

```csharp
[MessagePackObject]
public class MyGameConfig : GameConfig
{
    [Key(0)] public int targetCount = 10;
    [Key(1)] public float spawnInterval = 2.0f;
    [Key(2)] public float targetSpeed = 5.0f;
}

public override void Initialize(GameConfig config)
{
    base.Initialize(config);
    
    MyGameConfig myConfig = config as MyGameConfig;
    
    _targetCount = myConfig.targetCount;
    _spawnInterval = myConfig.spawnInterval;
    _targetSpeed = myConfig.targetSpeed;
}
```

### Pattern 3: Event-Driven Data Collection

```csharp
void OnHitTarget(Target target)
{
    CollectDataPoint("hit", new Dictionary<string, object>
    {
        { "targetId", target.id },
        { "reactionTime", Time.time - target.spawnTime },
        { "distance", Vector3.Distance(target.position, player.position) }
    });
}

void OnMissTarget(Target target)
{
    CollectDataPoint("miss", new Dictionary<string, object>
    {
        { "targetId", target.id },
        { "reason", "timeout" }
    });
}
```

---

## 🧪 Testing Your Game

### Local Testing (Without Controller)

```csharp
// Add test buttons in Inspector
void OnGUI()
{
    if (GUILayout.Button("Start Game"))
    {
        var config = new MyGameConfig { difficulty = 1 };
        Initialize(config);
        StartGame();
    }
    
    if (GUILayout.Button("End Game"))
    {
        EndGame();
    }
}
```

### With Controller App

```
1. Build Quest app
2. Install on Quest 3
3. Run Flutter controller
4. Scan for device
5. Connect
6. Select your game
7. Configure settings
8. Press Start
```

---

## 📊 Data Collection Best Practices

### What to Collect

**✅ DO Collect:**
- Reaction times
- Accuracy metrics
- Error rates
- Completion times
- User choices
- Difficulty progression

**❌ DON'T Collect:**
- Personally identifiable information (PII)
- Raw sensor data (too large)
- Redundant data (duplicate events)

### Data Structure

```csharp
// ✅ GOOD: Structured, queryable
CollectDataPoint("hit_target", new Dictionary<string, object>
{
    { "reactionTime", 0.523f },
    { "accuracy", 0.92f },
    { "level", 3 }
});

// ❌ BAD: Unstructured string
CollectDataPoint("event", new Dictionary<string, object>
{
    { "data", "hit target in 0.523s at level 3" }
});
```

### Performance

```csharp
// ✅ GOOD: Batch naturally (10 points/batch)
void OnEvent() {
    CollectDataPoint(...); // Auto-batched
}

// ❌ BAD: Force flush every point
void OnEvent() {
    CollectDataPoint(...);
    FlushDataBuffer(); // Too frequent!
}
```

---

## 🔧 Advanced Topics

### Custom Services Injection

```csharp
// In your LifetimeScope
builder.Register<IMyCustomService, MyCustomService>(Lifetime.Singleton);

// In your game
public class MyGame : BaseGame
{
    private IMyCustomService _customService;
    
    [Inject]
    public void ConstructWithCustomService(
        ISessionService session,
        IConnectionService connection,
        IMyCustomService customService)
    {
        base.Construct(session, connection);
        _customService = customService;
    }
}
```

### Dynamic Config Updates

```csharp
[SerializeField] private NetworkCommand _onConfigUpdate;

void Awake()
{
    _onConfigUpdate.OnReceived += HandleConfigUpdate;
}

void HandleConfigUpdate(string payload)
{
    MyGameConfig newConfig = MessagePackSerializer.Deserialize<MyGameConfig>(
        Convert.FromBase64String(payload));
    
    UpdateConfig(newConfig);
    
    // Apply changes on the fly
    UpdateDifficulty(newConfig.difficulty);
}
```

### Async Operations

```csharp
public override async void StartGame()
{
    base.StartGame();
    
    // Load assets
    await LoadAssetsAsync();
    
    // Wait for animation
    await PlayIntroAnimation();
    
    // Start gameplay
    BeginGameplay();
}
```

---

## 📁 Project Structure

```
Assets/_YourGames/MyGame/
├── Scripts/
│   ├── MyGame.cs                    # Main game logic
│   ├── MyGameConfig.cs              # Configuration
│   ├── Gameplay/
│   │   ├── TargetSpawner.cs
│   │   └── ScoreManager.cs
│   └── UI/
│       └── GameUI.cs
├── Prefabs/
│   ├── Target.prefab
│   └── GameManager.prefab
├── Scenes/
│   └── MyGame.unity
├── ScriptableObjects/
│   ├── Commands/
│   │   ├── CMD_StartMyGame.asset
│   │   ├── CMD_PauseMyGame.asset
│   │   └── CMD_EndMyGame.asset
│   └── Config/
│       └── DefaultMyGameConfig.asset
└── Materials/
    └── ...
```

---

## 🐛 Common Issues

### Issue: NetworkCommand not receiving events

**Solution:**
```csharp
// ✅ Subscribe in Awake, not Start
void Awake() {
    _command.OnReceived += Handler;
}

// ✅ Always unsubscribe
void OnDestroy() {
    _command.OnReceived -= Handler;
}
```

### Issue: Data not uploading to Firebase

**Solution:**
```csharp
// ✅ Call base.EndGame() to flush buffer
public override void EndGame() {
    base.EndGame(); // Flushes data
    // Your cleanup
}
```

### Issue: Services are null

**Solution:**
```csharp
// ✅ Use [Inject] attribute
[Inject]
public void Construct(ISessionService session) {
    SessionService = session;
}

// ❌ Don't try to access in Awake()
void Awake() {
    SessionService.DoSomething(); // NULL!
}
```

---

## ✅ Checklist

Before releasing your game:

- [ ] Implements IGameModule interface
- [ ] All NetworkCommands created and assigned
- [ ] Config class marked with [MessagePackObject]
- [ ] Data collection points added
- [ ] EndGame() reports results
- [ ] Tested locally with test buttons
- [ ] Tested with controller app
- [ ] Data appears in Firebase
- [ ] No memory leaks (unsubscribe events)
- [ ] Performance: maintains 90fps on Quest

---

## 🎓 Next Steps

1. **Study the example:** [SimpleCubeGame](../unity-quest-template/Assets/_Examples/SimpleCubeGame/)
2. **Read API docs:** [IGameModule](../unity-quest-template/Assets/_TheraplyCore/Games/IGameModule.cs), [BaseGame](../unity-quest-template/Assets/_TheraplyCore/Games/BaseGame.cs)
3. **Join discussions:** [GitHub Discussions](https://github.com/yourusername/theraply-vr-framework/discussions)

---

**Happy game building! 🎮**
