# SUMMARY (2026-02-21)

## Scope
- Fixed scene routing migration so stale serialized bindings are overwritten.
- Made MainScene media capture XR-ready (auto-detect camera/audio listener).

## Changes
- `Assets/_Examples/Scripts/ExampleAdditiveSceneRouter.cs`
  - `EnsureBinding(...)` now updates existing binding when scene path differs.
  - This forces migration from old `ExampleCubeScene` mapping to `CubeClickerVR` at runtime.
- `Assets/_Examples/Scenes/MainScene.unity`
  - `MediaStreamService` now uses auto-detection (`_autoDetectCamera=1`) with cleared static camera/listener refs.

## Validation
- `flutter analyze` PASS
- `flutter test` PASS

## Manual test target
- Start from MainScene, connect mobile, open Demo Cube Clicker, verify router log indicates `CubeClickerVR.unity`.
- Verify headset controls/stream still work with auto-detected camera.
