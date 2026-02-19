# Manual Checklist: Self-Finish + Scene Unload Validation

Date: 2026-02-19  
Scope: close remaining manual checks from session/game flow hardening (`docs/08-Session-Resilience-Worklog.md`, section 17).

## Goal

1. Confirm game self-finish (Demo Cube) updates mobile terminal state without manual refresh.
2. Confirm `Wroc` (Back) from game setup/session returns to catalog and unloads active game scene in Unity.

## Preconditions

1. Phone and Unity/Quest are on the same network.
2. Mobile app logs in successfully and reaches student -> scanner -> control flow.
3. Unity runtime scene is running with Demo Cube available.
4. Optional but recommended: keep Unity Console visible with timestamps enabled.

## Test A: Self-Finish -> Mobile Terminal State

1. On mobile, open game catalog and select `Demo Cube Clicker`.
2. Enter game session screen and start game.
3. In Unity/gameplay, finish all cubes naturally (self-finish, no manual STOP).
4. Observe mobile UI state transition:
   - runtime/session state should move to terminal/completed-like state,
   - no manual refresh/reconnect action should be required.
5. Capture evidence:
   - short note with observed mobile behavior,
   - Unity log line(s) showing terminal/game-complete handling.

Expected result:
- PASS if mobile updates terminal state automatically after self-finish.

## Test B: `Wroc` -> Active Scene Unload

1. Start `Demo Cube Clicker` from mobile.
2. While game is active, use `Wroc`/Back from setup/session screen to return to catalog.
3. Observe Unity logs:
   - active game should be cleared,
   - additive game scene should be unloaded.
4. Capture evidence:
   - short note with observed mobile transition,
   - Unity log line(s) from router/runtime confirming unload.

Expected result:
- PASS if return to catalog unloads the active game scene in Unity.

## Evidence Output

Create a new evidence folder for the run, for example:

`docs/evidence/<timestamp>/manual_self_finish_unload/notes/SUMMARY.md`

Use template:

`docs/evidence/_templates/manual_self_finish_unload_summary_template.md`

## Final Pass Criteria

1. Test A PASS.
2. Test B PASS.
3. Worklog section 17 can be moved from `PARTIAL` to `OK` with evidence link.
