# Summary

- Scope: P0 reconnect hardening for false-connected TCP state after Unity-side network flap.
- Change:
  - `flutter_controller/lib/screens/control_screen.dart`
    - added connection liveness watchdog (`Timer.periodic`) evaluating runtime/heartbeat signal freshness,
    - if transport remains marked connected but runtime signals go stale past adaptive timeout, mobile now forces `disconnect()` to trigger existing auto-reconnect + `SESSION_ATTACH` recovery path,
    - added disconnect reason override to persist lifecycle event reason codes for watchdog-triggered drops.
- Validation:
  - `flutter analyze` PASS (`docs/evidence/20260220_175439/flutter_analyze.log`)
  - `flutter test` PASS (`docs/evidence/20260220_175439/flutter_test.log`)
