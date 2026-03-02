[CmdletBinding()]
param(
    [string]$RcTag = "board-demo-rc-20260302",
    [string]$CmsUrl = "https://theraply-vr-demo.web.app/admin/",
    [string]$PackageUrl = "https://theraply-vr-demo.web.app/content/board_demo_probe_1_0_0.pkg.json",
    [string]$EvidenceRoot = "",
    [switch]$SkipValidation,
    [switch]$ManualBoardRunConfirmed
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
}

function Ensure-Directory {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -Path $Path -PathType Container)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Resolve-RepoRoot {
    $scriptRoot = Split-Path -Parent $PSCommandPath
    return (Split-Path -Parent $scriptRoot)
}

function Invoke-LoggedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @()
    )

    $logPath = Join-Path $script:CommandsDir ($Name + ".log")
    $startedAtUtc = (Get-Date).ToUniversalTime()
    $commandText = if ($Arguments.Count -gt 0) {
        "$FilePath $($Arguments -join ' ')"
    } else {
        $FilePath
    }

    "startedAtUtc=$($startedAtUtc.ToString('o'))" | Set-Content -Path $logPath -Encoding UTF8
    "workingDirectory=$WorkingDirectory" | Add-Content -Path $logPath
    "command=$commandText" | Add-Content -Path $logPath
    "" | Add-Content -Path $logPath

    $status = "PASS"
    $exitCode = 0
    $notes = ""
    try {
        Push-Location $WorkingDirectory
        & $FilePath @Arguments 2>&1 | Tee-Object -FilePath $logPath -Append | Out-Host
        if ($null -ne $LASTEXITCODE) {
            $exitCode = [int]$LASTEXITCODE
        }
    }
    catch {
        $status = "FAIL"
        $exitCode = 1
        $notes = $_.Exception.Message
        "EXCEPTION: $($_.Exception)" | Add-Content -Path $logPath
    }
    finally {
        Pop-Location
    }

    if ($status -eq "PASS" -and $exitCode -ne 0) {
        $status = "FAIL"
    }

    $script:Results.Add([PSCustomObject]@{
            Name = $Name
            Status = $status
            ExitCode = $exitCode
            LogPath = $logPath
            Notes = $notes
        }) | Out-Null
}

function Add-CheckResult {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Status,
        [string]$Details = ""
    )

    $script:Results.Add([PSCustomObject]@{
            Name = $Name
            Status = $Status
            ExitCode = if ($Status -eq "PASS") { 0 } else { 1 }
            LogPath = ""
            Notes = $Details
        }) | Out-Null
}

function Get-TextOrEmpty {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    return $Value.Trim()
}

function Invoke-WebProbe {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Url,
        [ValidateSet("GET", "HEAD")][string]$Method = "GET"
    )

    $logPath = Join-Path $script:CommandsDir ($Name + ".log")
    try {
        $response = Invoke-WebRequest -Uri $Url -Method $Method -UseBasicParsing -TimeoutSec 8
        @(
            "url=$Url",
            "method=$Method",
            "statusCode=$($response.StatusCode)",
            "rawContentLength=$($response.RawContentLength)",
            "contentLengthHeader=$($response.Headers['Content-Length'])",
            "etag=$($response.Headers['ETag'])",
            "contentType=$($response.Headers['Content-Type'])"
        ) | Set-Content -Path $logPath -Encoding UTF8

        Add-CheckResult -Name $Name -Status "PASS" -Details "HTTP $($response.StatusCode)"
    }
    catch {
        @(
            "url=$Url",
            "method=$Method",
            "error=$($_.Exception.Message)"
        ) | Set-Content -Path $logPath -Encoding UTF8
        Add-CheckResult -Name $Name -Status "FAIL" -Details $_.Exception.Message
    }
}

$repoRoot = Resolve-RepoRoot
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $EvidenceRoot = Join-Path $repoRoot ("docs\evidence\{0}\board_demo_preflight" -f $timestamp)
}

Ensure-Directory -Path $EvidenceRoot
$script:CommandsDir = Join-Path $EvidenceRoot "commands"
Ensure-Directory -Path $script:CommandsDir
$notesDir = Join-Path $EvidenceRoot "notes"
Ensure-Directory -Path $notesDir

$script:Results = New-Object 'System.Collections.Generic.List[object]'

Write-Host "Board demo preflight evidence: $EvidenceRoot"
Write-Host "RC tag target: $RcTag"
Write-Host "Skip validation: $SkipValidation"

$headCommit = ""
try {
    Push-Location $repoRoot
    $headCommit = (git rev-parse --short HEAD).Trim()
    $tagExists = @(git tag --list $RcTag).Count -gt 0
    $headTags = @(git tag --points-at HEAD)
    Pop-Location

    if ($tagExists) {
        Add-CheckResult -Name "rc_tag_exists" -Status "PASS" -Details $RcTag
    }
    else {
        Add-CheckResult -Name "rc_tag_exists" -Status "FAIL" -Details "Tag not found: $RcTag"
    }

    if ($headTags -contains $RcTag) {
        Add-CheckResult -Name "rc_tag_points_at_head" -Status "PASS" -Details "HEAD=$headCommit"
    }
    else {
        Add-CheckResult -Name "rc_tag_points_at_head" -Status "FAIL" -Details "HEAD=$headCommit, headTags=$($headTags -join ',')"
    }
}
catch {
    Add-CheckResult -Name "git_rc_tag_checks" -Status "FAIL" -Details $_.Exception.Message
    try {
        Pop-Location
    }
    catch {}
}

Invoke-WebProbe -Name "cms_domain_probe" -Url $CmsUrl -Method "GET"
Invoke-WebProbe -Name "package_url_head_probe" -Url $PackageUrl -Method "HEAD"
Invoke-WebProbe -Name "package_url_get_probe" -Url $PackageUrl -Method "GET"

if (-not $SkipValidation) {
    Invoke-LoggedCommand `
        -Name "unity_session_flow_validation_pack" `
        -WorkingDirectory $repoRoot `
        -FilePath "powershell" `
        -Arguments @(
            "-ExecutionPolicy", "Bypass",
            "-File", (Join-Path $repoRoot "scripts\unity_session_flow_validation_pack.ps1"),
            "-SkipCompile",
            "-EvidenceRoot", $EvidenceRoot
        )

    Invoke-LoggedCommand `
        -Name "flutter_analyze" `
        -WorkingDirectory (Join-Path $repoRoot "flutter_controller") `
        -FilePath "flutter" `
        -Arguments @("analyze")

    Invoke-LoggedCommand `
        -Name "flutter_test" `
        -WorkingDirectory (Join-Path $repoRoot "flutter_controller") `
        -FilePath "flutter" `
        -Arguments @("test")

    Invoke-LoggedCommand `
        -Name "unity_ops_dataset_trace_export_validate" `
        -WorkingDirectory $repoRoot `
        -FilePath "powershell" `
        -Arguments @(
            "-ExecutionPolicy", "Bypass",
            "-File", (Join-Path $repoRoot "scripts\unity_ops_dataset_trace_export_validate.ps1"),
            "-LogFile", (Join-Path $script:CommandsDir "unity_ops_dataset_trace_export_validate.log"),
            "-ExportOutputDirectory", (Join-Path $EvidenceRoot "artifacts\trace_export")
        )
}
else {
    Add-CheckResult -Name "validation_matrix" -Status "PASS" -Details "Skipped by -SkipValidation"
}

if ($ManualBoardRunConfirmed) {
    Add-CheckResult -Name "manual_board_run_confirmed" -Status "PASS" -Details "Operator confirmed target-device run."
}
else {
    Add-CheckResult -Name "manual_board_run_confirmed" -Status "FAIL" -Details "Pending target-device manual run confirmation."
}

$failed = @($script:Results | Where-Object { $_.Status -ne "PASS" })
$technicalFailed = @($failed | Where-Object { $_.Name -ne "manual_board_run_confirmed" })
$technicalGo = $technicalFailed.Count -eq 0
$boardGo = $failed.Count -eq 0

$summaryLines = @(
    "# Board Demo Preflight Summary",
    "",
    "- generatedUtc: $((Get-Date).ToUniversalTime().ToString('o'))",
    "- repoRoot: $repoRoot",
    "- headCommit: $headCommit",
    "- rcTag: $RcTag",
    "- cmsUrl: $CmsUrl",
    "- packageUrl: $PackageUrl",
    "- skipValidation: $SkipValidation",
    "- manualBoardRunConfirmed: $ManualBoardRunConfirmed",
    "",
    "## Result",
    "",
    "- technicalGo: $technicalGo",
    "- boardGo: $boardGo",
    "",
    "## Checks",
    "",
    "| check | status | notes |",
    "| --- | --- | --- |"
)

foreach ($item in $script:Results) {
    $notes = (Get-TextOrEmpty -Value $item.Notes).Replace("|", "/")
    $summaryLines += "| $($item.Name) | $($item.Status) | $notes |"
}

$summaryLines | Set-Content -Path (Join-Path $notesDir "SUMMARY.md") -Encoding UTF8

Set-Content -Path (Join-Path $repoRoot ".last_evidence_dir") -Value $EvidenceRoot -Encoding UTF8

Write-Host "[DONE] Board demo preflight completed."
Write-Host "Summary: $(Join-Path $notesDir 'SUMMARY.md')"
Write-Host "technicalGo=$technicalGo boardGo=$boardGo"
