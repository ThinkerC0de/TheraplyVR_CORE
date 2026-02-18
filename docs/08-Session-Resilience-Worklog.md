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

- `PARTIAL` `DATA-001` - Login-time entitlement gate (role + app license status) added in Flutter before entering student flow (`THERAPIST`, `PARENT`, future roles), with legacy fallback and optional strict mode (`STRICT_ENTITLEMENT_GATE=true`) that blocks missing/unavailable entitlement backend.
- `PARTIAL` `DATA-002` - Shared child/student access model defined and wired in Flutter (`student_access_bindings`: relation binding + owner/write separation + merged visibility from ownership and bindings; role-level permissions matrix still TODO).
- `PARTIAL` `DATA-003` - Local-first student roster cache implemented with sync policy:
  - initial hydrate from server,
  - offline edits in pending queue,
  - write-confirm-then-commit locally for create/update/delete (+ basic revision conflict guard),
  - manual and periodic auto-reconciliation with retry backoff (`merge/conflict policy` still TODO).
- `PARTIAL` `DATA-004` - Firebase traffic minimization in Flutter:
  - snapshot listener kept in therapist scope,
  - delta apply on `docChanges` instead of full-list remap in listener,
  - cooldown for repeated identical updates,
  - explicit refresh on selected UI boundaries (`TODO`: wider debounce policy + server-side query/index tuning).
- `PARTIAL` `LIC-001` - Minimal entitlement model defined:
  - app-wide license,
  - per-game license,
  - grant validity windows (`fromUtc` -> `toUtc`) and perpetual grants (contract level).
- `PARTIAL` `LIC-002` - Admin/system grant path now includes:
  - minimal backend policy in `firestore.rules` (`user_entitlements` + `entitlement_grants` write only for `admin_operator`),
  - role gate in `admin_console_web` (`role=admin_operator` or `admin_operator=true` claim),
  - immutable admin audit stream (`admin_audit_trail`) with required `reason` + `correlationId`,
  - mobile dev bootstrap write path now requires `admin_operator` claim (no therapist/operatorless write),
  - mobile-side smoke coverage for "web entitlement change -> next login decision" (`flutter_controller/test/entitlement_admin_e2e_smoke_test.dart`);
  full production workflow (approval chain, retention policy, claim provisioning runbook) remains TODO.
- `PARTIAL` `CAT-001` - Purchased-content state contract drafted and wired in Flutter (`GAME_INSTALL_STATUS` payload + runtime enum + request builders):
  - what user owns,
  - what is installed,
  - target version vs installed version,
  - update required/optional flags.
- `PARTIAL` `CAT-002` - Install/update orchestration flow added in Flutter catalog/setup path (`SYNC_CATALOG`, `INSTALL_GAME`, `UNINSTALL_GAME`) with deterministic status reporting (`NOT_INSTALLED`, `INSTALLING`, `READY`, `UPDATE_REQUIRED`, `FAILED`) and launch gating; Quest has dev simulator for this lifecycle (no real download bundles yet), while production installer lifecycle/backend authorization policy still TODO.
- `PARTIAL` `SEC-001` - Data minimization and pseudonymization payload contracts drafted for clinical/research mode:
  - separate identity store from telemetry/session metrics,
  - irreversible or strongly controlled pseudonymous IDs in analytics pipelines,
  - no direct patient identifiers in gameplay telemetry payloads (runtime guard now blocks forbidden keys/values before dispatch; crypto/KMS still TODO).
- `PARTIAL` `SEC-002` - Compliance hardening checklist drafted (`docs/14-Compliance-Hardening-Checklist.md`) covering access control, retention, encryption, audit trail, breach-response posture; implementation/audit evidence still TODO before medical-study rollout.

## 9) Sprint update: Admin entitlement hardening (2026-02-18)

- `OK` Implemented minimal Firestore rules + role enforcement:
  - `firestore.rules`,
  - `firebase.json`,
  - `admin_console_web` claim gate before dashboard access.
- `OK` Added audit trail for admin operations:
  - payload contract update with `reason` and `correlationId`,
  - append-only `admin_audit_trail` writes in admin service,
  - service tests in `admin_console_web/test/entitlement_admin_service_test.dart`.
- `OK` Added CMS-lite testing UX in web admin panel:
  - quick action buttons (`grant app access`, `revoke app access`),
  - `Use my UID` helper,
  - fallback `reason` auto-fill to reduce operator friction in manual testing.
- `OK` Reduced web startup friction for test env:
  - `admin_console_web` now has built-in Firebase web fallback config for `theraply-vr-demo`,
  - panel can run with plain `flutter run -d chrome` (still supports project override via `dart-define`).
- `OK` Added E2E smoke coverage (web write contract -> mobile login gate effect):
  - `flutter_controller/test/entitlement_admin_e2e_smoke_test.dart`.
- `OK` Improved mobile login testability to avoid false revoke results:
  - `flutter_controller` login now remembers last used email locally (no hardcoded therapist default on restart),
  - `Students` screen app bar now shows active signed-in account (email/uid) for quick verification.
- `OK` Expanded admin web panel from raw form to tabbed operator directory:
  - `Operations` (existing entitlement/grant actions),
  - `Therapists/Parents` list (from `user_entitlements`),
  - `Children` list (from `students`),
  - `Games` list (known catalog + GAME grant statistics from `entitlement_grants`).
- `OK` Validation commands (local):
  - `admin_console_web`: `flutter analyze`, `flutter test`,
  - `flutter_controller`: `flutter analyze`, `flutter test`.

## 10) Sprint 1 closeout and next execution queues (2026-02-18)

- `DONE` Sprint 1 closeout checklist:
  - `docs/18-Sprint-1-Closure-Checklist.md`
- `DONE` Mobile MVP completion queue:
  - `docs/19-Mobile-MVP-Completion-Tasklist.md`
- `DONE` Unity Editor MVP completion queue:
  - `docs/20-Unity-Editor-MVP-Completion-Tasklist.md`
- `DONE` Integrated validation + decision gate:
  - `docs/21-Integrated-Validation-And-GoNoGo.md`

## 11) Sprint 1 closure execution evidence (2026-02-18)

- `OK` Firestore rules deployed to target project:
  - command: `firebase.cmd deploy --only firestore:rules --project theraply-vr-demo`
  - evidence: `docs/evidence/20260218_174335/commands/firebase_deploy_firestore_rules.log`
- `OK` `admin_operator` claim verified:
  - `scripts/set_admin_operator_claim.ps1` (test operator account),
  - `scripts/check_admin_operator_claim.ps1` (final check),
  - evidence: `docs/evidence/20260218_174335/commands/check_admin_operator_claim_final.log`
- `OK` Grant/revoke smoke with next-login gate result:
  - grant -> allow (`APP_LICENSE_ACTIVE`),
  - revoke -> deny (`APP_LICENSE_INACTIVE`),
  - app grant override -> allow (`APP_LICENSE_ACTIVE`),
  - evidence: `docs/evidence/20260218_174335/commands/manual_smoke_grant_revoke_next_login.log`
- `OK` Audit contract sample validated (`reason`, `correlationId`, actor fields):
  - evidence: `docs/evidence/20260218_174335/commands/admin_audit_trail_contract_sample.log`
- `OK` Validation commands rerun:
  - `admin_console_web`: `flutter analyze`, `flutter test`,
  - `flutter_controller`: `flutter analyze`, `flutter test`,
  - evidence summary: `docs/evidence/20260218_174335/notes/sprint1_closure.md`

## 12) Mobile MVP execution update (2026-02-18)

- `OK` A1 release-profile gate hardening in `flutter_controller`:
  - strict entitlement gate now enforced in release runtime,
  - dev bootstrap explicitly disabled in release runtime,
  - file: `flutter_controller/lib/services/entitlement_service.dart`
- `OK` A2 explicit login denial UI states + actionable hints:
  - dedicated error card for:
    - missing profile,
    - inactive app license,
    - strict-mode backend unavailable,
  - file: `flutter_controller/lib/screens/login_screen.dart`
- `OK` A3 login gate regression matrix:
  - active entitlement,
  - revoked entitlement,
  - app grant override,
  - missing entitlement in strict mode,
  - file: `flutter_controller/test/entitlement_login_gate_matrix_test.dart`
- `OK` B2 consolidated smoke script in docs:
  - file: `docs/22-Mobile-Session-Smoke-Script.md`
- `OK` B3 visible diagnostics in control UI:
  - active session id,
  - connection state,
  - explicit remote summary line (`Session` + `Runtime`),
  - file: `flutter_controller/lib/screens/control_screen.dart`
- `OK` C1/C2/C3 UX hardening:
  - account-switch guard clears stale local state,
  - selected student marker on list,
  - role indicator (manage/read-only) in student screen,
  - cleaner error copy with next-step hints,
  - files:
    - `flutter_controller/lib/screens/login_screen.dart`
    - `flutter_controller/lib/screens/students_screen.dart`
- `OK` Mobile validation rerun:
  - `flutter analyze`
  - `flutter test`
  - evidence:
    - `docs/evidence/20260218_174335/commands/flutter_controller_flutter_analyze_post_mobile.log`
    - `docs/evidence/20260218_174335/commands/flutter_controller_flutter_test_post_mobile.log`

## 13) Unity Editor MVP execution update (2026-02-18)

- `OK` A1/A2 scene + bootstrap stability:
  - canonical scene set remains explicit in Build Settings,
  - deterministic example game registration on Editor startup remains in place,
  - bootstrap now also ensures runtime diagnostics overlay host,
  - files:
    - `unity-quest-template/Assets/_Examples/Scripts/ExampleGameRuntimeBootstrap.cs`
    - `unity-quest-template/ProjectSettings/EditorBuildSettings.asset`
- `OK` A3 pre-run stale-state cleanup:
  - added `scripts/cleanup_unity_utmp.ps1`,
  - used before Unity batch validation runs to avoid scene recovery prompt.
- `OK` B1/B2/B3 command/session parity and payload path:
  - fixed Firebase validator to resolve/auto-register requested `validationGameId` (not only first discovered module),
  - verified `demo_cube_clicker`, `pulse_target_tap`, `smoke_test_game` validation runs with PASS markers,
  - added additional `demo_cube_clicker` runs for consecutive stability evidence,
  - file:
    - `unity-quest-template/Assets/_TheraplyCore/Editor/Automation/FirebaseNetworkValidation.cs`
- `OK` C1/C2 Firebase resilience path:
  - network-backed path remains enabled in validation (`_simulateFirebase=false`),
  - online/offline/reconnect PASS markers recorded for validated runs.
- `OK` C3 strict reconnect outcomes documented:
  - `docs/23-Unity-Firebase-Reconnect-Expected-Outcomes.md`
- `OK` D1 single-command local smoke profile:
  - `scripts/unity_editor_mvp_smoke.ps1`
- `OK` D2 runtime diagnostics in scene HUD/log:
  - added `EditorRuntimeDiagnosticsOverlay` with:
    - session id,
    - active game id,
    - runtime status,
    - backend sync health summary,
  - files:
    - `unity-quest-template/Assets/_Examples/Scripts/EditorRuntimeDiagnosticsOverlay.cs`
    - `unity-quest-template/Assets/_Examples/Scripts/EditorRuntimeDiagnosticsOverlay.cs.meta`
- `OK` D3 troubleshooting notes centralized:
  - runbook updated with single-command lane + cleanup guidance,
  - file: `docs/09-Demo-Operator-Runbook.md`
- `OK` E1 definition-of-done evidence:
  - Unity CLI compile/build PASS:
    - `docs/evidence/20260218_174335/commands/unity_cli_validate_post_unity_mvp.log`
  - Firebase validation PASS logs:
    - `docs/evidence/20260218_174335/unity_editor_mvp_probe/artifacts/unity_editor_mvp/firebase_validation_run1_demo_cube_clicker.log`
    - `docs/evidence/20260218_174335/unity_editor_mvp_runs/firebase_validation_pulse_run1.log`
    - `docs/evidence/20260218_174335/unity_editor_mvp_runs/firebase_validation_smoke_run1.log`
    - `docs/evidence/20260218_174335/unity_editor_mvp_runs/firebase_validation_demo_run2.log`
    - `docs/evidence/20260218_174335/unity_editor_mvp_runs/firebase_validation_demo_run3.log`
    - summary: `docs/evidence/20260218_174335/unity_editor_mvp_runs/SUMMARY.md`
- `OK` post-step mobile validation rerun:
  - `docs/evidence/20260218_174335/commands/flutter_controller_flutter_analyze_post_unity_mvp.log`
  - `docs/evidence/20260218_174335/commands/flutter_controller_flutter_test_post_unity_mvp.log`

## 14) Integrated validation + Go/No-Go decision (2026-02-18)

- `OK` Lane A (admin control plane):
  - rules deploy + admin claim verification + deterministic grant/revoke next-login decision:
    - `docs/evidence/20260218_174335/notes/sprint1_closure.md`
  - audit trail contract sample (`reason`, `correlationId`, actor fields) verified:
    - `docs/evidence/20260218_174335/commands/admin_audit_trail_contract_sample.log`
- `OK` Lane B (mobile MVP):
  - `admin_console_web`: `flutter analyze`, `flutter test` PASS:
    - `docs/evidence/20260218_174335/commands/admin_console_web_flutter_analyze_integrated.log`
    - `docs/evidence/20260218_174335/commands/admin_console_web_flutter_test_integrated.log`
  - `flutter_controller`: `flutter analyze`, `flutter test` PASS:
    - `docs/evidence/20260218_174335/commands/flutter_controller_flutter_analyze_integrated.log`
    - `docs/evidence/20260218_174335/commands/flutter_controller_flutter_test_integrated.log`
- `OK` Lane C (Unity Editor MVP):
  - Unity validation logs include PASS markers:
    - `docs/evidence/20260218_174335/unity_editor_mvp_probe/artifacts/unity_editor_mvp/firebase_validation_run1_demo_cube_clicker.log`
    - `docs/evidence/20260218_174335/unity_editor_mvp_runs/firebase_validation_pulse_run1.log`
    - `docs/evidence/20260218_174335/unity_editor_mvp_runs/firebase_validation_smoke_run1.log`
    - `docs/evidence/20260218_174335/unity_editor_mvp_runs/firebase_validation_demo_run2.log`
    - `docs/evidence/20260218_174335/unity_editor_mvp_runs/firebase_validation_demo_run3.log`
- `OK` Required operator note captured:
  - `docs/evidence/20260218_174335/notes/integrated_operator_note.md`

Go/No-Go decision:
- `GO` for editor-first Sprint 1 scope.
- Residual risk to track next:
  - run one fully manual phone+Unity operator rehearsal (3x in a row) with explicit student selection and capture note per run.

## 15) Evidence footprint control + real-device delta preflight (2026-02-18)

- `OK` Evidence footprint tooling added:
  - `scripts/report_evidence_footprint.ps1`
  - `scripts/cleanup_evidence_artifacts.ps1`
  - policy doc: `docs/24-Evidence-Footprint-Guardrails.md`
- `OK` Evidence collection defaults hardened (text-first):
  - `scripts/collect_validation_evidence.ps1` now skips binary artifacts and zip unless explicitly requested:
    - `-IncludeBinaryArtifacts`
    - `-IncludeZip`
- `OK` Local evidence cleanup executed:
  - pre-cleanup footprint report:
    - `docs/evidence/_footprint/20260218_pre_cleanup.md`
    - total `docs/evidence` size: `1930.44 MB`
  - cleanup command:
    - `powershell -ExecutionPolicy Bypass -File .\scripts\cleanup_evidence_artifacts.ps1 -RemoveEvidenceZips -Apply`
  - post-cleanup footprint report:
    - `docs/evidence/_footprint/20260218_post_cleanup.md`
    - total `docs/evidence` size: `2.82 MB`
  - reclaimed local workspace size: `~2102.14 MB`
- `OK` Git hygiene guardrails updated:
  - `.gitignore` now excludes evidence zip and binary artifact folders under `docs/evidence`.
  - historical blob size is still visible in git object stats (`176.61 MiB` pre-GC), because old blobs remain in commit history.
- `OK` Real-device environment delta workflow added:
  - script: `scripts/mobile_real_device_preflight.ps1`
  - checklist doc: `docs/25-Mobile-Real-Device-Login-Reconnect-Delta-Checklist.md`
  - runbook preflight hook:
    - `docs/09-Demo-Operator-Runbook.md`
- `BLOCKED` Real-device preflight execution in current session:
  - no phone detected via `adb devices` (empty list),
  - evidence: `docs/evidence/20260218_183442/mobile_real_device_preflight/SUMMARY.md`.
- `OK` Real-device preflight rerun after USB device attach:
  - device: `RFCY9019KYF` (`SM_S938B`, Android API 36),
  - status: `PASS`,
  - evidence: `docs/evidence/20260218_184036/mobile_real_device_preflight/SUMMARY.md`.
- `OK` Real-device clean-start preflight with app data reset:
  - status: `PASS` with `-ClearAppData`,
  - evidence: `docs/evidence/20260218_184202/mobile_real_device_preflight/SUMMARY.md`.
- `OK` Preflight script stability fix:
  - package check updated from `pm list packages` (false-negative prone) to `pm path`,
  - file: `scripts/mobile_real_device_preflight.ps1`.
- `OK` Manual real-device smoke executed after preflight PASS:
  - operator confirmation: `done`,
  - evidence pack:
    - `docs/evidence/20260218_185045/mobile_real_device_smoke/notes/manual_real_device_smoke_report.md`
    - `docs/evidence/20260218_185045/mobile_real_device_smoke/artifacts/connection_timeline.log`
    - `docs/evidence/20260218_185045/mobile_real_device_smoke/artifacts/auth_timeline.log`
  - log highlights:
    - reconnect observed: `Reconnected on attempt 5`,
    - command ACK flow observed for `PAUSE_GAME`, `STOP_GAME`, `START_GAME`, `END_SESSION`.
- `OK` Post-step validation rerun:
  - `admin_console_web`: `flutter analyze`, `flutter test` PASS:
    - `docs/evidence/20260218_185045/mobile_real_device_smoke/commands/admin_console_web_flutter_analyze.log`
    - `docs/evidence/20260218_185045/mobile_real_device_smoke/commands/admin_console_web_flutter_test.log`
  - `flutter_controller`: `flutter analyze`, `flutter test` PASS:
    - `docs/evidence/20260218_185045/mobile_real_device_smoke/commands/flutter_controller_flutter_analyze.log`
    - `docs/evidence/20260218_185045/mobile_real_device_smoke/commands/flutter_controller_flutter_test.log`

## 16) Auto-reconnect continuity hardening (2026-02-18)

- `OK` `ControlScreen` reconnect policy hardened:
  - reconnect loop is now triggered automatically on connection loss (not only on app resume),
  - reconnect retries continue in background until connected or screen exit flow begins,
  - intentional exit/disconnect now disables auto-reconnect to avoid reconnect while leaving screen,
  - file: `flutter_controller/lib/screens/control_screen.dart`.
- `OK` Status UX for reconnect attempts:
  - disconnected state now surfaces `Disconnected (auto-retry N)` while loop is active.
- `OK` Validation after reconnect hardening:
  - `flutter_controller`: `flutter analyze`, `flutter test` PASS.
  - `admin_console_web`: `flutter analyze`, `flutter test` PASS.
- `TODO` Manual verification rerun on physical phone:
  - confirm session continuity without extra operator clicks after Wi-Fi flap,
  - confirm commands after auto-reconnect still target same remote session.

## 17) Game flow hardening: mobile lock rules + Unity terminal sync (2026-02-18)

- `OK` Session decision gate no longer triggers for remote `CREATED` state:
  - reason: fresh install / auto-bootstrap `CREATED` should not be treated as unfinished in-progress session,
  - files:
    - `flutter_controller/lib/models/session_recovery_policy.dart`
    - `flutter_controller/test/session_recovery_policy_test.dart`
    - `flutter_controller/test/save_resume_regression_test.dart`
- `OK` Mobile setup/runtime UX hardened to prevent operator chaos:
  - setup sliders/dropdowns are now locked while runtime is active,
  - `Start` is disabled while runtime is active,
  - `Restart` is enabled only while runtime is active,
  - `PAUSE/RESUME/STOP` availability now follows runtime/session state,
  - terminal session update (`COMPLETED` etc.) now shows explicit snackbar feedback to operator,
  - file:
    - `flutter_controller/lib/screens/control_screen.dart`
- `OK` `STOP_GAME` payload reason updated:
  - default reason switched from `TherapistStop` to `UserExit` (game stop treated as interruption, not full therapist session abort),
  - file:
    - `flutter_controller/lib/screens/control_screen.dart`
- `OK` Unity runtime now clears active game selection after stop/end and auto-syncs terminal game state:
  - `StopActiveGame(...)` now clears active game binding (prevents stale active game id),
  - watchdog now reconciles module terminal state (`Completed`/`Failed`) into session lifecycle and emits status updates,
  - this enables mobile to receive completion state when game self-finishes (e.g. cube clicker),
  - files:
    - `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/GameRuntimeService.cs`
    - `unity-quest-template/Assets/_Examples/Scripts/ExampleAdditiveSceneRouter.cs`
- `OK` Scene unload behavior aligned with expected return-to-catalog flow:
  - additive router now unloads game scene when no active game is selected,
  - file:
    - `unity-quest-template/Assets/_Examples/Scripts/ExampleAdditiveSceneRouter.cs`
- `OK` Validation rerun:
  - `flutter_controller`: `flutter analyze`, `flutter test` PASS,
  - `admin_console_web`: `flutter analyze`, `flutter test` PASS,
  - evidence:
    - `docs/evidence/20260218_200034/game_runtime_flow_hardening/commands/flutter_controller_flutter_analyze.log`
    - `docs/evidence/20260218_200034/game_runtime_flow_hardening/commands/flutter_controller_flutter_test.log`
    - `docs/evidence/20260218_200034/game_runtime_flow_hardening/commands/admin_console_web_flutter_analyze.log`
    - `docs/evidence/20260218_200034/game_runtime_flow_hardening/commands/admin_console_web_flutter_test.log`
    - `docs/evidence/20260218_200034/game_runtime_flow_hardening/notes/SUMMARY.md`
- `TODO` Manual operator confirmation still required:
  - phone + Unity run to confirm self-finish cube flow updates mobile without manual refresh,
  - verify `Wroc` from setup unloads active game scene in live run,
  - verify reconnect continuity targets same active session after temporary network flap.

## 18) Mobile operator UX/IA iteration 1 (2026-02-18)

- `OK` UX/IA target spec added (short + implementable):
  - `docs/26-Mobile-Operator-UX-IA-Spec.md`
- `OK` Login screen IA refreshed (`flutter_controller/lib/screens/login_screen.dart`):
  - sections clarified: brand, restore status, credentials, primary CTA, support hint,
  - explicit empty credential validation before auth call,
  - entitlement error-card next-step guidance retained.
- `OK` Students workspace IA refreshed (`flutter_controller/lib/screens/students_screen.dart`):
  - operator workspace context card (account + role + access mode),
  - stronger `loading` / `empty` / `error` / `offline` states,
  - clearer roster header + tap-to-control affordance on student cards,
  - role guard behavior preserved (`therapist` manage actions only).
- `OK` Control screen IA refreshed (`flutter_controller/lib/screens/control_screen.dart`):
  - session header now exposes compact state pills (link/session/runtime/game/selected readiness),
  - game catalog step now surfaces explicit state banners (offline, active runtime lock, readiness),
  - setup step now surfaces explicit state banners and runtime controls section,
  - setup action bar clarified (`Start new`, `Restart`, `Back`) and save-resume path surfaced via `Resume from saved state` when supported,
  - runtime command guard semantics preserved (no regression in lock rules / command gating).
- `OK` Validation after each major batch (Flutter mobile only):
  - batch A (`login` + `students`):
    - `flutter analyze` PASS -> `docs/evidence/20260218_201049/mobile_operator_ux_iteration1/commands/flutter_controller_flutter_analyze_post_login_students.log`
    - `flutter test` PASS -> `docs/evidence/20260218_201049/mobile_operator_ux_iteration1/commands/flutter_controller_flutter_test_post_login_students.log`
  - batch B (`control`):
    - `flutter analyze` PASS -> `docs/evidence/20260218_201049/mobile_operator_ux_iteration1/commands/flutter_controller_flutter_analyze_post_control_iteration1.log`
    - `flutter test` PASS -> `docs/evidence/20260218_201049/mobile_operator_ux_iteration1/commands/flutter_controller_flutter_test_post_control_iteration1.log`
  - summary note:
    - `docs/evidence/20260218_201049/mobile_operator_ux_iteration1/notes/SUMMARY.md`
- `TODO` Manual operator validation remains intentionally deferred for next lane:
  - no phone + Unity manual pass executed in this iteration.

## 19) Mobile operator UX/IA iteration 2 (2026-02-18)

- `OK` Students screen cleanup (`flutter_controller/lib/screens/students_screen.dart`):
  - logout is now only available on Students,
  - logout now requires therapist confirmation and always navigates to `LoginScreen` via route reset (no black screen fallback),
  - therapist account + role moved into a compact expandable section,
  - roster copy updated from `Student roster` to `Your students`,
  - reconcile action kept but moved to low-noise placement (`Refresh and reconcile` in expandable context and subtle sync icon near roster header).
- `OK` Scanner screen cleanup (`flutter_controller/lib/screens/scanner_screen.dart`):
  - logout action removed.
- `OK` Control UX split into two logical screens (`flutter_controller/lib/screens/control_screen.dart`):
  - Screen A (catalog): game selection + install/update + readiness status only,
  - Screen A hides runtime/session technical diagnostics and no longer shows active unfinished-session banner in main UI,
  - Screen A keeps VR preview as collapsible panel and removes previous large top-gap layout,
  - Screen A adds explicit operator hint for requesting additional licensed games (`Need more games?` + `How to add`),
  - Screen B (game session): dedicated view with preview panel, game settings, and controls (`Start`, `Pause/Resume`, `Restart`, `End Game`),
  - Screen B bottom action changed from student-navigation CTA to `End Session` flow.
- `OK` Runtime safety and command gating preserved:
  - command guard path in `_sendCommand` remains intact (`_requiresSessionDecision`, session-bound critical payloads),
  - active-runtime setup lock behavior remains enforced in settings widgets.
- `OK` Validation after each major mobile batch (`flutter_controller`):
  - batch A (`students` + `scanner`):
    - `flutter analyze` PASS -> `docs/evidence/20260218_205006/mobile_operator_ux_iteration2/commands/flutter_controller_flutter_analyze_post_students_scanner.log`
    - `flutter test` PASS -> `docs/evidence/20260218_205006/mobile_operator_ux_iteration2/commands/flutter_controller_flutter_test_post_students_scanner.log`
  - batch B (`control` split):
    - `flutter analyze` PASS -> `docs/evidence/20260218_205006/mobile_operator_ux_iteration2/commands/flutter_controller_flutter_analyze_post_control_split.log`
    - `flutter test` PASS -> `docs/evidence/20260218_205006/mobile_operator_ux_iteration2/commands/flutter_controller_flutter_test_post_control_split.log`
  - summary note:
    - `docs/evidence/20260218_205006/mobile_operator_ux_iteration2/notes/SUMMARY.md`
- `TODO` Manual phone + Unity validation remains deferred in this iteration by design.

## 20) Mobile media preview toggle hotfix (2026-02-18)

- `OK` Reproduced regression from operator feedback:
  - first expand of VR preview works,
  - collapse + re-expand can produce black preview surface.
- `OK` Root cause identified in mobile widget lifecycle:
  - preview collapse removed `MediaStreamWidget` from tree,
  - widget `dispose()` closed WebRTC peer/signaling state,
  - Unity-side renegotiation was not guaranteed on every re-expand, leaving stale black view.
- `OK` Fix implemented in `flutter_controller/lib/screens/control_screen.dart`:
  - preview panel now keeps `MediaStreamWidget` mounted and hides it via `Offstage` instead of conditional removal,
  - this preserves WebRTC renderer + signaling lifecycle across collapse/expand interactions.
- `OK` Validation rerun (`flutter_controller`):
  - `flutter analyze` PASS -> `docs/evidence/20260218_211250/mobile_operator_ux_iteration2_preview_toggle_fix/commands/flutter_controller_flutter_analyze.log`
  - `flutter test` PASS -> `docs/evidence/20260218_211250/mobile_operator_ux_iteration2_preview_toggle_fix/commands/flutter_controller_flutter_test.log`
  - summary:
    - `docs/evidence/20260218_211250/mobile_operator_ux_iteration2_preview_toggle_fix/notes/SUMMARY.md`
- `TODO` Manual phone + Unity confirmation still required for this specific regression:
  - perform repeated preview expand/collapse cycles in both catalog and game-session screens.
