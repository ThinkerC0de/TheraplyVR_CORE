# AI Scaffold Workflow

This workflow turns a therapist idea into a mini-game scaffold without changing core code.

## Input Template (Therapist Idea)

Prepare a normalized spec (JSON/YAML) with:
- therapeutic goal,
- target user group,
- game loop,
- session duration,
- difficulty model,
- success/failure conditions,
- telemetry requirements,
- safety/accessibility constraints.

## Generation Rules

AI generation must:
1. implement `IMiniGameModule`,
2. use `IMiniGameContext` services only,
3. subscribe to typed commands only,
4. emit required telemetry events,
5. produce `IMiniGameResult`.

AI must not:
- change core contracts,
- add special-case logic to core systems,
- bypass telemetry standard.

## Expected Output

For each generated game:
- game folder in `_YourGames/<GameName>/`
- `<GameName>Config` implementing `IMiniGameConfig`
- `<GameName>Module` implementing `IMiniGameModule`
- command binding class
- telemetry mapping doc
- setup checklist

## Human Review Gate

Before merge, verify:
1. contract compliance,
2. lifecycle compliance,
3. telemetry completeness,
4. performance baseline,
5. no core code modifications.

## Suggested Prompt Skeleton

Use this structure in your AI prompt:
1. project architecture summary,
2. contract file references,
3. therapist idea spec,
4. output constraints,
5. required files list,
6. acceptance criteria.