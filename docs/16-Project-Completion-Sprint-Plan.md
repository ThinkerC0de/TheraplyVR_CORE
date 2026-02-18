# Project Completion Sprint Plan

Date: 2026-02-18  
Scope: finish remaining `PARTIAL` backlog items to production-ready state.

## Planning assumptions

- Sprint length: 2 weeks.
- Existing dev/test paths stay available until production replacements are verified.
- Priority order: entitlement safety -> real content lifecycle -> data consistency -> security/compliance -> release hardening.

## Sprint 1: Entitlement Control Plane (Prod-safe access)

Backlog targets:
- `DATA-001`
- `LIC-001`
- `LIC-002` (admin/ops path)

Technical tasks (in order):
1. Define operator workflow for grants/entitlements (CLI/tooling, no full admin portal required at first).
2. Implement and verify Firestore rules for:
   - `user_entitlements`
   - `entitlement_grants`
   - `entitlement_grant_requests`
3. Add backend-side audit fields enforcement (`updatedBy`, `updatedAtUtc`, source).
4. Add integration tests for strict entitlement gate (`STRICT_ENTITLEMENT_GATE=true`) and grant overlay behavior.
5. Lock release profile so dev bootstrap/fallback flags are disabled by default.

Definition of done:
- Production login works without legacy fallback.
- Grant/revoke workflow exists and is auditable.
- Access decisions are reproducible from stored policy records.

## Sprint 2: Real Quest Content Lifecycle

Backlog targets:
- `CAT-001`
- `CAT-002`

Technical tasks (in order):
1. Create runtime content manifest contract (game package metadata, checksum, version, scene/module binding).
2. Implement Quest installer manager:
   - fetch/download
   - verify
   - activate
   - rollback on failure
3. Replace simulator transitions with real lifecycle state updates (`GAME_INSTALL_STATUS`).
4. Add deterministic retry/backoff and operation timeout handling for install/update.
5. Add E2E validation path (mobile command -> Quest install -> game launch gate).

Definition of done:
- Real package install/update/uninstall works from mobile flow.
- Status transitions are deterministic and survive reconnect/restart.
- Flutter launch gating uses real runtime state only.

## Sprint 3: Data Consistency And Role Permissions

Backlog targets:
- `DATA-002`
- `DATA-003`
- `DATA-004`

Technical tasks (in order):
1. Finalize permission matrix for shared student access by role (`THERAPIST`, `PARENT`, etc.).
2. Enforce role write/read policy in backend rules.
3. Implement final reconciliation policy for local-first queue conflicts:
   - merge rules
   - rejection and retry semantics
   - operator-visible conflict markers
4. Tune data sync performance:
   - debounce/cooldown policy
   - Firebase indexes and query shape optimization
5. Add regression tests for concurrent edits + offline/online reconciliation.

Definition of done:
- Shared roster permissions are deterministic and enforced server-side.
- Conflict handling is documented, test-covered, and observable.
- Sync traffic and UI refresh behavior are stable under load.

## Sprint 4: Security + Compliance Hardening

Backlog targets:
- `SEC-001`
- `SEC-002`

Technical tasks (in order):
1. Implement pseudonymization crypto path and key-management integration (KMS-backed key references).
2. Enforce no direct identifiers in telemetry pipeline end-to-end (runtime + backend validation).
3. Implement retention controls and deletion workflows by data class.
4. Implement immutable audit trail for policy/data-sensitive actions.
5. Execute incident/breach-response tabletop and document gaps.

Definition of done:
- Pseudonymization is not just contractual; it is cryptographically enforced.
- Compliance checklist items have implementation evidence.
- Security posture is reviewable for medical-study rollout.

## Sprint 5: Release Hardening And Go-Live

Backlog targets:
- Close residual risk from all prior sprints.

Technical tasks (in order):
1. Build release validation matrix (role/license/content lifecycle/offline/reconnect/chaos paths).
2. Run full cross-platform rehearsal (Flutter + Quest) with evidence capture.
3. Finalize observability and on-call runbooks (alerts, dashboards, incident routing).
4. Freeze feature scope and cut release candidate.
5. Execute staged rollout (internal -> pilot -> wider).

Definition of done:
- Release candidate passes full matrix with no P0/P1 open issues.
- Operational runbooks and evidence are complete.
- Team can support pilot safely.

## Critical dependencies

- Sprint 2 depends on Sprint 1 entitlement policy finalization (license-aware content access).
- Sprint 4 depends on stable payload contracts from Sprints 1-3.
- Sprint 5 depends on completed evidence from Sprints 1-4.

## Immediate next 3 implementation tasks

1. Implement minimal operator tooling for entitlement grant/revoke + audit writes.
2. Add Quest installer manager skeleton with real `INSTALLING -> READY/FAILED` transitions.
3. Add strict-gate integration tests for login under entitlement/grant edge cases.
