# Pseudonymization Payload Contract (Draft)

Date: 2026-02-18
Status: draft, no cryptography implementation in this session.

## Scope

- Define data contracts for clinical/research mode (`SEC-001`).
- Keep direct identifiers separated from gameplay telemetry.
- Keep Unity<->mobile runtime command flow unchanged.

## Contract A: identity binding (restricted store only)

Purpose: map internal IDs to pseudonymous IDs in restricted backend storage.

Required fields:
- `schemaVersion`
- `tenantId`
- `therapistId`
- `studentId`
- `therapistPseudoId`
- `studentPseudoId`
- `pseudonymKeyRef`
- `algorithmTag`
- `createdAtUtc`
- `expiresAtUtc` (optional)

Rules:
- Never include names in this payload.
- Store in restricted identity store, not in analytics stream.

## Contract B: session telemetry (analytics stream)

Purpose: send gameplay/session analytics without direct patient identifiers.

Required fields:
- `schemaVersion`
- `mode` (`STANDARD` or `CLINICAL_RESEARCH`)
- `tenantId`
- `sessionPseudoId`
- `therapistPseudoId`
- `studentPseudoId`
- `gameId`
- `eventAtUtc`
- `eventType`
- `metrics` (event-specific map)
- `pseudonymKeyRef`
- `algorithmTag`

Rules:
- Must not include: `firstName`, `lastName`, `email`.
- Internal IDs (`studentId`, `therapistId`) are not allowed in telemetry payload.
- Pseudonym key reference is metadata only, not secret material.

## Implemented artifacts

- Flutter contract models:
  - `flutter_controller/lib/models/pseudonymization_contract.dart`
- Included checks:
  - serialization/deserialization,
  - simple direct-identifier guard (`containsDirectIdentifiers`),
  - runtime dispatch guard that blocks forbidden PII keys/values before telemetry write (`flutter_controller/lib/services/telemetry_privacy_guard.dart`).

## Deferred

- No hash/HMAC/key-derivation implementation yet.
- No KMS/HSM integration.
- No backend enforcement policy yet (schema validation/audit hooks).
