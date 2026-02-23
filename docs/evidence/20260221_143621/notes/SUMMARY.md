# SUMMARY (2026-02-21)

## Scope
- Removed broken save-state resume path from mobile game control UX.
- Kept session handoff decision (`Continue` / `Start new`) while simplifying `Continue` behavior.

## Changes
- `flutter_controller/lib/screens/control_screen.dart`
  - removed resume-strategy dialog (`start from beginning` vs `load saved state`),
  - removed "Start from saved state" button,
  - simplified `_startFromSetup()` to always issue `START_GAME` with `resumeFromSaved=false`,
  - handoff `Continue` now attaches session and opens setup screen without auto-forcing resume strategy,
  - critical game command payload now sends `resumeFromSaved=false` consistently.

## Validation
- `flutter analyze` PASS
- `flutter test` PASS

## Logs
- `flutter_analyze.log`
- `flutter_test.log`
