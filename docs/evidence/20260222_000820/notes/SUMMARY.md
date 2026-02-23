# RDM-002 configurable recovery window closure evidence

Date: 2026-02-22
Scope:
- Finalize `SessionRecoveryManager` behavior for formal `UNDER_WINDOW`/`OVER_WINDOW` recovery decisions.
- Enforce documented recovery-window bounds (`5..240`) in manager evaluation.
- Handle clock-skew edge case (future `lastConnectionLostAtUtc`) as zero-age under-window recovery.
- Expand test coverage for no-decision path and boundary conditions.

Validation:
- `flutter analyze` -> PASS (`commands/flutter_controller_flutter_analyze.log`)
- `flutter test` -> PASS (`commands/flutter_controller_flutter_test.log`)
- `powershell -ExecutionPolicy Bypass -File .\\scripts\\unity_cli_validate.ps1 -Mode compile` -> PASS (`commands/unity_cli_validate_compile.log`)

Changed files:
- `flutter_controller/lib/models/session_recovery_manager.dart`
- `flutter_controller/test/session_recovery_manager_test.dart`
