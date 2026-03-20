# Network Setup Guide

## Overview
The Theraply VR Framework uses a client-server architecture for Quest-Controller communication:

- **Quest (Unity)** = UDP Broadcaster + TCP Server + WebRTC Video Source
- **Flutter Controller** = UDP Listener + TCP Client + WebRTC Video Receiver

## Architecture

```
┌─────────────────┐           ┌──────────────────┐
│  Quest (Unity)  │           │ Flutter Controller│
├─────────────────┤           ├──────────────────┤
│                 │           │                  │
│ UDP Discovery   │◄─────────►│ UDP Discovery    │
│ (Port 8769)     │  Broadcast │ (Port 8769)      │
│                 │           │                  │
│ TCP Server      │◄─────────►│ TCP Client       │
│ (Port 8080)     │  Connect  │                  │
│                 │           │                  │
│ WebRTC Video    │◄─────────►│ WebRTC Receiver  │
│ (ICE dynamic)   │  P2P Media│ (ICE dynamic)    │
└─────────────────┘           └──────────────────┘
```

## Unity Setup

### 1. Add Required Components to Scene

Create a GameObject named `NetworkManager` with these components:

```
NetworkManager (GameObject)
├─ UDPDiscoveryService   (broadcasts device info)
└─ TCPServerService      (accepts TCP connections)
```

Create a GameObject named `VideoStreaming` with these components:

```
VideoStreaming (GameObject)
├─ Camera
├─ MediaStreamService
└─ WebRTCServerSignaling
```

### 2. Configure Components

**UDPDiscoveryService:**
- Discovery Port: `8769`
- Broadcast Interval: `0.5` seconds
- Device Timeout: `10` seconds
- Custom Device Name: (optional, defaults to device name)

**TCPServerService:**
- Server Port: `8080`
- Send Buffer Size: `8192` bytes
- Receive Buffer Size: `8192` bytes
- Log Connections: `true` (for debugging)
- Log Messages: `false` (reduce spam)

**Component References (important):**
- `TCPServerService._discoveryService` → `UDPDiscoveryService`
- `TCPServerService._mediaStreamService` → `MediaStreamService`
- `WebRTCServerSignaling._mediaStreamService` → `MediaStreamService`
- `WebRTCServerSignaling._tcpServer` → `TCPServerService`

**MediaStreamService:**
- `sourceCamera`: assign your XR camera manually (recommended)
- `autoDetectCamera`: `false` when source camera is assigned
- Streaming settings: `1280x720 @ 30fps` (default)

### 3. How It Works

**On Quest Start:**
1. `UDPDiscoveryService` starts broadcasting device info every 0.5s
2. `TCPServerService` starts listening on port 8080
3. Quest waits for Flutter controller to connect

**When Client Connects:**
1. `TCPServerService` detects client connection
2. Automatically pauses UDP broadcast (saves bandwidth)
3. `WebRTCServerSignaling` starts stream and sends `WEBRTC_OFFER`
4. Flutter sends `WEBRTC_ANSWER` and ICE candidates
5. Video starts over WebRTC media channel

**When Client Disconnects:**
1. `TCPServerService` detects disconnection
2. Automatically resumes UDP broadcast
3. WebRTC stream is stopped
4. Quest becomes discoverable again for reconnection

**Device Info Broadcast (UDP):**
```json
{
  "deviceId": "unique-device-id",
  "deviceName": "Quest3-Demo",
  "ip": "192.168.0.221",
  "controlPort": 8080,
  "videoPort": 8081,
  "audioPort": 8082,
  "studentId": "",
  "timestamp": 1707838418
}
```

**Message Protocol (TCP):**
- Length-prefixed messages (4 bytes big-endian + JSON)
- Format: `[4 bytes length][JSON message]`

```json
{
  "messageId": "guid",
  "timestamp": 1707838418,
  "commandId": "WEBRTC_OFFER",
  "payload": null
}
```

WebRTC signaling command IDs:
- `WEBRTC_OFFER` (Unity → Flutter)
- `WEBRTC_ANSWER` (Flutter → Unity)
- `WEBRTC_ICE_CANDIDATE` (both directions)

## Flutter Setup

### 1. Services

The Flutter app uses two services:

**Discovery Pause on Connect:**
When Flutter connects to Quest via TCP, it automatically stops UDP scanning to save battery and CPU. When returning to scanner screen (after disconnect), scanning resumes automatically.

**DiscoveryService** (`lib/services/discovery_service.dart`):
- Listens for UDP broadcasts from Quest
- Provides stream of discovered devices
- Auto-removes stale devices (15s timeout)

**ConnectionService** (`lib/services/connection_service.dart`):
- Connects to Quest TCP server
- Sends/receives length-prefixed JSON messages
- Handles connection state
- Auto-reconnects on app resume (retry/backoff)

**WebRTCMediaService** (`lib/services/webrtc_media_service.dart`):
- Handles WebRTC offer/answer flow over TCP signaling
- Applies ICE candidates
- Emits remote `MediaStream` to UI

**Foreground Session (Android):**
- Active control session starts an Android foreground service
- Helps keep connection alive when app is backgrounded
- Stops automatically when app task is removed from recents

### 2. Usage Flow

```dart
// 1. Start discovery
final discovery = DiscoveryService();
await discovery.startScanning();

// 2. Listen for devices
discovery.devices.listen((deviceInfo) {
  print('Found: ${deviceInfo.deviceName} @ ${deviceInfo.ip}');
});

// 3. Connect to device
final connection = ConnectionService();
await connection.connect(deviceInfo.ip, deviceInfo.controlPort);

// 4. Send commands
await connection.sendCommand('SESSION_START', {
  'sessionId': '12345',
  'studentId': 'abc123'
});

// 5. Receive messages
connection.messages.listen((message) {
  print('Received: ${message['commandId']}');
});
```

## Network Requirements

### Same Network
- Quest and Flutter device must be on **same WiFi network**
- Check IP addresses are in same range (e.g., both 192.168.1.x)

### Firewall Rules
- **Quest:** Allow UDP 8769 (broadcast) and TCP 8080 (server)
- **Flutter Device:** Allow UDP 8769 (listen)

### Network Discovery
- Quest broadcasts to `X.Y.Z.255` (subnet broadcast)
- Skips virtual adapters (100.x, 169.254.x, 127.x)
- Prefers: 192.168.x (home) > 10.x (corporate) > 172.16-31.x

## Troubleshooting

### Device Not Discovered

**Check Unity Console:**
```
[UDPDiscovery] Available network interfaces:
  - 100.112.161.47
    -> Skipped (likely virtual adapter)
  - 192.168.0.221
    -> SELECTED (192.168.x.x - typical home/office network)
[UDPDiscovery] Calculated broadcast: 192.168.0.221 -> 192.168.0.255
[UDPDiscovery] Broadcasting to 192.168.0.255:8769
```

**Check Flutter Logs:**
```
[Discovery] 🔍 Starting UDP scan on port 8769
[Discovery] ✅ Scanning started
[Discovery] ✅ Found: Quest3-Demo @ 192.168.0.221
```

**Common Issues:**
- Different subnets (Quest: 192.168.1.x, Flutter: 192.168.0.x)
- Firewall blocking UDP broadcast
- Virtual network adapter selected (check Unity logs)

### Connection Timeout

**Check Unity Console:**
```
[TCPServer] Server started on port 8080
[TCPServer] Waiting for client connection...
[TCPServer] Client connected: 192.168.0.100
```

**Check Flutter Logs:**
```
[Connection] 🔌 Connecting to 192.168.0.221:8080
[Connection] ✅ Connected successfully
```

**Common Issues:**
- TCP Server not started (add `TCPServerService` to scene)
- Firewall blocking TCP port 8080
- Wrong IP address or port

### Connected but Black Video

Common causes and checks:
- `MediaStreamService.sourceCamera` points to wrong camera
- `autoDetectCamera` selects a fallback camera unexpectedly
- `WebRTC.Update()` coroutine is not running in Unity
- Flutter reconnect happened but offer/answer did not renegotiate

Expected Unity logs:
```
[MediaStreamService] ✅ WebRTC streaming started (offer will be sent by signaling)
[WebRTCServerSignaling] Sent WEBRTC_OFFER to client
[MediaStreamService] Peer Connection State: Connected
```

### Messages Not Received

**Verify Protocol:**
- Both sides use length-prefixed messages (4 bytes + JSON)
- Big-endian byte order for length prefix
- UTF-8 encoding for JSON

**Check Logs:**
```
Unity: [TCPServer] Sent: SESSION_START (123 bytes)
Flutter: [Connection] 📥 Received: SESSION_START
```

## Testing

### Manual Test (Unity)

1. Build and run on Quest
2. Check Unity Console logs:
   - UDP broadcast starting
   - TCP server listening
3. Look for selected IP address

### Manual Test (Flutter)

1. Run Flutter app on phone/tablet
2. Check logs for discovery
3. Try connecting to found device
4. Send test command and check Unity logs

### Expected Timeline

From app start to connection:
- **0.0s**: Flutter starts scanning
- **0.5s**: Unity broadcasts (first packet)
- **0.5-1.0s**: Flutter detects device
- **1.0-2.0s**: User taps connect
- **2.0-2.5s**: TCP connection established ✅

## Performance Notes

- UDP broadcast every **0.5s** (fast discovery)
- Device timeout: **15s** (keeps list clean)
- TCP NoDelay enabled (low latency)
- Buffer sizes: 8KB (adequate for control messages)

## Code References

**Unity:**
- `Assets/_TheraplyCore/Network/Discovery/UDPDiscoveryService.cs`
- `Assets/_TheraplyCore/Network/Connection/TCPServerService.cs`
- `Assets/_TheraplyCore/Network/Connection/TCPConnectionService.cs` (legacy client)

**Flutter:**
- `lib/services/discovery_service.dart`
- `lib/services/connection_service.dart`
- `lib/screens/scanner_screen.dart`
