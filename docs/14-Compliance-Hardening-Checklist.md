# Compliance Hardening Checklist (Pre-Study)

Date: 2026-02-18  
Status: initial checklist draft (`SEC-002`).

## Scope

Checklist for medical-study rollout hardening:
- access control,
- retention,
- encryption,
- audit trail,
- breach-response posture.

## 1) Access Control

- [ ] Role matrix finalized (`THERAPIST`, `PARENT`, admin/system paths).
- [ ] Least-privilege rules enforced in backend (read/write scope by role).
- [ ] Grant/revoke operations require privileged actor and justification.
- [ ] Session/API access tokens have explicit TTL and rotation policy.
- [ ] Break-glass/emergency access path is logged and reviewed.

## 2) Retention

- [ ] Data classification per payload type (identity, telemetry, session events).
- [ ] Retention windows documented by dataset and legal basis.
- [ ] Automatic deletion/archival jobs implemented and monitored.
- [ ] Retention exceptions process approved (legal/clinical hold).
- [ ] Restore/purge process tested in non-prod.

## 3) Encryption

- [ ] Encryption in transit enforced (TLS policy, certificate lifecycle).
- [ ] Encryption at rest enabled for all data stores/backups.
- [ ] Key management defined (KMS/HSM, rotation cadence, access boundaries).
- [ ] Pseudonymization key references separated from analytics payload path.
- [ ] Secrets handling policy verified (no secrets in logs/build artifacts).

## 4) Audit Trail

- [ ] Audit events defined (entitlement changes, grants, data exports, deletes).
- [ ] Immutable/log-retention path for security-relevant events.
- [ ] Actor identity, timestamp, request correlation ID logged consistently.
- [ ] Alerting thresholds for anomalous access/update patterns.
- [ ] Regular review cadence and owner assigned.

## 5) Breach Response Posture

- [ ] Incident severity matrix and on-call responsibilities documented.
- [ ] Containment playbook for account compromise/data exfiltration.
- [ ] Notification obligations mapped to jurisdictions/contracts.
- [ ] Forensic evidence collection path tested.
- [ ] Post-incident corrective-action workflow defined and tracked.

## Current session coverage

- Runtime telemetry guard for direct identifiers is in place (`SEC-001` partial).
- This document defines the checklist contract only; controls are not fully implemented.
