# Evidence Footprint Guardrails

Date baseline: 2026-02-18
Scope: keep evidence in Git useful and auditable without inflating repository history.

## Why this exists

- Evidence logs are required for traceability.
- Binary artifacts (`APK`, validation build outputs, ZIP bundles) inflate local workspace and Git history very quickly.
- We keep textual proof (`SUMMARY`, command logs, notes) in Git; binaries stay local unless explicitly needed.

## Guardrails

1. Default evidence capture should be text-first.
2. Binary artifacts are opt-in (`-IncludeBinaryArtifacts`).
3. Evidence ZIP creation is opt-in (`-IncludeZip`).
4. Run footprint report after each major milestone.
5. Keep only current/recent operator logs in Git when they carry decision evidence.

## Commands

Inspect current footprint:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\report_evidence_footprint.ps1 `
  -TopN 20 `
  -OutputMarkdown docs/evidence/_footprint/latest.md
```

Dry-run cleanup of heavy local artifacts:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\cleanup_evidence_artifacts.ps1
```

Apply cleanup of heavy local artifacts:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\cleanup_evidence_artifacts.ps1 -Apply
```

Apply cleanup including evidence ZIP files:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\cleanup_evidence_artifacts.ps1 `
  -RemoveEvidenceZips `
  -Apply
```

## Collection defaults (updated)

`scripts/collect_validation_evidence.ps1` now defaults to:

- no binary artifacts in evidence output,
- no zip archive.

To explicitly include binaries and zip:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\collect_validation_evidence.ps1 `
  -Preset full `
  -IncludeBinaryArtifacts `
  -IncludeZip
```

