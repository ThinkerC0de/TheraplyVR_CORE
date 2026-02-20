# Summary

- Scope: Firestore permissions + handoff false-positive hardening.
- Root cause: `therapy_sessions` collection had no Firestore rules, so mobile session journal writes/reads were denied.
- Fixes:
  - Added `therapy_sessions` (+ nested `events`) access rules for admin/operator and session owner therapist.
  - Deployed rules to Firebase project `theraply-vr-demo`.
  - Removed `sync_pending` from handoff-decision runtime fallback to avoid false gate on reconnect.
  - Aligned regression test expectations for `sync_pending`.

## Validation

- `flutter analyze` PASS (`flutter_analyze.log`)
- `flutter test` PASS (`flutter_test.log`)
- Firestore rules deploy PASS (`firebase/firestore_rules_deploy.log`)
