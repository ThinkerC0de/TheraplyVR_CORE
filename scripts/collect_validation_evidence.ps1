[CmdletBinding()]
param(
    [ValidateSet("collect", "quick", "full")]
    [string]$Preset = "quick",
    [string]$OutputDirectory = "",
    [string]$UnityExe = $env:UNITY_EDITOR_PATH,
    [switch]$AllowUnityWhenEditorRunning,
    [switch]$IncludeAdbLogcat,
    [switch]$NoZip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
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

function Ensure-Directory {
    param([string]$Path)

    if (-not (Test-Path -Path $Path -PathType Container)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Write-TextFile {
    param(
        [string]$Path,
        [string[]]$Lines
    )

    Ensure-Directory (Split-Path -Parent $Path)
    $Lines | Set-Content -Path $Path -Encoding UTF8
}

function Add-CommandResult {
    param(
        [string]$Id,
        [string]$Status,
        [int]$ExitCode,
        [string]$Command,
        [string]$WorkingDirectory,
        [string]$LogFile,
        [DateTime]$StartedAtUtc,
        [DateTime]$FinishedAtUtc,
        [string]$Notes = ""
    )

    $script:commandResults.Add([PSCustomObject]@{
            id = $Id
            status = $Status
            exitCode = $ExitCode
            command = $Command
            workingDirectory = $WorkingDirectory
            logFile = $LogFile
            startedAtUtc = $StartedAtUtc.ToString("o")
            finishedAtUtc = $FinishedAtUtc.ToString("o")
            notes = $Notes
        }) | Out-Null
}

function Invoke-LoggedCommand {
    param(
        [string]$Id,
        [string]$WorkingDirectory,
        [string]$FilePath,
        [string[]]$Arguments
    )

    $startedAtUtc = (Get-Date).ToUniversalTime()
    $logFile = Join-Path $commandsDir ($Id + ".log")
    $commandDisplay = $FilePath
    if ($Arguments.Count -gt 0) {
        $commandDisplay += " " + ($Arguments -join " ")
    }

    Write-TextFile -Path $logFile -Lines @(
        "startedAtUtc=$($startedAtUtc.ToString('o'))",
        "workingDirectory=$WorkingDirectory",
        "command=$commandDisplay",
        ""
    )

    $exitCode = 0
    $status = "PASS"
    $notes = ""

    try {
        Push-Location $WorkingDirectory
        & $FilePath @Arguments 2>&1 | Tee-Object -FilePath $logFile -Append | Out-Host
        if ($null -ne $LASTEXITCODE) {
            $exitCode = [int]$LASTEXITCODE
        }
    }
    catch {
        $status = "FAIL"
        $exitCode = 1
        $notes = $_.Exception.Message
        "EXCEPTION: $($_.Exception)" | Add-Content -Path $logFile
    }
    finally {
        Pop-Location
    }

    if ($status -ne "FAIL" -and $exitCode -ne 0) {
        $status = "FAIL"
    }

    $finishedAtUtc = (Get-Date).ToUniversalTime()
    Add-CommandResult `
        -Id $Id `
        -Status $status `
        -ExitCode $exitCode `
        -Command $commandDisplay `
        -WorkingDirectory $WorkingDirectory `
        -LogFile (Resolve-Path $logFile).Path `
        -StartedAtUtc $startedAtUtc `
        -FinishedAtUtc $finishedAtUtc `
        -Notes $notes

    return [PSCustomObject]@{
        id = $Id
        status = $status
        exitCode = $exitCode
        logFile = $logFile
    }
}

function Invoke-CapturedProcess {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory
    )

    $stdoutPath = [System.IO.Path]::GetTempFileName()
    $stderrPath = [System.IO.Path]::GetTempFileName()

    $process = Start-Process `
        -FilePath $FilePath `
        -ArgumentList $Arguments `
        -WorkingDirectory $WorkingDirectory `
        -NoNewWindow `
        -Wait `
        -PassThru `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath

    $stdout = @()
    $stderr = @()
    if (Test-Path -Path $stdoutPath -PathType Leaf) {
        $stdout = @(Get-Content -Path $stdoutPath)
    }
    if (Test-Path -Path $stderrPath -PathType Leaf) {
        $stderr = @(Get-Content -Path $stderrPath)
    }

    try { Remove-Item -Path $stdoutPath -Force -ErrorAction Stop } catch {}
    try { Remove-Item -Path $stderrPath -Force -ErrorAction Stop } catch {}

    return [PSCustomObject]@{
        exitCode = [int]$process.ExitCode
        stdout = $stdout
        stderr = $stderr
        stdoutPath = $stdoutPath
        stderrPath = $stderrPath
    }
}

function Add-BlockedResult {
    param(
        [string]$Id,
        [string]$Command,
        [string]$WorkingDirectory,
        [string]$Notes
    )

    $nowUtc = (Get-Date).ToUniversalTime()
    $logFile = Join-Path $commandsDir ($Id + ".log")
    Write-TextFile -Path $logFile -Lines @(
        "status=BLOCKED",
        "notes=$Notes",
        "command=$Command"
    )

    Add-CommandResult `
        -Id $Id `
        -Status "BLOCKED" `
        -ExitCode -1 `
        -Command $Command `
        -WorkingDirectory $WorkingDirectory `
        -LogFile (Resolve-Path $logFile).Path `
        -StartedAtUtc $nowUtc `
        -FinishedAtUtc $nowUtc `
        -Notes $Notes
}

function Copy-ArtifactIfExists {
    param(
        [string]$Source,
        [string]$RelativeDestination
    )

    if (-not (Test-Path -Path $Source)) {
        return
    }

    $target = Join-Path $artifactsDir $RelativeDestination
    Ensure-Directory (Split-Path -Parent $target)

    if (Test-Path -Path $Source -PathType Container) {
        Ensure-Directory $target
        Copy-Item -Path (Join-Path $Source "*") -Destination $target -Recurse -Force -ErrorAction SilentlyContinue
        return
    }

    Copy-Item -Path $Source -Destination $target -Force
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$flutterRoot = Join-Path $repoRoot "flutter_controller"
$unityProjectRoot = Join-Path $repoRoot "unity-quest-template"
$unityCliScript = Join-Path $scriptRoot "unity_cli_validate.ps1"
$powershellExe = Join-Path $PSHOME "powershell.exe"
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot ("docs\evidence\" + $timestamp)
}

Ensure-Directory $OutputDirectory
$commandsDir = Join-Path $OutputDirectory "commands"
$snapshotsDir = Join-Path $OutputDirectory "snapshots"
$artifactsDir = Join-Path $OutputDirectory "artifacts"
Ensure-Directory $commandsDir
Ensure-Directory $snapshotsDir
Ensure-Directory $artifactsDir

$script:commandResults = New-Object 'System.Collections.Generic.List[object]'
$notes = New-Object 'System.Collections.Generic.List[string]'

Write-Host "Evidence output directory: $OutputDirectory"
Write-Host "Preset: $Preset"

# Snapshot machine + repo context.
$nowUtc = (Get-Date).ToUniversalTime().ToString("o")
$machineLines = @(
    "timestampUtc=$nowUtc",
    "computerName=$env:COMPUTERNAME",
    "userName=$env:USERNAME",
    "powershellVersion=$($PSVersionTable.PSVersion)",
    "repoRoot=$repoRoot",
    "flutterRoot=$flutterRoot",
    "unityProjectRoot=$unityProjectRoot"
)
Write-TextFile -Path (Join-Path $snapshotsDir "environment.txt") -Lines $machineLines

$gitBranchText = ""
$gitHeadText = ""
try {
    $gitBranchResult = Invoke-CapturedProcess -FilePath "git" -Arguments @("branch", "--show-current") -WorkingDirectory $repoRoot
    $gitHeadResult = Invoke-CapturedProcess -FilePath "git" -Arguments @("rev-parse", "--short", "HEAD") -WorkingDirectory $repoRoot
    $gitStatusResult = Invoke-CapturedProcess -FilePath "git" -Arguments @("status", "--short") -WorkingDirectory $repoRoot
    $gitChangedResult = Invoke-CapturedProcess -FilePath "git" -Arguments @("diff", "--name-only") -WorkingDirectory $repoRoot

    $gitBranchText = ($gitBranchResult.stdout -join " ").Trim()
    $gitHeadText = ($gitHeadResult.stdout -join " ").Trim()

    Write-TextFile -Path (Join-Path $snapshotsDir "git.txt") -Lines @(
        "branch=$gitBranchText",
        "head=$gitHeadText",
        "",
        "[git branch --show-current stderr]",
        $gitBranchResult.stderr,
        "",
        "[git rev-parse --short HEAD stderr]",
        $gitHeadResult.stderr,
        "",
        "[git status --short stderr]",
        $gitStatusResult.stderr,
        "",
        "[git diff --name-only stderr]",
        $gitChangedResult.stderr,
        "",
        "[git status --short]",
        $gitStatusResult.stdout,
        "",
        "[git diff --name-only]",
        $gitChangedResult.stdout
    )

    foreach ($result in @($gitBranchResult, $gitHeadResult, $gitStatusResult, $gitChangedResult)) {
        if ($result.exitCode -ne 0) {
            $notes.Add("git command returned non-zero exit code ($($result.exitCode)). See snapshots/git.txt.") | Out-Null
            break
        }
    }
}
catch {
    $notes.Add("Failed to capture git snapshot: $($_.Exception.Message)") | Out-Null
}

$unityProcesses = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like "Unity" })
$unityProcessSummary = @()
foreach ($process in $unityProcesses) {
    $unityProcessSummary += ("{0} pid={1}" -f $process.ProcessName, $process.Id)
}
Write-TextFile -Path (Join-Path $snapshotsDir "unity_processes.txt") -Lines @(
    "unityProcessCount=$($unityProcesses.Count)",
    $unityProcessSummary
)

$runFlutter = $false
$runUnityCli = $false
$runFirebaseValidation = $false

switch ($Preset) {
    "collect" {
        $runFlutter = $false
        $runUnityCli = $false
        $runFirebaseValidation = $false
    }
    "quick" {
        $runFlutter = $true
        $runUnityCli = $true
        $runFirebaseValidation = $false
    }
    "full" {
        $runFlutter = $true
        $runUnityCli = $true
        $runFirebaseValidation = $true
    }
}

if ($runFlutter) {
    Invoke-LoggedCommand -Id "flutter_analyze" -WorkingDirectory $flutterRoot -FilePath "flutter" -Arguments @("analyze") | Out-Null
    Invoke-LoggedCommand -Id "flutter_test" -WorkingDirectory $flutterRoot -FilePath "flutter" -Arguments @("test") | Out-Null
    Invoke-LoggedCommand -Id "flutter_build_apk_debug" -WorkingDirectory $flutterRoot -FilePath "flutter" -Arguments @("build", "apk", "--debug") | Out-Null
}

if ($runUnityCli) {
    $unityBlockReason = ""
    if ($unityProcesses.Count -gt 0 -and -not $AllowUnityWhenEditorRunning) {
        $unityBlockReason = "Unity process detected. Close Unity editor instances or run with -AllowUnityWhenEditorRunning."
    }

    if ([string]::IsNullOrWhiteSpace($unityBlockReason)) {
        Invoke-LoggedCommand `
            -Id "unity_cli_validate_both" `
            -WorkingDirectory $repoRoot `
            -FilePath $powershellExe `
            -Arguments @("-ExecutionPolicy", "Bypass", "-File", $unityCliScript, "-Mode", "both") | Out-Null
    }
    else {
        Add-BlockedResult `
            -Id "unity_cli_validate_both" `
            -Command ("powershell -ExecutionPolicy Bypass -File `"" + $unityCliScript + "`" -Mode both") `
            -WorkingDirectory $repoRoot `
            -Notes $unityBlockReason
        $notes.Add($unityBlockReason) | Out-Null
    }
}

if ($runFirebaseValidation) {
    $unityBlockReason = ""
    if ($unityProcesses.Count -gt 0 -and -not $AllowUnityWhenEditorRunning) {
        $unityBlockReason = "Unity process detected. Firebase validation requires batchmode access to project."
    }

    if ([string]::IsNullOrWhiteSpace($unityBlockReason)) {
        $resolvedUnityExe = Resolve-UnityExecutable $UnityExe
        $firebaseUnityLog = Join-Path $commandsDir "unity_firebase_network_validation.log"
        Invoke-LoggedCommand `
            -Id "unity_firebase_network_validation" `
            -WorkingDirectory $repoRoot `
            -FilePath $resolvedUnityExe `
            -Arguments @(
                "-batchmode",
                "-nographics",
                "-quit",
                "-projectPath", $unityProjectRoot,
                "-executeMethod", "TheraplyCore.Editor.Automation.FirebaseNetworkValidation.RunFirebaseNetworkValidation",
                "-logFile", $firebaseUnityLog
            ) | Out-Null
    }
    else {
        Add-BlockedResult `
            -Id "unity_firebase_network_validation" `
            -Command "Unity.exe -batchmode ... -executeMethod TheraplyCore.Editor.Automation.FirebaseNetworkValidation.RunFirebaseNetworkValidation" `
            -WorkingDirectory $repoRoot `
            -Notes $unityBlockReason
        $notes.Add($unityBlockReason) | Out-Null
    }
}

if ($IncludeAdbLogcat) {
    $adb = Get-Command adb -ErrorAction SilentlyContinue
    if ($null -eq $adb) {
        $notes.Add("adb not found in PATH. Skipped adb capture.") | Out-Null
    }
    else {
        Invoke-LoggedCommand -Id "adb_devices" -WorkingDirectory $repoRoot -FilePath "adb" -Arguments @("devices", "-l") | Out-Null
        Invoke-LoggedCommand -Id "adb_logcat_dump" -WorkingDirectory $repoRoot -FilePath "adb" -Arguments @("logcat", "-d") | Out-Null
    }
}

# Collect useful artifacts even if commands were not executed in this run.
Copy-ArtifactIfExists -Source (Join-Path $unityProjectRoot "Temp\CliValidation\logs") -RelativeDestination "unity_cli_logs"
Copy-ArtifactIfExists -Source (Join-Path $unityProjectRoot "Temp\CliValidation\build\TheraplyCliValidation.apk") -RelativeDestination "unity_build\TheraplyCliValidation.apk"
Copy-ArtifactIfExists -Source (Join-Path $unityProjectRoot "Temp\CliValidation\firebase_network_validation_result.txt") -RelativeDestination "unity_cli_logs\firebase_network_validation_result.txt"
Copy-ArtifactIfExists -Source (Join-Path $flutterRoot "build\app\outputs\flutter-apk\app-debug.apk") -RelativeDestination "flutter_build\app-debug.apk"

$passCount = @($commandResults | Where-Object { $_.status -eq "PASS" }).Count
$failCount = @($commandResults | Where-Object { $_.status -eq "FAIL" }).Count
$blockedCount = @($commandResults | Where-Object { $_.status -eq "BLOCKED" }).Count

$summaryLines = @(
    "# Validation Evidence Pack",
    "",
    "- generatedUtc: $((Get-Date).ToUniversalTime().ToString('o'))",
    "- preset: $Preset",
    "- repoRoot: $repoRoot",
    "- branch: $gitBranchText",
    "- head: $gitHeadText",
    "- outputDirectory: $OutputDirectory",
    "",
    "## Command Results",
    "",
    "| id | status | exitCode | log |",
    "| --- | --- | ---: | --- |"
)

foreach ($result in $commandResults) {
    $relativeLog = $result.logFile.Replace($OutputDirectory + "\", "")
    $summaryLines += ("| {0} | {1} | {2} | `{3}` |" -f $result.id, $result.status, $result.exitCode, $relativeLog)
}

$summaryLines += @(
    "",
    "## Totals",
    "",
    "- pass: $passCount",
    "- fail: $failCount",
    "- blocked: $blockedCount"
)

if ($notes.Count -gt 0) {
    $summaryLines += ""
    $summaryLines += "## Notes"
    $summaryLines += ""
    foreach ($note in $notes) {
        $summaryLines += ("- " + $note)
    }
}

Write-TextFile -Path (Join-Path $OutputDirectory "SUMMARY.md") -Lines $summaryLines

$resultsJsonPath = Join-Path $OutputDirectory "results.json"
$commandResults | ConvertTo-Json -Depth 5 | Set-Content -Path $resultsJsonPath -Encoding UTF8

$zipPath = ""
if (-not $NoZip) {
    $zipPath = $OutputDirectory.TrimEnd("\") + ".zip"
    if (Test-Path -Path $zipPath) {
        Remove-Item -Path $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $OutputDirectory "*") -DestinationPath $zipPath -Force
}

Write-Host ""
Write-Host "[DONE] Evidence pack ready."
Write-Host "Summary: $(Join-Path $OutputDirectory 'SUMMARY.md')"
Write-Host "Results JSON: $resultsJsonPath"
if (-not [string]::IsNullOrWhiteSpace($zipPath)) {
    Write-Host "ZIP: $zipPath"
}
