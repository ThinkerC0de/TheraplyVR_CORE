param(
    [Parameter(Mandatory = $true)]
    [string]$Email,
    [Parameter(Mandatory = $true)]
    [string]$ServiceAccountJsonPath
)

$ErrorActionPreference = "Stop"

if (!(Test-Path $ServiceAccountJsonPath)) {
    throw "Service account JSON not found: $ServiceAccountJsonPath"
}

$workingDir = Join-Path $env:TEMP "theraply-firebase-claims"
New-Item -ItemType Directory -Path $workingDir -Force | Out-Null

Push-Location $workingDir
try {
    if (!(Test-Path (Join-Path $workingDir "package.json"))) {
        npm.cmd init -y | Out-Host
    }

    npm.cmd i firebase-admin --silent | Out-Host
    $env:GOOGLE_APPLICATION_CREDENTIALS = (Resolve-Path $ServiceAccountJsonPath).Path

    @'
const admin = require("firebase-admin");
admin.initializeApp({ credential: admin.credential.applicationDefault() });

const email = process.argv[2];

(async () => {
  const user = await admin.auth().getUserByEmail(email);
  console.log("EMAIL:", user.email);
  console.log("UID:", user.uid);
  console.log("CLAIMS:", JSON.stringify(user.customClaims || {}));
  process.exit(0);
})().catch((error) => {
  console.error(error);
  process.exit(1);
});
'@ | Set-Content -Path (Join-Path $workingDir "check-admin-claim.js") -Encoding UTF8

    node (Join-Path $workingDir "check-admin-claim.js") $Email | Out-Host
}
finally {
    Pop-Location
}
