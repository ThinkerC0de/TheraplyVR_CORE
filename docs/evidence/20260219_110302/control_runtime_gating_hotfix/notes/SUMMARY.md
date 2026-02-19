# Control Runtime Gating Hotfix Summary (2026-02-19)

Input from manual run:
- In Demo Cube session screen, `Start` stayed active after game start.
- `Pause`, `Restart`, and `End Game` remained inactive.
- Repeated `Start` taps could restart game repeatedly.
- `Back` returned to game catalog on mobile.
- Unity logs provided did not include explicit additive-scene unload markers in that run.
- Firebase ingest warnings were present (`SESSION_INGEST_CONNECTION_ERROR` on `http://127.0.0.1:18765/session-ingest`).

Applied fix (mobile):
- Added optimistic runtime-active lock in `control_screen.dart` to avoid control drift when runtime/session signals lag or are inconsistent.
- `_isGameRuntimeActive` now also uses heartbeat activeGame inference and optimistic runtime state.
- Runtime state updates now clear/set optimistic lock consistently.
- Critical command sender now returns success/failure and updates optimistic runtime lock on start/resume/stop/end.
- Restart flow now requires successful STOP before issuing new START.

Validation:
- PASS `flutter analyze`
- PASS `flutter test`

Follow-up required:
- Manual retest on phone+Unity to confirm:
  1) `Start` disables immediately after start and runtime controls become available,
  2) self-finish and scene unload checks from section 17 are explicitly captured with Unity log lines.