# Session Data Contract (Draft v1)

Date: 2026-02-19  
Scope: unify session/game lifecycle across mobile controller, Unity runtime, and Firebase storage.

## 1) Domain model

### Session

- One session belongs to one student.
- A session can contain many game attempts.
- Session identity: `sessionId`.

Required fields:

- `sessionId`
- `studentId`
- `therapistId`
- `state` (`CREATED`, `IN_PROGRESS`, `PAUSED`, `INTERRUPTED`, `COMPLETED`, `ABORTED_BY_THERAPIST`, `FAILED_TECHNICAL`)
- `unfinished` (derived flag, for decision gate)
- `isTerminal` (derived flag)
- `latestGameId`
- `reasonCode`
- `createdAtUtc`, `updatedAtUtc`, optional `endedAtUtc`

### Game attempt

- Every game launch inside a session is a separate attempt.
- Recommended lifecycle:
  - `STARTED`
  - `PAUSED`
  - `COMPLETED`
  - `ABORTED`
  - `FAILED`

Important rule:

- conscious therapist exit from game is not "game not chosen"; it is `ABORTED` with reason (for example `THERAPIST_EXIT`).

### Event stream

- Session timeline is append-only.
- Store operator + runtime actions as normalized events with timestamps.

Minimum event set:

- `SESSION_STARTED`
- `SESSION_RESUMED`
- `SESSION_ENDED`
- `GAME_SELECTED`
- `GAME_STARTED`
- `GAME_PAUSED`
- `GAME_RESUMED`
- `GAME_ENDED`
- `CONNECTION_LOST`
- `CONNECTION_RESTORED`
- `RUNTIME_SESSION_STATE_UPDATE`
- `ERROR_OCCURRED`

## 2) Storage layout (Firestore)

- `therapy_sessions/{sessionId}`
- `therapy_sessions/{sessionId}/events/{eventId}`

Notes:

- `therapy_sessions` is the source of truth for handoff decision (`continue` vs `finish`).
- Runtime heartbeat/status remains a live operational signal, not a primary decision source.

## 3) Decision gate rules

When opening control for a student:

1. Read latest session from `therapy_sessions` for the student.
2. If latest session is non-terminal and requires handoff -> show prompt:
   - `Continue`
   - `Finish and start new`
3. If latest session is terminal -> no handoff prompt.
4. Runtime-only signal can trigger fallback gate only when no trusted data snapshot is available.

## 4) Interaction logging policy

- Do not log noisy UI adjustments (for example every slider movement).
- On `START_GAME`, store final game config snapshot used for launch.
- Track therapist/device/runtime errors as events with reason codes.

## 5) Current implementation scope (Pack A)

- Added mobile-side session source-of-truth integration via `therapy_sessions`.
- Added handoff gating preference for persisted session snapshot (with runtime fallback).
- Added session state/event persistence hooks for critical commands and runtime state updates.

## 6) Pack B target

- Unity self-finish must emit terminal game/session state reliably.
- Mobile should auto-reflect terminal game end (`End Game` button state sync).
- Full game-attempt persistence and replay/resume detail policy.
