[CmdletBinding()]
param(
    [string]$UnityExe = $env:UNITY_EDITOR_PATH,
    [string]$ProjectPath = "",
    [string]$EvidenceRoot = "",
    [int]$ValidationRuns = 3,
    [string[]]$ValidationGameIds = @("demo_cube_clicker", "pulse_target_tap", "smoke_test_game"),
    [int]$ValidationTimeoutMinutes = 12,
    [int]$PassMarkerGraceSeconds = 20,
    [int]$PollingIntervalSeconds = 2
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
        [string]$CleanupScriptPath,
        [int]$TimeoutMinutes,
        [int]$PassGraceSeconds,
        [int]$PollIntervalSeconds
    )

    function Stop-ProcessTree {
        param([int]$ProcessId)

        if ($ProcessId -le 0) {
            return
        }

        $children = @(Get-CimInstance -ClassName Win32_Process -Filter ("ParentProcessId = {0}" -f $ProcessId) -ErrorAction SilentlyContinue)
        foreach ($child in $children) {
            Stop-ProcessTree -ProcessId ([int]$child.ProcessId)
        }

        Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
    }

    function Read-ValidationSignals {
        param(
            [string]$ValidationLogPath,
            [string]$ValidationResultPath
        )

        $signal = @{
            PassDetected = $false
            PassSource = ""
            FailDetected = $false
            FailSource = ""
            FailDetails = ""
        }

        if (Test-Path -Path $ValidationLogPath -PathType Leaf) {
            if (Select-String -Path $ValidationLogPath -Pattern "\[FirebaseNetworkValidation\] PASS:" -Quiet) {
                $signal.PassDetected = $true
                $signal.PassSource = "log"
            }

            $failLine = Select-String -Path $ValidationLogPath -Pattern "\[FirebaseNetworkValidation\] FAIL:" | Select-Object -Last 1
            if ($null -ne $failLine) {
                $signal.FailDetected = $true
                $signal.FailSource = "log"
                $signal.FailDetails = $failLine.Line
            }
        }

        if (Test-Path -Path $ValidationResultPath -PathType Leaf) {
            $resultLines = Get-Content -Path $ValidationResultPath
            $statusLine = $resultLines | Where-Object { $_ -like "status=*" } | Select-Object -First 1
            $detailsLine = $resultLines | Where-Object { $_ -like "details=*" } | Select-Object -First 1

            if (-not [string]::IsNullOrWhiteSpace($detailsLine)) {
                $signal.FailDetails = $detailsLine.Substring("details=".Length)
            }

            if ($statusLine -eq "status=PASS") {
                $signal.PassDetected = $true
                if ([string]::IsNullOrWhiteSpace($signal.PassSource)) {
                    $signal.PassSource = "result_file"
                }
            } elseif ($statusLine -eq "status=FAIL") {
                $signal.FailDetected = $true
                $signal.FailSource = "result_file"
            }
        }

        return $signal
    }

    function Show-LogExcerpt {
        param(
            [string]$ValidationLogPath,
            [int]$TailCount = 120
        )

        if (Test-Path -Path $ValidationLogPath -PathType Leaf) {
            Write-Host ("----- Last {0} lines from {1} -----" -f $TailCount, $ValidationLogPath)
            Get-Content -Path $ValidationLogPath -Tail $TailCount | ForEach-Object { Write-Host $_ }
            Write-Host "----- End of log excerpt -----"
        } else {
            Write-Host "Validation log not found: $ValidationLogPath"
        }
    }

    & powershell -ExecutionPolicy Bypass -File $CleanupScriptPath -ProjectPath $UnityProjectPath | Out-Host

    Write-Host "[START] Firebase validation run=$RunIndex gameId=$GameId"
    if (Test-Path -Path $LogFilePath -PathType Leaf) {
        Remove-Item -Path $LogFilePath -Force
    }

    $resultFilePath = Join-Path $UnityProjectPath "Temp\CliValidation\firebase_network_validation_result.txt"
    if (Test-Path -Path $resultFilePath -PathType Leaf) {
        Remove-Item -Path $resultFilePath -Force
    }

    $arguments = @(
        "-batchmode",
        "-nographics",
        "-projectPath", $UnityProjectPath,
        "-buildTarget", "Android",
        "-executeMethod", "TheraplyCore.Editor.Automation.FirebaseNetworkValidation.RunFirebaseNetworkValidation",
        "-validationGameId=$GameId",
        "-logFile", $LogFilePath
    )

    $process = Start-Process -FilePath $UnityExecutable -ArgumentList $arguments -NoNewWindow -PassThru
    $startAt = Get-Date
    $passObservedAt = $null

    while ($true) {
        $signals = Read-ValidationSignals -ValidationLogPath $LogFilePath -ValidationResultPath $resultFilePath
        if ($signals.FailDetected) {
            if (-not $process.HasExited) {
                Stop-ProcessTree -ProcessId ([int]$process.Id)
                $process.WaitForExit(5000) | Out-Null
            }

            Show-LogExcerpt -ValidationLogPath $LogFilePath -TailCount 120
            throw ("Firebase validation failed (run={0}, gameId={1}, source={2}, details={3})." -f $RunIndex, $GameId, $signals.FailSource, $signals.FailDetails)
        }

        if ($signals.PassDetected -and $null -eq $passObservedAt) {
            $passObservedAt = Get-Date
            Write-Host ("[INFO] PASS marker detected (source={0}) for run={1} gameId={2}" -f $signals.PassSource, $RunIndex, $GameId)
        }

        if ($process.HasExited) {
            $exitCode = [int]$process.ExitCode
            if ($exitCode -ne 0) {
                Write-Host "[FAIL] Unity exited with code $exitCode for run=$RunIndex gameId=$GameId"
                Show-LogExcerpt -ValidationLogPath $LogFilePath -TailCount 120
                throw "Firebase validation failed (run=$RunIndex, gameId=$GameId, exitCode=$exitCode)."
            }

            if (-not $signals.PassDetected) {
                Show-LogExcerpt -ValidationLogPath $LogFilePath -TailCount 120
                throw "Unity exited successfully but PASS marker was not found (run=$RunIndex, gameId=$GameId)."
            }

            Write-Host "[OK] Firebase validation run=$RunIndex gameId=$GameId"
            break
        }

        $elapsed = (Get-Date) - $startAt
        if ($signals.PassDetected -and $null -ne $passObservedAt) {
            $sincePass = (Get-Date) - $passObservedAt
            if ($sincePass.TotalSeconds -ge $PassGraceSeconds) {
                Write-Host ("[WARN] Unity process still running {0:n1}s after PASS marker. Forcing shutdown for run={1} gameId={2}." -f $sincePass.TotalSeconds, $RunIndex, $GameId)
                Stop-ProcessTree -ProcessId ([int]$process.Id)
                $process.WaitForExit(5000) | Out-Null
                Write-Host "[OK] Firebase validation run=$RunIndex gameId=$GameId (completed via PASS marker)."
                break
            }
        }

        if ($elapsed.TotalMinutes -ge $TimeoutMinutes) {
            Stop-ProcessTree -ProcessId ([int]$process.Id)
            $process.WaitForExit(5000) | Out-Null

            if ($signals.PassDetected) {
                Write-Host ("[WARN] Firebase validation exceeded timeout ({0} min) but PASS marker was detected; treated as PASS." -f $TimeoutMinutes)
                Write-Host "[OK] Firebase validation run=$RunIndex gameId=$GameId (timeout with PASS marker)."
                break
            }

            Show-LogExcerpt -ValidationLogPath $LogFilePath -TailCount 120
            throw ("Firebase validation timed out after {0} minutes without PASS marker (run={1}, gameId={2})." -f $TimeoutMinutes, $RunIndex, $GameId)
        }

        Start-Sleep -Seconds $PollIntervalSeconds
    }
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
if ($ValidationTimeoutMinutes -lt 1) {
    throw "ValidationTimeoutMinutes must be >= 1"
}
if ($PassMarkerGraceSeconds -lt 1) {
    throw "PassMarkerGraceSeconds must be >= 1"
}
if ($PollingIntervalSeconds -lt 1) {
    throw "PollingIntervalSeconds must be >= 1"
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
Write-Host ("Validation timeout (minutes): " + $ValidationTimeoutMinutes)
Write-Host ("PASS marker grace (seconds): " + $PassMarkerGraceSeconds)

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
            -CleanupScriptPath $cleanupScript `
            -TimeoutMinutes $ValidationTimeoutMinutes `
            -PassGraceSeconds $PassMarkerGraceSeconds `
            -PollIntervalSeconds $PollingIntervalSeconds
    }
}

$summaryPath = Join-Path $EvidenceRoot "SUMMARY.md"
$summaryLines = @(
    "# Unity Editor MVP Smoke Summary",
    "",
    ("Timestamp (UTC): {0:yyyy-MM-dd HH:mm:ss}" -f (Get-Date).ToUniversalTime()),
    "Validation runs: $ValidationRuns",
    ("Validation game IDs: " + ($ValidationGameIds -join ", ")),
    ("Validation timeout (minutes): " + $ValidationTimeoutMinutes),
    ("PASS marker grace (seconds): " + $PassMarkerGraceSeconds),
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
