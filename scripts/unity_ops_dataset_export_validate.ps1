[CmdletBinding()]
param(
    [string]$UnityExe = $env:UNITY_EDITOR_PATH,
    [string]$ProjectPath = "",
    [string]$LogFile = "",
    [string]$ExportOutputDirectory = "",
    [string]$ExportName = ""
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

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot "unity-quest-template"
}
$ProjectPath = (Resolve-Path $ProjectPath).Path

$UnityExe = Resolve-UnityExecutable $UnityExe

if ([string]::IsNullOrWhiteSpace($LogFile)) {
    $LogFile = Join-Path $ProjectPath "Temp\CliValidation\logs\unity_operational_dataset_export_validation.log"
}
$logDirectory = Split-Path -Parent $LogFile
if (-not [string]::IsNullOrWhiteSpace($logDirectory)) {
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
}

Write-Host "Unity executable: $UnityExe"
Write-Host "Unity project: $ProjectPath"
Write-Host "Unity log: $LogFile"
if (-not [string]::IsNullOrWhiteSpace($ExportOutputDirectory)) {
    Write-Host "Export output directory override: $ExportOutputDirectory"
}
if (-not [string]::IsNullOrWhiteSpace($ExportName)) {
    Write-Host "Export name override: $ExportName"
}

if (-not [string]::IsNullOrWhiteSpace($ExportOutputDirectory)) {
    $env:THERAPLY_DATASET_EXPORT_OUTPUT = $ExportOutputDirectory
}
if (-not [string]::IsNullOrWhiteSpace($ExportName)) {
    $env:THERAPLY_DATASET_EXPORT_NAME = $ExportName
}

try {
    $arguments = @(
        "-batchmode",
        "-nographics",
        "-quit",
        "-projectPath", $ProjectPath,
        "-executeMethod", "TheraplyCore.Editor.Automation.OperationalDatasetExportValidation.RunOperationalDatasetExportValidation",
        "-logFile", $LogFile
    )

    $process = Start-Process -FilePath $UnityExe -ArgumentList $arguments -NoNewWindow -Wait -PassThru
    $exitCode = [int]$process.ExitCode
    Write-Host "[EXIT] $exitCode"

    if ($exitCode -ne 0) {
        if (Test-Path -Path $LogFile -PathType Leaf) {
            Write-Host "----- Last 100 lines from $LogFile -----"
            Get-Content -Path $LogFile -Tail 100 | ForEach-Object { Write-Host $_ }
            Write-Host "----- End of log excerpt -----"
        }

        throw "Operational dataset export validation failed."
    }
}
finally {
    Remove-Item Env:THERAPLY_DATASET_EXPORT_OUTPUT -ErrorAction SilentlyContinue
    Remove-Item Env:THERAPLY_DATASET_EXPORT_NAME -ErrorAction SilentlyContinue
}

Write-Host "[DONE] Operational dataset export validation passed."
