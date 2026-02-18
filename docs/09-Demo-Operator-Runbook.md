# Demo Operator Runbook (10-Min)

Date baseline: 2026-02-17

## 0) Preflight (2 min)

- PC and phone in same Wi-Fi.
- Unity Editor open on `unity-quest-template`.
- Scene: `Assets/_Examples/Scenes/MainScene.unity`.
- Flutter APK installed from:
  - `flutter_controller/build/app/outputs/flutter-apk/app-debug.apk`
- Build Settings include:
  - `Assets/_Examples/Scenes/MainScene.unity`
  - `Assets/_Examples/Scenes/ExampleCubeScene.unity`
  - `Assets/_Examples/Scenes/PulseTargetsScene.unity`
  - `Assets/_Examples/Scenes/DemoCubeScene.unity`
  - `Assets/_Examples/Scenes/SessionResilienceTest.unity`
- Current note: MetaXR package/profile is not integrated yet in this workspace; demo uses standard camera path in Editor.

Quick checks:
- `flutter analyze`
- `flutter test`
- `powershell -ExecutionPolicy Bypass -File .\scripts\unity_cli_validate.ps1 -Mode both`
- `powershell -ExecutionPolicy Bypass -File .\scripts\unity_editor_mvp_smoke.ps1 -ValidationRuns 1 -ValidationGameIds demo_cube_clicker`
- `powershell -ExecutionPolicy Bypass -File .\scripts\mobile_real_device_preflight.ps1`

## 1) Start Order (1 min)

1. Start Unity Play Mode in `DemoCubeScene`.
2. Open mobile app.
3. Login therapist.
4. Select student.
5. Connect to student device (scanner screen).

## 2) Session Gate Behavior (1 min)

If app reports active unfinished session:
- Choose `Resume` to continue existing session.
- Choose `Start New` to send `END_SESSION`, then create new session.

Expected:
- No hidden duplicate session.
- Active session id is visible in control screen header.

## 3) Demo Game Flow (4 min)

1. Step 4: choose `Demo Cube Clicker` (`gameId=demo_cube_clicker`) or `Pulse Target Tap` (`gameId=pulse_target_tap`).
2. Step 5: set:
   - `cubeCount` (for example 12)
   - `cubeSpeed` (for example 0.7)
   - `levelMode`:
     - `basic`
     - `alternate_colors`
     - `random_target_color`
3. Press `Start`.
4. In Unity Editor click cubes with mouse until all removed.

Expected in Unity HUD:
- remaining counter
- timer
- best time
- target color (for color modes)

Expected behavior:
- Game ends after all cubes clicked.
- `PAUSE`, `RESUME`, `STOP` from mobile affect current game.

## 4) Firebase Resilience Check (1 min)

Fast lane (single command, includes stale `.utmp` cleanup):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\unity_editor_mvp_smoke.ps1 `
  -ValidationRuns 1 `
  -ValidationGameIds demo_cube_clicker
```

Direct Unity command (manual lane):

```powershell
"C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe" `
  -batchmode -nographics `
  -projectPath "C:\Users\licen\Projects\theraply-vr-framework\unity-quest-template" `
  -buildTarget Android `
  -executeMethod "TheraplyCore.Editor.Automation.FirebaseNetworkValidation.RunFirebaseNetworkValidation" `
  -logFile "C:\Users\licen\Projects\theraply-vr-framework\docs\evidence\firebase_network_validation_latest.log"
```

PASS markers in log:
- `[FirebaseNetworkValidation] Online phase: ...`
- `[FirebaseNetworkValidation] Offline phase: ...`
- `[FirebaseNetworkValidation] Reconnect phase: ...`
- `[FirebaseNetworkValidation] PASS: ...`

Strict expected outcomes reference:
- `docs/23-Unity-Firebase-Reconnect-Expected-Outcomes.md`

## 5) Fallback Actions (1 min)

- If scanner cannot connect:
  - verify same Wi-Fi and Unity Play Mode active
  - retry scanner screen
- If session gate blocks commands:
  - choose `Resume` or `Start New`
- If Firebase validation fails with scene backup prompt:
  - rerun `scripts/unity_editor_mvp_smoke.ps1` (it performs `.utmp` cleanup before Unity batch run)
- If no backend logs:
  - verify `_simulateFirebase = false`
  - verify endpoint urls in scene config

## 6) Demo Exit

- Use `Back` in setup screen to return to game catalog.
- On app exit choose:
  - `End session` to close cleanly, or
  - `Keep unfinished` to continue later.
