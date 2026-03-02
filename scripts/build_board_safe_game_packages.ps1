[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [string[]]$GameIds = @("demo_cube_clicker", "pulse_target_tap"),
    [string]$CatalogPath = "contracts/game_catalog_seed.json",
    [string]$ExportManifestPath = "contracts/game_definition_export_manifest.json",
    [string]$OutputDirectory = "hosting/public/content",
    [string]$BasePackageUrl = "https://theraply-vr-demo.web.app/content",
    [switch]$UpdateCatalogPackageUris,
    [switch]$SyncAdminConsoleSeedAssets
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-AbsolutePath {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][string]$PathValue
    )

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $PathValue))
}

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)][string]$FilePath)

    return (Get-FileHash -Algorithm SHA256 -Path $FilePath).Hash.ToLowerInvariant()
}

function Get-CompositeSha256 {
    param([Parameter(Mandatory = $true)][string[]]$Lines)

    $joined = [string]::Join("|", $Lines)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($joined)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha.ComputeHash($bytes)
    }
    finally {
        $sha.Dispose()
    }

    return ([System.BitConverter]::ToString($hashBytes).Replace("-", "").ToLowerInvariant())
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    [System.IO.File]::WriteAllText(
        $Path,
        $Content + [Environment]::NewLine,
        (New-Object System.Text.UTF8Encoding($false)))
}

function Update-CatalogPackageUris {
    param(
        [Parameter(Mandatory = $true)][string]$CatalogFilePath,
        [Parameter(Mandatory = $true)][object[]]$Packages
    )

    $raw = Get-Content -Path $CatalogFilePath -Raw -Encoding UTF8
    foreach ($package in $Packages) {
        $gameId = [string]$package.gameId
        $packageUri = [string]$package.packageUri
        if ([string]::IsNullOrWhiteSpace($gameId) -or [string]::IsNullOrWhiteSpace($packageUri)) {
            continue
        }

        $escapedGameId = [System.Text.RegularExpressions.Regex]::Escape($gameId.Trim())
        $pattern = '(?ms)(^\s{{24}}"gameId"\s*:\s*"{0}",[\s\S]*?^\s{{24}}"packageUri"\s*:\s*")([^"]*)(")' -f $escapedGameId
        if (-not [System.Text.RegularExpressions.Regex]::IsMatch($raw, $pattern)) {
            throw "Could not locate packageUri field for gameId='$gameId' in $CatalogFilePath"
        }

        $nextUri = $packageUri.Trim()
        $raw = [System.Text.RegularExpressions.Regex]::Replace(
            $raw,
            $pattern,
            [System.Text.RegularExpressions.MatchEvaluator]{
                param($match)
                return $match.Groups[1].Value + $nextUri + $match.Groups[3].Value
            },
            1)
    }

    Write-Utf8NoBom -Path $CatalogFilePath -Content ($raw.TrimEnd())
    Write-Host ("[DONE] Updated catalog packageUri values: {0}" -f $CatalogFilePath)
}

$scriptRoot = Split-Path -Parent $PSCommandPath
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $scriptRoot
}
$RepoRoot = (Resolve-Path $RepoRoot).Path

$catalogFullPath = Resolve-AbsolutePath -BasePath $RepoRoot -PathValue $CatalogPath
$exportManifestFullPath = Resolve-AbsolutePath -BasePath $RepoRoot -PathValue $ExportManifestPath
$outputDirectoryFullPath = Resolve-AbsolutePath -BasePath $RepoRoot -PathValue $OutputDirectory

if (-not (Test-Path -Path $catalogFullPath -PathType Leaf)) {
    throw "Catalog file not found: $catalogFullPath"
}
if (-not (Test-Path -Path $exportManifestFullPath -PathType Leaf)) {
    throw "Export manifest file not found: $exportManifestFullPath"
}

New-Item -ItemType Directory -Force -Path $outputDirectoryFullPath | Out-Null

$catalog = Get-Content -Path $catalogFullPath -Raw -Encoding UTF8 | ConvertFrom-Json
$catalogEntries = @($catalog.entries)
$exportManifest = Get-Content -Path $exportManifestFullPath -Raw -Encoding UTF8 | ConvertFrom-Json
$exportEntries = @($exportManifest.entries)

$packageResults = New-Object 'System.Collections.Generic.List[object]'

foreach ($gameId in $GameIds) {
    $normalizedGameId = if ([string]::IsNullOrWhiteSpace($gameId)) { "" } else { $gameId.Trim() }
    if ([string]::IsNullOrWhiteSpace($normalizedGameId)) {
        continue
    }

    $catalogEntry = $catalogEntries | Where-Object { $_.gameId -eq $normalizedGameId } | Select-Object -First 1
    if ($null -eq $catalogEntry) {
        throw "Catalog entry not found for gameId='$normalizedGameId'."
    }

    $exportEntry = $exportEntries | Where-Object { $_.gameId -eq $normalizedGameId } | Select-Object -First 1
    if ($null -eq $exportEntry) {
        throw "Export manifest entry not found for gameId='$normalizedGameId'."
    }

    $targetContentVersion = [string]$catalogEntry.targetContentVersion
    if ([string]::IsNullOrWhiteSpace($targetContentVersion)) {
        throw "Missing targetContentVersion for gameId='$normalizedGameId'."
    }
    $targetContentVersion = $targetContentVersion.Trim()

    $sourcePaths = New-Object 'System.Collections.Generic.List[string]'
    $definitionPath = [string]$exportEntry.definitionJsonPath
    $schemaPath = [string]$exportEntry.mobileControlSchemaJsonPath
    if (-not [string]::IsNullOrWhiteSpace($definitionPath)) {
        $sourcePaths.Add($definitionPath.Trim()) | Out-Null
    }
    if (-not [string]::IsNullOrWhiteSpace($schemaPath)) {
        $sourcePaths.Add($schemaPath.Trim()) | Out-Null
    }

    $layoutPath = "contracts/mobile_control_layout_{0}.json" -f $normalizedGameId
    $layoutFullPath = Resolve-AbsolutePath -BasePath $RepoRoot -PathValue $layoutPath
    if (Test-Path -Path $layoutFullPath -PathType Leaf) {
        $sourcePaths.Add($layoutPath) | Out-Null
    }

    $uniqueSourcePaths = @($sourcePaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
    if ($uniqueSourcePaths.Count -eq 0) {
        throw "No source contracts found for gameId='$normalizedGameId'."
    }

    $sourceContracts = New-Object 'System.Collections.Generic.List[object]'
    $compositeLines = New-Object 'System.Collections.Generic.List[string]'
    foreach ($sourcePath in $uniqueSourcePaths) {
        $sourceFullPath = Resolve-AbsolutePath -BasePath $RepoRoot -PathValue $sourcePath
        if (-not (Test-Path -Path $sourceFullPath -PathType Leaf)) {
            throw "Source contract file not found: $sourceFullPath"
        }

        $normalizedPath = $sourcePath.Replace("\", "/")
        $sha256 = Get-FileSha256 -FilePath $sourceFullPath
        $bytes = (Get-Item -Path $sourceFullPath).Length
        $sourceContracts.Add([ordered]@{
                path = $normalizedPath
                sha256 = $sha256
                bytes = $bytes
            }) | Out-Null
        $compositeLines.Add("${normalizedPath}:${sha256}:${bytes}") | Out-Null
    }

    $checksumSha256 = Get-CompositeSha256 -Lines $compositeLines.ToArray()
    $contentVersionToken = ($targetContentVersion -replace "[^0-9A-Za-z]+", "_").Trim("_")
    if ([string]::IsNullOrWhiteSpace($contentVersionToken)) {
        throw "Could not build filename token from version '$targetContentVersion' for gameId='$normalizedGameId'."
    }

    $fileName = "{0}_{1}.pkg.json" -f $normalizedGameId, $contentVersionToken
    $packageUri = ("{0}/{1}" -f $BasePackageUrl.TrimEnd("/"), $fileName)
    $outputPath = Join-Path $outputDirectoryFullPath $fileName
    $generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o", [System.Globalization.CultureInfo]::InvariantCulture)

    $manifest = [ordered]@{
        schema = "THERAPLY_BOARD_SAFE_GAME_PACKAGE"
        schemaVersion = "2026-03-02"
        packageId = $normalizedGameId
        contentVersion = $targetContentVersion
        artifactType = "game_contract_bundle"
        deliveryMode = "on_demand"
        generatedAtUtc = $generatedAtUtc
        packageUri = $packageUri
        sourceContracts = $sourceContracts.ToArray()
        checksumSha256 = $checksumSha256
        probeOnly = $true
        notes = "Board-safe package manifest for packageUri download proof without runtime executable module loading."
    }

    $manifestJson = $manifest | ConvertTo-Json -Depth 8
    Write-Utf8NoBom -Path $outputPath -Content $manifestJson

    $packageResults.Add([ordered]@{
            gameId = $normalizedGameId
            fileName = $fileName
            packageUri = $packageUri
            contentVersion = $targetContentVersion
            checksumSha256 = $checksumSha256
        }) | Out-Null

    Write-Host ("[DONE] Wrote package manifest for {0}: {1}" -f $normalizedGameId, $outputPath)
}

$summary = [ordered]@{
    schema = "THERAPLY_BOARD_SAFE_GAME_PACKAGE_INDEX"
    schemaVersion = "2026-03-02"
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o", [System.Globalization.CultureInfo]::InvariantCulture)
    outputDirectory = $outputDirectoryFullPath
    packageCount = $packageResults.Count
    packages = $packageResults.ToArray()
}
$summaryPath = Join-Path $outputDirectoryFullPath "board_safe_game_packages_index.json"
Write-Utf8NoBom -Path $summaryPath -Content (($summary | ConvertTo-Json -Depth 8))
Write-Host ("[DONE] Package index: {0}" -f $summaryPath)

if ($UpdateCatalogPackageUris) {
    Update-CatalogPackageUris -CatalogFilePath $catalogFullPath -Packages $packageResults.ToArray()

    if ($SyncAdminConsoleSeedAssets) {
        $syncScriptPath = Resolve-AbsolutePath -BasePath $RepoRoot -PathValue "scripts/sync_admin_console_seed_assets.ps1"
        if (-not (Test-Path -Path $syncScriptPath -PathType Leaf)) {
            throw "Missing sync script: $syncScriptPath"
        }

        & $syncScriptPath -RepoRoot $RepoRoot
    }
}
