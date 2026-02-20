# Summary

- Scope: Handoff prompt regression hotfix after operator report.
- Validation:
  - `flutter analyze` PASS (`flutter_analyze.log`)
  - `flutter test` PASS (`flutter_test.log`)

## Fixes

- Cleared stale decision-session pointer after both decision paths (`Continue` and `Start new`).
- Reordered critical session-id resolution for `END_SESSION` to prioritize runtime/session signals over old decision id.
- Limited usage of pending decision session id only while decision gate is active.

## Expected behavioral impact

- `End Session` should no longer target stale pre-decision session ids.
- Completed/ended sessions should stop re-triggering immediate false `Session handoff needed` on next reconnect for the same student.
