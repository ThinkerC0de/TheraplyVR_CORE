[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExportDirectory,
    [string]$HandoffOutputDirectory = "",
    [string]$PackageName = "",
    [string]$OperatorId = "unknown_operator",
    [string]$SourceTracePath = "",
    [string]$SessionId = "",
    [switch]$CreateZip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        throw "Path is required."
    }

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue.Trim())
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $PathValue.Trim()))
}

$exportDir = Resolve-FullPath -PathValue $ExportDirectory
if (-not (Test-Path -Path $exportDir -PathType Container)) {
    throw "Export directory does not exist: $exportDir"
}

if ([string]::IsNullOrWhiteSpace($HandoffOutputDirectory)) {
    $HandoffOutputDirectory = Join-Path $exportDir "handoff"
}
$handoffRoot = Resolve-FullPath -PathValue $HandoffOutputDirectory
New-Item -ItemType Directory -Force -Path $handoffRoot | Out-Null

if ([string]::IsNullOrWhiteSpace($PackageName)) {
    $PackageName = "ops_pretrain_handoff_" + (Get-Date).ToUniversalTime().ToString("yyyyMMdd_HHmmss", [System.Globalization.CultureInfo]::InvariantCulture)
}

$packageDir = Join-Path $handoffRoot $PackageName
if (Test-Path -Path $packageDir) {
    Remove-Item -Path $packageDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

$requiredFiles = @(
    "canonical_events.ndjson",
    "pretrain_manifest.json",
    "operator_report.md"
)

$sourceFiles = @()
foreach ($fileName in $requiredFiles) {
    $sourcePath = Join-Path $exportDir $fileName
    if (-not (Test-Path -Path $sourcePath -PathType Leaf)) {
        throw "Missing required export artifact: $sourcePath"
    }

    $destinationPath = Join-Path $packageDir $fileName
    Copy-Item -Path $sourcePath -Destination $destinationPath -Force
    $sourceFiles += $destinationPath
}

$pretrainManifestPath = Join-Path $packageDir "pretrain_manifest.json"
$pretrainManifestRaw = Get-Content -Path $pretrainManifestPath -Raw -Encoding UTF8
$pretrainManifest = $pretrainManifestRaw | ConvertFrom-Json

$checksumLines = @()
$fileEntries = @()
foreach ($path in $sourceFiles) {
    $hash = (Get-FileHash -Algorithm SHA256 -Path $path).Hash.ToLowerInvariant()
    $name = Split-Path -Leaf $path
    $length = (Get-Item -Path $path).Length
    $checksumLines += "$hash *$name"
    $fileEntries += [PSCustomObject]@{
        name = $name
        sha256 = $hash
        bytes = $length
    }
}

$checksumsPath = Join-Path $packageDir "checksums.sha256"
$checksumLines | Set-Content -Path $checksumsPath -Encoding UTF8

$handoffManifest = [PSCustomObject]@{
    packageSchema = "THERAPLY_PRETRAIN_HANDOFF"
    packageVersion = "2026-02-22"
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o", [System.Globalization.CultureInfo]::InvariantCulture)
    operatorId = if ([string]::IsNullOrWhiteSpace($OperatorId)) { "unknown_operator" } else { $OperatorId.Trim() }
    exportDirectory = $exportDir
    sourceTracePath = if ([string]::IsNullOrWhiteSpace($SourceTracePath)) { "" } else { (Resolve-FullPath -PathValue $SourceTracePath) }
    sessionId = if ([string]::IsNullOrWhiteSpace($SessionId)) { "" } else { $SessionId.Trim() }
    exportName = [string]$pretrainManifest.exportName
    exportMode = [string]$pretrainManifest.exportMode
    readyForTraining = [bool]$pretrainManifest.readyForTraining
    reasonCode = [string]$pretrainManifest.reasonCode
    totalEvents = [int]$pretrainManifest.totalEvents
    uniqueTaskRuns = [int]$pretrainManifest.uniqueTaskRuns
    traceLoad = $pretrainManifest.traceLoad
    intakeAckStatus = "PENDING_OPERATOR_INTAKE_ACK"
    intakeAckFile = "intake_acknowledgement.json"
    intakeAckAtUtc = ""
    intakeAckBy = ""
    intakeTicketId = ""
    ackTrail = @()
    files = $fileEntries
}

$handoffManifestPath = Join-Path $packageDir "handoff_manifest.json"
$handoffManifest | ConvertTo-Json -Depth 8 | Set-Content -Path $handoffManifestPath -Encoding UTF8

$ackTemplatePath = Join-Path $packageDir "intake_ack_template.json"
$ackTemplate = [PSCustomObject]@{
    ackSchema = "THERAPLY_PRETRAIN_INTAKE_ACK"
    ackVersion = "2026-02-22"
    ackStatus = "ACKNOWLEDGED"
    intakeTicketId = "<required>"
    intakeOperatorId = "<required>"
    notes = ""
}
$ackTemplate | ConvertTo-Json -Depth 6 | Set-Content -Path $ackTemplatePath -Encoding UTF8

$checklistPath = Join-Path $packageDir "handoff_sop_checklist.md"
$checklistLines = @(
    "# OPS-002 Pre-Train Handoff Checklist",
    "",
    "- generatedUtc: $($handoffManifest.generatedAtUtc)",
    "- operatorId: $($handoffManifest.operatorId)",
    "- exportName: $($handoffManifest.exportName)",
    "- exportMode: $($handoffManifest.exportMode)",
    "- readyForTraining: $($handoffManifest.readyForTraining)",
    "- reasonCode: $($handoffManifest.reasonCode)",
    "",
    "## Required Files",
    "",
    "- [x] canonical_events.ndjson",
    "- [x] pretrain_manifest.json",
    "- [x] operator_report.md",
    "- [x] checksums.sha256",
    "- [x] handoff_manifest.json",
    "- [x] intake_ack_template.json",
    "- [ ] intake_acknowledgement.json (after intake confirmation)",
    "",
    "## Operator Steps",
    "",
    "1. Verify `readyForTraining=true` in `pretrain_manifest.json` and `handoff_manifest.json`.",
    "2. Verify file checksums against `checksums.sha256` before transfer.",
    "3. Attach package to training intake ticket and include `reasonCode`.",
    "4. Confirm intake ACK via `scripts/ops_pretrain_handoff_ack_confirm.ps1` and commit `intake_acknowledgement.json`.",
    "5. Record handoff acknowledgement in session worklog with package path + intake ticket."
)
$checklistLines | Set-Content -Path $checklistPath -Encoding UTF8

$zipPath = ""
if ($CreateZip.IsPresent) {
    $zipPath = Join-Path $handoffRoot ($PackageName + ".zip")
    if (Test-Path -Path $zipPath -PathType Leaf) {
        Remove-Item -Path $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath -Force
}

Write-Host "Handoff package directory: $packageDir"
Write-Host "Handoff manifest: $handoffManifestPath"
Write-Host "Intake ACK template: $ackTemplatePath"
Write-Host "Checksums file: $checksumsPath"
Write-Host "Checklist: $checklistPath"
if (-not [string]::IsNullOrWhiteSpace($zipPath)) {
    Write-Host "Handoff zip: $zipPath"
}
