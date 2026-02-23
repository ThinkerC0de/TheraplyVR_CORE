# SUMMARY (2026-02-21)

## Scope
- Extended LAN-first WebRTC strategy to Flutter client and aligned signaling with Unity fallback renegotiation.

## Changes
- `flutter_controller/lib/services/webrtc_media_service.dart`
  - Added LAN-first peer config (no STUN on first attempt).
  - Added LAN probe timeout and failure detection (`LAN_PROBE_TIMEOUT`, `ICE_FAILED`, `PEER_FAILED`).
  - Added fallback arming with explicit renegotiation request command: `WEBRTC_STUN_FALLBACK_REQUEST`.
  - Added support for offer metadata `iceMode` (`LAN`/`STUN`) from Unity.
- `unity-quest-template/Assets/_TheraplyCore/Streaming/WebRTCServerSignaling.cs`
  - Offer payload now includes `iceMode` and `reasonCode`.
  - Added handler for `WEBRTC_STUN_FALLBACK_REQUEST` from mobile.
- `unity-quest-template/Assets/_TheraplyCore/Streaming/MediaStreamService.cs`
  - Exposed `IsLanOnlyMode` for signaling metadata.

## Validation
- `flutter analyze` PASS
- `flutter test` PASS

## Manual test target
- Reinstall app(s), verify LAN-first normal path and STUN fallback path from both network-fault directions.
