# SUMMARY

## Scope
- RDM-002 implementation: configurable session recovery window with explicit under-window vs over-window decision flow.
- Integration in mobile session gate flow with therapist-configurable values loaded from Firebase (`user_entitlements/{therapistId}`) and safe defaults.

## Code touched
- `flutter_controller/lib/models/therapist_session_settings.dart`
- `flutter_controller/lib/models/session_recovery_manager.dart`
- `flutter_controller/lib/services/therapist_session_settings_service.dart`
- `flutter_controller/lib/screens/control_screen.dart`
- `flutter_controller/test/session_recovery_manager_test.dart`
- `flutter_controller/test/therapist_session_settings_test.dart`

## Validation commands
1. `flutter analyze`
   - log: `docs/evidence/20260221_234630/commands/flutter_controller_flutter_analyze.log`
   - result: PASS (`No issues found`).
2. `flutter test`
   - log: `docs/evidence/20260221_234630/commands/flutter_controller_flutter_test.log`
   - result: PASS (`All tests passed`).

## Notes
- Recovery gate now evaluates `lastConnectionLostAtUtc` against `sessionRecoveryWindowMinutes`:
  - under window -> silent attach (`RECOVERY_UNDER_WINDOW`),
  - over window -> therapist decision dialog (`RECOVERY_OVER_WINDOW`) unless confirmation-after-window is disabled.
- Settings are read-only from Firebase in this step; gear UI editing remains out-of-scope for P0.
