[CmdletBinding()]
param(
    [string]$CatalogPath = "contracts/error_catalog.json",
    [string]$OutputPath = "",
    [switch]$FailOnUnmapped
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        throw "Path is required."
    }

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue.Trim())
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $PathValue.Trim()))
}

function New-StringSet {
    return New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
}

$catalogFullPath = Resolve-FullPath -PathValue $CatalogPath
if (-not (Test-Path -LiteralPath $catalogFullPath)) {
    throw "Catalog file not found: $catalogFullPath"
}

$catalog = Get-Content -LiteralPath $catalogFullPath -Raw | ConvertFrom-Json
$catalogCodes = New-StringSet
foreach ($entry in $catalog.entries) {
    $code = [string]$entry.reasonCode
    if ([string]::IsNullOrWhiteSpace($code)) {
        continue
    }

    $null = $catalogCodes.Add($code.Trim().ToUpperInvariant())
}

$sourceRoots = @(
    "flutter_controller/lib",
    "unity-quest-template/Assets/_TheraplyCore/Games/Runtime",
    "unity-quest-template/Assets/_TheraplyCore/Games/Contracts",
    "scripts"
) | Where-Object { Test-Path -LiteralPath $_ }

if ($sourceRoots.Count -eq 0) {
    throw "No source roots found for audit."
}

$internalExceptionRoots = @(
    "unity-quest-template/Assets/_TheraplyCore/Games/Runtime"
) | Where-Object { Test-Path -LiteralPath $_ }

$reasonScanArgs = @(
    "--no-heading",
    "--line-number",
    "--glob",
    "*.dart",
    "--glob",
    "*.cs",
    "--glob",
    "*.ps1",
    "-F",
    "reasonCode"
) + $sourceRoots

$internalScanArgs = @(
    "--no-heading",
    "--line-number",
    "--glob",
    "*.cs",
    "-F",
    "InvalidOperationException("
) + $internalExceptionRoots

$reasonScanOutput = & rg @reasonScanArgs 2>$null
if ($LASTEXITCODE -gt 1) {
    throw "ripgrep reason scan failed (exitCode=$LASTEXITCODE)."
}

$internalScanOutput = & rg @internalScanArgs 2>$null
if ($LASTEXITCODE -gt 1) {
    throw "ripgrep internal exception scan failed (exitCode=$LASTEXITCODE)."
}

if ($null -eq $reasonScanOutput) {
    $reasonScanOutput = @()
} elseif ($reasonScanOutput -is [string]) {
    $reasonScanOutput = @($reasonScanOutput)
}

if ($null -eq $internalScanOutput) {
    $internalScanOutput = @()
} elseif ($internalScanOutput -is [string]) {
    $internalScanOutput = @($internalScanOutput)
}

$reasonCodeRegex = [regex]'reasonCode\s*[:=]\s*["''](?<code>[A-Z][A-Z0-9_]{2,})["'']'
$internalExceptionRegex = [regex]'InvalidOperationException\(["''](?<code>[A-Z][A-Z0-9_]{2,})["'']\)'

$reasonCandidates = New-StringSet
$internalExceptionCandidates = New-StringSet

foreach ($line in $reasonScanOutput) {
    $reasonMatches = $reasonCodeRegex.Matches($line)
    foreach ($match in $reasonMatches) {
        $code = $match.Groups["code"].Value.Trim().ToUpperInvariant()
        if ([string]::IsNullOrWhiteSpace($code)) {
            continue
        }

        $null = $reasonCandidates.Add($code)
    }

}

foreach ($line in $internalScanOutput) {
    $internalMatches = $internalExceptionRegex.Matches($line)
    foreach ($match in $internalMatches) {
        $code = $match.Groups["code"].Value.Trim().ToUpperInvariant()
        if ([string]::IsNullOrWhiteSpace($code)) {
            continue
        }

        $null = $internalExceptionCandidates.Add($code)
    }
}

$unmappedReasonCodes = @()
foreach ($code in ($reasonCandidates | Sort-Object)) {
    if (-not $catalogCodes.Contains($code)) {
        $unmappedReasonCodes += $code
    }
}

$unmappedInternalExceptionCodes = @()
foreach ($code in ($internalExceptionCandidates | Sort-Object)) {
    if (-not $catalogCodes.Contains($code)) {
        $unmappedInternalExceptionCodes += $code
    }
}

$summaryLines = @(
    "Error catalog audit summary",
    "catalogEntries: $($catalogCodes.Count)",
    "reasonCodeCandidates: $($reasonCandidates.Count)",
    "internalExceptionCandidates: $($internalExceptionCandidates.Count)",
    "unmappedReasonCodes: $($unmappedReasonCodes.Count)",
    "unmappedInternalExceptionCodes: $($unmappedInternalExceptionCodes.Count)"
)

foreach ($line in $summaryLines) {
    Write-Host $line
}

if ($unmappedReasonCodes.Count -gt 0) {
    Write-Host ""
    Write-Host "Unmapped reasonCode candidates:"
    foreach ($code in $unmappedReasonCodes) {
        Write-Host " - $code"
    }
}

if ($unmappedInternalExceptionCodes.Count -gt 0) {
    Write-Host ""
    Write-Host "Unmapped internal exception identifiers:"
    foreach ($code in $unmappedInternalExceptionCodes) {
        Write-Host " - $code"
    }
}

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $outputFullPath = Resolve-FullPath -PathValue $OutputPath
    $parent = Split-Path -Parent $outputFullPath
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    $markdown = New-Object System.Collections.Generic.List[string]
    $markdown.Add("# Error Catalog Coverage Audit")
    $markdown.Add("")
    $markdown.Add("- generatedAtUtc: $([DateTime]::UtcNow.ToString("O"))")
    $markdown.Add("- catalogEntries: $($catalogCodes.Count)")
    $markdown.Add("- reasonCodeCandidates: $($reasonCandidates.Count)")
    $markdown.Add("- internalExceptionCandidates: $($internalExceptionCandidates.Count)")
    $markdown.Add("- unmappedReasonCodes: $($unmappedReasonCodes.Count)")
    $markdown.Add("- unmappedInternalExceptionCodes: $($unmappedInternalExceptionCodes.Count)")
    $markdown.Add("")
    $markdown.Add("## Unmapped reasonCode candidates")
    if ($unmappedReasonCodes.Count -eq 0) {
        $markdown.Add("- none")
    } else {
        foreach ($code in $unmappedReasonCodes) {
            $markdown.Add("- $code")
        }
    }
    $markdown.Add("")
    $markdown.Add("## Unmapped internal exception identifiers")
    if ($unmappedInternalExceptionCodes.Count -eq 0) {
        $markdown.Add("- none")
    } else {
        foreach ($code in $unmappedInternalExceptionCodes) {
            $markdown.Add("- $code")
        }
    }

    Set-Content -LiteralPath $outputFullPath -Value $markdown -Encoding utf8
    Write-Host ""
    Write-Host "Audit report saved: $outputFullPath"
}

if ($FailOnUnmapped -and ($unmappedReasonCodes.Count -gt 0 -or $unmappedInternalExceptionCodes.Count -gt 0)) {
    exit 2
}
