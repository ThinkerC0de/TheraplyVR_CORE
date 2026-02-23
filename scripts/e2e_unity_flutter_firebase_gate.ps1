[CmdletBinding()]
param(
    [string]$EvidenceRoot = "",
    [string]$UnityExe = $env:UNITY_EDITOR_PATH,
    [string]$ProjectPath = "",
    [int]$UnityValidationRuns = 1,
    [string[]]$UnityValidationGameIds = @("demo_cube_clicker"),
    [switch]$SkipFlutterController,
    [switch]$SkipAdminConsole,
    [switch]$SkipUnity
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
    $EvidenceRoot = Join-Path $repoRoot "docs\evidence\$timestamp\e2e_unity_flutter_firebase_gate"
}

$ProjectPath = Resolve-FullPath -PathValue $ProjectPath -BasePath $repoRoot
$EvidenceRoot = Resolve-FullPath -PathValue $EvidenceRoot -BasePath $repoRoot

New-Item -ItemType Directory -Force -Path $EvidenceRoot | Out-Null
$commandsDir = Join-Path $EvidenceRoot "commands"
$artifactsDir = Join-Path $EvidenceRoot "artifacts"
$notesDir = Join-Path $EvidenceRoot "notes"
New-Item -ItemType Directory -Force -Path $commandsDir, $artifactsDir, $notesDir | Out-Null

$summary = New-Object System.Collections.Generic.List[string]
$summary.Add("# Unity + Flutter + Firebase E2E Gate")
$summary.Add("")
$summary.Add(("- generatedAtUtc: {0}" -f ((Get-Date).ToUniversalTime().ToString("o", [System.Globalization.CultureInfo]::InvariantCulture))))
$summary.Add("- evidenceRoot: $EvidenceRoot")
$summary.Add("- unityValidationRuns: $UnityValidationRuns")
$summary.Add(("- unityValidationGameIds: " + ($UnityValidationGameIds -join ", ")))
$summary.Add("")
$summary.Add("## Results")
$summary.Add("")

if (-not $SkipFlutterController.IsPresent) {
    Push-Location (Join-Path $repoRoot "flutter_controller")
    try {
        Invoke-LoggedStep `
            -Name "flutter_controller: flutter analyze" `
            -Command { flutter analyze } `
            -LogFile (Join-Path $commandsDir "flutter_controller_flutter_analyze.log")
        Invoke-LoggedStep `
            -Name "flutter_controller: flutter test" `
            -Command { flutter test } `
            -LogFile (Join-Path $commandsDir "flutter_controller_flutter_test.log")
    }
    finally {
        Pop-Location
    }
    $summary.Add("- flutter_controller: PASS")
} else {
    $summary.Add("- flutter_controller: SKIPPED")
}

if (-not $SkipAdminConsole.IsPresent) {
    Push-Location (Join-Path $repoRoot "admin_console_web")
    try {
        Invoke-LoggedStep `
            -Name "admin_console_web: flutter analyze" `
            -Command { flutter analyze } `
            -LogFile (Join-Path $commandsDir "admin_console_web_flutter_analyze.log")
        Invoke-LoggedStep `
            -Name "admin_console_web: flutter test" `
            -Command { flutter test } `
            -LogFile (Join-Path $commandsDir "admin_console_web_flutter_test.log")
    }
    finally {
        Pop-Location
    }
    $summary.Add("- admin_console_web: PASS")
} else {
    $summary.Add("- admin_console_web: SKIPPED")
}

if (-not $SkipUnity.IsPresent) {
    $unitySmokeScript = Join-Path $scriptRoot "unity_editor_mvp_smoke.ps1"
    $unityEvidenceRoot = Join-Path $artifactsDir "unity_editor_mvp_smoke"
    $unityLog = Join-Path $commandsDir "unity_editor_mvp_smoke.log"

    Invoke-LoggedStep `
        -Name "unity_editor_mvp_smoke" `
        -Command {
            $unityArgs = @(
                "-ExecutionPolicy", "Bypass",
                "-File", $unitySmokeScript,
                "-ProjectPath", $ProjectPath,
                "-EvidenceRoot", $unityEvidenceRoot,
                "-ValidationRuns", $UnityValidationRuns
            )
            if (-not [string]::IsNullOrWhiteSpace($UnityExe)) {
                $unityArgs += @("-UnityExe", $UnityExe)
            }
            foreach ($gameId in $UnityValidationGameIds) {
                if (-not [string]::IsNullOrWhiteSpace($gameId)) {
                    $unityArgs += @("-ValidationGameIds", $gameId)
                }
            }

            powershell @unityArgs
        } `
        -LogFile $unityLog

    $summary.Add("- unity_editor_mvp_smoke: PASS")
    $summary.Add("- unity_evidence: $unityEvidenceRoot")
} else {
    $summary.Add("- unity_editor_mvp_smoke: SKIPPED")
}

$summary.Add("")
$summary.Add("## Artifacts")
$summary.Add("")
$summary.Add("- commands/: gate command logs")
$summary.Add("- artifacts/: nested Unity evidence")
$summary.Add("")
$summary.Add("Result: PASS")

$summaryPath = Join-Path $notesDir "SUMMARY.md"
$summary | Set-Content -Path $summaryPath -Encoding UTF8

Write-Host "[DONE] E2E gate passed."
Write-Host "Summary: $summaryPath"
