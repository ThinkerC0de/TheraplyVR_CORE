# SUMMARY

## Scope
- Fixed socket lifecycle race in mobile reconnect path.
- Root cause from live logs: stale socket `onDone/onError` callbacks could fire after reconnect and close the new active socket, causing immediate reconnect drop.
- `ConnectionService` now guards callbacks with socket identity check (`identical(_socket, socket)`) and ignores stale callbacks.

## Validation
- `flutter analyze`: PASS
- `flutter test`: PASS
