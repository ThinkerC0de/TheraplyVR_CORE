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

$syncTargets = @(
    @{
        Label  = "catalog seed"
        Source = "contracts\game_catalog_seed.json"
        Target = "admin_console_web\assets\contracts\game_catalog_seed.json"
    },
    @{
        Label  = "export manifest"
        Source = "contracts\game_definition_export_manifest.json"
        Target = "admin_console_web\assets\contracts\game_definition_export_manifest.json"
    }
)

foreach ($target in $syncTargets) {
    $sourcePath = Join-Path $RepoRoot $target.Source
    $targetPath = Join-Path $RepoRoot $target.Target

    if (-not (Test-Path -Path $sourcePath -PathType Leaf)) {
        throw "Source $($target.Label) file not found: $sourcePath"
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

    Write-Host ("[DONE] Synced admin console {0} asset." -f $target.Label)
    Write-Host ("Source: {0}" -f $sourcePath)
    Write-Host ("Target: {0}" -f $targetPath)
}
