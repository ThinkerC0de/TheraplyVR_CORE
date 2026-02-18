# Shared Student Access Model (Draft)

Date: 2026-02-18
Status: draft contract + Flutter integration.

## Purpose

- Allow same child/student to be visible on multiple accounts/devices.
- Distinguish ownership from relation-based access.

## Core ownership

- Student owner remains `students/{studentId}.therapistId`.
- Owner account keeps write rights (create/update/delete).

## Relation bindings

Collection: `student_access_bindings`

Fields:
- `studentId`
- `accountId`
- `relationRole` (`THERAPIST`, `PARENT`, `GUARDIAN`, `OBSERVER`)
- `status` (`ACTIVE`, `REVOKED`)
- `grantedBy`
- `grantedAtUtc`
- `expiresAtUtc` (optional)
- `note` (optional)

Binding rule:
- Student appears for account when binding is `ACTIVE` and not expired.

## Flutter behavior

- Student list is merged from:
  - owner scope (`students.where(therapistId == currentUserId)`),
  - shared scope (`student_access_bindings` -> `studentId` -> `students` lookup).
- Shared students are treated as read-only for non-owner accounts.

## Deferred

- Fine-grained permission matrix per `relationRole` (e.g. edit-notes only).
- Backend rules and audit policy for who can grant/revoke.
- Push/event model for low-latency updates to shared students changed by owner.
