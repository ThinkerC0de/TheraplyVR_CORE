# Error Code Coverage Policy

## Goal
Keep operator-facing failures stable and actionable via `E-xxxx`, without breaking existing wire contracts.

## Current Status (2026-02-22)
- `DONE`: critical command ACK/NACK reasons mapped to `E-1xxx`.
- `DONE`: entitlement/login gate reasons mapped to `E-2xxx`.
- `DONE`: runtime presence/connectivity reasons mapped to `E-21xx`.
- `DONE`: runtime/manual-resync and attach internal failure identifiers mapped to `E-22xx` and `E-23xx`.
- `DONE`: adaptive/sequence/trace pipeline reason codes and workflow decision reason codes mapped to `E-24xx` and `E-25xx`.
- `DONE`: current scoped audit reports zero unmapped `reasonCode` and zero unmapped internal runtime exception identifiers.

## Rule Of Use
1. If a reason can be shown to operator UI, SOP, or evidence package, it must have `E-xxxx`.
2. If a reason is internal-only debug telemetry, mapping is optional.
3. If an internal reason becomes operationally relevant, promote it to catalog immediately.

## Source Of Truth
- `contracts/error_catalog.json`
- Flutter helper mirror: `flutter_controller/lib/models/ops_error_catalog.dart`

## Audit Procedure
Run:

```powershell
pwsh -File scripts/error_catalog_coverage_audit.ps1
```

The audit reports:
- reasonCode literals found in scoped runtime/controller sources,
- codes missing in catalog,
- internal `InvalidOperationException("CODE")` identifiers missing in catalog.

## Change Procedure
1. Add mapping in `contracts/error_catalog.json`.
2. Mirror mapping in `flutter_controller/lib/models/ops_error_catalog.dart`.
3. Extend `flutter_controller/test/ops_error_catalog_test.dart`.
4. Update `docs/31-Error-Code-Catalog.md` coverage if scope changed.
