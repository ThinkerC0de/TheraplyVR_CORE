# Guided Session Plan Authoring

## Purpose
Provide therapist-authored guided session plans that parent mode can execute with one button.

## Where To Configure
- Mobile app -> `Students` -> `Session settings`.
- Fields:
  - `Guided continuation policy`
  - `Guided plan steps`

## Guided Plan Steps Format
One line per step:

`gameId|{"presetKey":"value"}`

Examples:
- `demo_cube_clicker|{"cubeCount":10,"cubeSpeed":0.7,"levelMode":"basic"}`
- `pulse_target_tap|{"targetCount":8,"targetSpeed":0.8,"targetScale":0.35}`

If preset JSON is omitted, defaults are used:
- `demo_cube_clicker`

## Continuation Policies
- `manual`: parent start waits for explicit handoff/closure decision.
- `resume_under_recovery_window`: unfinished session is auto-continued only inside therapist recovery window.
- `resume_always`: unfinished session is auto-continued whenever present.

## Runtime Behavior
1. Parent taps `Start guided session now`.
2. App resolves first valid guided step from therapist settings.
3. Game + preset are applied to setup.
4. Continuation policy is evaluated for unfinished session state.
5. App starts guided session (or auto-continues unfinished session if policy allows).

## Validation
- `flutter analyze`
- `flutter test test/guided_session_plan_test.dart test/guided_session_continuation_policy_test.dart`
- `powershell -ExecutionPolicy Bypass -File .\scripts\unity_session_flow_validation_pack.ps1 -SkipCompile`
