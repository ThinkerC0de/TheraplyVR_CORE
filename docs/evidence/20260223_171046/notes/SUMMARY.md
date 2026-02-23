# SUMMARY

- generatedAtUtc: 2026-02-23T16:14:54Z
- evidenceRoot: docs/evidence/20260223_171046
- scope:
  - post-fix Quest runtime verification for repeated `pulse_target_tap` rounds in one controller flow
  - check for session lock/ownership conflicts and crash signals after session rollover (`COMPLETED -> CREATED`)

## Results

- Quest connectivity: PASS
  - `adb devices` shows active headset (`Quest 3`, serial `2G0YC5ZF8P009T`)
  - app process present and resumed (`com.DefaultCompany.unityquesttemplate`)
- Runtime/session continuity: PASS
  - from `2026-02-23T14:08:00Z` window:
    - `pulseCompletedSessionStopCountWindow=7`
    - repeated `session_stop` with `reason=ACTIVE_GAME_COMPLETED` present for `pulse_target_tap`
- Conflict/crash scan: PASS
  - `conflictReasonHitsInEvents=0` (`SESSION_LOCK_CONFLICT`, `SESSION_OWNERSHIP_CONFLICT`, `SESSION_OWNERSHIP_MISSING`)
  - `crashReportsSinceWindowStart=0`
  - latest crash report on device is older (`2026-02-23T13:50:57.0470070Z`)

## Artifacts

- device checks:
  - `docs/evidence/20260223_171046/commands/adb_devices.log`
  - `docs/evidence/20260223_171046/commands/quest_model.log`
  - `docs/evidence/20260223_171046/commands/quest_serial.log`
  - `docs/evidence/20260223_171046/commands/quest_pid.log`
- pulled runtime data:
  - `docs/evidence/20260223_171046/artifacts/quest_events.ndjson`
  - `docs/evidence/20260223_171046/artifacts/quest_crash_reports.ndjson`
- derived verification reports:
  - `docs/evidence/20260223_171046/artifacts/quest_ops003_followup_report.json`
  - `docs/evidence/20260223_171046/artifacts/quest_ops003_followup_report.txt`
  - `docs/evidence/20260223_171046/artifacts/quest_runtime_domain.log`
  - `docs/evidence/20260223_171046/artifacts/quest_pulse_sequence.log`
  - `docs/evidence/20260223_171046/artifacts/quest_streaming_stats.log`
  - `docs/evidence/20260223_171046/artifacts/quest_conflict_or_crash_hits.log`

## Status

- OPS-003 stability follow-up after rollover fix: DONE (Quest logs clean for the reported regression path)
