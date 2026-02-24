param(
    [switch]$SkipPubGet,
    [switch]$CleanBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$adminAppDir = Join-Path $repoRoot "admin_console_web"
$adminBuildDir = Join-Path $adminAppDir "build\web"
$hostingPublicDir = Join-Path $repoRoot "hosting\public"
$hostingAdminDir = Join-Path $hostingPublicDir "admin"

if (!(Test-Path $adminAppDir)) {
    throw "Missing admin console directory: $adminAppDir"
}

if (!(Get-Command flutter -ErrorAction SilentlyContinue)) {
    throw "Flutter CLI is not available in PATH."
}

if (!(Test-Path $hostingPublicDir)) {
    New-Item -ItemType Directory -Path $hostingPublicDir -Force | Out-Null
}

Push-Location $adminAppDir
try {
    if (-not $SkipPubGet) {
        Write-Host "[admin_console_web] flutter pub get"
        flutter pub get | Out-Host
    }

    if ($CleanBuild -and (Test-Path $adminBuildDir)) {
        Write-Host "[admin_console_web] Cleaning existing build/web"
        Remove-Item $adminBuildDir -Recurse -Force
    }

    Write-Host "[admin_console_web] flutter build web --release --base-href /admin/"
    flutter build web --release --base-href /admin/ | Out-Host
}
finally {
    Pop-Location
}

if (!(Test-Path $adminBuildDir)) {
    throw "Build output not found: $adminBuildDir"
}

if (!(Test-Path $hostingAdminDir)) {
    New-Item -ItemType Directory -Path $hostingAdminDir -Force | Out-Null
}

$existing = Get-ChildItem $hostingAdminDir -Force -ErrorAction SilentlyContinue
foreach ($item in $existing) {
    if ($item.Name -ne ".gitkeep") {
        Remove-Item $item.FullName -Recurse -Force
    }
}

Copy-Item (Join-Path $adminBuildDir "*") $hostingAdminDir -Recurse -Force

if (!(Test-Path (Join-Path $hostingAdminDir ".gitkeep"))) {
    New-Item -ItemType File -Path (Join-Path $hostingAdminDir ".gitkeep") -Force | Out-Null
}

Write-Host "[hosting] Admin console bundle updated at: $hostingAdminDir"
