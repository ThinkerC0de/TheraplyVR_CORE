# End-session handoff follow-up fix validation

Timestamp: 20260219_113723

- Scope:
  - End session now requires both command send success and terminal-state confirmation before leaving control screen.
  - Added short-lived "recently ended sessions" cache to suppress false handoff prompts immediately after a clean end.
  - Session decision gate now avoids cross-session stale fallback by binding state/runtime fallback only to matching `sessionId`.
  - Active session id auto-attach was limited to remote `CREATED` state only (no terminal-session attach).

- Changed file:
  - `flutter_controller/lib/screens/control_screen.dart`

- flutter analyze exit code: 0
- flutter test exit code: 0
