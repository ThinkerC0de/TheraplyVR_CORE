# Entitlement Grant Path Contract (Draft)

Date: 2026-02-18
Status: draft, Flutter-side contract + Firestore path only.

## Purpose

- Define remote admin/system assignment path for entitlements (`LIC-002`).
- Keep grant assignment separate from login identity credential flow.

## Collections

### `entitlement_grants`

One document per active/revoked grant assignment.

Fields:
- `granteeUserId`
- `scope` (`APP` or `GAME`)
- `gameId` (required for `GAME`)
- `licenseGrant`:
  - `status`
  - `fromUtc`
  - `toUtc`
  - `perpetual`
- `source` (`ADMIN` or `SYSTEM`)
- `assignedBy`
- `assignedAtUtc`
- `revoked`
- `revokedAtUtc`
- `role` (optional role override: `THERAPIST` / `PARENT`)
- `note`

### `entitlement_grant_requests`

One document per request for assignment/revocation workflow.

Fields:
- `requestedForUserId`
- `scope` (`APP` or `GAME`)
- `gameId` (optional)
- `requestedGrant`
- `requestedBy`
- `requestedAtUtc`
- `reason`
- `status` (`PENDING`, `APPROVED`, `REJECTED`, etc.)

## Runtime behavior (Flutter)

- Login gate reads:
  1. `user_entitlements/{uid}` (base access contract),
  2. `entitlement_grants` for `granteeUserId == uid` and `revoked == false`.
- Effective access is resolved as:
  - base access + grant overlay,
  - optional role override from grant,
  - app or game-specific grant activation by validity window.

## Deferred

- No admin UI yet.
- No backend policy/rules enforcement in this session.
- No revocation propagation SLA (eventing/push) defined yet.
