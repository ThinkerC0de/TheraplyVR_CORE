[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [string[]]$GameIds = @("demo_cube_clicker"),
    [string]$CatalogPath = "contracts/game_catalog_seed.json",
    [string]$ExportManifestPath = "contracts/game_definition_export_manifest.json",
    [string]$OutputDirectory = "hosting/public/content",
    [string]$BasePackageUrl = "https://pranasense.pl/content",
    [bool]$BuildAssetBundlePackages = $true,
    [string]$UnityExe = $env:UNITY_EDITOR_PATH,
    [string]$UnityProjectPath = "unity-quest-template",
    [string]$UnityBuildTarget = "Android",
    [string]$DemoCubeScenePath = "Assets/_Examples/Scenes/CubeClickerVR.unity",
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

function Resolve-UnityExecutable {
    param([string]$ConfiguredPath)

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredPath)) {
        if (-not (Test-Path -Path $ConfiguredPath -PathType Leaf)) {
            throw "Configured Unity executable not found: $ConfiguredPath"
        }

        return (Resolve-Path $ConfiguredPath).Path
    }

    $preferred = "C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe"
    if (Test-Path -Path $preferred -PathType Leaf) {
        return $preferred
    }

    $candidateEditors = Get-ChildItem -Path "C:\Program Files\Unity\Hub\Editor" -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending
    foreach ($editor in $candidateEditors) {
        $candidate = Join-Path $editor.FullName "Editor\Unity.exe"
        if (Test-Path -Path $candidate -PathType Leaf) {
            return $candidate
        }
    }

    throw "Unity executable not found. Set UNITY_EDITOR_PATH or pass -UnityExe."
}

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)][string]$FilePath)

    return (Get-FileHash -Algorithm SHA256 -Path $FilePath).Hash.ToLowerInvariant()
}

function Get-BytesSha256 {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha.ComputeHash($Bytes)
    }
    finally {
        $sha.Dispose()
    }

    return ([System.BitConverter]::ToString($hashBytes).Replace("-", "").ToLowerInvariant())
}

function Get-CompositeSha256 {
    param([Parameter(Mandatory = $true)][string[]]$Lines)

    $joined = [string]::Join("|", $Lines)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($joined)
    return Get-BytesSha256 -Bytes $bytes
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

function Build-VersionToken {
    param([Parameter(Mandatory = $true)][string]$Version)

    $raw = if ([string]::IsNullOrWhiteSpace($Version)) { "1.0.0" } else { $Version.Trim() }
    $chars = $raw.ToCharArray()
    $builder = New-Object System.Text.StringBuilder
    foreach ($ch in $chars) {
        if ([char]::IsLetterOrDigit($ch)) {
            [void]$builder.Append([char]::ToLowerInvariant($ch))
        }
        else {
            [void]$builder.Append("_")
        }
    }

    $token = $builder.ToString().Trim("_")
    if ([string]::IsNullOrWhiteSpace($token)) {
        return "1_0_0"
    }

    return $token
}

function Normalize-PackageUrl {
    param([Parameter(Mandatory = $true)][string]$BaseUrl)

    $normalized = $BaseUrl.Trim()
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        throw "Base package URL cannot be empty."
    }

    return $normalized.TrimEnd("/")
}

function Resolve-SceneAssetPathForGame {
    param(
        [Parameter(Mandatory = $true)][string]$GameId,
        [Parameter(Mandatory = $true)][string]$DemoCubeScenePathValue
    )

    $normalized = if ([string]::IsNullOrWhiteSpace($GameId)) { "" } else { $GameId.Trim().ToLowerInvariant() }
    switch ($normalized) {
        "demo_cube_clicker" { return $DemoCubeScenePathValue }
        default { return "" }
    }
}

function Invoke-UnityAssetBundlePackageBuild {
    param(
        [Parameter(Mandatory = $true)][string]$UnityExecutable,
        [Parameter(Mandatory = $true)][string]$UnityProjectPathValue,
        [Parameter(Mandatory = $true)][string]$RepoRootValue,
        [Parameter(Mandatory = $true)][string]$GameId,
        [Parameter(Mandatory = $true)][string]$ContentVersion,
        [Parameter(Mandatory = $true)][string]$SceneAssetPath,
        [Parameter(Mandatory = $true)][string]$OutputDirectoryValue,
        [Parameter(Mandatory = $true)][string]$BasePackageUrlValue,
        [Parameter(Mandatory = $true)][string]$ManifestFileName,
        [Parameter(Mandatory = $true)][string]$BundleFileName,
        [Parameter(Mandatory = $true)][string]$BuildTargetValue
    )

    $unityLogPath = Join-Path $OutputDirectoryValue ("build_{0}.unity.log" -f $GameId)
    if (Test-Path -Path $unityLogPath -PathType Leaf) {
        Remove-Item -Path $unityLogPath -Force
    }

    $arguments = @(
        "-batchmode",
        "-nographics",
        "-quit",
        "-projectPath", $UnityProjectPathValue,
        "-executeMethod", "TheraplyCore.Editor.Automation.AssetBundlePackageBuilder.BuildPackageFromCommandLine",
        "-logFile", $unityLogPath,
        "-packageGameId", $GameId,
        "-packageVersion", $ContentVersion,
        "-packageScenePath", $SceneAssetPath,
        "-packageOutputDirectory", $OutputDirectoryValue,
        "-packageBaseUrl", $BasePackageUrlValue,
        "-packageManifestFileName", $ManifestFileName,
        "-packageBundleFileName", $BundleFileName,
        "-packageBuildTarget", $BuildTargetValue
    )

    $process = Start-Process `
        -FilePath $UnityExecutable `
        -ArgumentList $arguments `
        -WorkingDirectory $RepoRootValue `
        -NoNewWindow `
        -Wait `
        -PassThru
    $exitCode = [int]$process.ExitCode
    if ($exitCode -ne 0) {
        if (Test-Path -Path $unityLogPath -PathType Leaf) {
            Write-Host "----- Unity build log tail (last 120 lines) -----"
            Get-Content -Path $unityLogPath -Tail 120 | ForEach-Object { Write-Host $_ }
            Write-Host "----- End Unity build log tail -----"
        }

        throw "Unity package build failed for gameId='$GameId' (exitCode=$exitCode)."
    }

    Write-Host ("[DONE] Unity package build completed for {0}. Log: {1}" -f $GameId, $unityLogPath)
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
$unityProjectFullPath = Resolve-AbsolutePath -BasePath $RepoRoot -PathValue $UnityProjectPath
$basePackageUrlNormalized = Normalize-PackageUrl -BaseUrl $BasePackageUrl

if (-not (Test-Path -Path $catalogFullPath -PathType Leaf)) {
    throw "Catalog file not found: $catalogFullPath"
}
if (-not (Test-Path -Path $exportManifestFullPath -PathType Leaf)) {
    throw "Export manifest file not found: $exportManifestFullPath"
}
if (-not (Test-Path -Path $unityProjectFullPath -PathType Container)) {
    throw "Unity project directory not found: $unityProjectFullPath"
}

New-Item -ItemType Directory -Force -Path $outputDirectoryFullPath | Out-Null

$catalog = Get-Content -Path $catalogFullPath -Raw -Encoding UTF8 | ConvertFrom-Json
$catalogEntries = @($catalog.entries)
$exportManifest = Get-Content -Path $exportManifestFullPath -Raw -Encoding UTF8 | ConvertFrom-Json
$exportEntries = @($exportManifest.entries)

$unityExecutable = ""
if ($BuildAssetBundlePackages) {
    $unityExecutable = Resolve-UnityExecutable $UnityExe
    Write-Host ("[INFO] Asset bundle packaging enabled. Unity executable: {0}" -f $unityExecutable)
}
else {
    Write-Host "[INFO] Asset bundle packaging disabled. Generating manifest-only package files."
}

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
    $versionToken = Build-VersionToken -Version $targetContentVersion

    $definitionPath = [string]$exportEntry.definitionJsonPath
    $schemaPath = [string]$exportEntry.mobileControlSchemaJsonPath
    $layoutPath = "contracts/mobile_control_layout_{0}.json" -f $normalizedGameId

    $sourcePaths = New-Object 'System.Collections.Generic.List[string]'
    if (-not [string]::IsNullOrWhiteSpace($definitionPath)) {
        $sourcePaths.Add($definitionPath.Trim()) | Out-Null
    }
    if (-not [string]::IsNullOrWhiteSpace($schemaPath)) {
        $sourcePaths.Add($schemaPath.Trim()) | Out-Null
    }
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
    $manifestFileName = "{0}_{1}.pkg.json" -f $normalizedGameId, $versionToken
    $bundleFileName = "{0}_{1}_android.bundle" -f $normalizedGameId, $versionToken
    $manifestUri = ("{0}/{1}" -f $basePackageUrlNormalized, $manifestFileName)
    $bundleUri = ("{0}/{1}" -f $basePackageUrlNormalized, $bundleFileName)
    $manifestOutputPath = Join-Path $outputDirectoryFullPath $manifestFileName
    $bundleOutputPath = Join-Path $outputDirectoryFullPath $bundleFileName
    $generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o", [System.Globalization.CultureInfo]::InvariantCulture)

    $sceneAssetPath = Resolve-SceneAssetPathForGame -GameId $normalizedGameId -DemoCubeScenePathValue $DemoCubeScenePath
    if ($BuildAssetBundlePackages -and -not [string]::IsNullOrWhiteSpace($sceneAssetPath)) {
        Invoke-UnityAssetBundlePackageBuild `
            -UnityExecutable $unityExecutable `
            -UnityProjectPathValue $unityProjectFullPath `
            -RepoRootValue $RepoRoot `
            -GameId $normalizedGameId `
            -ContentVersion $targetContentVersion `
            -SceneAssetPath $sceneAssetPath `
            -OutputDirectoryValue $outputDirectoryFullPath `
            -BasePackageUrlValue $basePackageUrlNormalized `
            -ManifestFileName $manifestFileName `
            -BundleFileName $bundleFileName `
            -BuildTargetValue $UnityBuildTarget

        if (-not (Test-Path -Path $bundleOutputPath -PathType Leaf)) {
            throw "Asset bundle file not found after Unity build: $bundleOutputPath"
        }

        $bundleBytes = (Get-Item -Path $bundleOutputPath).Length
        $bundleSha256 = Get-FileSha256 -FilePath $bundleOutputPath
        $sceneName = [System.IO.Path]::GetFileNameWithoutExtension($sceneAssetPath)

        $manifest = [ordered]@{
            schema = "THERAPLY_ASSET_BUNDLE_GAME_PACKAGE"
            schemaVersion = "2026-03-03"
            packageId = $normalizedGameId
            contentVersion = $targetContentVersion
            artifactType = "unity_scene_asset_bundle"
            deliveryMode = "on_demand"
            generatedAtUtc = $generatedAtUtc
            packageUri = $manifestUri
            assetBundle = [ordered]@{
                bundleUri = $bundleUri
                bundleFileName = $bundleFileName
                bundleSha256 = $bundleSha256
                bundleBytes = $bundleBytes
                unityBuildTarget = $UnityBuildTarget
                compression = "chunk"
                sceneAssetPath = $sceneAssetPath
                sceneName = $sceneName
                loadMode = "additive"
            }
            gameDefinitionPath = if ([string]::IsNullOrWhiteSpace($definitionPath)) { "" } else { $definitionPath.Trim().Replace("\", "/") }
            mobileControlSchemaPath = if ([string]::IsNullOrWhiteSpace($schemaPath)) { "" } else { $schemaPath.Trim().Replace("\", "/") }
            mobileControlLayoutPath = if (Test-Path -Path $layoutFullPath -PathType Leaf) { $layoutPath.Replace("\", "/") } else { "" }
            sourceContracts = $sourceContracts.ToArray()
            checksumSha256 = $checksumSha256
            notes = "Install downloads package manifest + asset bundle; START loads scene additively from persistent storage."
        }

        Write-Utf8NoBom -Path $manifestOutputPath -Content (($manifest | ConvertTo-Json -Depth 10))
        Write-Host ("[DONE] Wrote asset-bundle package manifest for {0}: {1}" -f $normalizedGameId, $manifestOutputPath)

        $packageResults.Add([ordered]@{
                gameId = $normalizedGameId
                fileName = $manifestFileName
                packageUri = $manifestUri
                contentVersion = $targetContentVersion
                checksumSha256 = $checksumSha256
                artifactType = "unity_scene_asset_bundle"
                deliveryMode = "on_demand"
                artifactFiles = @($manifestFileName, $bundleFileName)
                bundleFileName = $bundleFileName
                bundleUri = $bundleUri
                bundleSha256 = $bundleSha256
                bundleBytes = $bundleBytes
                sceneAssetPath = $sceneAssetPath
                sceneName = $sceneName
            }) | Out-Null
    }
    else {
        $manifest = [ordered]@{
            schema = "THERAPLY_BOARD_SAFE_GAME_PACKAGE"
            schemaVersion = "2026-03-02"
            packageId = $normalizedGameId
            contentVersion = $targetContentVersion
            artifactType = "game_contract_bundle"
            deliveryMode = "on_demand"
            generatedAtUtc = $generatedAtUtc
            packageUri = $manifestUri
            sourceContracts = $sourceContracts.ToArray()
            checksumSha256 = $checksumSha256
            probeOnly = $true
            notes = "Board-safe package manifest for packageUri download proof without runtime executable module loading."
        }

        Write-Utf8NoBom -Path $manifestOutputPath -Content (($manifest | ConvertTo-Json -Depth 8))
        Write-Host ("[DONE] Wrote manifest-only package for {0}: {1}" -f $normalizedGameId, $manifestOutputPath)

        $packageResults.Add([ordered]@{
                gameId = $normalizedGameId
                fileName = $manifestFileName
                packageUri = $manifestUri
                contentVersion = $targetContentVersion
                checksumSha256 = $checksumSha256
                artifactType = "game_contract_bundle"
                deliveryMode = "on_demand"
                artifactFiles = @($manifestFileName)
            }) | Out-Null
    }
}

$summary = [ordered]@{
    schema = "THERAPLY_GAME_PACKAGE_INDEX"
    schemaVersion = "2026-03-03"
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o", [System.Globalization.CultureInfo]::InvariantCulture)
    outputDirectory = $outputDirectoryFullPath
    packageCount = $packageResults.Count
    packages = $packageResults.ToArray()
}
$summaryPath = Join-Path $outputDirectoryFullPath "board_safe_game_packages_index.json"
Write-Utf8NoBom -Path $summaryPath -Content (($summary | ConvertTo-Json -Depth 10))
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
