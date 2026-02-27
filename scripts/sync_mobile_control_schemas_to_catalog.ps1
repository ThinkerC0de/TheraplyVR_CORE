[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [string]$CatalogPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $scriptRoot
}
$RepoRoot = (Resolve-Path $RepoRoot).Path

if ([string]::IsNullOrWhiteSpace($CatalogPath)) {
    $CatalogPath = Join-Path $RepoRoot "contracts\game_catalog_seed.json"
}

if (-not (Test-Path -Path $CatalogPath -PathType Leaf)) {
    throw "Catalog file not found: $CatalogPath"
}

$contractsDir = Join-Path $RepoRoot "contracts"
$schemaFiles = @(Get-ChildItem -Path $contractsDir -File -Filter "mobile_control_schema_*.json" |
    Sort-Object Name)
$layoutFiles = @(Get-ChildItem -Path $contractsDir -File -Filter "mobile_control_layout_*.json" |
    Sort-Object Name)

$catalog = Get-Content -Raw -Path $CatalogPath | ConvertFrom-Json
if ($null -eq $catalog) {
    throw "Failed to parse catalog JSON: $CatalogPath"
}

$entries = @($catalog.entries)
if ($entries.Count -eq 0) {
    throw "Catalog entries must be non-empty: $CatalogPath"
}

$schemaByGameId = @{}
foreach ($schemaFile in $schemaFiles) {
    $schema = Get-Content -Raw -Path $schemaFile.FullName | ConvertFrom-Json
    if ($null -eq $schema) {
        throw "Failed to parse schema file: $($schemaFile.FullName)"
    }

    $gameId = ([string]$schema.gameId).Trim()
    if ([string]::IsNullOrWhiteSpace($gameId)) {
        throw "Schema file has empty gameId: $($schemaFile.Name)"
    }

    if ($schemaByGameId.ContainsKey($gameId.ToLowerInvariant())) {
        throw "Duplicate schema gameId: $gameId"
    }

    $schemaByGameId[$gameId.ToLowerInvariant()] = $schema
}

$layoutByGameId = @{}
foreach ($layoutFile in $layoutFiles) {
    $layout = Get-Content -Raw -Path $layoutFile.FullName | ConvertFrom-Json
    if ($null -eq $layout) {
        throw "Failed to parse layout file: $($layoutFile.FullName)"
    }

    $layoutGameId = ([string]$layout.gameId).Trim()
    if ([string]::IsNullOrWhiteSpace($layoutGameId)) {
        throw "Layout file has empty gameId: $($layoutFile.Name)"
    }

    if ($layoutByGameId.ContainsKey($layoutGameId.ToLowerInvariant())) {
        throw "Duplicate layout gameId: $layoutGameId"
    }

    $layoutByGameId[$layoutGameId.ToLowerInvariant()] = $layout
}

$syncedCount = 0
$layoutSyncedCount = 0
foreach ($entry in $entries) {
    if ($null -eq $entry) {
        continue
    }

    $gameId = ([string]$entry.gameId).Trim()
    if ([string]::IsNullOrWhiteSpace($gameId)) {
        continue
    }

    $key = $gameId.ToLowerInvariant()
    if (-not $schemaByGameId.ContainsKey($key)) {
        $schemaPresent = $false
    }
    else {
        $schemaPresent = $true
    }

    if ($schemaPresent) {
        if ($entry.PSObject.Properties.Name -contains 'mobileControlSchema') {
            $entry.mobileControlSchema = $schemaByGameId[$key]
        }
        else {
            Add-Member -InputObject $entry -NotePropertyName 'mobileControlSchema' -NotePropertyValue $schemaByGameId[$key]
        }
        $syncedCount++
    }

    if ($layoutByGameId.ContainsKey($key)) {
        if ($entry.PSObject.Properties.Name -contains 'mobileControlLayout') {
            $entry.mobileControlLayout = $layoutByGameId[$key]
        }
        else {
            Add-Member -InputObject $entry -NotePropertyName 'mobileControlLayout' -NotePropertyValue $layoutByGameId[$key]
        }
        $layoutSyncedCount++
    }
}

$json = $catalog | ConvertTo-Json -Depth 100
$json = $json -replace '":\s+', '": '
[System.IO.File]::WriteAllText(
    $CatalogPath,
    $json + [Environment]::NewLine,
    (New-Object System.Text.UTF8Encoding($false)))

Write-Host "[DONE] Synced mobile control schemas into catalog."
Write-Host "Schemas discovered: $($schemaFiles.Count)"
Write-Host "Catalog entries updated: $syncedCount"
Write-Host "Layouts discovered: $($layoutFiles.Count)"
Write-Host "Catalog layout entries updated: $layoutSyncedCount"
