# Sprint 1 Closure Evidence (2026-02-18)

## Scope executed

1. Firestore rules deploy to `theraply-vr-demo`.
2. `admin_operator` claim set + verified.
3. Grant/revoke smoke for entitlement gate (next-login decision path).
4. Audit trail contract sample check.

## Tested account

- email: `therapist@test.com`
- uid: `2KE0jTpGIiVfNnbmUKl3epSHWD33`

## Command artifacts

- `commands/firebase_deploy_firestore_rules.log`
- `commands/set_admin_operator_claim.log`
- `commands/check_admin_operator_claim_final.log`
- `commands/manual_smoke_grant_revoke_next_login.log`
- `commands/admin_audit_trail_contract_sample.log`
- `commands/admin_console_web_flutter_analyze.log`
- `commands/admin_console_web_flutter_test.log`
- `commands/flutter_controller_flutter_analyze_pre_mobile.log`
- `commands/flutter_controller_flutter_test_pre_mobile.log`

## Smoke outcome summary

- grant -> next login decision: `APP_LICENSE_ACTIVE` (allow)
- revoke -> next login decision: `APP_LICENSE_INACTIVE` (deny)
- app grant override -> next login decision: `APP_LICENSE_ACTIVE` (allow)

## Notes

- Claim verification run done sequentially (`check_admin_operator_claim_final.log`) to avoid parallel timing race.
- Audit sample confirms required fields are present in `admin_audit_trail` entries (`contract=true`).
