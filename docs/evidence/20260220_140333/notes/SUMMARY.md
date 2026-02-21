# SUMMARY

## Scope
- Reconnect recovery hardening for stale mobile `Connected` state when Unity host link is effectively dead.
- Updated `ConnectionService.sendCriticalCommand(...)` policy:
  - keep full retry behavior for transient delayed ACK scenarios,
  - after retries are exhausted, if final reason indicates transport failure (`ACK_TIMEOUT`, `DISCONNECTED`, etc.), force `disconnect()` to publish `connectionStatus=false` and trigger reconnect path.

## Validation
- `flutter analyze`: PASS
- `flutter test`: PASS
