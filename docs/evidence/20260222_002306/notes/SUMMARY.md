# RDM-003 interrupted session auto-close evidence

Date: 2026-02-22
Scope:
- Add durable interrupted timestamp persistence (`interruptedAtUtc` / `interruptedAtUnixMs`) for session records.
- Add interrupted-session auto-close policy evaluation based on therapist settings:
  - `interruptedSessionAutoCloseHours`
  - `autoCloseInterruptedSessionsEnabled`
- Auto-close stale `INTERRUPTED` sessions on persisted snapshot refresh (startup/reconnect path) and write audit `SessionEvent`.
- Preserve ownership constraints while auto-closing via existing owner-scoped journal writes.
- Add regression tests for auto-close decision logic and legacy interrupted timestamp fallback.

Validation:
- `dart format` -> PASS (`commands/dart_format.log`)
- `flutter analyze` -> PASS (`commands/flutter_controller_flutter_analyze.log`)
- `flutter test` -> PASS (`commands/flutter_controller_flutter_test.log`)

Changed files:
- `flutter_controller/lib/services/session_journal_service.dart`
- `flutter_controller/lib/models/therapy_session_record.dart`
- `flutter_controller/lib/models/session_recovery_manager.dart`
- `flutter_controller/lib/screens/control_screen.dart`
- `flutter_controller/test/session_recovery_manager_test.dart`
- `flutter_controller/test/therapy_session_record_test.dart`
