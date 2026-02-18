# Admin Console Web

Date: 2026-02-18  
Status: minimal operator console available for browser workflow.

## Purpose

- Provide entitlement/grant operations in browser (Chrome) without depending on mobile UI.
- Keep admin workflow in a separate module from `flutter_controller`.

## Module

- Path: `admin_console_web/`
- Stack: Flutter Web + Firebase Auth + Firestore

## Implemented scope

- Email/password login for operator account.
- Upsert `user_entitlements/{uid}`:
  - role,
  - app license status,
  - perpetual/expiry.
- Create grant in `entitlement_grants`:
  - scope `APP`/`GAME`,
  - gameId (for `GAME`),
  - grant status/perpetual/expiry,
  - optional role override and note.
- Revoke existing grant entry from live list.
- Live snapshot for target UID:
  - entitlement profile,
  - grants list.

## Run in Chrome

From repo root:

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

Minimal required defines:
- `FIREBASE_API_KEY`
- `FIREBASE_APP_ID`
- `FIREBASE_MESSAGING_SENDER_ID`
- `FIREBASE_PROJECT_ID`

## Notes

- This is a minimal operations panel, not a full role-based admin portal.
- Firestore Rules hardening is still required in Sprint 1 for production safety.
