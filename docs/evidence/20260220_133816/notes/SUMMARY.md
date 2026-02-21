# SUMMARY

## Scope
- Reconnect control-gating hotfix in mobile operator flow.
- Root cause: `ControlScreen` auto-reconnect loop pre-set `_isConnected=true` before `connectionStatus` event, which bypassed `wasConnected` transition detection and could skip `SESSION_ATTACH` bootstrap.
- Fix: keep `_isConnected` sourced only from connection stream; reconnect loop now only clears stale attach-ready state and waits for stream event path.

## Validation
- `flutter analyze`: PASS
- `flutter test`: PASS
