# Demo Readiness Checklist (2026-02-18)

Scope: practical demo of Flutter controller -> Unity runtime -> demo game -> resilience/Firebase path.

Status legend:
- `OK` = ready
- `PARTIAL` = works, but not production-complete for this area
- `MISSING` = not ready yet

## 1) UX Flow (Flutter)

- `OK` Therapist login screen exists (`flutter_controller/lib/screens/login_screen.dart`).
- `OK` Student selection screen exists (`flutter_controller/lib/screens/students_screen.dart`).
- `OK` Device connect screen exists (`flutter_controller/lib/screens/scanner_screen.dart`).
- `OK` Session decision gate exists in control flow (Resume vs Start New) (`flutter_controller/lib/screens/control_screen.dart`).
- `OK` Mini-game catalog step exists (step 4) (`flutter_controller/lib/screens/control_screen.dart`).
- `OK` Game setup step exists with Start/Restart/Back (step 5) (`flutter_controller/lib/screens/control_screen.dart`).
- `OK` Exit prompt exists (keep unfinished vs end session) on system back (`flutter_controller/lib/screens/control_screen.dart`).

## 2) Runtime Command Path

- `OK` START/PAUSE/RESUME/STOP payloads include selected `gameId` (`flutter_controller/lib/screens/control_screen.dart`).
- `OK` Demo config payload is sent for `demo_cube_clicker` (`cubeCount`, `cubeSpeed`, `levelMode`) (`flutter_controller/lib/screens/control_screen.dart`).
- `OK` Unity runtime resolves command game by explicit `gameId` (`unity-quest-template/Assets/_TheraplyCore/Games/Runtime/GameRuntimeService.cs`).

## 3) Demo Game (Unity)

- `OK` Main runtime scene exists (`unity-quest-template/Assets/_Examples/Scenes/MainScene.unity`).
- `OK` Additive game scenes exist:
  - `unity-quest-template/Assets/_Examples/Scenes/ExampleCubeScene.unity`
  - `unity-quest-template/Assets/_Examples/Scenes/PulseTargetsScene.unity`
- `OK` Demo module exists (`unity-quest-template/Assets/_Examples/Scripts/DemoCubeGameModule.cs`).
- `OK` Second sample module exists (`unity-quest-template/Assets/_Examples/Scripts/PulseTargetsGameModule.cs`).
- `OK` Demo module supports:
  - moving cubes in viewport bounds,
  - click to remove,
  - level modes (`basic`, `alternate_colors`, `random_target_color`),
  - timer + best time,
  - completion on all cubes clicked.
- `OK` Example bootstrap registers game modules in `_Examples` layer (`unity-quest-template/Assets/_Examples/Scripts/ExampleGameRuntimeBootstrap.cs`).
- `OK` Example additive scene router exists in `_Examples` layer (`unity-quest-template/Assets/_Examples/Scripts/ExampleAdditiveSceneRouter.cs`).
- `OK` No hardcoded `smoke_test_game`/`demo_cube_clicker` IDs in core runtime layer (`unity-quest-template/Assets/_TheraplyCore`).

## 4) Firebase / Resilience Validation Path

- `OK` Firebase default is real network path (`_simulateFirebase = false`) (`unity-quest-template/Assets/_TheraplyCore/Firebase/FirebaseDataService.cs`).
- `OK` Test scenes have ingest/reconciliation endpoint fields wired (`DemoCubeScene.unity`, `SessionResilienceTest.unity`).
- `OK` Firebase automation supports dynamic validation `gameId` resolution with CLI override (`-validationGameId`) (`unity-quest-template/Assets/_TheraplyCore/Editor/Automation/FirebaseNetworkValidation.cs`).

## 5) Build/Test Validation (latest local run)

- `OK` `flutter analyze` PASS.
- `OK` `flutter test` PASS.
- `OK` `flutter build apk --debug` PASS.
- `OK` `powershell -ExecutionPolicy Bypass -File .\scripts\unity_cli_validate.ps1 -Mode both` PASS.
- `OK` Firebase network validation PASS (online/offline/reconnect) log:
  - `docs/evidence/firebase_network_validation_latest.log`
  - PASS marker present: `[FirebaseNetworkValidation] PASS: gameId=demo_cube_clicker ...`

## 6) Gaps / Missing Before "clean demo package"

- `OK` Build Settings scene list is explicit:
  - `Assets/_Examples/Scenes/MainScene.unity`
  - `Assets/_Examples/Scenes/ExampleCubeScene.unity`
  - `Assets/_Examples/Scenes/PulseTargetsScene.unity`
  - `Assets/_Examples/Scenes/DemoCubeScene.unity`
  - `Assets/_Examples/Scenes/SessionResilienceTest.unity`
- `PARTIAL` MetaXR runtime profile is not integrated in this workspace yet (no Meta/Oculus package stack in `Packages/manifest.json`; current demo scenes use standard camera fallback path).
- `PARTIAL` Firebase validation still logs scene-recovery prompt when stale `.utmp` state exists, but run can still pass. Keep cleanup step in operator flow.
- `OK` DemoCube save/resume restores concrete cube state (remaining cubes with color/position/direction, counters, target mode context, elapsed session offset) when `resumeFromSaved=true`.
- `OK` Single short operator script exists:
  - `docs/09-Demo-Operator-Runbook.md`

## 7) Recommended next actions (demo hardening)

1. Add lightweight pre-demo cleanup command for stale Unity recovery files (`.utmp`) to avoid batch prompt noise.
2. Add one Unity editor playmode checklist item to verify resume path manually (`Resume -> Wznow zapis`) before live demo.
3. Record one fresh evidence zip after final rehearsal using the runbook.

## 8) Product/Data Backlog (discussion 2026-02-18)

- `TODO` `DATA-001` - Add login-time entitlement gate (role + license status) before entering student flow (`THERAPIST`, `PARENT`, future roles).
- `TODO` `DATA-002` - Define shared child/student access model for multi-parent/multi-guardian accounts (same child visible on multiple devices/accounts by relation binding, not local-only ownership).
- `TODO` `DATA-003` - Implement local-first student roster cache with sync policy:
  - initial hydrate from server,
  - offline edits in pending queue,
  - write-confirm-then-commit locally for create/update/delete,
  - periodic/manual reconciliation.
- `TODO` `DATA-004` - Minimize Firebase traffic in Flutter:
  - snapshot listener scope reduction,
  - delta sync instead of full-list refresh,
  - cooldown/debounce for repeated writes,
  - explicit refresh only on selected UI boundaries.
- `TODO` `LIC-001` - Define entitlement model:
  - app-wide license,
  - per-game license,
  - grant validity windows (`fromUtc` -> `toUtc`) and perpetual grants.
- `TODO` `LIC-002` - Add admin/system grant path for remote entitlement assignment (single game or full app, temporary or permanent).
- `TODO` `CAT-001` - Add purchased-content state contract for mobile + Quest:
  - what user owns,
  - what is installed,
  - target version vs installed version,
  - update required/optional flags.
- `TODO` `CAT-002` - Add install/update orchestration flow for mobile + VR bundles with deterministic status reporting (`NOT_INSTALLED`, `INSTALLING`, `READY`, `UPDATE_REQUIRED`, `FAILED`).
- `TODO` `SEC-001` - Add data minimization and pseudonymization design for clinical/research mode:
  - separate identity store from telemetry/session metrics,
  - irreversible or strongly controlled pseudonymous IDs in analytics pipelines,
  - no direct patient identifiers in gameplay telemetry payloads.
- `TODO` `SEC-002` - Add compliance hardening checklist (access control, retention, encryption, audit trail, breach-response posture) before medical-study rollout.
