# SUMMARY

## Scope
- Hotfix for `SESSION_ATTACH_ACTIVE_GAME_CONFLICT` surfaced in live phone+Unity run.
- Mobile no longer forces attach to persisted unfinished session before therapist handoff decision.
- Attach target now prefers latest runtime-reported session ids.
- Unity now keeps current active-game session on attach conflict and returns without handler exception.

## Validation
- `flutter analyze`: PASS
- `flutter test`: PASS
