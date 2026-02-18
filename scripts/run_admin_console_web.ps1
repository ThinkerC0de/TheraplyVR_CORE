param(
    [string]$Device = "chrome",
    [switch]$UseDemoFallback,
    [string]$FirebaseApiKey = "",
    [string]$FirebaseAppId = "",
    [string]$FirebaseMessagingSenderId = "",
    [string]$FirebaseProjectId = "",
    [string]$FirebaseAuthDomain = "",
    [string]$FirebaseStorageBucket = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$appDir = Join-Path $repoRoot "admin_console_web"

if (!(Test-Path $appDir)) {
    throw "Missing directory: $appDir"
}

Push-Location $appDir
try {
    Write-Host "[admin_console_web] flutter pub get"
    flutter pub get | Out-Host

    if ($UseDemoFallback) {
        Write-Host "[admin_console_web] Starting with built-in demo Firebase config (theraply-vr-demo)"
        flutter run -d $Device | Out-Host
        return
    }

    $hasCustomConfig = @(
        $FirebaseApiKey,
        $FirebaseAppId,
        $FirebaseMessagingSenderId,
        $FirebaseProjectId
    ) -notcontains ""

    if (-not $hasCustomConfig) {
        Write-Host "[admin_console_web] Starting without explicit dart-defines. Fallback config will be used."
        flutter run -d $Device | Out-Host
        return
    }

    $args = @(
        "run",
        "-d", $Device,
        "--dart-define=FIREBASE_API_KEY=$FirebaseApiKey",
        "--dart-define=FIREBASE_APP_ID=$FirebaseAppId",
        "--dart-define=FIREBASE_MESSAGING_SENDER_ID=$FirebaseMessagingSenderId",
        "--dart-define=FIREBASE_PROJECT_ID=$FirebaseProjectId"
    )

    if ($FirebaseAuthDomain.Trim().Length -gt 0) {
        $args += "--dart-define=FIREBASE_AUTH_DOMAIN=$FirebaseAuthDomain"
    }
    if ($FirebaseStorageBucket.Trim().Length -gt 0) {
        $args += "--dart-define=FIREBASE_STORAGE_BUCKET=$FirebaseStorageBucket"
    }

    Write-Host "[admin_console_web] Starting with explicit Firebase dart-defines"
    flutter @args | Out-Host
}
finally {
    Pop-Location
}
