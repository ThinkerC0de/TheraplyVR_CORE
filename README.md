# Theraply VR Framework

**A production-ready framework for building VR therapy applications with Quest 3 and mobile controller integration.**

[![License: Proprietary](https://img.shields.io/badge/License-Proprietary-red.svg)](LICENSE)
[![Unity Version](https://img.shields.io/badge/Unity-6000.3.8f1-blue.svg)](https://unity.com/)
[![Platform](https://img.shields.io/badge/Platform-Quest%203%20%7C%20Android-green.svg)](https://www.meta.com/quest/)

---

## 🎯 Overview

Theraply VR Framework is a **clean, modular foundation** for building therapeutic VR experiences. It provides:

- **Real-time P2P networking** (UDP discovery + TCP control + WebRTC video streaming)
- **Session management** with Firebase integration
- **ML-ready data collection** (structured, batched writes)
- **Game module API** for plug-and-play mini-games
- **Lifecycle resilience** (reconnection, pause/resume handling)
- **Zero external dependencies** (uses Unity's built-in JsonUtility)

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
│  │   ├── Session Manager                                │
│  │   ├── Data Collection                                │
│  │   └── Game API                                       │
│  └── Your Games (separate repo)                         │
│      └── Implements IGameModule                         │
└─────────────────────────────────────────────────────────┘
                        ↕ UDP/TCP/WebRTC
┌─────────────────────────────────────────────────────────┐
│  Android Phone (Therapist)                              │
├─────────────────────────────────────────────────────────┤
│  Flutter Controller                                     │
│  ├── Login & Auth (Firebase)                            │
│  ├── Student Management                                 │
│  ├── Device Scanner (UDP)                               │
│  └── Game Controls (TCP)                                │
└─────────────────────────────────────────────────────────┘
                        ↕
┌─────────────────────────────────────────────────────────┐
│  Firebase (Backend)                                     │
│  ├── Authentication                                     │
│  ├── Firestore (sessions & data)                        │
│  └── Cloud Functions (ML export)                        │
└─────────────────────────────────────────────────────────┘
```

### Network Stack

| Protocol | Port | Purpose | Format |
|----------|------|---------|--------|
| UDP | 8767 | Device Discovery | JSON |
| TCP | 8080 | Control Messages | JSON |
| WebRTC | dynamic (ICE) | Video Stream | RTP/RTCP |

---

## 🚀 Quick Start

### Prerequisites

- **Unity 6.3 (6000.3.8f1)** or later
- Android Build Support installed
- Meta XR All-in-One SDK
- Flutter 3.35+ (for controller app)
- Firebase project

### Installation

```bash
# 1. Clone the repository
git clone https://github.com/ThinkerC0de/theraply-vr-framework.git
cd theraply-vr-framework

# 2. Open Unity project
# Unity Hub → Add → theraply-vr-framework/unity-quest-template/

# 3. Install Meta XR SDK
# Unity → Window → Package Manager → XR Plugin Management
# Enable Oculus/Meta Quest support

# 4. Setup Firebase
# Copy your google-services.json to flutter_controller/android/app/
# Update Firebase config in Flutter app

# 5. Build for Quest 3
# File → Build Settings → Android → Switch Platform
# Build and Run
```

### Automatic Dependency Bootstrap (Unity)

After opening `unity-quest-template` in Unity:

- UPM and Git packages are restored from `unity-quest-template/Packages/manifest.json`
- Locked package versions are resolved from `unity-quest-template/Packages/packages-lock.json`
- NuGet dependencies are restored by NuGetForUnity from:
  - `unity-quest-template/Assets/packages.config`
  - `unity-quest-template/Assets/NuGet.config`

No manual package installation should be required on a clean clone.

### If Auto-Restore Fails

Use this quick fallback:

1. Open Unity Package Manager and wait for package resolution to finish.
2. In Unity, run `NuGet -> Restore Packages`.
3. Confirm internet access to:
   - `https://packages.unity.com`
   - `https://api.nuget.org/v3/index.json`
4. Reopen the Unity project once.

---

## 📚 Documentation

### Getting Started
- **[Unity Network Setup](unity-quest-template/NETWORK_SETUP.md)** - Scene wiring and protocol flow
- **[Flutter Controller Setup](flutter_controller/README.md)** - Controller app setup and troubleshooting
- **[Installation Notes](INSTALLATION.md)** - Project bootstrap notes

### Architecture
- **[Network Protocol](docs/04-Network-Protocol.md)** - Message formats and flow
- **[Data Collection](docs/05-Data-Collection.md)** - ML-ready data structure

### Reliability and Recovery
- **[Session Resilience Roadmap](docs/06-Session-Resilience-Roadmap.md)** - Failure scenarios, architecture, and phased delivery plan
- **[Session Resilience Prompting Guide](docs/07-Session-Resilience-Prompting.md)** - Copy/paste prompts to resume work after context/token resets
- **[Session Resilience Worklog](docs/08-Session-Resilience-Worklog.md)** - Single source of truth for done/todo items

---

## 🎮 Creating Your First Game

### 1. Implement BaseGame

```csharp
using TheraplyCore.Games;
using UnityEngine;

public class MyGame : BaseGame 
{
    public override string GameId => "my_awesome_game";
    public override string DisplayName => "My Awesome Game";
    
    [SerializeField] private NetworkCommand _onSessionStart;
    
    void Awake() 
    {
        _onSessionStart.OnReceived += HandleSessionStart;
    }
    
    void HandleSessionStart(string payload) 
    {
        // Deserialize config from JSON
        MyGameConfig config = JsonUtility.FromJson<MyGameConfig>(
            System.Text.Encoding.UTF8.GetString(
                System.Convert.FromBase64String(payload)));
        
        Initialize(config);
        StartGame();
    }
    
    public override void StartGame() 
    {
        base.StartGame();
        // Your game logic here
    }
    
    void OnGameEvent() 
    {
        // Collect data for ML
        CollectDataPoint("player_action", new Dictionary<string, object> {
            { "timestamp", Time.time },
            { "score", currentScore }
        });
    }
}
```

### 2. Create Config Class

```csharp
[System.Serializable]
public class MyGameConfig : GameConfig 
{
    public int difficulty;
    public float timeLimit;
}
```

### 3. Done!

Your game auto-registers and is available to the controller app.

See **[SimpleCubeGame example](unity-quest-template/Assets/_Examples/SimpleCubeGame/)** for complete reference.

---

## 🧩 Example: Simple Cube Game

A minimal example demonstrating the API:

```csharp
public class SimpleCubeGame : BaseGame 
{
    public override string GameId => "example_cube_clicker";
    
    void OnCubeClicked() 
    {
        score++;
        
        // Framework handles batching & Firebase upload
        CollectDataPoint("cube_clicked", new Dictionary<string, object> {
            { "score", score },
            { "reactionTime", Time.time - lastClickTime }
        });
    }
}
```

**Complete source:** [_Examples/SimpleCubeGame/](unity-quest-template/Assets/_Examples/SimpleCubeGame/)

---

## 🏆 Features

### Core Systems

- ✅ **UDP Discovery** - Automatic device detection on local network
- ✅ **TCP Control Channel** - Reliable command delivery
- ✅ **WebRTC Video Streaming** - Quest video preview in Flutter controller
- ✅ **JSON Serialization** - Unity's built-in JsonUtility (zero dependencies)
- ✅ **Reconnection Logic** - Exponential backoff with automatic retry
- ✅ **Session Management** - Firebase-backed with local caching
- ✅ **Data Collection** - Batched writes for ML training
- ✅ **Lifecycle Handling** - App pause/resume support

### Game API

- ✅ **IGameModule Interface** - Standard contract for all games
- ✅ **BaseGame Helper** - Common functionality (data collection, state management)
- ✅ **ScriptableObject Commands** - Designer-friendly event system
- ✅ **Dependency Injection** - VContainer-based service resolution

### Flutter Controller

- ✅ **Firebase Auth** - Therapist login
- ✅ **UDP Scanner** - Discover Quest devices
- ✅ **TCP Control** - Send game commands
- ✅ **Foreground Session Persistence (Android)** - Keeps active control session alive in background
- ✅ **Auto-Recovery on Resume** - Reconnects TCP and restores WebRTC preview after interruptions
- ✅ **Generic UI** - Works with any game module

---

## 📦 Dependencies

### Unity (Quest)

| Package | Version | License | Purpose |
|---------|---------|---------|---------|
| VContainer | 1.17.0 | MIT | Dependency Injection |
| Meta XR SDK | Latest | Proprietary | Quest 3 support |
| Firebase Unity SDK | 11.x | Apache 2.0 | Backend integration |

**Note:** MessagePack removed - using Unity's built-in JsonUtility instead!

### Flutter (Controller)

| Package | Version | License |
|---------|---------|---------|
| firebase_core | ^3.8.1 | BSD-3 |
| firebase_auth | ^5.3.4 | BSD-3 |
| cloud_firestore | ^5.6.1 | BSD-3 |
| flutter_webrtc | ^1.3.0 | BSD-3 |
| wakelock_plus | ^1.2.8 | BSD-3 |

---

## 🛠️ Technical Specifications

### Unity Version
- **Minimum:** Unity 6.3 (6000.3.8f1)
- **Platform:** Android (Quest 3)
- **API Level:** 29+ (Android 10+)

### Serialization
- **Format:** JSON (UTF-8)
- **Library:** Unity JsonUtility (built-in)
- **Encoding:** Base64 for network transport

### Network Performance
- **Discovery:** <2s device detection
- **Control Latency:** <50ms command delivery
- **Connection:** Auto-reconnect with exponential backoff

### Streaming Lifecycle
- Unity starts video streaming after TCP client connection and WebRTC signaling.
- Flutter can recover connection after app resume/background interruptions.
- Android foreground service is used during active control sessions and is stopped when the app task is removed.

---

## 🧪 Testing

### Unity Compilation Test
```
1. Open project in Unity 6.3
2. Wait for import (first time: ~5 minutes)
3. Check Console → Should be 0 errors ✅
```

### Network Stack Test
```
1. Build Quest APK
2. Install on Quest 3
3. Run Flutter app on phone
4. Same WiFi network
5. Scan → Should find device within 2s
```

### Game Integration Test
```
1. Open SimpleCubeGame scene
2. Play in editor
3. Send START_GAME command from Flutter
4. Verify game starts and collects data
```

---

## 📁 Project Structure

```
theraply-vr-framework/
├── unity-quest-template/           # Unity VR project
│   └── Assets/
│       ├── _TheraplyCore/          # Framework code
│       │   ├── Connection/         # Reconnection logic
│       │   ├── Firebase/           # Data service
│       │   ├── Games/              # Game API (BaseGame, IGameModule)
│       │   ├── Logging/            # Structured logging
│       │   └── Network/            # UDP/TCP services
│       └── _Examples/
│           └── SimpleCubeGame/     # Example game
├── flutter_controller/             # Mobile controller app
│   ├── lib/
│   │   ├── screens/               # Login, Scanner, Control
│   │   ├── services/              # Firebase, Network
│   │   └── models/                # DeviceInfo, GameStatus
│   └── android/                   # Android config
├── docs/                          # Documentation
└── README.md                      # This file
```

---

## 🤝 Contributing

This is a **framework project**. Contributions should focus on:

✅ Core infrastructure improvements
✅ Bug fixes in network/connection
✅ Documentation enhancements
✅ Example improvements

❌ Production games (those go in separate repos)

---

## 📄 License

Proprietary license (All Rights Reserved).  
Usage, redistribution, and commercial use require prior written authorization.  
See [LICENSE](LICENSE) for details.

---

## 🙏 Acknowledgments

- **VContainer** - Lightweight DI for Unity
- **Meta** - Quest 3 platform and XR SDK
- **Firebase** - Backend infrastructure
- **Unity Technologies** - JsonUtility and core tools

---

## 📞 Support

- **Repository:** [https://github.com/ThinkerC0de/theraply-vr-framework](https://github.com/ThinkerC0de/theraply-vr-framework)
- **Issues:** [GitHub Issues](https://github.com/ThinkerC0de/theraply-vr-framework/issues)
- **Documentation:** [docs/](docs/)
- **Creator:** Marcin Szewczyk (`szewczyk.marcin@pranasense.pl`)

---

**Built with ❤️ for the VR therapy community**

**Current Status:** Core framework complete, ready for game development ✅
