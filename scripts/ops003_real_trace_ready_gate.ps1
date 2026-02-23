[CmdletBinding()]
param(
    [string]$UnityExe = $env:UNITY_EDITOR_PATH,
    [string]$ProjectPath = "",
    [string]$EvidenceRoot = "",
    [string]$TraceInputPath = "",
    [string]$TraceSessionId = "",
    [string]$AdbSerial = "",
    [string[]]$LocalTracePaths = @(),
    [switch]$SkipQuestPull,
    [string]$OperatorId = "unknown_operator",
    [string]$IntakeTicketId = "",
    [string]$IntakeOperatorId = "",
    [ValidateSet("ACKNOWLEDGED", "REJECTED", "NEEDS_INFO")]
    [string]$AckStatus = "ACKNOWLEDGED",
    [string]$AckNotes = "",
    [switch]$SkipIntakeAck
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

    $base = if ([string]::IsNullOrWhiteSpace($BasePath)) { Get-Location } else { $BasePath }
    return [System.IO.Path]::GetFullPath((Join-Path $base $trimmed))
}

function Invoke-LoggedStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Command,
        [Parameter(Mandatory = $true)][string]$LogFile
    )

    Write-Host "[START] $Name"
    & $Command *>&1 | Tee-Object -FilePath $LogFile | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Step failed: $Name (exitCode=$LASTEXITCODE). Log: $LogFile"
    }
    Write-Host "[OK] $Name"
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot "unity-quest-template"
}
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $timestamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd_HHmmss", [System.Globalization.CultureInfo]::InvariantCulture)
    $EvidenceRoot = Join-Path $repoRoot "docs\evidence\$timestamp\ops003_real_trace_ready_gate"
}

$ProjectPath = Resolve-FullPath -PathValue $ProjectPath -BasePath $repoRoot
$EvidenceRoot = Resolve-FullPath -PathValue $EvidenceRoot -BasePath $repoRoot

New-Item -ItemType Directory -Force -Path $EvidenceRoot | Out-Null
$commandsDir = Join-Path $EvidenceRoot "commands"
$artifactsDir = Join-Path $EvidenceRoot "artifacts"
$notesDir = Join-Path $EvidenceRoot "notes"
New-Item -ItemType Directory -Force -Path $commandsDir, $artifactsDir, $notesDir | Out-Null

$collectScript = Join-Path $scriptRoot "ops_dataset_trace_collect.ps1"
$validateScript = Join-Path $scriptRoot "unity_ops_dataset_trace_export_validate.ps1"
$handoffScript = Join-Path $scriptRoot "ops_pretrain_handoff_package.ps1"
$ackScript = Join-Path $scriptRoot "ops_pretrain_handoff_ack_confirm.ps1"

$resolvedTraceInputPath = ""
$resolvedTraceSessionId = if ([string]::IsNullOrWhiteSpace($TraceSessionId)) { "" } else { $TraceSessionId.Trim() }
$traceDiscoveryReportPath = ""

if ([string]::IsNullOrWhiteSpace($TraceInputPath)) {
    $traceCollectOutput = Join-Path $artifactsDir "ops_trace_collect"
    $collectLog = Join-Path $commandsDir "ops_dataset_trace_collect.log"

    Invoke-LoggedStep `
        -Name "ops_dataset_trace_collect" `
        -Command {
            $args = @(
                "-ExecutionPolicy", "Bypass",
                "-File", $collectScript,
                "-OutputDirectory", $traceCollectOutput
            )
            if (-not [string]::IsNullOrWhiteSpace($AdbSerial)) {
                $args += @("-AdbSerial", $AdbSerial)
            }
            if (-not [string]::IsNullOrWhiteSpace($resolvedTraceSessionId)) {
                $args += @("-TraceSessionId", $resolvedTraceSessionId)
            }
            if ($SkipQuestPull.IsPresent) {
                $args += "-SkipQuestPull"
            }
            foreach ($localPath in $LocalTracePaths) {
                if (-not [string]::IsNullOrWhiteSpace($localPath)) {
                    $args += @("-LocalTracePaths", $localPath)
                }
            }

            powershell @args
        } `
        -LogFile $collectLog

    $traceDiscoveryReportPath = Join-Path $traceCollectOutput "reports\trace_discovery_report.json"
    if (-not (Test-Path -Path $traceDiscoveryReportPath -PathType Leaf)) {
        throw "Missing trace discovery report: $traceDiscoveryReportPath"
    }

    $report = Get-Content -Path $traceDiscoveryReportPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($null -eq $report) {
        throw "Trace discovery report deserialization failed: $traceDiscoveryReportPath"
    }

    $ready = $false
    if ($null -ne $report.selection.readyForOpsTraceValidation) {
        $ready = [bool]$report.selection.readyForOpsTraceValidation
    }
    if (-not $ready) {
        throw "Trace discovery did not produce dataset-ready trace input."
    }

    $resolvedTraceInputPath = [string]$report.selection.recommendedTraceInputPath
    if ([string]::IsNullOrWhiteSpace($resolvedTraceInputPath)) {
        throw "Trace discovery report missing recommendedTraceInputPath."
    }

    if ([string]::IsNullOrWhiteSpace($resolvedTraceSessionId)) {
        $resolvedTraceSessionId = [string]$report.selection.recommendedTraceSessionId
    }
} else {
    $resolvedTraceInputPath = Resolve-FullPath -PathValue $TraceInputPath -BasePath $repoRoot
}

$resolvedTraceInputPath = Resolve-FullPath -PathValue $resolvedTraceInputPath -BasePath $repoRoot
if (-not (Test-Path -Path $resolvedTraceInputPath -PathType Leaf)) {
    throw "Trace input file does not exist: $resolvedTraceInputPath"
}

$exportOutput = Join-Path $artifactsDir "ops_dataset_trace_export"
$validationLog = Join-Path $commandsDir "unity_ops_dataset_trace_export_validate.log"

Invoke-LoggedStep `
    -Name "unity_ops_dataset_trace_export_validate" `
    -Command {
        $args = @(
            "-ExecutionPolicy", "Bypass",
            "-File", $validateScript,
            "-UnityExe", $UnityExe,
            "-ProjectPath", $ProjectPath,
            "-ExportOutputDirectory", $exportOutput,
            "-ExportName", "ops003_real_trace_export",
            "-TraceInputPath", $resolvedTraceInputPath,
            "-RequireProvidedTrace"
        )
        if (-not [string]::IsNullOrWhiteSpace($resolvedTraceSessionId)) {
            $args += @("-TraceSessionId", $resolvedTraceSessionId)
        }
        powershell @args
    } `
    -LogFile $validationLog

$pretrainManifestPath = Join-Path $exportOutput "pretrain_manifest.json"
if (-not (Test-Path -Path $pretrainManifestPath -PathType Leaf)) {
    throw "Missing pre-train manifest: $pretrainManifestPath"
}

$pretrainManifest = Get-Content -Path $pretrainManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($null -eq $pretrainManifest) {
    throw "Could not deserialize pre-train manifest."
}
if (-not [bool]$pretrainManifest.readyForTraining) {
    throw "Pre-train manifest reported readyForTraining=false."
}

$handoffRoot = Join-Path $exportOutput "handoff"
$packageName = "ops003_pretrain_handoff"
$handoffLog = Join-Path $commandsDir "ops_pretrain_handoff_package.log"

Invoke-LoggedStep `
    -Name "ops_pretrain_handoff_package" `
    -Command {
        $args = @(
            "-ExecutionPolicy", "Bypass",
            "-File", $handoffScript,
            "-ExportDirectory", $exportOutput,
            "-HandoffOutputDirectory", $handoffRoot,
            "-PackageName", $packageName,
            "-OperatorId", $OperatorId,
            "-SourceTracePath", $resolvedTraceInputPath
        )
        if (-not [string]::IsNullOrWhiteSpace($resolvedTraceSessionId)) {
            $args += @("-SessionId", $resolvedTraceSessionId)
        }
        powershell @args
    } `
    -LogFile $handoffLog

$handoffPackageDirectory = Join-Path $handoffRoot $packageName
$handoffManifestPath = Join-Path $handoffPackageDirectory "handoff_manifest.json"
if (-not (Test-Path -Path $handoffManifestPath -PathType Leaf)) {
    throw "Missing handoff manifest: $handoffManifestPath"
}

$handoffManifest = Get-Content -Path $handoffManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($null -eq $handoffManifest) {
    throw "Could not deserialize handoff manifest."
}
if (-not [bool]$handoffManifest.readyForTraining) {
    throw "Handoff manifest reported readyForTraining=false."
}

$ackExecuted = $false
if (-not $SkipIntakeAck.IsPresent -and
    -not [string]::IsNullOrWhiteSpace($IntakeTicketId) -and
    -not [string]::IsNullOrWhiteSpace($IntakeOperatorId)) {
    $ackLog = Join-Path $commandsDir "ops_pretrain_handoff_ack_confirm.log"
    Invoke-LoggedStep `
        -Name "ops_pretrain_handoff_ack_confirm" `
        -Command {
            powershell -ExecutionPolicy Bypass -File $ackScript `
                -HandoffPackageDirectory $handoffPackageDirectory `
                -IntakeTicketId $IntakeTicketId `
                -IntakeOperatorId $IntakeOperatorId `
                -AckStatus $AckStatus `
                -AckNotes $AckNotes
        } `
        -LogFile $ackLog
    $ackExecuted = $true
}

$summaryPath = Join-Path $notesDir "SUMMARY.md"
$summaryLines = @(
    "# OPS-003 Real Trace Ready Gate",
    "",
    ("- generatedAtUtc: {0}" -f ((Get-Date).ToUniversalTime().ToString("o", [System.Globalization.CultureInfo]::InvariantCulture))),
    "- traceInputPath: $resolvedTraceInputPath",
    "- traceSessionId: $resolvedTraceSessionId",
    "- traceDiscoveryReport: $traceDiscoveryReportPath",
    "- pretrainManifest: $pretrainManifestPath",
    "- handoffManifest: $handoffManifestPath",
    "- handoffPackageDirectory: $handoffPackageDirectory",
    "- readyForTraining(pretrain): $([bool]$pretrainManifest.readyForTraining)",
    "- readyForTraining(handoff): $([bool]$handoffManifest.readyForTraining)",
    "- intakeAckExecuted: $ackExecuted",
    "",
    "## Commands",
    "",
    "- $commandsDir",
    "",
    "Result: PASS"
)
$summaryLines | Set-Content -Path $summaryPath -Encoding UTF8

Write-Host "[DONE] OPS-003 real trace ready gate passed."
Write-Host "Summary: $summaryPath"
