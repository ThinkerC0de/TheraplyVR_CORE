# Changelog

All notable changes to the Theraply VR Framework will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- Android foreground session service for active control sessions.
- Automatic TCP reconnect on Flutter app resume with retry/backoff.
- WebRTC stream recovery path after reconnect/resume.
- 16:9 video preview container in Flutter control screen.

### Changed
- Network stack documentation updated to reflect UDP discovery + TCP control + WebRTC media.
- Unity WebRTC lifecycle usage aligned with current package API (`WebRTC.Update()` coroutine without unsupported init/dispose calls).

---

## [0.1.0] - 2025-02-11

### Added - Core Framework Foundation

#### Game API
- `IGameModule` interface - Contract for all games
- `BaseGame` abstract class - Helper with common functionality
- `GameConfig` base class for configuration
- `GameDataPoint` and `GameResult` structures
- `GameState` enum for lifecycle management

#### Network Layer
- `NetworkCommand` ScriptableObject system for designer-friendly events
- `UDPDiscoveryService` for automatic device detection (port 8767)
- `TCPConnectionService` for reliable control messages (port 8080)
- `NetworkMessage` structure with MessagePack serialization
- Thread-safe message queuing for Unity main thread

#### Services Architecture
- `ISessionService` interface (placeholder)
- `IConnectionService` interface (placeholder)
- VContainer dependency injection setup
- Service-based architecture pattern

#### Example Game
- `SimpleCubeGame` - Minimal example demonstrating API
- `SimpleCubeConfig` - Configuration with MessagePack
- `CubeClickHandler` - VR interaction component
- Complete documentation and usage examples

#### Documentation
- Comprehensive README with architecture overview
- "Creating Games" guide (03-Creating-Games.md)
- SimpleCubeGame documentation
- _YourGames folder guide with best practices
- API usage patterns and examples

#### Project Structure
- Mono-repo structure (Unity + Flutter + Firebase)
- Organized folder hierarchy
- Separation of framework (_TheraplyCore) and games (_YourGames, _Examples)
- .gitignore for Unity, Flutter, Firebase

#### Dependencies
- VContainer 1.17.0 (MIT) - Dependency Injection
- MessagePack-CSharp 3.1.4 (MIT) - Binary serialization
- Meta XR SDK (latest) - Quest 3 support

### Design Decisions
- **Framework over Product** - Provide infrastructure, not games
- **Loose Coupling** - ScriptableObject commands decouple layers
- **Plugin Architecture** - Games are separate from core
- **ML-Ready** - Structured data collection from day one
- **Commercial-Friendly** - All dependencies MIT/Apache 2.0

---

## Version History

### Version Numbering

```
MAJOR.MINOR.PATCH

MAJOR: Breaking API changes
MINOR: New features, backwards compatible
PATCH: Bug fixes, backwards compatible
```

### Upgrade Paths

Future versions will include migration guides for breaking changes.

---

## [0.0.0] - 2025-02-11

### Initial Project Setup
- Repository created
- Folder structure established
- Core architecture defined

---

## Future Roadmap

### v0.2.0 - Streaming Module
- H.264 hardware encoding with Android MediaCodec
- UDP video streaming (port 8081)
- UDP audio streaming (port 8082)
- Forward Error Correction (FEC)

### v0.3.0 - Firebase Integration
- Authentication service
- Session management with Firestore
- Data collection and batching
- Cloud Functions for ML export

### v0.4.0 - Lifecycle Management
- Reconnection logic with exponential backoff
- HMD mount/unmount handling
- Application pause/resume
- 5-minute session timeout

### v0.5.0 - Flutter Controller
- Login & authentication UI
- Student management (CRUD)
- UDP device scanner
- Connection establishment UI

### v0.6.0 - Streaming UI
- Real-time video/audio playback
- Generic game controls (start/pause/resume)
- Configuration updates
- Session monitoring

### v1.0.0 - Production Ready
- Full test coverage
- Performance optimization
- Security audit
- Complete documentation
- Example game: Piniata (separate repo)

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

---

## Links

- **Repository:** https://github.com/yourusername/theraply-vr-framework
- **Issues:** https://github.com/yourusername/theraply-vr-framework/issues
- **Discussions:** https://github.com/yourusername/theraply-vr-framework/discussions
- **Documentation:** [docs/](docs/)
