param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectId,
    [switch]$SkipBundleBuild,
    [switch]$CleanBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$bundleScript = Join-Path $repoRoot "scripts\build_admin_console_hosting_bundle.ps1"
$firebaseCli = if (Get-Command firebase.cmd -ErrorAction SilentlyContinue) {
    "firebase.cmd"
}
elseif (Get-Command firebase -ErrorAction SilentlyContinue) {
    "firebase"
}
else {
    $null
}

if ([string]::IsNullOrWhiteSpace($firebaseCli)) {
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
    Write-Host "[hosting] $firebaseCli deploy --only hosting --project $ProjectId"
    & $firebaseCli deploy --only hosting --project $ProjectId | Out-Host
}
finally {
    Pop-Location
}
