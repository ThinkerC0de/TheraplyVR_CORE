param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectId,
    [switch]$SkipBundleBuild,
    [switch]$CleanBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$bundleScript = Join-Path $repoRoot "scripts\build_admin_console_hosting_bundle.ps1"

if (!(Get-Command firebase -ErrorAction SilentlyContinue)) {
    throw "Firebase CLI is not available in PATH."
}

if (-not $SkipBundleBuild) {
    if (!(Test-Path $bundleScript)) {
        throw "Missing script: $bundleScript"
    }

    & $bundleScript -CleanBuild:$CleanBuild
}

Push-Location $repoRoot
try {
    Write-Host "[hosting] firebase deploy --only hosting --project $ProjectId"
    firebase deploy --only hosting --project $ProjectId | Out-Host
}
finally {
    Pop-Location
}
