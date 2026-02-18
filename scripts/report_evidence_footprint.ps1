[CmdletBinding()]
param(
    [string]$EvidenceRoot = "",
    [int]$TopN = 15,
    [string]$OutputMarkdown = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Ensure-Directory {
    param([string]$Path)
    if (-not (Test-Path -Path $Path -PathType Container)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Format-Mb {
    param([long]$Bytes)
    return [math]::Round(($Bytes / 1MB), 2)
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $repoRoot "docs\evidence"
}

if (-not (Test-Path -Path $EvidenceRoot -PathType Container)) {
    throw "Evidence root not found: $EvidenceRoot"
}

$evidenceRootResolved = (Resolve-Path $EvidenceRoot).Path

$dirRows = New-Object 'System.Collections.Generic.List[object]'
$directories = @(Get-ChildItem -Path $evidenceRootResolved -Directory | Sort-Object Name)

foreach ($dir in $directories) {
    $sum = (Get-ChildItem -Path $dir.FullName -Recurse -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
    if ($null -eq $sum) {
        $sum = 0
    }

    $dirRows.Add([PSCustomObject]@{
            name = $dir.Name
            bytes = [long]$sum
            mb = Format-Mb([long]$sum)
        }) | Out-Null
}

$totalEvidenceBytes = ($dirRows | Measure-Object -Property bytes -Sum).Sum
if ($null -eq $totalEvidenceBytes) {
    $totalEvidenceBytes = 0
}

$largestFiles = @(Get-ChildItem -Path $evidenceRootResolved -Recurse -File -ErrorAction SilentlyContinue |
    Sort-Object Length -Descending |
    Select-Object -First $TopN |
    ForEach-Object {
        [PSCustomObject]@{
            path = $_.FullName.Replace($repoRoot + "\", "")
            bytes = [long]$_.Length
            mb = Format-Mb([long]$_.Length)
        }
    })

$gitCountObjects = @()
try {
    $gitCountObjects = @(git -C $repoRoot count-objects -vH 2>$null)
}
catch {
    $gitCountObjects = @("git count-objects unavailable: $($_.Exception.Message)")
}

$topEvidenceBlobs = @()
try {
    $blobLines = @(
        git -C $repoRoot rev-list --objects --all -- docs/evidence 2>$null |
        git -C $repoRoot cat-file --batch-check="%(objecttype) %(objectname) %(objectsize) %(rest)" 2>$null
    )

    $blobRows = foreach ($line in $blobLines) {
        if ($line -notlike "blob *") {
            continue
        }

        $parts = $line.Split(' ', 4)
        if ($parts.Length -lt 4) {
            continue
        }

        [PSCustomObject]@{
            sha = $parts[1]
            bytes = [long]$parts[2]
            mb = Format-Mb([long]$parts[2])
            path = $parts[3]
        }
    }

    $topEvidenceBlobs = @($blobRows | Sort-Object bytes -Descending | Select-Object -First $TopN)
}
catch {
    $topEvidenceBlobs = @([PSCustomObject]@{
            sha = ""
            bytes = 0
            mb = 0
            path = "failed to inspect blob history: $($_.Exception.Message)"
        })
}

$utcNow = (Get-Date).ToUniversalTime().ToString("o")
$totalEvidenceMb = Format-Mb([long]$totalEvidenceBytes)

Write-Host ""
Write-Host "Evidence root: $evidenceRootResolved"
Write-Host "Generated UTC: $utcNow"
Write-Host "Evidence total: $totalEvidenceMb MB"
Write-Host ""
Write-Host "Top directories by size:"
$dirRows |
Sort-Object bytes -Descending |
Select-Object -First $TopN name, @{Name = "MB"; Expression = { $_.mb } } |
Format-Table -AutoSize

Write-Host ""
Write-Host "Largest files:"
$largestFiles |
Select-Object @{Name = "MB"; Expression = { $_.mb } }, path |
Format-Table -AutoSize

Write-Host ""
Write-Host "Git object footprint:"
$gitCountObjects | ForEach-Object { Write-Host $_ }

Write-Host ""
Write-Host "Top historical blobs under docs/evidence:"
$topEvidenceBlobs |
Select-Object @{Name = "MB"; Expression = { $_.mb } }, path, sha |
Format-Table -AutoSize

if (-not [string]::IsNullOrWhiteSpace($OutputMarkdown)) {
    $reportPath = $OutputMarkdown
    if (-not [System.IO.Path]::IsPathRooted($reportPath)) {
        $reportPath = Join-Path $repoRoot $reportPath
    }

    Ensure-Directory (Split-Path -Parent $reportPath)

    $lines = @(
        "# Evidence Footprint Report",
        "",
        "- generatedUtc: $utcNow",
        "- evidenceRoot: $evidenceRootResolved",
        "- totalEvidenceMb: $totalEvidenceMb",
        "",
        "## Git Object Footprint",
        ""
    )

    foreach ($line in $gitCountObjects) {
        $lines += "- $line"
    }

    $lines += @(
        "",
        "## Largest Evidence Directories",
        "",
        "| directory | MB |",
        "| --- | ---: |"
    )

    foreach ($row in ($dirRows | Sort-Object bytes -Descending | Select-Object -First $TopN)) {
        $lines += ("| {0} | {1} |" -f $row.name, $row.mb)
    }

    $lines += @(
        "",
        "## Largest Evidence Files",
        "",
        "| file | MB |",
        "| --- | ---: |"
    )

    foreach ($row in $largestFiles) {
        $lines += ("| `{0}` | {1} |" -f $row.path, $row.mb)
    }

    $lines += @(
        "",
        "## Largest Historical Blobs (docs/evidence)",
        "",
        "| file | MB | sha |",
        "| --- | ---: | --- |"
    )

    foreach ($row in $topEvidenceBlobs) {
        $lines += ("| `{0}` | {1} | `{2}` |" -f $row.path, $row.mb, $row.sha)
    }

    $lines | Set-Content -Path $reportPath -Encoding UTF8
    Write-Host ""
    Write-Host "Report written: $reportPath"
}

