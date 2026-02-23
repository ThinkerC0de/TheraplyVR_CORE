[CmdletBinding()]
param(
    [string]$UnityExe = $env:UNITY_EDITOR_PATH,
    [string]$ProjectPath = "",
    [string]$LogFile = "",
    [string]$ExportOutputDirectory = "",
    [string]$ExportName = "",
    [string]$TraceInputPath = "",
    [string]$TraceSessionId = "",
    [switch]$RequireProvidedTrace
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param(
        [Parameter(Mandatory = $true)][string]$PathValue,
        [string]$BasePath = ""
    )

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        throw "Path is required."
    }

    $trimmed = $PathValue.Trim()
    if ([System.IO.Path]::IsPathRooted($trimmed)) {
        return [System.IO.Path]::GetFullPath($trimmed)
    }

    $root = if ([string]::IsNullOrWhiteSpace($BasePath)) { Get-Location } else { $BasePath }
    return [System.IO.Path]::GetFullPath((Join-Path $root $trimmed))
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

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot "unity-quest-template"
}
$ProjectPath = Resolve-FullPath -PathValue $ProjectPath -BasePath $repoRoot
$ProjectPath = (Resolve-Path $ProjectPath).Path

$UnityExe = Resolve-UnityExecutable $UnityExe

if ([string]::IsNullOrWhiteSpace($LogFile)) {
    $LogFile = Join-Path $ProjectPath "Temp\CliValidation\logs\unity_operational_dataset_trace_export_validation.log"
}
else {
    $LogFile = Resolve-FullPath -PathValue $LogFile -BasePath $repoRoot
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
if (-not [string]::IsNullOrWhiteSpace($TraceInputPath)) {
    Write-Host "Trace input path override: $TraceInputPath"
}
if (-not [string]::IsNullOrWhiteSpace($TraceSessionId)) {
    Write-Host "Trace session filter override: $TraceSessionId"
}
if ($RequireProvidedTrace.IsPresent) {
    Write-Host "Require provided trace mode: ENABLED"
}

if ($RequireProvidedTrace.IsPresent -and [string]::IsNullOrWhiteSpace($TraceInputPath)) {
    throw "TraceInputPath is required when -RequireProvidedTrace is set."
}

if (-not [string]::IsNullOrWhiteSpace($ExportOutputDirectory)) {
    $ExportOutputDirectory = Resolve-FullPath -PathValue $ExportOutputDirectory -BasePath $repoRoot
    New-Item -ItemType Directory -Force -Path $ExportOutputDirectory | Out-Null
    Write-Host "Resolved export output directory: $ExportOutputDirectory"
    $env:THERAPLY_DATASET_EXPORT_OUTPUT = $ExportOutputDirectory
}
if (-not [string]::IsNullOrWhiteSpace($ExportName)) {
    $env:THERAPLY_DATASET_EXPORT_NAME = $ExportName
}
if (-not [string]::IsNullOrWhiteSpace($TraceInputPath)) {
    $TraceInputPath = Resolve-FullPath -PathValue $TraceInputPath -BasePath $repoRoot
    if (-not (Test-Path -Path $TraceInputPath -PathType Leaf)) {
        throw "TraceInputPath does not exist: $TraceInputPath"
    }
    Write-Host "Resolved trace input path: $TraceInputPath"
    $env:THERAPLY_DATASET_TRACE_INPUT = $TraceInputPath
}
if (-not [string]::IsNullOrWhiteSpace($TraceSessionId)) {
    $env:THERAPLY_DATASET_TRACE_SESSION_ID = $TraceSessionId
}
if ($RequireProvidedTrace.IsPresent) {
    $env:THERAPLY_DATASET_TRACE_REQUIRE_PROVIDED = "1"
}

try {
    $arguments = @(
        "-batchmode",
        "-nographics",
        "-quit",
        "-projectPath", $ProjectPath,
        "-executeMethod", "TheraplyCore.Editor.Automation.OperationalDatasetTraceExportValidation.RunOperationalDatasetTraceExportValidation",
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

        throw "Operational dataset trace export validation failed."
    }
}
finally {
    Remove-Item Env:THERAPLY_DATASET_EXPORT_OUTPUT -ErrorAction SilentlyContinue
    Remove-Item Env:THERAPLY_DATASET_EXPORT_NAME -ErrorAction SilentlyContinue
    Remove-Item Env:THERAPLY_DATASET_TRACE_INPUT -ErrorAction SilentlyContinue
    Remove-Item Env:THERAPLY_DATASET_TRACE_SESSION_ID -ErrorAction SilentlyContinue
    Remove-Item Env:THERAPLY_DATASET_TRACE_REQUIRE_PROVIDED -ErrorAction SilentlyContinue
}

Write-Host "[DONE] Operational dataset trace export validation passed."
