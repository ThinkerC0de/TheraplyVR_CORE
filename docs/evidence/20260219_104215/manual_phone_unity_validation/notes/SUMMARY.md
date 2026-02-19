# Manual Phone+Unity Validation Summary (2026-02-19)

Source:
- Operator manual run confirmation from session follow-up on 2026-02-19 (covering checks executed on 2026-02-18 evening).

Validated observations:
1. Active game run (Demo Cube) was started successfully.
2. During active session, phone internet was disabled for ~10 seconds and re-enabled.
3. Mobile control flow reconnected automatically; streaming returned after reconnect.
4. VR preview remained stable after repeated collapse/expand in operator UI.

Notes:
- Unity/Firebase ingest warnings were observed (`SESSION_INGEST_CONNECTION_ERROR` on `http://127.0.0.1:18765/session-ingest`).
- These warnings were treated as non-blocking for reconnect/preview UX validation and should be tracked as separate backend/dev-environment follow-up.