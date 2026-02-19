# End-session handoff regression fix v2

Timestamp: 20260219_115005

- Scope:
  - Reverted blocking wait-for-terminal-state behavior that prevented return after `End Session`.
  - Kept "recently ended session" suppression but tightened it:
    - short TTL (`20s`) instead of long hold,
    - suppression only when remote evidence is weak (no strong non-terminal state/runtime),
    - strong active-session signals now override suppression and allow handoff prompt.
  - Removed dialog-level suppression shortcut to avoid silently skipping a required handoff decision.

- Changed file:
  - `flutter_controller/lib/screens/control_screen.dart`

- flutter analyze exit code: 0
- flutter test exit code: 0
