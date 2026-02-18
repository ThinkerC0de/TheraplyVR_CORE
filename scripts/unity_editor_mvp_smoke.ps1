[CmdletBinding()]
param(
    [string]$UnityExe = $env:UNITY_EDITOR_PATH,
    [string]$ProjectPath = "",
    [string]$EvidenceRoot = "",
    [int]$ValidationRuns = 3,
    [string[]]$ValidationGameIds = @("demo_cube_clicker", "pulse_target_tap", "smoke_test_game")
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

function Invoke-UnityFirebaseValidation {
    param(
        [string]$UnityExecutable,
        [string]$UnityProjectPath,
        [string]$GameId,
        [int]$RunIndex,
        [string]$LogFilePath,
        [string]$CleanupScriptPath
    )

    & powershell -ExecutionPolicy Bypass -File $CleanupScriptPath -ProjectPath $UnityProjectPath | Out-Host

    Write-Host "[START] Firebase validation run=$RunIndex gameId=$GameId"
    $arguments = @(
        "-batchmode",
        "-nographics",
        "-projectPath", $UnityProjectPath,
        "-buildTarget", "Android",
        "-executeMethod", "TheraplyCore.Editor.Automation.FirebaseNetworkValidation.RunFirebaseNetworkValidation",
        "-validationGameId=$GameId",
        "-logFile", $LogFilePath
    )

    $process = Start-Process -FilePath $UnityExecutable -ArgumentList $arguments -NoNewWindow -Wait -PassThru
    $exitCode = [int]$process.ExitCode
    if ($exitCode -ne 0) {
        Write-Host "[FAIL] Unity exited with code $exitCode for run=$RunIndex gameId=$GameId"
        if (Test-Path -Path $LogFilePath) {
            Write-Host "----- Last 80 lines from $LogFilePath -----"
            Get-Content -Path $LogFilePath -Tail 80 | ForEach-Object { Write-Host $_ }
            Write-Host "----- End of log excerpt -----"
        }
        throw "Firebase validation failed (run=$RunIndex, gameId=$GameId, exitCode=$exitCode)."
    }

    if (-not (Test-Path -Path $LogFilePath -PathType Leaf)) {
        throw "Expected Unity validation log file was not produced: $LogFilePath"
    }

    $passMarker = Select-String -Path $LogFilePath -Pattern "\[FirebaseNetworkValidation\] PASS:" -SimpleMatch:$false -Quiet
    if (-not $passMarker) {
        Write-Host "----- Last 120 lines from $LogFilePath -----"
        Get-Content -Path $LogFilePath -Tail 120 | ForEach-Object { Write-Host $_ }
        Write-Host "----- End of log excerpt -----"
        throw "PASS marker not found for run=$RunIndex gameId=$GameId."
    }

    Write-Host "[OK] Firebase validation run=$RunIndex gameId=$GameId"
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$unityCliScript = Join-Path $scriptRoot "unity_cli_validate.ps1"
$cleanupScript = Join-Path $scriptRoot "cleanup_unity_utmp.ps1"

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot "unity-quest-template"
}
$ProjectPath = (Resolve-Path $ProjectPath).Path
$UnityExe = Resolve-UnityExecutable $UnityExe

if ($ValidationRuns -lt 1) {
    throw "ValidationRuns must be >= 1"
}

$normalizedGameIds = New-Object System.Collections.Generic.List[string]
foreach ($rawGameId in $ValidationGameIds) {
    if ([string]::IsNullOrWhiteSpace($rawGameId)) {
        continue
    }

    $parts = $rawGameId.Split(",", [StringSplitOptions]::RemoveEmptyEntries)
    foreach ($part in $parts) {
        $trimmed = $part.Trim()
        if (-not [string]::IsNullOrWhiteSpace($trimmed)) {
            $normalizedGameIds.Add($trimmed)
        }
    }
}

$ValidationGameIds = $normalizedGameIds | Select-Object -Unique

if ($ValidationGameIds.Count -eq 0) {
    throw "ValidationGameIds must contain at least one gameId."
}

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $ts = Get-Date -Format "yyyyMMdd_HHmmss"
    $EvidenceRoot = Join-Path $repoRoot ("docs\evidence\unity_editor_mvp_{0}" -f $ts)
}

New-Item -ItemType Directory -Force -Path $EvidenceRoot | Out-Null
$commandsDir = Join-Path $EvidenceRoot "commands"
$unityLogsDir = Join-Path $EvidenceRoot "artifacts\unity_editor_mvp"
New-Item -ItemType Directory -Force -Path $commandsDir | Out-Null
New-Item -ItemType Directory -Force -Path $unityLogsDir | Out-Null

$unityCliLog = Join-Path $commandsDir "unity_cli_validate_for_editor_mvp.log"

Write-Host "Unity executable: $UnityExe"
Write-Host "Unity project: $ProjectPath"
Write-Host "Evidence root: $EvidenceRoot"
Write-Host "Validation runs: $ValidationRuns"
Write-Host ("Validation game IDs: " + ($ValidationGameIds -join ", "))

& powershell -ExecutionPolicy Bypass -File $cleanupScript -ProjectPath $ProjectPath | Out-Host
& powershell -ExecutionPolicy Bypass -File $unityCliScript -Mode both -UnityExe $UnityExe -ProjectPath $ProjectPath *>&1 |
    Tee-Object -FilePath $unityCliLog | Out-Host

for ($run = 1; $run -le $ValidationRuns; $run++) {
    foreach ($gameId in $ValidationGameIds) {
        $safeGameId = ($gameId -replace '[^a-zA-Z0-9_-]', '_')
        $logFile = Join-Path $unityLogsDir ("firebase_validation_run{0}_{1}.log" -f $run, $safeGameId)
        Invoke-UnityFirebaseValidation `
            -UnityExecutable $UnityExe `
            -UnityProjectPath $ProjectPath `
            -GameId $gameId `
            -RunIndex $run `
            -LogFilePath $logFile `
            -CleanupScriptPath $cleanupScript
    }
}

$summaryPath = Join-Path $EvidenceRoot "SUMMARY.md"
$summaryLines = @(
    "# Unity Editor MVP Smoke Summary",
    "",
    ("Timestamp (UTC): {0:yyyy-MM-dd HH:mm:ss}" -f (Get-Date).ToUniversalTime()),
    "Validation runs: $ValidationRuns",
    ("Validation game IDs: " + ($ValidationGameIds -join ", ")),
    "",
    "Artifacts:",
    "- $unityCliLog",
    "- $unityLogsDir",
    "",
    "Result: PASS"
)
$summaryLines | Set-Content -Path $summaryPath -Encoding UTF8

Write-Host "[DONE] Unity Editor MVP smoke completed successfully."
Write-Host "Summary: $summaryPath"
