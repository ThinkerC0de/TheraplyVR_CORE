[CmdletBinding()]
param(
    [string]$UnityExe = $env:UNITY_EDITOR_PATH,
    [string]$ProjectPath = "",
    [string]$EvidenceRoot = "",
    [string]$LogDirectory = "",
    [switch]$SkipCompile
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

function Invoke-UnityValidationStep {
    param(
        [string]$UnityExecutable,
        [string]$UnityProjectPath,
        [string]$StepName,
        [string]$ExecuteMethod,
        [string]$LogFilePath
    )

    Write-Host ("[START] {0}" -f $StepName)

    if (Test-Path -Path $LogFilePath -PathType Leaf) {
        Remove-Item -Path $LogFilePath -Force
    }

    $arguments = @(
        "-batchmode",
        "-nographics",
        "-quit",
        "-projectPath", $UnityProjectPath,
        "-buildTarget", "Android",
        "-executeMethod", $ExecuteMethod,
        "-logFile", $LogFilePath
    )

    $process = Start-Process -FilePath $UnityExecutable -ArgumentList $arguments -NoNewWindow -Wait -PassThru
    $exitCode = [int]$process.ExitCode

    if ($exitCode -ne 0) {
        Write-Host ("[FAIL] {0} exited with code {1}" -f $StepName, $exitCode)
        if (Test-Path -Path $LogFilePath -PathType Leaf) {
            Write-Host "----- Last 120 lines from log -----"
            Get-Content -Path $LogFilePath -Tail 120 | ForEach-Object { Write-Host $_ }
            Write-Host "----- End of log excerpt -----"
        }

        throw ("Session flow validation step failed: {0}" -f $StepName)
    }

    Write-Host ("[OK] {0} (log: {1})" -f $StepName, $LogFilePath)
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$unityCliValidateScript = Join-Path $scriptRoot "unity_cli_validate.ps1"
$cleanupScript = Join-Path $scriptRoot "cleanup_unity_utmp.ps1"

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot "unity-quest-template"
}
$ProjectPath = (Resolve-Path $ProjectPath).Path
$UnityExe = Resolve-UnityExecutable $UnityExe

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $EvidenceRoot = Join-Path $repoRoot ("docs\evidence\{0}" -f $timestamp)
}
New-Item -ItemType Directory -Force -Path $EvidenceRoot | Out-Null

if ([string]::IsNullOrWhiteSpace($LogDirectory)) {
    $LogDirectory = Join-Path $EvidenceRoot "commands"
}
New-Item -ItemType Directory -Force -Path $LogDirectory | Out-Null

Write-Host "Unity executable: $UnityExe"
Write-Host "Unity project: $ProjectPath"
Write-Host "Evidence root: $EvidenceRoot"
Write-Host "Log directory: $LogDirectory"
Write-Host ("Compile step: {0}" -f ($(if ($SkipCompile) { "SKIPPED" } else { "ENABLED" })))

& powershell -ExecutionPolicy Bypass -File $cleanupScript -ProjectPath $ProjectPath | Out-Host

if (-not $SkipCompile) {
    $compileLogPath = Join-Path $LogDirectory "unity_cli_validate_compile.log"
    & powershell -ExecutionPolicy Bypass -File $unityCliValidateScript -Mode compile -UnityExe $UnityExe -ProjectPath $ProjectPath *>&1 |
        Tee-Object -FilePath $compileLogPath | Out-Host
}

$validationSteps = @(
    @{
        name = "SessionFlowContractsValidation"
        method = "TheraplyCore.Editor.Automation.SessionFlowContractsValidation.RunSessionFlowContractsValidation"
        log = "unity_session_flow_contracts_validation.log"
    },
    @{
        name = "NarratorLocalizationRuntimeValidation"
        method = "TheraplyCore.Editor.Automation.NarratorLocalizationRuntimeValidation.RunNarratorLocalizationRuntimeValidation"
        log = "unity_narrator_localization_runtime_validation.log"
    },
    @{
        name = "CalendarRuntimeValidation"
        method = "TheraplyCore.Editor.Automation.CalendarRuntimeValidation.RunCalendarRuntimeValidation"
        log = "unity_calendar_runtime_validation.log"
    },
    @{
        name = "SceneLifecycleEffectsValidation"
        method = "TheraplyCore.Editor.Automation.SceneLifecycleEffectsValidation.RunSceneLifecycleEffectsValidation"
        log = "unity_scene_lifecycle_effects_validation.log"
    },
    @{
        name = "MobileLocaleSyncValidation"
        method = "TheraplyCore.Editor.Automation.MobileLocaleSyncValidation.RunMobileLocaleSyncValidation"
        log = "unity_mobile_locale_sync_validation.log"
    },
    @{
        name = "TaskGraphRuntimeIntegrationValidation"
        method = "TheraplyCore.Editor.Automation.TaskGraphRuntimeIntegrationValidation.RunTaskGraphRuntimeIntegrationValidation"
        log = "unity_task_graph_runtime_integration_validation.log"
    },
    @{
        name = "AdapterIntegrationValidation"
        method = "TheraplyCore.Editor.Automation.AdapterIntegrationValidation.RunAdapterIntegrationValidation"
        log = "unity_adapter_integration_validation.log"
    },
    @{
        name = "CanonicalFlowTelemetryQualityGateValidation"
        method = "TheraplyCore.Editor.Automation.CanonicalFlowTelemetryQualityGateValidation.RunCanonicalFlowTelemetryQualityGateValidation"
        log = "unity_canonical_flow_telemetry_quality_gate_validation.log"
    },
    @{
        name = "SessionFlowOutboxResilienceValidation"
        method = "TheraplyCore.Editor.Automation.SessionFlowOutboxResilienceValidation.RunSessionFlowOutboxResilienceValidation"
        log = "unity_session_flow_outbox_resilience_validation.log"
    },
    @{
        name = "SessionFlowSmokeTemplateValidation"
        method = "TheraplyCore.Editor.Automation.SessionFlowSmokeTemplateValidation.RunSessionFlowSmokeTemplateValidation"
        log = "unity_session_flow_smoke_template_validation.log"
    }
)

foreach ($step in $validationSteps) {
    Invoke-UnityValidationStep `
        -UnityExecutable $UnityExe `
        -UnityProjectPath $ProjectPath `
        -StepName $step.name `
        -ExecuteMethod $step.method `
        -LogFilePath (Join-Path $LogDirectory $step.log)
}

$summaryPath = Join-Path $EvidenceRoot "SUMMARY.md"
$summaryLines = @(
    "# Session Flow Validation Pack Summary",
    "",
    ("Timestamp (UTC): {0:yyyy-MM-dd HH:mm:ss}" -f (Get-Date).ToUniversalTime()),
    ("Unity executable: {0}" -f $UnityExe),
    ("Unity project: {0}" -f $ProjectPath),
    ("Compile step: {0}" -f ($(if ($SkipCompile) { "SKIPPED" } else { "ENABLED" }))),
    "",
    "Validation order:",
    "1. SessionFlowContractsValidation",
    "2. NarratorLocalizationRuntimeValidation",
    "3. CalendarRuntimeValidation",
    "4. SceneLifecycleEffectsValidation",
    "5. MobileLocaleSyncValidation",
    "6. TaskGraphRuntimeIntegrationValidation",
    "7. AdapterIntegrationValidation",
    "8. CanonicalFlowTelemetryQualityGateValidation",
    "9. SessionFlowOutboxResilienceValidation",
    "10. SessionFlowSmokeTemplateValidation",
    "",
    "Logs:",
    ("- {0}" -f $LogDirectory),
    "",
    "Result: PASS"
)
$summaryLines | Set-Content -Path $summaryPath -Encoding UTF8

Write-Host "[DONE] Session flow validation pack completed successfully."
Write-Host "Summary: $summaryPath"
