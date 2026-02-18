[CmdletBinding()]
param(
    [string]$ProjectPath = "",
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot "unity-quest-template"
}
$ProjectPath = (Resolve-Path $ProjectPath).Path

$utmpPath = Join-Path $ProjectPath ".utmp"

if (-not (Test-Path -Path $utmpPath)) {
    Write-Host "[cleanup_unity_utmp] No stale .utmp state found: $utmpPath"
    return
}

$attempt = 0
$maxAttempts = 3

while ($attempt -lt $maxAttempts) {
    $attempt++
    try {
        Remove-Item -Path $utmpPath -Recurse -Force -ErrorAction Stop
        Write-Host "[cleanup_unity_utmp] Removed stale .utmp state: $utmpPath"
        return
    }
    catch {
        if ($VerboseOutput) {
            Write-Warning "[cleanup_unity_utmp] Attempt $attempt/$maxAttempts failed: $($_.Exception.Message)"
        }

        if ($attempt -ge $maxAttempts) {
            throw "Unable to remove stale .utmp state after $maxAttempts attempts: $utmpPath"
        }

        Start-Sleep -Milliseconds 500
    }
}
