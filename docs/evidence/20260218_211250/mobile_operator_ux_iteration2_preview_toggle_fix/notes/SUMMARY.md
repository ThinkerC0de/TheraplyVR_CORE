# Media Preview Toggle Hotfix (2026-02-18)

Issue:
- VR preview showed video on first expand.
- After collapsing and expanding again, preview became black.

Root cause:
- `MediaStreamWidget` was removed from widget tree when preview collapsed.
- `dispose()` closed WebRTC peer and signaling subscription.
- Re-expand did not always trigger fresh offer/renegotiation from Unity, leaving black frame.

Fix:
- Keep `MediaStreamWidget` mounted and hide it with `Offstage` instead of removing it.
- This preserves renderer/WebRTC lifecycle across expand/collapse.

Validation:
- PASS `flutter analyze`
- PASS `flutter test`