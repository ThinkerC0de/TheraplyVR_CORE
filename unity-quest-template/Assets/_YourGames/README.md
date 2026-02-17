# Your Games Go Here! 🎮

**This folder is for YOUR production mini-games.**

---

## Contract First Rule

Every new mini-game must follow the stable contracts from:

- `Assets/_TheraplyCore/Games/Contracts/GameContracts.cs`
- `Assets/_TheraplyCore/Games/Contracts/GameCommands.cs`
- `Assets/_TheraplyCore/Games/Contracts/GameRegistry.cs`

If a game needs core changes, stop and add an extension point instead.

---

## 📂 What Goes Here

### ✅ Your Production Games
- Piniata
- Puzzle
- Butterflies
- Mindfulness
- Any therapeutic mini-games you build

### ❌ Not Here
- ~~Framework code~~ (goes in `_TheraplyCore/`)
- ~~Examples~~ (goes in `_Examples/`)
- ~~Test scenes~~ (use separate test project)

---

## 🏗️ Recommended Structure

```
_YourGames/
├── Piniata/
│   ├── Scripts/
│   │   ├── PiniataGame.cs          # Implements IGameModule
│   │   ├── PiniataConfig.cs        # MessagePack config
│   │   └── Gameplay/
│   │       ├── PiniataSpawner.cs
│   │       └── HitDetection.cs
│   ├── Prefabs/
│   │   ├── Piniata.prefab
│   │   └── Stick.prefab
│   ├── Scenes/
│   │   └── PiniataGame.unity
│   ├── ScriptableObjects/
│   │   └── Commands/
│   │       ├── CMD_StartPiniata.asset
│   │       └── CMD_PausePiniata.asset
│   └── Materials/
│       └── ...
│
├── Puzzle/
│   └── ... (same structure)
│
└── YourNewGame/
    └── ... (same structure)
```

---

## 🚀 Quick Start: Create Your First Game

### Step 1: Create Folder

```bash
mkdir -p _YourGames/MyGame/{Scripts,Prefabs,Scenes,ScriptableObjects/Commands,Materials}
```

### Step 2: Create Game Script

`Scripts/MyGame.cs`:

```csharp
using UnityEngine;
using TheraplyCore.Games;
using VContainer;

public class MyGame : BaseGame
{
    public override string GameId => "my_game";
    public override string DisplayName => "My Game";
    
    [SerializeField] private NetworkCommand _onSessionStart;
    
    void Awake()
    {
        _onSessionStart.OnReceived += HandleStart;
    }
    
    void HandleStart(string payload)
    {
        // Deserialize config, start game
    }
}
```

### Step 3: Create Config

`Scripts/MyGameConfig.cs`:

```csharp
using MessagePack;
using TheraplyCore.Games;

[MessagePackObject]
public class MyGameConfig : GameConfig
{
    [Key(0)] public int difficulty = 1;
    [Key(1)] public float timeLimit = 60f;
    
    public MyGameConfig()
    {
        gameId = "my_game";
    }
}
```

### Step 4: Create Commands

In Unity Editor:
1. Right-click in `ScriptableObjects/Commands/`
2. Create → Theraply → Network Command
3. Name: `CMD_StartMyGame`
4. Set `commandId`: `"SESSION_START"`

### Step 5: Create Scene

1. New Scene: `Scenes/MyGame.unity`
2. Add GameObject: "MyGameManager"
3. Add `MyGame.cs` component
4. Assign ScriptableObjects in Inspector

### Step 6: Test

```csharp
// Add to MyGame.cs for testing:
#if UNITY_EDITOR
[ContextMenu("Test Start")]
void TestStart()
{
    var config = new MyGameConfig { difficulty = 2 };
    Initialize(config);
    StartGame();
}
#endif
```

---

## 📚 Essential Reading

Before you start building:

1. **[Unity Network Setup](../../NETWORK_SETUP.md)** ⭐ configure this first
2. **[Creating Games Guide](../../../docs/02-Creating-Games.md)** ⭐ then build the game
3. **[SimpleCubeGame Example](../_Examples/SimpleCubeGame/)** - Study this
4. **[Mini-Game Contracts](../_TheraplyCore/Games/Contracts/GameContracts.cs)** - Reference contracts
5. **[System Components Guide](../../../docs/01-System-Components-Guide.md)** - Architecture and configuration reference

---

## ✅ Best Practices

### DO:
- ✅ One folder per game
- ✅ Clear, descriptive names (e.g., `PiniataGame`, not `Game1`)
- ✅ Follow the recommended structure
- ✅ Call `base.StartGame()` in overrides
- ✅ Collect meaningful data points
- ✅ Test locally before building

### DON'T:
- ❌ Modify framework code in `_TheraplyCore/`
- ❌ Put games in root `Assets/` folder
- ❌ Mix multiple games in one scene
- ❌ Forget to unsubscribe from events
- ❌ Collect PII (personal identifiable information)

---

## 🎯 Checklist for New Games

Before considering a game "done":

- [ ] Implements `IGameModule` (via `BaseGame`)
- [ ] All `NetworkCommand` assets created
- [ ] Config class marked with `[MessagePackObject]`
- [ ] Data collection points added (at least 5 types)
- [ ] `EndGame()` reports results with metrics
- [ ] Tested locally with test methods
- [ ] Tested with controller app
- [ ] Data appears in Firebase console
- [ ] No memory leaks (events unsubscribed)
- [ ] Maintains 90 FPS on Quest 3
- [ ] Documentation in game's README.md

---

## 📊 Example: Piniata Game Structure

For reference, here's what a production game might look like:

```
Piniata/
├── README.md                       # Game-specific docs
├── Scripts/
│   ├── PiniataGame.cs              # Main IGameModule (200 lines)
│   ├── PiniataConfig.cs            # Config (50 lines)
│   └── Gameplay/
│       ├── PiniataSpawner.cs       # Spawn logic (150 lines)
│       ├── HitDetection.cs         # Hit validation (100 lines)
│       ├── ScoreManager.cs         # Score tracking (80 lines)
│       └── DifficultyScaler.cs     # Difficulty system (120 lines)
├── Prefabs/
│   ├── Piniata_Easy.prefab
│   ├── Piniata_Medium.prefab
│   ├── Piniata_Hard.prefab
│   └── Stick.prefab
├── Scenes/
│   └── PiniataGame.unity
├── ScriptableObjects/
│   ├── Commands/
│   │   ├── CMD_StartPiniata.asset
│   │   ├── CMD_PausePiniata.asset
│   │   ├── CMD_ResumePiniata.asset
│   │   └── CMD_UpdatePiniataConfig.asset
│   └── Configs/
│       ├── PiniataConfig_Level1.asset
│       ├── PiniataConfig_Level2.asset
│       └── PiniataConfig_Level3.asset
├── Materials/
│   ├── Piniata_Red.mat
│   └── Piniata_Blue.mat
├── Audio/
│   ├── hit.wav
│   ├── miss.wav
│   └── success.wav
└── Animations/
    └── PiniataSwing.anim
```

**Total:** ~700 lines of game-specific code + framework API usage

---

## 🐛 Common Issues

### "Services are null in Awake()"
**Fix:** Access services after VContainer injection completes (in Start() or later)

### "NetworkCommand not firing"
**Fix:** Check:
1. Command assigned in Inspector?
2. Subscribed in Awake()?
3. commandId matches network message?

### "Data not in Firebase"
**Fix:** Call `base.EndGame()` to flush buffer

### "Game lags on Quest"
**Fix:** Profile with Unity Profiler, optimize:
- Draw calls
- Physics calculations
- Particle systems

---

## 💡 Tips from Experience

### Start Simple
Don't build everything at once. Start with:
1. Basic game loop (spawn → interact → score)
2. Add one data collection point
3. Test with framework
4. Iterate and expand

### Copy Patterns
Study SimpleCubeGame:
- Command structure
- Config serialization
- Data collection
- Result reporting

Then adapt to your game's needs.

### Test Early, Test Often
1. Test locally (Context Menu buttons)
2. Test with controller app
3. Check Firebase data
4. Verify 90fps maintained

### Document as You Go
Write README.md for your game:
- Gameplay description
- Configuration options
- Data collected
- Known issues

---

## 🆘 Need Help?

- **[Creating Games Guide](../../../docs/02-Creating-Games.md)** - Complete tutorial
- **[Mini-Game Contracts](../_TheraplyCore/Games/Contracts/GameContracts.cs)** - Detailed contracts
- **[System Components Guide](../../../docs/01-System-Components-Guide.md)** - Runtime architecture and setup
- **[GitHub Discussions](https://github.com/yourusername/theraply-vr-framework/discussions)** - Community help
- **[SimpleCubeGame](../_Examples/SimpleCubeGame/)** - Working example

---

## 🎉 Ready to Build?

1. Read the Creating Games guide
2. Study the SimpleCubeGame example
3. Create your game folder
4. Start coding!

**Your game could help thousands of patients. Let's build something amazing! 🚀**
