# Admin Console Web

Browser-based entitlement operator console for Theraply.

## Scope

- Firebase email/password login
- Upsert `user_entitlements/{uid}`
- Create/revoke `entitlement_grants`
- Live snapshot for selected UID

## Run (Chrome)

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
- Firestore Rules hardening is still required before production rollout.
