[CmdletBinding()]
param(
    [string]$UnityExe = $env:UNITY_EDITOR_PATH,
    [string]$ProjectPath = "",
    [string]$LogFile = "",
    [switch]$SkipCatalogSync
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$syncScript = Join-Path $scriptRoot "sync_mobile_control_schemas_to_catalog.ps1"
$syncAdminAssetsScript = Join-Path $scriptRoot "sync_admin_console_seed_assets.ps1"

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot "unity-quest-template"
}
$ProjectPath = (Resolve-Path $ProjectPath).Path
$UnityExe = Resolve-UnityExecutable $UnityExe

if ([string]::IsNullOrWhiteSpace($LogFile)) {
    $LogFile = Join-Path $ProjectPath "Temp\CliValidation\logs\unity_authoring_export.log"
}

$logDir = Split-Path -Parent $LogFile
if (-not [string]::IsNullOrWhiteSpace($logDir)) {
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
}

if (Test-Path -Path $LogFile -PathType Leaf) {
    Remove-Item -Path $LogFile -Force
}

Write-Host "Unity executable: $UnityExe"
Write-Host "Unity project: $ProjectPath"
Write-Host "Log file: $LogFile"

$arguments = @(
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath", $ProjectPath,
    "-buildTarget", "Android",
    "-executeMethod", "TheraplyCore.Editor.Authoring.SessionFlowAuthoringExport.ExportAllGameDefinitionsToContracts",
    "-logFile", $LogFile
)

$process = Start-Process -FilePath $UnityExe -ArgumentList $arguments -NoNewWindow -Wait -PassThru
$exitCode = [int]$process.ExitCode

if ($exitCode -ne 0) {
    Write-Host "[FAIL] Unity authoring export failed with exit code $exitCode"
    if (Test-Path -Path $LogFile -PathType Leaf) {
        Write-Host "----- Last 120 lines from log -----"
        Get-Content -Path $LogFile -Tail 120 | ForEach-Object { Write-Host $_ }
        Write-Host "----- End of log excerpt -----"
    }
    throw "Unity authoring export failed."
}

if (-not $SkipCatalogSync) {
    & powershell -ExecutionPolicy Bypass -File $syncScript -RepoRoot $repoRoot | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "sync_mobile_control_schemas_to_catalog.ps1 failed."
    }

    & powershell -ExecutionPolicy Bypass -File $syncAdminAssetsScript -RepoRoot $repoRoot | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "sync_admin_console_seed_assets.ps1 failed."
    }
}

Write-Host "[DONE] Unity authoring export completed successfully."
