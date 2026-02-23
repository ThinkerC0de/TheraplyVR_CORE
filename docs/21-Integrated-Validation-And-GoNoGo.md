# Integrated Validation And Go/No-Go

Date: 2026-02-18  
Goal: run one consolidated validation gate after Sprint 1 + mobile/Unity MVP checklists.

## A) Validation order

1. Admin control plane smoke:
   - update entitlement in `admin_console_web`,
   - verify audit entry contract (`reason`, `correlationId`, actor fields),
   - verify revoke/grant effect at next controller login.
2. Mobile MVP smoke:
   - login -> student -> connect -> session start,
   - pause/resume/reconnect,
   - end/keep unfinished path.
3. Unity Editor MVP smoke:
   - command handling for selected game,
   - runtime session continuity after interruption,
   - Firebase reconnect validation.

## B) Evidence package (required)

1. `flutter analyze` + `flutter test` outputs for:
   - `admin_console_web`,
   - `flutter_controller`.
2. Unity validation log with PASS markers.
3. One short operator note with:
   - tested account,
   - tested student,
   - tested game ids,
   - timestamp.

## C) Go/No-Go rubric

1. `GO` if:
   - no P0/P1 defect in access control/session flow,
   - revoke/grant behavior is deterministic on next login,
   - Unity Editor run passes with no manual patching during session.
2. `NO-GO` if any of the following occurs:
   - operator without claim can write entitlement paths,
   - login gate allows revoked user without valid override,
   - session recovery produces inconsistent active session state,
   - Unity validation does not reach PASS in stable network cycle.

## D) Decision outputs

1. If `GO`:
   - freeze Sprint 1,
   - move to production-hardening backlog for mobile + Unity.
2. If `NO-GO`:
   - open issue list with severity and owner,
   - rerun only failed lane first,
   - repeat integrated gate after fixes.

## E) Next checkpoint

1. Prepare final Sprint 1 sign-off note in `docs/08-Session-Resilience-Worklog.md`.
2. Confirm backlog reorder based on validation findings.
3. Start next implementation window with explicit scope freeze.

## F) Unified gate command (current)

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\e2e_unity_flutter_firebase_gate.ps1 `
  -UnityValidationRuns 1 `
  -UnityValidationGameIds demo_cube_clicker
```

This wraps `flutter_controller`, `admin_console_web`, and Unity/Firebase smoke into one repeatable operator lane.
