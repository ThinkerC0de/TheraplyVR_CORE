# Mobile Real Device Preflight

- generatedUtc: 2026-02-18T17:42:03.3997430Z
- status: PASS
- deviceId: RFCY9019KYF
- packageName: com.yourcompany.flutter_controller
- clearAppData: True

## Command Results

| id | exitCode | log |
| --- | ---: | --- |
| adb_devices | 0 | commands\adb_devices.log |
| device_getprop_model | 0 | commands\device_getprop_model.log |
| device_getprop_brand | 0 | commands\device_getprop_brand.log |
| device_getprop_android_release | 0 | commands\device_getprop_android_release.log |
| device_getprop_android_sdk | 0 | commands\device_getprop_android_sdk.log |
| device_getprop_abi | 0 | commands\device_getprop_abi.log |
| device_utc_date | 0 | commands\device_utc_date.log |
| device_airplane_mode | 0 | commands\device_airplane_mode.log |
| device_wifi_status | 0 | commands\device_wifi_status.log |
| app_package_path | 0 | commands\app_package_path.log |
| app_package_dumpsys | 0 | commands\app_package_dumpsys.log |
| app_clear_data | 0 | commands\app_clear_data.log |

## Real-Device Delta Checklist

- Compare login behavior in debug vs release build (release enforces strict entitlement gate).
- Verify account switch clears previous local session context.
- Force reconnect path (Wi-Fi off/on) and confirm command ACK path recovers deterministically.
- Capture one run with Resume and one run with Start New decision.
- Record account uid/email, student id, session id, game id, and outcome.
