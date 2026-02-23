[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$HandoffPackageDirectory,
    [Parameter(Mandatory = $true)]
    [string]$IntakeTicketId,
    [Parameter(Mandatory = $true)]
    [string]$IntakeOperatorId,
    [ValidateSet("ACKNOWLEDGED", "REJECTED", "NEEDS_INFO")]
    [string]$AckStatus = "ACKNOWLEDGED",
    [string]$AckNotes = "",
    [string]$AckOutputName = "intake_acknowledgement.json"
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

$packageDirectory = Resolve-FullPath -PathValue $HandoffPackageDirectory
if (-not (Test-Path -Path $packageDirectory -PathType Container)) {
    throw "Handoff package directory does not exist: $packageDirectory"
}

$handoffManifestPath = Join-Path $packageDirectory "handoff_manifest.json"
if (-not (Test-Path -Path $handoffManifestPath -PathType Leaf)) {
    throw "Missing handoff manifest: $handoffManifestPath"
}

$safeTicketId = $IntakeTicketId.Trim()
if ([string]::IsNullOrWhiteSpace($safeTicketId)) {
    throw "IntakeTicketId cannot be empty."
}

$safeOperatorId = $IntakeOperatorId.Trim()
if ([string]::IsNullOrWhiteSpace($safeOperatorId)) {
    throw "IntakeOperatorId cannot be empty."
}

$handoffManifestRaw = Get-Content -Path $handoffManifestPath -Raw -Encoding UTF8
$handoffManifest = $handoffManifestRaw | ConvertFrom-Json
if ($null -eq $handoffManifest) {
    throw "Failed to deserialize handoff manifest: $handoffManifestPath"
}

$readyForTraining = $false
if ($null -ne $handoffManifest.readyForTraining) {
    $readyForTraining = [bool]$handoffManifest.readyForTraining
}

if ($AckStatus -eq "ACKNOWLEDGED" -and -not $readyForTraining) {
    throw "Cannot acknowledge intake because handoff manifest has readyForTraining=false."
}

$checksumsPath = Join-Path $packageDirectory "checksums.sha256"
$checksumsSha256 = ""
if (Test-Path -Path $checksumsPath -PathType Leaf) {
    $checksumsSha256 = (Get-FileHash -Algorithm SHA256 -Path $checksumsPath).Hash.ToLowerInvariant()
}

$ackUtc = (Get-Date).ToUniversalTime().ToString("o", [System.Globalization.CultureInfo]::InvariantCulture)
$ackId = [Guid]::NewGuid().ToString()
$ackOutputPath = Join-Path $packageDirectory $AckOutputName
$ackOutputFileName = Split-Path -Leaf $ackOutputPath
$manifestHashBefore = (Get-FileHash -Algorithm SHA256 -Path $handoffManifestPath).Hash.ToLowerInvariant()

$ackRecord = [PSCustomObject]@{
    ackSchema = "THERAPLY_PRETRAIN_INTAKE_ACK"
    ackVersion = "2026-02-22"
    ackId = $ackId
    ackedAtUtc = $ackUtc
    ackStatus = $AckStatus
    intakeTicketId = $safeTicketId
    intakeOperatorId = $safeOperatorId
    notes = if ([string]::IsNullOrWhiteSpace($AckNotes)) { "" } else { $AckNotes.Trim() }
    packageDirectory = $packageDirectory
    packageName = Split-Path -Leaf $packageDirectory
    exportName = [string]$handoffManifest.exportName
    exportMode = [string]$handoffManifest.exportMode
    sessionId = [string]$handoffManifest.sessionId
    sourceTracePath = [string]$handoffManifest.sourceTracePath
    readyForTraining = $readyForTraining
    reasonCode = [string]$handoffManifest.reasonCode
    checksumsSha256 = $checksumsSha256
    handoffManifestSha256Before = $manifestHashBefore
}

$ackTrail = @()
if ($null -ne $handoffManifest.ackTrail) {
    foreach ($entry in @($handoffManifest.ackTrail)) {
        if ($null -ne $entry) {
            $ackTrail += $entry
        }
    }
}

$ackTrail += [PSCustomObject]@{
    ackId = $ackId
    ackedAtUtc = $ackUtc
    ackStatus = $AckStatus
    intakeTicketId = $safeTicketId
    intakeOperatorId = $safeOperatorId
    ackFile = $ackOutputFileName
    notes = if ([string]::IsNullOrWhiteSpace($AckNotes)) { "" } else { $AckNotes.Trim() }
}

$handoffManifest | Add-Member -NotePropertyName intakeAckStatus -NotePropertyValue $AckStatus -Force
$handoffManifest | Add-Member -NotePropertyName intakeAckFile -NotePropertyValue $ackOutputFileName -Force
$handoffManifest | Add-Member -NotePropertyName intakeAckAtUtc -NotePropertyValue $ackUtc -Force
$handoffManifest | Add-Member -NotePropertyName intakeAckBy -NotePropertyValue $safeOperatorId -Force
$handoffManifest | Add-Member -NotePropertyName intakeTicketId -NotePropertyValue $safeTicketId -Force
$handoffManifest | Add-Member -NotePropertyName ackTrail -NotePropertyValue $ackTrail -Force
$handoffManifest | Add-Member -NotePropertyName intakeAck -NotePropertyValue $ackRecord -Force
$handoffManifest | ConvertTo-Json -Depth 12 | Set-Content -Path $handoffManifestPath -Encoding UTF8

$manifestHashAfter = (Get-FileHash -Algorithm SHA256 -Path $handoffManifestPath).Hash.ToLowerInvariant()
$ackRecord | Add-Member -NotePropertyName handoffManifestSha256After -NotePropertyValue $manifestHashAfter -Force
$ackRecord | ConvertTo-Json -Depth 12 | Set-Content -Path $ackOutputPath -Encoding UTF8

$ackMarkdownPath = Join-Path $packageDirectory "intake_acknowledgement.md"
$ackMarkdown = @(
    "# OPS-003 Intake ACK",
    "",
    "- ackId: $ackId",
    "- ackedAtUtc: $ackUtc",
    "- ackStatus: $AckStatus",
    "- intakeTicketId: $safeTicketId",
    "- intakeOperatorId: $safeOperatorId",
    "- packageDirectory: $packageDirectory",
    "- ackJson: $ackOutputFileName",
    "- handoffManifestSha256: $manifestHashAfter",
    "- checksumsSha256: $checksumsSha256"
)
$ackMarkdown | Set-Content -Path $ackMarkdownPath -Encoding UTF8

Write-Host "Handoff package directory: $packageDirectory"
Write-Host "Updated handoff manifest: $handoffManifestPath"
Write-Host "Intake ACK JSON: $ackOutputPath"
Write-Host "Intake ACK markdown: $ackMarkdownPath"
