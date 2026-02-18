[CmdletBinding()]
param(
    [string]$DeviceId = "",
    [string]$PackageName = "com.yourcompany.flutter_controller",
    [string]$OutputDirectory = "",
    [switch]$ClearAppData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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

function Invoke-AdbLoggedCommand {
    param(
        [string]$Id,
        [string[]]$Arguments
    )

    $logPath = Join-Path $commandsDir ($Id + ".log")
    $cmd = "adb " + ($Arguments -join " ")
    $started = (Get-Date).ToUniversalTime().ToString("o")

    Write-TextFile -Path $logPath -Lines @(
        "startedAtUtc=$started",
        "command=$cmd",
        ""
    )

    try {
        & adb @Arguments 2>&1 | Tee-Object -FilePath $logPath -Append | Out-Host
        $exitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
        return [PSCustomObject]@{
            id = $Id
            exitCode = $exitCode
            log = $logPath
        }
    }
    catch {
        "EXCEPTION: $($_.Exception.Message)" | Add-Content -Path $logPath
        return [PSCustomObject]@{
            id = $Id
            exitCode = 1
            log = $logPath
        }
    }
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot ("docs\evidence\" + $timestamp + "\mobile_real_device_preflight")
}

Ensure-Directory $OutputDirectory
$commandsDir = Join-Path $OutputDirectory "commands"
Ensure-Directory $commandsDir

$adbCommand = Get-Command adb -ErrorAction SilentlyContinue
if ($null -eq $adbCommand) {
    throw "adb not found in PATH."
}

$devicesRaw = @(adb devices)
$deviceRows = @($devicesRaw | Select-Object -Skip 1 | Where-Object { $_ -match "\S" })
$connected = @()
foreach ($row in $deviceRows) {
    $parts = $row -split "\s+"
    if ($parts.Length -ge 2 -and $parts[1] -eq "device") {
        $connected += $parts[0]
    }
}

if ($connected.Count -eq 0) {
    $summary = @(
        "# Mobile Real Device Preflight",
        "",
        "- generatedUtc: $((Get-Date).ToUniversalTime().ToString('o'))",
        "- status: BLOCKED",
        "- reason: no adb device attached (adb devices empty).",
        "",
        "## Next Step",
        "",
        "Connect phone with USB debugging enabled and rerun:",
        "powershell -ExecutionPolicy Bypass -File .\\scripts\\mobile_real_device_preflight.ps1"
    )
    Write-TextFile -Path (Join-Path $OutputDirectory "SUMMARY.md") -Lines $summary
    Write-Host "No connected device. Summary: $(Join-Path $OutputDirectory 'SUMMARY.md')"
    exit 2
}

if ([string]::IsNullOrWhiteSpace($DeviceId)) {
    $DeviceId = $connected[0]
}

if (-not ($connected -contains $DeviceId)) {
    throw "Selected device '$DeviceId' is not in connected device list: $($connected -join ', ')"
}

$results = New-Object 'System.Collections.Generic.List[object]'

$results.Add((Invoke-AdbLoggedCommand -Id "adb_devices" -Arguments @("devices", "-l"))) | Out-Null
$results.Add((Invoke-AdbLoggedCommand -Id "device_getprop_model" -Arguments @("-s", $DeviceId, "shell", "getprop", "ro.product.model"))) | Out-Null
$results.Add((Invoke-AdbLoggedCommand -Id "device_getprop_brand" -Arguments @("-s", $DeviceId, "shell", "getprop", "ro.product.brand"))) | Out-Null
$results.Add((Invoke-AdbLoggedCommand -Id "device_getprop_android_release" -Arguments @("-s", $DeviceId, "shell", "getprop", "ro.build.version.release"))) | Out-Null
$results.Add((Invoke-AdbLoggedCommand -Id "device_getprop_android_sdk" -Arguments @("-s", $DeviceId, "shell", "getprop", "ro.build.version.sdk"))) | Out-Null
$results.Add((Invoke-AdbLoggedCommand -Id "device_getprop_abi" -Arguments @("-s", $DeviceId, "shell", "getprop", "ro.product.cpu.abi"))) | Out-Null
$results.Add((Invoke-AdbLoggedCommand -Id "device_utc_date" -Arguments @("-s", $DeviceId, "shell", "date", "-u"))) | Out-Null
$results.Add((Invoke-AdbLoggedCommand -Id "device_airplane_mode" -Arguments @("-s", $DeviceId, "shell", "settings", "get", "global", "airplane_mode_on"))) | Out-Null
$results.Add((Invoke-AdbLoggedCommand -Id "device_wifi_status" -Arguments @("-s", $DeviceId, "shell", "cmd", "wifi", "status"))) | Out-Null
$results.Add((Invoke-AdbLoggedCommand -Id "app_package_presence" -Arguments @("-s", $DeviceId, "shell", "pm", "list", "packages", $PackageName))) | Out-Null
$results.Add((Invoke-AdbLoggedCommand -Id "app_package_dumpsys" -Arguments @("-s", $DeviceId, "shell", "dumpsys", "package", $PackageName))) | Out-Null

if ($ClearAppData) {
    $results.Add((Invoke-AdbLoggedCommand -Id "app_clear_data" -Arguments @("-s", $DeviceId, "shell", "pm", "clear", $PackageName))) | Out-Null
}

$failedCount = @($results | Where-Object { $_.exitCode -ne 0 }).Count
$status = if ($failedCount -eq 0) { "PASS" } else { "PARTIAL" }

$summaryLines = @(
    "# Mobile Real Device Preflight",
    "",
    "- generatedUtc: $((Get-Date).ToUniversalTime().ToString('o'))",
    "- status: $status",
    "- deviceId: $DeviceId",
    "- packageName: $PackageName",
    "- clearAppData: $ClearAppData",
    "",
    "## Command Results",
    "",
    "| id | exitCode | log |",
    "| --- | ---: | --- |"
)

foreach ($result in $results) {
    $relativeLog = $result.log.Replace($OutputDirectory + "\", "")
    $summaryLines += ("| {0} | {1} | {2} |" -f $result.id, $result.exitCode, $relativeLog)
}

$summaryLines += @(
    "",
    "## Real-Device Delta Checklist",
    "",
    "- Compare login behavior in debug vs release build (release enforces strict entitlement gate).",
    "- Verify account switch clears previous local session context.",
    "- Force reconnect path (Wi-Fi off/on) and confirm command ACK path recovers deterministically.",
    "- Capture one run with Resume and one run with Start New decision.",
    "- Record account uid/email, student id, session id, game id, and outcome."
)

Write-TextFile -Path (Join-Path $OutputDirectory "SUMMARY.md") -Lines $summaryLines

Write-Host ""
Write-Host "Preflight done. Summary: $(Join-Path $OutputDirectory 'SUMMARY.md')"
if ($failedCount -gt 0) {
    Write-Host "Result: PARTIAL ($failedCount command(s) non-zero)"
    exit 1
}
