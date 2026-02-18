# Dev Testing Without Admin Panel Or Downloadable Bundles

Date: 2026-02-18  
Status: active development testing guide.

## Problem

- No admin panel for managing Firebase entitlements/grants.
- No production content pipeline for downloadable Quest bundles yet.

## Goal

Keep product work moving with end-to-end testability.

## Entitlement testing path (no admin UI)

Option A (default):
- Keep `STRICT_ENTITLEMENT_GATE=false`.
- Login uses legacy fallback when entitlement backend/profile is missing.

Option B (explicit dev bootstrap):
- Build Flutter with `ENABLE_DEV_ENTITLEMENT_BOOTSTRAP=true`.
- On first login, app creates missing `user_entitlements/{uid}` with therapist active license profile.

## Content delivery testing path (no real bundles)

- Quest runtime includes dev simulator for:
  - `SYNC_CATALOG`
  - `INSTALL_GAME`
  - `UNINSTALL_GAME`
  - `GAME_INSTALL_STATUS`
- Simulator publishes deterministic status transitions:
  - `NOT_INSTALLED` -> `INSTALLING` -> `READY`
  - `UNINSTALL_GAME` -> `NOT_INSTALLED`
- Flutter catalog/setup uses these states for launch gating.

## Recommended smoke test

1. Login on mobile (with fallback or dev bootstrap enabled).
2. Open control screen and tap `Sync` in game catalog.
3. Verify `GAME_INSTALL_STATUS` chips are visible per game.
4. For a game in `NOT_INSTALLED` or `UPDATE_REQUIRED`, run `Install/Update`.
5. Verify transition to `INSTALLING`, then `READY`.
6. Enter setup and start game only when state is `READY`.

## Deferred for production

- Admin UI + backend authorization workflows.
- Real package distribution/install/rollback pipeline.
- Security and compliance evidence for medical-study rollout.
