# Manual Self-Finish + Unload Validation Summary

Date: <YYYY-MM-DD>  
Operator: <name/alias>  
Environment: <phone model + Unity/Quest mode + network note>

## Scope

Close remaining manual checks from section 17 in:

`docs/08-Session-Resilience-Worklog.md`

## Test A: Self-Finish -> Mobile Terminal State

Status: `PASS` | `FAIL`

Steps executed:
1. Selected game: `<gameId>`
2. Started game from mobile
3. Completed game naturally (self-finish)

Observed mobile behavior:
- <what changed in UI/state>
- <whether manual refresh was needed>

Observed Unity logs:
- <timestamp> <log snippet>
- <timestamp> <log snippet>

## Test B: `Wroc` -> Active Scene Unload

Status: `PASS` | `FAIL`

Steps executed:
1. Started active game
2. Used `Wroc` / Back to return to catalog

Observed mobile behavior:
- <result on mobile>

Observed Unity logs:
- <timestamp> <log snippet showing active game clear>
- <timestamp> <log snippet showing scene unload>

## Overall Result

Overall: `PASS` | `PARTIAL` | `FAIL`

Open issues:
1. <if any>
2. <if any>

Recommended worklog update:
- Section 17: set manual checks to `OK` and attach this evidence path if overall `PASS`.
