# Session Resilience Prompting Guide

## Goal
Provide a reliable way to continue work after chat reset, token limits, or workspace restart.

Use this guide together with:
- `docs/06-Session-Resilience-Roadmap.md`
- `docs/08-Session-Resilience-Worklog.md`

## Golden Rule
Every time a work block ends, update the worklog first.

That makes resume deterministic.

## Minimal Resume Prompt (copy/paste)
Use this when chat context is gone:

```text
Resume work from docs/08-Session-Resilience-Worklog.md and docs/06-Session-Resilience-Roadmap.md.
Project main is C:\Users\licen\Projects\theraply-vr-framework.
Reference projects are read-only:
- C:\Users\licen\Focus&Calm_old\Focus&Calm
- C:\Users\licen\Theraply_Playground_old_1
Do not edit reference projects.
Implement the next item with status TODO in Phase P0.
After changes: run available validation, then update worklog.
```

## Resume Prompt with Explicit Item ID
Use when you want one exact task:

```text
Continue Session Resilience roadmap.
Implement item R-P0-00X from docs/08-Session-Resilience-Worklog.md.
Follow constraints in docs/06-Session-Resilience-Roadmap.md.
Update docs/08-Session-Resilience-Worklog.md when done.
```

## Audit Prompt (status only, no code)
Use for planning checkpoints:

```text
Audit current progress for Session Resilience.
Read docs/06-Session-Resilience-Roadmap.md and docs/08-Session-Resilience-Worklog.md.
Report:
1) done items,
2) risks,
3) next highest-priority task.
Do not edit code.
```

## Incident Analysis Prompt
Use when new therapist issues are reported:

```text
Analyze these new field issues against Session Resilience roadmap.
Classify each issue:
- already covered by an existing roadmap item, or
- missing (create new item proposal).
Then update docs/08-Session-Resilience-Worklog.md backlog section.
```

## What To Include In Any Resume Prompt
1. Main project path.
2. Reference project paths and read-only rule.
3. Target roadmap phase/item.
4. Required output (`code`, `docs`, or `analysis only`).
5. Requirement to update worklog before finishing.

## Optional: Fast Context Block
Add this block if needed:

```text
Current branch: <branch-name>
Last completed item: <item-id>
Current blocker: <short blocker>
Expected next step: <item-id>
```

## Token Limit Survival Pattern
When context is getting long:
1. Stop coding at a stable checkpoint.
2. Update worklog item status and notes.
3. Add next action in worklog.
4. Start new chat with Minimal Resume Prompt.

If this is followed, continuity is preserved even with zero chat history.

