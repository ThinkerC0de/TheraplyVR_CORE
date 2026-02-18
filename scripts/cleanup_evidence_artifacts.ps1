[CmdletBinding()]
param(
    [string]$EvidenceRoot = "",
    [switch]$RemoveEvidenceZips,
    [switch]$Apply
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $repoRoot "docs\evidence"
}

if (-not (Test-Path -Path $EvidenceRoot -PathType Container)) {
    throw "Evidence root not found: $EvidenceRoot"
}

$evidenceRootResolved = (Resolve-Path $EvidenceRoot).Path

$apkCandidates = @(Get-ChildItem -Path $evidenceRootResolved -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object {
        (
            $_.Name -eq "app-debug.apk" -and
            $_.FullName -like "*\artifacts\flutter_build\*"
        ) -or (
            $_.Name -eq "TheraplyCliValidation.apk" -and
            $_.FullName -like "*\artifacts\unity_build\*"
        )
    })

$zipCandidates = @()
if ($RemoveEvidenceZips) {
    $zipCandidates = @(Get-ChildItem -Path $evidenceRootResolved -File -Filter "*.zip" -ErrorAction SilentlyContinue)
}

$candidates = @($apkCandidates + $zipCandidates)
$totalBytes = ($candidates | Measure-Object -Property Length -Sum).Sum
if ($null -eq $totalBytes) {
    $totalBytes = 0
}

Write-Host ""
Write-Host "Evidence root: $evidenceRootResolved"
Write-Host "Candidates: $($candidates.Count)"
Write-Host "Potential reclaim: $([math]::Round(($totalBytes / 1MB), 2)) MB"
Write-Host ""

if ($candidates.Count -gt 0) {
    $candidates |
    Sort-Object Length -Descending |
    Select-Object @{Name = "MB"; Expression = { [math]::Round(($_.Length / 1MB), 2) } }, @{
            Name = "Path"
            Expression = { $_.FullName.Replace($repoRoot + "\", "") }
        } |
    Format-Table -AutoSize
}
else {
    Write-Host "No matching artifacts found."
}

if (-not $Apply) {
    Write-Host ""
    Write-Host "Dry-run mode. Re-run with -Apply to delete listed files."
    return
}

foreach ($file in $candidates) {
    Remove-Item -Path $file.FullName -Force
}

Write-Host ""
Write-Host "Deleted: $($candidates.Count) files"
Write-Host "Reclaimed: $([math]::Round(($totalBytes / 1MB), 2)) MB"

