# SUMMARY (2026-02-21)

## Scope
- Implemented LAN-first WebRTC negotiation on Unity side with automatic STUN fallback renegotiation.

## Changes
- `unity-quest-template/Assets/_TheraplyCore/Streaming/MediaStreamService.cs`
  - Added LAN-first mode (`_preferLanFirst`) and probe timeout (`_lanProbeTimeoutSeconds`).
  - First connection attempt now uses host ICE candidates only.
  - Added fallback trigger paths (`LAN_PROBE_TIMEOUT`, `ICE_FAILED`, `PEER_FAILED`).
  - Emits `OnStunFallbackRequested(reasonCode)` for signaling renegotiation.
- `unity-quest-template/Assets/_TheraplyCore/Streaming/WebRTCServerSignaling.cs`
  - Subscribes to fallback event.
  - On fallback request: restarts stream in forced STUN mode and sends a fresh `WEBRTC_OFFER`.

## Validation
- `flutter analyze` PASS
- `flutter test` PASS

## Manual test target
- Connect mobile+Quest on same LAN and verify first attempt stays stable.
- Simulate LAN-path issues; verify logs show fallback renegotiation and stream recovery.
