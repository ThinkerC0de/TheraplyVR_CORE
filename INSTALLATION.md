# Theraply VR Framework - Installation Guide

This document describes the current setup flow for the full stack:

- Unity Quest app (`unity-quest-template`)
- Flutter controller (`flutter_controller`)
- Local network communication (UDP discovery + TCP control + WebRTC video)

## 1. Clone Repository

```bash
git clone https://github.com/ThinkerC0de/theraply-vr-framework.git
cd theraply-vr-framework
```

## 2. Unity Setup (Quest)

1. Open `unity-quest-template` in Unity 6.3 (or newer compatible 6000.3.x).
2. Let package import complete.
3. Wait for automatic dependency restore:
   - Unity UPM/Git packages from `Packages/manifest.json`
   - NuGet packages (NuGetForUnity) from `Assets/packages.config`
4. Verify required scene components:
   - `NetworkManager` with:
     - `UDPDiscoveryService`
     - `TCPServerService`
   - `VideoStreaming` with:
     - `Camera`
     - `VideoStreamService`
     - `WebRTCServerSignaling`
5. Wire required references:
   - `TCPServerService._discoveryService` -> `UDPDiscoveryService`
   - `TCPServerService._videoStreamService` -> `VideoStreamService`
   - `WebRTCServerSignaling._videoStreamService` -> `VideoStreamService`
   - `WebRTCServerSignaling._tcpServer` -> `TCPServerService`
6. Assign `VideoStreamService.sourceCamera` to your XR camera and disable `autoDetectCamera` (recommended).

### Unity dependency fallback (if auto-restore did not complete)

1. Open `Window -> Package Manager` and wait for package resolution.
2. Run `NuGet -> Restore Packages`.
3. Reopen project if any assemblies are still missing.
4. Verify network access to:
   - `https://packages.unity.com`
   - `https://api.nuget.org/v3/index.json`

Detailed network and troubleshooting guide:
- `unity-quest-template/NETWORK_SETUP.md`

## 3. Flutter Controller Setup

```bash
cd flutter_controller
flutter pub get
```

### Firebase

Place Firebase Android config in:

```text
flutter_controller/android/app/google-services.json
```

Ensure Firebase Authentication and Firestore are configured for your project.

### Run / Build

```bash
flutter run
```

or

```bash
flutter build apk
```

Detailed controller notes:
- `flutter_controller/README.md`

## 4. Runtime Requirements

- Quest and controller phone must be on the same WiFi subnet.
- UDP discovery port: `8767`
- TCP control port: `8080`
- Video stream: WebRTC media channel (dynamic ICE ports)

## 5. Lifecycle Behavior (Current)

- Control sessions use Android foreground service while active.
- Foreground service is stopped automatically when app task is removed from recents.
- On app resume, controller attempts TCP reconnect and recovers WebRTC preview automatically.

## 6. Verification Checklist

- Unity logs show:
  - TCP server started
  - UDP broadcast active
  - WebRTC offer sent on controller connect
- Flutter logs show:
  - device discovered
  - TCP connected
  - WebRTC answer + ICE exchange
  - video track received
