# Admin Console Web

Date: 2026-02-18  
Status: minimal operator console with role gate + audit writes.

## Purpose

- Provide entitlement/grant operations in browser (Chrome) without depending on mobile UI.
- Keep admin workflow in a separate module from `flutter_controller`.

## Module

- Path: `admin_console_web/`
- Stack: Flutter Web + Firebase Auth + Firestore

## Implemented scope

- Email/password login for operator account.
- Role gate in app for Firebase Auth claim:
  - `role=admin_operator` or
  - `admin_operator=true`.
- Upsert `user_entitlements/{uid}`:
  - role,
  - app license status,
  - perpetual/expiry,
  - `reason`,
  - `correlationId`.
- Create grant in `entitlement_grants`:
  - scope `APP`/`GAME`,
  - gameId (for `GAME`),
  - grant status/perpetual/expiry,
  - optional role override and note,
  - `reason`,
  - `correlationId`.
- Revoke existing grant entry from live list.
- Live snapshot for target UID:
  - entitlement profile,
  - grants list.
- Immutable audit write per operation to `admin_audit_trail`:
  - who (`actorUid`, `actorEmail`, `actorRole`),
  - what (`action`, target collection/doc/user),
  - when (`occurredAtUtc`),
  - why (`reason`),
  - trace (`correlationId`).
- CMS-lite usability helpers for testing:
  - quick actions: `grant app access` / `revoke app access`,
  - `Use my UID` shortcut,
  - automatic fallback `reason` for quick/manual actions if empty.
- Tabbed directory views:
  - `Therapists/Parents` from `user_entitlements`,
  - `Children` from `students`,
  - `Games` from known catalog + GAME grant stats (`entitlement_grants`).
- Games tab can seed Firestore `game_catalog` in one action (`Seed game_catalog`).

## Minimal Firestore Rules

- Source of truth: repo root `firestore.rules`.
- Key policy:
  - write on `user_entitlements` and `entitlement_grants` only for `admin_operator`,
  - mobile users can read own entitlement/grants but cannot write,
  - `admin_audit_trail` is append-only and restricted to operator role,
  - `students` and `student_access_bindings` remain available for signed-in app flows; admin can also read via same policy.
- Deploy example:

```powershell
firebase deploy --only firestore:rules --project <FIREBASE_PROJECT_ID>
```

## Validation (local)

- `admin_console_web`:
  - `flutter analyze` PASS
  - `flutter test` PASS (includes `test/entitlement_admin_service_test.dart`)
- `flutter_controller`:
  - `flutter analyze` PASS
  - `flutter test` PASS (includes `test/entitlement_admin_e2e_smoke_test.dart`)

## Run in Chrome

From repo root:

```powershell
cd admin_console_web
flutter pub get
flutter run -d chrome
```

Default startup:
- If `dart-define` values are omitted, app uses built-in web config for project `theraply-vr-demo`.

Explicit override (other Firebase project):

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

## Hosting setup (Firebase)

- Firebase Hosting is configured in root `firebase.json` with:
  - public dir: `hosting/public`
  - landing page: `hosting/public/index.html` (button to open `/admin/`)
  - admin rewrites: `/admin` and `/admin/**` -> `/admin/index.html`

Build hosting bundle from repo root:

```powershell
.\scripts\build_admin_console_hosting_bundle.ps1
```

If local execution policy blocks script launch, use:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build_admin_console_hosting_bundle.ps1
```

This builds `admin_console_web` with `--base-href /admin/` and copies output into:
- `hosting/public/admin/`

Deploy hosting to a Firebase project:

```powershell
.\scripts\deploy_admin_console_hosting.ps1 -ProjectId <FIREBASE_PROJECT_ID>
```

Deploy without rebuild (use existing `hosting/public/admin` files):

```powershell
.\scripts\deploy_admin_console_hosting.ps1 -ProjectId <FIREBASE_PROJECT_ID> -SkipBundleBuild
```

If local execution policy blocks script launch, use:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\deploy_admin_console_hosting.ps1 -ProjectId <FIREBASE_PROJECT_ID>
```

## Notes

- This is still a minimal operations panel, not a full workflow portal.
- Production hardening still needs:
  - formal approval flow,
  - production claim provisioning policy/runbook,
  - retention/export policy for audit data.
