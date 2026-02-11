# Theraply VR Framework

**A production-ready framework for building VR therapy applications with Quest 3 and mobile controller integration.**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Unity Version](https://img.shields.io/badge/Unity-6000.0.48f1-blue.svg)](https://unity.com/)
[![Platform](https://img.shields.io/badge/Platform-Quest%203%20%7C%20Android-green.svg)](https://www.meta.com/quest/)

---

## 🎯 Overview

Theraply VR Framework is a **clean, modular foundation** for building therapeutic VR experiences. It provides:

- **Real-time P2P networking** (UDP discovery + TCP control + UDP streaming)
- **Hardware-accelerated video streaming** (H.264 @ 90fps maintained)
- **Session management** with Firebase integration
- **ML-ready data collection** (structured, batched writes)
- **Game module API** for plug-and-play mini-games
- **Lifecycle resilience** (reconnection, pause/resume handling)

### Architecture Philosophy

This is a **FRAMEWORK, not a product**. Think of it like Unity Engine:
- We provide the infrastructure and APIs
- You build games on top of it
- Games are decoupled from core systems
- Clean separation of concerns

---

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────┐
│  Quest 3 (Student/Patient)                              │
├─────────────────────────────────────────────────────────┤
│  VR Application (Unity)                                 │
│  ├── Core Framework (this repo)                         │
│  │   ├── Network Stack                                  │
│  │   ├── Streaming Engine                               │
│  │   ├── Session Manager                                │
│  │   └── Game API                                       │
│  └── Your Games (separate repo)                         │
│      └── Implements IGameModule                         │
└─────────────────────────────────────────────────────────┘
                        ↕ UDP/TCP
┌─────────────────────────────────────────────────────────┐
│  Android Phone (Therapist)                              │
├─────────────────────────────────────────────────────────┤
│  Flutter Controller                                     │
│  ├── Login & Auth                                       │
│  ├── Student Management                                 │
│  ├── Connection UI                                      │
│  └── Streaming View + Controls                          │
└─────────────────────────────────────────────────────────┘
                        ↕
┌─────────────────────────────────────────────────────────┐
│  Firebase (Backend)                                     │
│  ├── Authentication                                     │
│  ├── Firestore (metadata only)                          │
│  └── Cloud Functions (ML export)                        │
└─────────────────────────────────────────────────────────┘
```

### Network Stack

| Protocol | Port | Purpose | Codec |
|----------|------|---------|-------|
| UDP | 8767 | Device Discovery | JSON |
| TCP | 8080 | Control Messages | MessagePack |
| UDP | 8081 | Video Stream | H.264 |
| UDP | 8082 | Audio Stream | Opus |

---

## 🚀 Quick Start

### Prerequisites

- Unity 6000.0.48f1 (or later)
- Android Build Support installed
- Meta XR All-in-One SDK
- Flutter 3.35+ (for controller app)
- Firebase project

### Installation

```bash
# 1. Clone the repository
git clone https://github.com/yourusername/theraply-vr-framework.git
cd theraply-vr-framework

# 2. Open Unity project
# Open unity-quest-template/ in Unity Hub

# 3. Install dependencies (automatic via Package Manager)
# - VContainer (DI)
# - MessagePack-CSharp
# - Meta XR SDK

# 4. Setup Firebase
# - Copy your google-services.json to flutter-controller/android/app/
# - Copy your GoogleService-Info.plist for iOS (if needed)

# 5. Build for Quest 3
# File → Build Settings → Android → Switch Platform
# Build and Run
```

---

## 📚 Documentation

- **[Getting Started](docs/01-Getting-Started.md)** - Setup and first steps
- **[Architecture Overview](docs/02-Architecture.md)** - System design
- **[Creating Games](docs/03-Creating-Games.md)** - ⭐ Build your own mini-games
- **[Network Protocol](docs/04-Network-Protocol.md)** - Message formats
- **[Data Collection](docs/05-Data-Collection.md)** - ML-ready data structure
- **[Migration Guide](docs/06-Migration-Guide.md)** - Port existing games

### API Reference

- [IGameModule Interface](docs/API-Reference/IGameModule.md)
- [BaseGame Helper Class](docs/API-Reference/BaseGame.md)
- [Network Commands](docs/API-Reference/NetworkCommands.md)
- [Core Services](docs/API-Reference/Services.md)

---

## 🎮 Creating Your First Game

### 1. Implement IGameModule

```csharp
using TheraplyCore.Games;
using MessagePack;

public class MyGame : BaseGame {
    public override string GameId => "my_awesome_game";
    public override string DisplayName => "My Awesome Game";
    
    [SerializeField] private NetworkCommand _onSessionStart;
    
    private ISessionService _session;
    
    [Inject]
    public MyGame(ISessionService session) {
        _session = session;
    }
    
    void Awake() {
        _onSessionStart.OnReceived += HandleSessionStart;
    }
    
    void HandleSessionStart(string payload) {
        MyGameConfig config = MessagePackSerializer.Deserialize<MyGameConfig>(
            Convert.FromBase64String(payload));
        
        Initialize(config);
        StartGame();
    }
    
    public override void StartGame() {
        // Your game logic here
    }
    
    void OnGameEvent() {
        // Collect data for ML
        CollectDataPoint("player_action", new {
            timestamp = Time.time,
            score = currentScore
        });
    }
}
```

### 2. Create Config Class

```csharp
[MessagePackObject]
public class MyGameConfig : GameConfig {
    [Key(0)] public int difficulty;
    [Key(1)] public float timeLimit;
}
```

### 3. Create ScriptableObject Commands

```
Right-click in Project → Create → Theraply → Network Command
Name: CMD_StartMyGame
```

### 4. Done!

Your game auto-registers and is available to the controller app.

See [SimpleCubeGame example](unity-quest-template/Assets/_Examples/SimpleCubeGame/) for complete reference.

---

## 🧩 Example: Simple Cube Game

A minimal example demonstrating the API:

```csharp
public class SimpleCubeGame : BaseGame {
    public override string GameId => "example_cube_clicker";
    
    void OnCubeClicked() {
        score++;
        
        // Framework handles batching & Firebase upload
        CollectDataPoint("cube_clicked", new {
            score = score,
            reactionTime = Time.time - lastClickTime
        });
    }
}
```

**Complete source:** [_Examples/SimpleCubeGame/](unity-quest-template/Assets/_Examples/SimpleCubeGame/)

---

## 🏆 Features

### Core Systems

- ✅ **UDP Discovery** - Automatic device detection on local network
- ✅ **TCP Control Channel** - Reliable command delivery with MessagePack
- ✅ **UDP Video Streaming** - Hardware H.264 encoding, 30fps @ 1280x720
- ✅ **UDP Audio Streaming** - Opus codec, low-latency
- ✅ **Reconnection Logic** - Exponential backoff, 5-minute timeout
- ✅ **Session Management** - Firebase-backed with local caching
- ✅ **Data Collection** - Batched writes (10 points/batch)
- ✅ **Lifecycle Handling** - HMD mount/unmount, app pause/resume

### Game API

- ✅ **IGameModule Interface** - Standard contract for all games
- ✅ **BaseGame Helper** - Common functionality (data collection, commands)
- ✅ **GameRegistry** - Automatic game discovery
- ✅ **ScriptableObject Commands** - Designer-friendly event system
- ✅ **Dependency Injection** - VContainer-based service resolution

### Flutter Controller

- ✅ **Firebase Auth** - Therapist login
- ✅ **Student Management** - CRUD with local caching
- ✅ **UDP Scanner** - Discover Quest devices
- ✅ **Streaming View** - Real-time video + audio
- ✅ **Generic Controls** - Pause, restart, config updates

---

## 📦 Dependencies

### Unity (Quest)

| Package | Version | License | Purpose |
|---------|---------|---------|---------|
| VContainer | 1.17.0 | MIT | Dependency Injection |
| MessagePack-CSharp | 3.1.4 | MIT | Binary serialization |
| Meta XR SDK | Latest | Proprietary | Quest 3 support |
| Firebase Unity SDK | 11.x | Apache 2.0 | Backend integration |

### Flutter (Controller)

| Package | Version | License |
|---------|---------|---------|
| firebase_core | ^2.x | BSD-3 |
| firebase_auth | ^4.x | BSD-3 |
| cloud_firestore | ^4.x | BSD-3 |
| provider | ^6.x | MIT |

---

## 🧪 Testing

### Network Stack Test

```bash
# Quest: Start broadcasting
# Controller: Scan for devices
# Expected: Device appears within 2s
```

### Streaming Test

```bash
# Quest: Enable streaming at 30fps
# Controller: View stream
# Expected: <50ms latency, 90fps maintained on Quest
```

### Data Collection Test

```bash
# Play example game for 1 minute
# Check Firestore: /sessions/{id}/gameData
# Expected: ~600 data points (10 points/second)
```

---

## 🤝 Contributing

This is a **framework project**. Contributions should focus on:

✅ Core infrastructure improvements
✅ Bug fixes in network/streaming
✅ Documentation enhancements
✅ Example game improvements

❌ Production mini-games (those go in separate repos)

---

## 📄 License

MIT License - See [LICENSE](LICENSE) for details.

**All dependencies are commercial-friendly:**
- MessagePack-CSharp: MIT
- VContainer: MIT
- Android MediaCodec: Apache 2.0 (built-in)

---

## 🙏 Acknowledgments

- **MessagePack** - Fast binary serialization
- **VContainer** - Lightweight DI for Unity
- **Meta** - Quest 3 platform and XR SDK
- **Firebase** - Backend infrastructure

---

## 📞 Support

- **Documentation:** [docs/](docs/)
- **Issues:** [GitHub Issues](https://github.com/yourusername/theraply-vr-framework/issues)
- **Discussions:** [GitHub Discussions](https://github.com/yourusername/theraply-vr-framework/discussions)

---

**Built with ❤️ for the VR therapy community**
