# SUMMARY

## Scope
- Mobile reconnect hardening for Unity-network-loss scenario.
- Added discovery-based reconnect fallback in `ControlScreen`:
  - when reconnect to last endpoint fails, app attempts reconnect using latest fresh UDP-discovered candidate matching selected device/student.
  - discovery listener now stores reconnect candidate and can trigger reconnect loop while disconnected.

## Validation
- `flutter analyze`: PASS
- `flutter test`: PASS

## Manual target scenario
- Unity host loses network and returns.
- Mobile should reconnect TCP route using either last endpoint or fresh discovery endpoint, then re-run attach flow and unlock controls.
