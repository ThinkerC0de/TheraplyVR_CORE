# Manual Real-Device Smoke Report

- generatedUtc: 2026-02-18T18:51:30Z
- operatorStatus: DONE (reported by operator)
- deviceId: RFCY9019KYF
- deviceModel: SM_S938B
- appPackage: com.yourcompany.flutter_controller
- preflight: PASS (`docs/evidence/20260218_184202/mobile_real_device_preflight/SUMMARY.md`)

## Scope

- login/relogin flow after entitlement operations (manual UI verification by operator)
- connection/reconnect path during active session
- Resume vs Start New session decision behavior

## Evidence captured

- raw logcat: `artifacts/adb_logcat_full.log`
- filtered app/logical events: `artifacts/adb_logcat_filtered.log`
- connection timeline: `artifacts/connection_timeline.log`
- auth timeline: `artifacts/auth_timeline.log`
- package/process snapshot:
  - `commands/adb_dumpsys_package.log`
  - `commands/adb_pidof.log`

## Observations

- Auth path seen in logs with multiple sign-in/sign-out cycles.
- One transient sign-in error exists (`firebase_auth/channel-error` with empty credentials), followed by successful sign-in.
- Connection timeline confirms critical command flow with ACKs for `PAUSE_GAME`, `STOP_GAME`, `START_GAME`, `END_SESSION`.
- Reconnect path observed (`Reconnected on attempt 5`) after disconnect event.
- Operator marked full smoke sequence as completed (`done`).

## Operator-dependent checks

- Grant/revoke + next-login entitlement gate decisions are considered PASS by operator confirmation.
- UI-level entitlement reason codes are not explicitly emitted into logcat in this run.
