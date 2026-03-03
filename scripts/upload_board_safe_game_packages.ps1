[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [ValidateSet("ftp", "ftps", "sftp")]
    [string]$Protocol = "ftps",
    [Parameter(Mandatory = $true)]
    [string]$FtpHost,
    [Parameter(Mandatory = $true)]
    [string]$Username,
    [string]$Password = "",
    [string]$RemoteDirectory = "/public_html/content",
    [string]$PackageIndexPath = "hosting/public/content/board_safe_game_packages_index.json",
    [switch]$IncludeIndexFile,
    [switch]$SkipHttpProbe,
    [int]$HttpProbeTimeoutSec = 8,
    [switch]$InsecureTls
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-AbsolutePath {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][string]$PathValue
    )

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $PathValue))
}

function Normalize-RemoteDirectory {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    $trimmed = $PathValue.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return "/"
    }

    $normalized = $trimmed.Replace("\", "/")
    if (-not $normalized.StartsWith("/")) {
        $normalized = "/" + $normalized
    }

    return $normalized.TrimEnd("/")
}

function Upload-FileViaCurl {
    param(
        [Parameter(Mandatory = $true)][string]$CurlPath,
        [Parameter(Mandatory = $true)][string]$ProtocolValue,
        [Parameter(Mandatory = $true)][string]$FtpHostValue,
        [Parameter(Mandatory = $true)][string]$RemoteDirValue,
        [Parameter(Mandatory = $true)][string]$CredentialPair,
        [Parameter(Mandatory = $true)][string]$LocalFilePath,
        [switch]$AllowInsecureTls
    )

    $fileName = Split-Path -Leaf $LocalFilePath
    $remoteUri = "{0}://{1}{2}/{3}" -f $ProtocolValue, $FtpHostValue, $RemoteDirValue, $fileName
    $curlArgs = New-Object 'System.Collections.Generic.List[string]'
    $curlArgs.Add("--silent") | Out-Null
    $curlArgs.Add("--show-error") | Out-Null
    $curlArgs.Add("--fail") | Out-Null
    $curlArgs.Add("--ftp-create-dirs") | Out-Null

    if ([string]::Equals($ProtocolValue, "ftps", [System.StringComparison]::OrdinalIgnoreCase)) {
        $curlArgs.Add("--ssl-reqd") | Out-Null
    }
    if ($AllowInsecureTls.IsPresent) {
        $curlArgs.Add("--insecure") | Out-Null
    }

    $curlArgs.Add("--user") | Out-Null
    $curlArgs.Add($CredentialPair) | Out-Null
    $curlArgs.Add("--upload-file") | Out-Null
    $curlArgs.Add($LocalFilePath) | Out-Null
    $curlArgs.Add($remoteUri) | Out-Null

    & $CurlPath $curlArgs.ToArray()
    if ($LASTEXITCODE -ne 0) {
        throw "curl upload failed for '$fileName' (exitCode=$LASTEXITCODE)."
    }

    Write-Host ("[DONE] Uploaded: {0}" -f $remoteUri)
}

$scriptRoot = Split-Path -Parent $PSCommandPath
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $scriptRoot
}
$RepoRoot = (Resolve-Path $RepoRoot).Path

$curlPath = (Get-Command curl.exe -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($curlPath)) {
    throw "curl.exe is required but was not found in PATH."
}

$passwordValue = $Password
if ([string]::IsNullOrWhiteSpace($passwordValue)) {
    $passwordValue = [Environment]::GetEnvironmentVariable("THERAPLY_HOSTING_PASSWORD")
}
if ([string]::IsNullOrWhiteSpace($passwordValue)) {
    throw "Provide -Password or set THERAPLY_HOSTING_PASSWORD environment variable."
}

$credentialPair = "{0}:{1}" -f $Username.Trim(), $passwordValue
$remoteDirectoryNormalized = Normalize-RemoteDirectory -PathValue $RemoteDirectory

$packageIndexFullPath = Resolve-AbsolutePath -BasePath $RepoRoot -PathValue $PackageIndexPath
if (-not (Test-Path -Path $packageIndexFullPath -PathType Leaf)) {
    throw "Package index not found: $packageIndexFullPath"
}

$index = Get-Content -Path $packageIndexFullPath -Raw -Encoding UTF8 | ConvertFrom-Json
$packages = @($index.packages)
if ($packages.Count -eq 0) {
    throw "No package entries found in index: $packageIndexFullPath"
}

$filesToUpload = New-Object 'System.Collections.Generic.List[string]'
foreach ($pkg in $packages) {
    $artifactFileNames = New-Object 'System.Collections.Generic.List[string]'
    if ($null -ne $pkg.artifactFiles) {
        foreach ($artifactFile in @($pkg.artifactFiles)) {
            $candidate = [string]$artifactFile
            if (-not [string]::IsNullOrWhiteSpace($candidate)) {
                $artifactFileNames.Add($candidate.Trim()) | Out-Null
            }
        }
    }

    if ($artifactFileNames.Count -eq 0) {
        $fallbackFileName = [string]$pkg.fileName
        if ([string]::IsNullOrWhiteSpace($fallbackFileName)) {
            throw "Index entry is missing fileName/artifactFiles."
        }

        $artifactFileNames.Add($fallbackFileName.Trim()) | Out-Null
    }

    foreach ($artifactName in $artifactFileNames | Select-Object -Unique) {
        $filePath = Join-Path (Split-Path -Parent $packageIndexFullPath) $artifactName
        if (-not (Test-Path -Path $filePath -PathType Leaf)) {
            throw "Package artifact from index not found: $filePath"
        }

        $filesToUpload.Add($filePath) | Out-Null
    }
}

if ($IncludeIndexFile.IsPresent) {
    $filesToUpload.Add($packageIndexFullPath) | Out-Null
}

foreach ($filePath in $filesToUpload | Select-Object -Unique) {
    Upload-FileViaCurl `
        -CurlPath $curlPath `
        -ProtocolValue $Protocol `
        -FtpHostValue $FtpHost `
        -RemoteDirValue $remoteDirectoryNormalized `
        -CredentialPair $credentialPair `
        -LocalFilePath $filePath `
        -AllowInsecureTls:$InsecureTls
}

if (-not $SkipHttpProbe) {
    $timeoutSec = [Math]::Max(2, [Math]::Min(30, $HttpProbeTimeoutSec))
    foreach ($pkg in $packages) {
        $publicUrls = New-Object 'System.Collections.Generic.List[string]'
        $packageUrl = [string]$pkg.packageUri
        $bundleUrl = [string]$pkg.bundleUri
        if (-not [string]::IsNullOrWhiteSpace($packageUrl)) {
            $publicUrls.Add($packageUrl.Trim()) | Out-Null
        }
        if (-not [string]::IsNullOrWhiteSpace($bundleUrl)) {
            $publicUrls.Add($bundleUrl.Trim()) | Out-Null
        }

        foreach ($publicUrl in $publicUrls | Select-Object -Unique) {
            try {
                $headResponse = Invoke-WebRequest -Uri $publicUrl -Method Head -UseBasicParsing -TimeoutSec $timeoutSec
                Write-Host ("[PROBE] HEAD {0} -> {1}" -f $publicUrl, $headResponse.StatusCode)
            }
            catch {
                try {
                    $getResponse = Invoke-WebRequest -Uri $publicUrl -Method Get -UseBasicParsing -TimeoutSec $timeoutSec
                    Write-Host ("[PROBE] GET  {0} -> {1}" -f $publicUrl, $getResponse.StatusCode)
                }
                catch {
                    throw "HTTP probe failed for '$publicUrl': $($_.Exception.Message)"
                }
            }
        }
    }
}
