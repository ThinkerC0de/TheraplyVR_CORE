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

## Testing mode without admin panel

- Existing legacy fallback still allows login when entitlement backend is missing and strict gate is disabled.
- Optional dev bootstrap path can auto-create missing `user_entitlements/{uid}` document on login:
  - build flag: `ENABLE_DEV_ENTITLEMENT_BOOTSTRAP=true`
  - intended for local/test environments only.

## Operator panel (current)

- Entitlement/grant operator flow is handled in a separate browser module:
  - `admin_console_web/` (Chrome, Firebase-authenticated)
  - details: `docs/17-Admin-Console-Web.md`
- Supported actions:
  - upsert `user_entitlements/{uid}` (role + app license),
  - create grant (`entitlement_grants`),
  - revoke existing grant entries from live list.

## Deferred

- No production-grade admin workflow yet (approval/audit/policy enforcement).
- No backend policy/rules enforcement in this session.
- No revocation propagation SLA (eventing/push) defined yet.
