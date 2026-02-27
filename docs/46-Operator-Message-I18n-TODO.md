# Operator Message i18n TODO

Date: 2026-02-27
Status: TODO (non-blocking for current resilience rollout)

## Scope

- Localize therapist/operator-facing messages (alerts, dialogs, timeline labels, recovery prompts).
- Keep canonical reason codes unchanged (`reasonCode` remains wire/runtime source of truth).
- Keep presentation mapping (`displayCode` with `E/W/I`) separate from message translation.

## Translation Source Contract

- Source format: CSV.
- Header format: `key,pl,en,...`.
- Each row represents one message key.
- `key` must be stable and unique.
- `pl` is required baseline locale for current operator UX.
- Additional locale columns are optional and can be expanded over time.

## Runtime Asset Generation

- Add generator step from CSV to runtime assets (for example locale JSON files).
- Output must be deterministic and reproducible from the same CSV input.
- Runtime must support fallback chain:
  - requested locale,
  - baseline locale (`pl`),
  - message key literal as last resort.

## Validation TODO

- Fail build/validation on:
  - duplicate keys,
  - missing `key`,
  - missing baseline locale value (`pl`) for any row,
  - malformed CSV header contract.
- Report missing translations for non-baseline locales as warnings.
