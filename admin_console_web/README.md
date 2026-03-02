# Admin Console Web

Browser-based entitlement operator console for Theraply.

## Scope

- Firebase email/password login
- Role gate (`role=admin_operator` or `admin_operator=true` claim required)
- Create therapist/parent Firebase Auth account (`email + password`) + upsert `user_entitlements/{generatedUid}`
- Create/revoke `entitlement_grants`
- Mandatory audit context on writes (`reason`, `correlationId`)
- Immutable audit event write (`admin_audit_trail`)
- Live snapshot for selected UID
- Directory tabs for:
  - therapists/parents (`user_entitlements`)
  - children/patients (`students`)
  - known games + GAME grant stats

## Run (Chrome)

```powershell
cd admin_console_web
flutter pub get
flutter run -d chrome
```

Default:
- If no `dart-define` is passed, web app uses built-in demo Firebase config for `theraply-vr-demo`.

Override with explicit project config:

```powershell
cd admin_console_web
flutter pub get
flutter run -d chrome `
  --dart-define=FIREBASE_API_KEY=... `
  --dart-define=FIREBASE_APP_ID=... `
  --dart-define=FIREBASE_MESSAGING_SENDER_ID=... `
  --dart-define=FIREBASE_PROJECT_ID=... `
  --dart-define=FIREBASE_AUTH_DOMAIN=... `
  --dart-define=FIREBASE_STORAGE_BUCKET=...
```

Required:
- `FIREBASE_API_KEY`
- `FIREBASE_APP_ID`
- `FIREBASE_MESSAGING_SENDER_ID`
- `FIREBASE_PROJECT_ID`

## Notes

- This is operator tooling, not a full admin product.
- Minimal Firestore rules are in repo root `firestore.rules`.
