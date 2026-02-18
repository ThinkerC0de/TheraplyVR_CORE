# Mobile Real-Device Login/Reconnect Delta Checklist

Date baseline: 2026-02-18
Scope: detect environment-specific differences between local/debug lane and physical Android device lane.

## Known delta sources

1. `release` build enforces strict entitlement gate in `EntitlementService`.
2. Real-device network transitions (Wi-Fi off/on, captive network) are noisier than editor-local tests.
3. Device time skew can affect token/entitlement validity windows.
4. Cached app state across account switch can hide regressions if app data is not reset.

## Preflight command

Run before manual smoke:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\mobile_real_device_preflight.ps1
```

Optional hard reset of app state:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\mobile_real_device_preflight.ps1 -ClearAppData
```

Summary output is generated under:

- `docs/evidence/<timestamp>/mobile_real_device_preflight/SUMMARY.md`

## Manual smoke matrix

1. **Login entitlement gate**
- Case A: active APP entitlement -> expect login allow (`APP_LICENSE_ACTIVE`).
- Case B: revoked APP entitlement -> expect explicit deny (`APP_LICENSE_INACTIVE`).
- Case C: backend unavailable in strict lane -> expect explicit deny (`ENTITLEMENT_BACKEND_UNAVAILABLE`).

2. **Account switch isolation**
- Login account A, then sign out.
- Login account B.
- Confirm selected student/session context does not leak from account A.

3. **Reconnect robustness**
- Start session, send `PAUSE` and `RESUME`.
- Toggle Wi-Fi off/on.
- Confirm reconnect and deterministic ACK path for critical commands.

4. **Session decision gate**
- Run once with `Resume`.
- Run once with `Start New`.
- Confirm no duplicate active session remains.

## Evidence minimum for each run

- device id + model + Android API
- app build type (`debug` or `release`)
- test account uid/email
- student id
- session id + selected game id
- result (`PASS`/`FAIL`) and one-line reason

