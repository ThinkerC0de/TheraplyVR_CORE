[CmdletBinding()]
param(
    [string]$RepoRoot = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $scriptRoot
}
$RepoRoot = (Resolve-Path $RepoRoot).Path

$sourcePath = Join-Path $RepoRoot "contracts\game_catalog_seed.json"
$targetPath = Join-Path $RepoRoot "admin_console_web\assets\contracts\game_catalog_seed.json"

if (-not (Test-Path -Path $sourcePath -PathType Leaf)) {
    throw "Source catalog file not found: $sourcePath"
}

$targetDir = Split-Path -Parent $targetPath
if (-not [string]::IsNullOrWhiteSpace($targetDir)) {
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
}

$raw = Get-Content -Raw -Path $sourcePath
[System.IO.File]::WriteAllText(
    $targetPath,
    $raw,
    (New-Object System.Text.UTF8Encoding($false)))

Write-Host "[DONE] Synced admin console catalog seed asset."
Write-Host "Source: $sourcePath"
Write-Host "Target: $targetPath"
