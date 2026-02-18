# Mobile Session Smoke Script

Date baseline: 2026-02-18
Scope: Flutter controller session flow against Unity Editor runtime.

## Preconditions

- Unity project open: `unity-quest-template`.
- Scene in Play Mode: `Assets/_Examples/Scenes/DemoCubeScene.unity`.
- Mobile app installed from `flutter_controller/build/app/outputs/flutter-apk/app-debug.apk`.
- Therapist account has active entitlement (`APP` access).

## Step-by-step smoke path

1. Login in mobile app.
2. Select student on `Students` screen.
3. Connect on scanner screen.
4. On control screen:
   - choose game (`Demo Cube Clicker` or `Pulse Target Tap`),
   - go to setup,
   - press `Start`.
5. Verify Unity receives `START` and game begins.
6. Press `PAUSE` in mobile.
7. Press `RESUME` in mobile.
8. Simulate interruption:
   - disable network briefly (or stop Play Mode),
   - restore network/Play Mode,
   - wait for reconnect.
9. If session decision appears:
   - run one pass with `Resume`,
   - run second pass with `Start New`.
10. Press `STOP` and then `END SESSION`.

## Expected outcomes

- No hidden duplicate session.
- Session header in mobile shows:
  - active session id,
  - connection status,
  - remote state summary (`Session`, `Runtime`, watchdog line).
- `PAUSE`/`RESUME`/`STOP` are deterministic after reconnect.
- `Start New` always closes previous remote session before starting another one.

## Evidence to capture

- Tested account email/uid.
- Tested student id.
- Tested `gameId` values.
- Timestamp and run result (`PASS` / `FAIL`) with short note.
