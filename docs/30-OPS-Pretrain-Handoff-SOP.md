# OPS-002/OPS-003 Pre-Train Handoff SOP

## Cel
Zapewnic powtarzalny, operatorski handoff paczki pre-train z:
- artefaktami eksportu datasetu,
- kontrola integralnosci (`sha256`),
- jawnym manifestem handoff do intake treningu.

## Wejscie
Wymagane artefakty eksportu w jednym katalogu:
- `canonical_events.ndjson`
- `pretrain_manifest.json`
- `operator_report.md`

## Krok 0: Auto-collect trace input (OPS-003)

### A) Quest autodiscovery (ADB)
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\ops_dataset_trace_collect.ps1 `
  -OutputDirectory .\docs\evidence\<timestamp>\artifacts\ops_trace_collect `
  -AllowNoDatasetEvents
```

Wynik:
- raport: `...\reports\trace_discovery_report.json`,
- gotowe pola wejscia do lane (`selection.recommendedTraceInputPath`, `selection.recommendedTraceSessionId`),
- status gotowosci: `selection.readyForOpsTraceValidation`.

### B) Dowolny lokalny trace path (fallback)
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\ops_dataset_trace_collect.ps1 `
  -OutputDirectory .\docs\evidence\<timestamp>\artifacts\ops_trace_collect_local `
  -SkipQuestPull `
  -LocalTracePaths C:\path\to\trace.ndjson
```

Uzyj `recommendedTraceInputPath` i `recommendedTraceSessionId` z raportu w kroku 1B.

## Krok 1: Walidacja lane OPS-002 (trace -> export)

### A) Fixture trace (domyslnie)
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\unity_ops_dataset_trace_export_validate.ps1 `
  -ExportOutputDirectory .\docs\evidence\<timestamp>\artifacts\ops_dataset_trace_export `
  -ExportName ops002_pretrain_export
```

### B) Realny trace sesji (durable NDJSON)
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\unity_ops_dataset_trace_export_validate.ps1 `
  -ExportOutputDirectory .\docs\evidence\<timestamp>\artifacts\ops_dataset_trace_export `
  -ExportName ops002_pretrain_export `
  -TraceInputPath C:\path\to\session_trace.ndjson `
  -TraceSessionId <session_id_optional> `
  -RequireProvidedTrace
```

## Krok 2: Zbudowanie paczki handoff
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\ops_pretrain_handoff_package.ps1 `
  -ExportDirectory .\docs\evidence\<timestamp>\artifacts\ops_dataset_trace_export `
  -HandoffOutputDirectory .\docs\evidence\<timestamp>\artifacts\ops_dataset_trace_export\handoff `
  -PackageName ops002_pretrain_handoff `
  -OperatorId <operator_id> `
  -SourceTracePath C:\path\to\session_trace.ndjson `
  -SessionId <session_id_optional> `
  -CreateZip
```

## Krok 3: Operator checklist przed handoff
1. Potwierdz `readyForTraining=true` w `pretrain_manifest.json`.
2. Potwierdz `readyForTraining=true` w `handoff_manifest.json`.
3. Zweryfikuj sumy z `checksums.sha256`.
4. Dolacz paczke do intake treningu i wpisz `reasonCode`.
5. Zapisz odwolanie do paczki w `docs/08-Session-Resilience-Worklog.md`.

## Krok 4: Potwierdzenie intake ACK (OPS-003)
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\ops_pretrain_handoff_ack_confirm.ps1 `
  -HandoffPackageDirectory .\docs\evidence\<timestamp>\artifacts\ops_dataset_trace_export\handoff\ops002_pretrain_handoff `
  -IntakeTicketId <ticket_id> `
  -IntakeOperatorId <intake_operator_id> `
  -AckStatus ACKNOWLEDGED `
  -AckNotes "Ready for model intake"
```

Po wykonaniu:
- `handoff_manifest.json` ma wypelnione pola `intakeAck*` i wpis w `ackTrail`,
- paczka zawiera `intake_acknowledgement.json` i `intake_acknowledgement.md`.

## Wyjscie
Katalog paczki handoff zawiera:
- `canonical_events.ndjson`
- `pretrain_manifest.json`
- `operator_report.md`
- `checksums.sha256`
- `handoff_manifest.json`
- `handoff_sop_checklist.md`
- `intake_ack_template.json`
- `intake_acknowledgement.json` (po kroku 4)
- `intake_acknowledgement.md` (po kroku 4)
- opcjonalnie zip (`-CreateZip`)
