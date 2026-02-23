# Session Handoff / Attach / EndSession Stabilizer (2026-02-21)

## Scope
- Flutter controller + Unity runtime quick stabilizer for:
  - END_SESSION bypassing attach precondition.
  - Handoff popup gated to entry/reconnect context.
  - Dialog/back-close consistency in EN.
  - Explicit decision logs for handoff/attach reasons.

## Notes
- AS-IS vs TO-BE snapshot:
  - ../notes/as_is_vs_to_be.md

## Validation
- flutter analyze: PASS
  - ../commands/flutter_controller_flutter_analyze.log
- flutter test: PASS
  - ../commands/flutter_controller_flutter_test.log

## Files touched
- flutter_controller/lib/screens/control_screen.dart
- unity-quest-template/Assets/_TheraplyCore/Games/Runtime/GameCommandBus.cs
- unity-quest-template/Assets/_TheraplyCore/Games/Runtime/GameRuntimeService.cs
