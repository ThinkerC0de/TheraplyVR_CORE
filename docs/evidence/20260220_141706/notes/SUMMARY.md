# SUMMARY

## Scope
- UI command serialization hardening for reconnect instability windows.
- `End Game` button now runs via `_runPrimaryAction(...)`, preventing overlap with other critical actions (e.g., `END_SESSION`) and reducing concurrent critical retry races.

## Validation
- `flutter analyze`: PASS
- `flutter test`: PASS
