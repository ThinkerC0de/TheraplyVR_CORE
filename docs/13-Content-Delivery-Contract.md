# Content Delivery Contract (Draft)

Date: 2026-02-18  
Status: draft contract + Flutter orchestration + Quest dev simulator (`CAT-001`, `CAT-002`).

## Purpose

- Define shared wire contract for content ownership/install state.
- Keep Flutter game launch deterministic based on content runtime state.
- Preserve existing Unity runtime session flow (no breaking change in critical commands).

## Runtime status enum

`runtimeStatus` must use one of:
- `NOT_INSTALLED`
- `INSTALLING`
- `READY`
- `UPDATE_REQUIRED`
- `FAILED`

## Purchased content state payload

Canonical payload (`GAME_INSTALL_STATUS`) fields:
- `gameId`
- `owned`
- `installedVersion` (nullable)
- `targetVersion`
- `updateRequired`
- `updateOptional`
- `runtimeStatus`
- `lastError` (nullable)
- `updatedAtUtc`

## Firebase game catalog source (`game_catalog`)

Mobile store/installed tabs can be sourced from Firestore collection `game_catalog`.

Recommended document fields:
- `gameId`
- `title`
- `description`
- `targetContentVersion`
- `packageUri`
- `thumbnailUrl`
- `supportsSaveResume`
- `availableForPurchase`
- `requiresExplicitLicense`
- `runtimeLaunchEnabled`
- `sortOrder`
- `active`
- `previewLines`

## Mobile -> Quest command payloads

### `SYNC_CATALOG`

Fields:
- `actorId`
- `role` (default `THERAPIST`)
- `issuedAtUtc`

### `INSTALL_GAME`

Fields:
- `actorId`
- `gameId`
- `targetVersion`
- `issuedAtUtc`

### `UNINSTALL_GAME`

Fields:
- `actorId`
- `gameId`
- `issuedAtUtc`

## Flutter behavior in this session

- Local purchased-content map per game is tracked in `ControlScreen`.
- UI exposes:
  - state chips (`runtime`, ownership, installed->target version),
  - actions (`Sync`, `Install/Update`, `Uninstall`),
  - launch gating (game setup/start blocked when state is not launchable and until Quest publishes at least one `GAME_INSTALL_STATUS` for selected game).
- Incoming `GAME_INSTALL_STATUS` updates local state and clears in-flight operation markers.

## Implemented artifacts

- Model + signal parser:
  - `flutter_controller/lib/models/content_delivery_contract.dart`
- Flutter UI flow:
  - `flutter_controller/lib/screens/control_screen.dart`
- Quest dev simulator handlers:
  - `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/GameRuntimeService.cs`
  - `unity-quest-template/Assets/_TheraplyCore/Games/Contracts/GameCommands.cs`
  - `unity-quest-template/Assets/_TheraplyCore/Games/Runtime/GameCommandBus.cs`

## Testing mode without downloadable bundles

- Quest simulator responds to:
  - `SYNC_CATALOG` (publishes full `GAME_INSTALL_STATUS` snapshot),
  - `INSTALL_GAME` (state `INSTALLING` -> `READY` after short delay),
  - `UNINSTALL_GAME` (state -> `NOT_INSTALLED`).
- Simulator state persistence:
  - runtime writes lifecycle snapshot to `Application.persistentDataPath/session_resilience/content_delivery_state.json`,
  - startup reloads the snapshot so status is deterministic after reconnect/restart.
- This mode enables end-to-end mobile<->Quest contract testing even when real package download/install pipeline is not implemented.

## Deferred

- Quest-side production installer lifecycle implementation (download/verify/activate/rollback).
- Backend policy for ownership/license-authorized install requests.
- Retry/backoff + operation timeout state machine for long installs.
