# Unity Editor MVP Completion Tasklist

Date: 2026-02-18  
Goal: stable Unity Editor path for therapist-led demo/testing before Quest hardware packaging.

## A) Runtime bootstrap and scene hygiene

1. Freeze canonical demo scene set and bootstrap scripts:
   - `MainScene.unity`,
   - `ExampleCubeScene.unity`,
   - `PulseTargetsScene.unity`,
   - `DemoCubeScene.unity`,
   - `SessionResilienceTest.unity`.
2. Ensure deterministic registration of game modules in Editor startup.
3. Add pre-run cleanup step for stale `.utmp` state to avoid scene recovery prompts.

## B) Command/session parity with mobile controller

1. Verify `START/PAUSE/RESUME/STOP` path for each supported `gameId`.
2. Confirm `Resume` vs `Start New` behavior is deterministic after reconnect.
3. Ensure game setup payload (`cubeCount`, `cubeSpeed`, `levelMode`) is applied and logged in runtime.

## C) Firebase resilience path in Editor

1. Keep network-backed Firebase path enabled in validation scenes (`_simulateFirebase=false`).
2. Validate online -> offline -> reconnect recovery with current `gameId` contract.
3. Document strict expected outcomes for ingest/reconciliation after reconnect.

## D) Editor operator ergonomics

1. Add short run profile for local smoke (`single command` path).
2. Expose minimal runtime diagnostics in scene HUD/log:
   - session id,
   - active game id,
   - backend connectivity state.
3. Keep troubleshooting notes in one place for non-Unity operators.

## E) Definition of done (Unity Editor MVP)

1. Unity validation script passes 3 consecutive runs.
2. Mobile controller can run one full session cycle against Editor without manual code changes.
3. Evidence logs are captured in `docs/evidence/` with timestamped run record.
