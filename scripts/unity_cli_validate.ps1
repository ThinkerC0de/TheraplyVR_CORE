[CmdletBinding()]
param(
    [ValidateSet("compile", "build", "both")]
    [string]$Mode = "both",
    [string]$UnityExe = $env:UNITY_EDITOR_PATH,
    [string]$ProjectPath = "",
    [string]$BuildOutputPath = "",
    [string]$LogDirectory = "",
    [string[]]$BuildScenes = @()
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

function Invoke-UnityCliStep {
    param(
        [string]$StepName,
        [string]$ExecuteMethod,
        [string]$LogFile
    )

    Write-Host "[START] Unity CLI step '$StepName'"

    $arguments = @(
        "-batchmode",
        "-nographics",
        "-quit",
        "-projectPath", $ProjectPath,
        "-buildTarget", "Android",
        "-executeMethod", $ExecuteMethod,
        "-logFile", $LogFile
    )

    $unityProcess = Start-Process -FilePath $UnityExe -ArgumentList $arguments -NoNewWindow -Wait -PassThru
    $exitCode = [int]$unityProcess.ExitCode

    if ($exitCode -ne 0) {
        Write-Host "[FAIL] Step '$StepName' failed with exit code $exitCode."

        if (Test-Path -Path $LogFile -PathType Leaf) {
            Write-Host "----- Last 80 lines from $LogFile -----"
            Get-Content -Path $LogFile -Tail 80 | ForEach-Object { Write-Host $_ }
            Write-Host "----- End of log excerpt -----"
        }

        throw "Unity CLI validation failed in step '$StepName'."
    }

    Write-Host "[OK] Step '$StepName' passed. Log: $LogFile"
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot "unity-quest-template"
}
$ProjectPath = (Resolve-Path $ProjectPath).Path

$UnityExe = Resolve-UnityExecutable $UnityExe

if ([string]::IsNullOrWhiteSpace($LogDirectory)) {
    $LogDirectory = Join-Path $ProjectPath "Temp\CliValidation\logs"
}
if ([string]::IsNullOrWhiteSpace($BuildOutputPath)) {
    $BuildOutputPath = Join-Path $ProjectPath "Temp\CliValidation\build\TheraplyCliValidation.apk"
}

New-Item -ItemType Directory -Force -Path $LogDirectory | Out-Null

$compileLog = Join-Path $LogDirectory "unity_cli_compile.log"
$buildLog = Join-Path $LogDirectory "unity_cli_build.log"

Write-Host "Unity executable: $UnityExe"
Write-Host "Unity project: $ProjectPath"
Write-Host "Mode: $Mode"
if ($BuildScenes.Count -gt 0) {
    Write-Host ("Build scenes override: " + ($BuildScenes -join ", "))
}

if ($Mode -eq "compile" -or $Mode -eq "both") {
    Invoke-UnityCliStep -StepName "compile" -ExecuteMethod "TheraplyCore.Editor.Automation.UnityCliValidation.RunCompileValidation" -LogFile $compileLog
}

if ($Mode -eq "build" -or $Mode -eq "both") {
    $buildOutputDirectory = Split-Path -Parent $BuildOutputPath
    if (-not [string]::IsNullOrWhiteSpace($buildOutputDirectory)) {
        New-Item -ItemType Directory -Force -Path $buildOutputDirectory | Out-Null
    }

    $env:THERAPLY_UNITY_BUILD_OUTPUT = $BuildOutputPath
    if ($BuildScenes.Count -gt 0) {
        $env:THERAPLY_UNITY_BUILD_SCENES = ($BuildScenes -join ";")
    }
    try {
        Invoke-UnityCliStep -StepName "android-build" -ExecuteMethod "TheraplyCore.Editor.Automation.UnityCliValidation.RunAndroidDebugBuildValidation" -LogFile $buildLog
    }
    finally {
        Remove-Item Env:THERAPLY_UNITY_BUILD_OUTPUT -ErrorAction SilentlyContinue
        Remove-Item Env:THERAPLY_UNITY_BUILD_SCENES -ErrorAction SilentlyContinue
    }
}

Write-Host "[DONE] Unity CLI validation completed successfully."
Write-Host "Compile log: $compileLog"
if ($Mode -eq "build" -or $Mode -eq "both") {
    Write-Host "Build log: $buildLog"
    Write-Host "Build output: $BuildOutputPath"
}
