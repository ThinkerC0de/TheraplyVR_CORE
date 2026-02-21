# Summary

- Scope: Unity session-history instrumentation for controller connection lifecycle.
- Changes:
  - `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/GameRuntimeService.cs`
    - emits `controller_connected`, `controller_reconnected`, `controller_disconnected` via `TrackCriticalRuntimeEvent(...)`.
    - payload now includes connection epoch, client IP, session state, active game flag, and reconnect downtime.
  - `unity-quest-template/Assets/_TheraplyCore/Firebase/FirebaseDataService.cs`
    - added connection lifecycle events to `CriticalDurableEventTypes` for durable local persistence.
- Validation:
  - `flutter analyze` PASS (`docs/evidence/20260220_174841/flutter_analyze.log`)
  - `flutter test` PASS (`docs/evidence/20260220_174841/flutter_test.log`)
