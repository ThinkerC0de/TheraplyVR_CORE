# Summary

- Scope: cube-only catalog + server-authoritative session handoff gate.
- Changes:
  - Mobile game catalog reduced to `demo_cube_clicker` only.
  - Mobile content-delivery UI/actions disabled (no install/uninstall/sync flow).
  - Handoff prompt decision is now driven by persisted Firestore session snapshot (`therapy_sessions`) only.
  - Unity dev content simulation defaults disabled and simulated catalog reduced to cube-only.

## Validation

- `flutter analyze` PASS (`flutter_analyze.log`)
- `flutter test` PASS (`flutter_test.log`)

## Manual follow-up

- Verify live flow: end session -> reconnect same student -> no `Session handoff needed` unless latest persisted server session is unfinished.
