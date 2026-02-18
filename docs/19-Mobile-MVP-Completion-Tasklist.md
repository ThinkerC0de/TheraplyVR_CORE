# Mobile MVP Completion Tasklist (Flutter Controller)

Date: 2026-02-18  
Goal: deterministic therapist workflow in editor-first testing path.

## A) Login and access reliability

1. Ensure release profile enforces strict gate:
   - `STRICT_ENTITLEMENT_GATE=true`,
   - `ENABLE_DEV_ENTITLEMENT_BOOTSTRAP=false`.
2. Add explicit UI state for entitlement denial reasons:
   - missing profile,
   - inactive app license,
   - backend unavailable (strict mode).
3. Add regression test matrix for login gate:
   - active entitlement,
   - revoked entitlement,
   - active app grant override,
   - missing entitlement in strict mode.

## B) Session flow stability

1. Verify end-to-end `Resume vs Start New` behavior with reconnect interruptions.
2. Add one consolidated session smoke script in docs:
   - connect,
   - start,
   - pause/resume,
   - disconnect/reconnect,
   - stop/end.
3. Add visible session diagnostics in UI:
   - current session id,
   - connection state,
   - remote session state summary.

## C) Data and UX quality

1. Add account switch guard:
   - clear stale local state when user changes.
2. Improve operator clarity:
   - selected student marker,
   - current role indicator from entitlement.
3. Add short copy updates for non-technical testers:
   - error text cleanup,
   - actionable next-step hints.

## D) Definition of done (mobile MVP)

1. `flutter analyze` + `flutter test` green.
2. Manual E2E in editor path passes three times in a row.
3. No ambiguous login/account state for tester.
