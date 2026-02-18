# Sprint 1 Closure Checklist (Entitlement Control Plane)

Date: 2026-02-18  
Status: ready for final verification and sign-off.

## Scope

- Firestore role enforcement for entitlement writes.
- Admin web operator panel with audit trail.
- E2E smoke path: admin change -> next controller login decision.

## Completed in codebase

- `firestore.rules` + `firebase.json` added and wired.
- `admin_console_web`:
  - claim gate (`admin_operator`) before dashboard access,
  - required `reason` and `correlationId`,
  - immutable `admin_audit_trail` writes,
  - tabbed operator directory (`Operations`, `Therapists/Parents`, `Children`, `Games`).
- `flutter_controller`:
  - entitlement gate smoke test (`test/entitlement_admin_e2e_smoke_test.dart`),
  - login UX fix: last email remembered, active account visible on `Students`.
- Validation green:
  - `admin_console_web`: `flutter analyze`, `flutter test`,
  - `flutter_controller`: `flutter analyze`, `flutter test`.

## Final sign-off tasks

1. Deploy Firestore rules to target project:
   - `firebase.cmd deploy --only firestore:rules --project theraply-vr-demo`
2. Verify custom claim flow:
   - set claim: `scripts/set_admin_operator_claim.ps1`,
   - verify claim: `scripts/check_admin_operator_claim.ps1`.
3. Run manual smoke in target env:
   - grant access from web panel,
   - controller login should pass,
   - revoke access from web panel,
   - next controller login should be denied.
4. Save evidence in `docs/evidence/` and update `docs/08-Session-Resilience-Worklog.md`.

## Exit criteria

- No operator without `admin_operator` can write entitlement/grant paths.
- Audit event exists for each admin write action.
- Revoke/grant behavior is deterministic on next login.
