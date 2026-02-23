[CmdletBinding()]
param(
    [string]$OutputDirectory = "",
    [string]$TraceSessionId = "",
    [string]$AdbSerial = "",
    [string[]]$QuestPackages = @(
        "com.DefaultCompany.unityquesttemplate",
        "com.unicornvrworld.theraplyvr",
        "com.unicornvrworld.theraplyvrdev",
        "com.unicornvr.theraplyvr",
        "com.unicornvr.theraplyvrdev"
    ),
    [string[]]$LocalTracePaths = @(),
    [switch]$SkipQuestPull,
    [switch]$AllowNoDatasetEvents
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$SupportedDatasetEventTypes = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$null = $SupportedDatasetEventTypes.Add("TASK_OUTCOME_SUMMARY")
$null = $SupportedDatasetEventTypes.Add("TASK_LABEL_GENERATED")
$null = $SupportedDatasetEventTypes.Add("ADAPTIVE_DIFFICULTY_ADJUSTED")

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

function Normalize-Token {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    return $Value.Trim().Replace(" ", "_").ToUpperInvariant()
}

function Resolve-AdbExecutable {
    $command = Get-Command adb -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw "ADB executable not found in PATH."
    }

    return $command.Source
}

function Resolve-AdbSerial {
    param(
        [Parameter(Mandatory = $true)][string]$AdbExecutable,
        [string]$ConfiguredSerial
    )

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredSerial)) {
        return $ConfiguredSerial.Trim()
    }

    $output = & $AdbExecutable devices
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to list ADB devices."
    }

    $connected = @()
    foreach ($line in $output) {
        if ($line -match "^\s*(?<serial>[^\s]+)\s+device\s*$") {
            $connected += $Matches["serial"]
        }
    }

    if ($connected.Count -eq 0) {
        throw "No ADB device connected."
    }

    if ($connected.Count -gt 1) {
        throw "Multiple ADB devices connected. Pass -AdbSerial explicitly."
    }

    return $connected[0]
}

function Parse-UtcDate {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $parsed = [DateTimeOffset]::MinValue
    if ([DateTimeOffset]::TryParse(
            $Value,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::AssumeUniversal,
            [ref]$parsed)) {
        return $parsed.ToUniversalTime()
    }

    return $null
}

function Initialize-SessionStats {
    param([string]$SessionId)

    return [ordered]@{
        sessionId = $SessionId
        datasetEvents = 0
        firstOccurredAtUtc = ""
        lastOccurredAtUtc = ""
        eventTypeCounts = [ordered]@{}
        sourcePaths = @()
    }
}

function Update-EventTypeCount {
    param(
        [System.Collections.IDictionary]$Map,
        [string]$EventType
    )

    if (-not $Map.Contains($EventType)) {
        $Map[$EventType] = 1
        return
    }

    $Map[$EventType] = [int]$Map[$EventType] + 1
}

function Ensure-SourceInSession {
    param(
        [System.Collections.IDictionary]$SessionStats,
        [string]$SourcePath
    )

    $sources = @($SessionStats.sourcePaths)
    if ($sources -notcontains $SourcePath) {
        $SessionStats.sourcePaths = @($sources + $SourcePath)
    }
}

function Analyze-TraceFile {
    param(
        [Parameter(Mandatory = $true)][string]$TracePath,
        [Parameter(Mandatory = $true)][hashtable]$GlobalSessionMap
    )

    if (-not (Test-Path -Path $TracePath -PathType Leaf)) {
        throw "Trace file not found: $TracePath"
    }

    $result = [ordered]@{
        totalLines = 0
        invalidLines = 0
        interactionLines = 0
        payloadParseErrors = 0
        datasetLines = 0
        eventTypeCounts = [ordered]@{}
        sessionDatasetEventCounts = @{}
    }

    foreach ($line in [System.IO.File]::ReadLines($TracePath)) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $result.totalLines++

        $record = $null
        try {
            $record = $line | ConvertFrom-Json
        }
        catch {
            $result.invalidLines++
            continue
        }

        if ($null -eq $record) {
            $result.invalidLines++
            continue
        }

        $recordEventType = Normalize-Token ([string]$record.eventType)
        if (-not [string]::Equals($recordEventType, "INTERACTION_EVENT", [System.StringComparison]::Ordinal)) {
            continue
        }

        $result.interactionLines++

        $payloadJson = [string]$record.payloadJson
        if ([string]::IsNullOrWhiteSpace($payloadJson)) {
            $result.payloadParseErrors++
            continue
        }

        $payload = $null
        try {
            $payload = $payloadJson | ConvertFrom-Json
        }
        catch {
            $result.payloadParseErrors++
            continue
        }

        if ($null -eq $payload) {
            $result.payloadParseErrors++
            continue
        }

        $payloadEventType = Normalize-Token ([string]$payload.eventType)
        if (-not $SupportedDatasetEventTypes.Contains($payloadEventType)) {
            continue
        }

        $result.datasetLines++
        Update-EventTypeCount -Map $result.eventTypeCounts -EventType $payloadEventType

        $sessionId = [string]$payload.sessionId
        if ([string]::IsNullOrWhiteSpace($sessionId)) {
            $sessionId = [string]$record.sessionId
        }
        if ([string]::IsNullOrWhiteSpace($sessionId)) {
            $sessionId = "<unknown_session>"
        }
        $sessionId = $sessionId.Trim()

        if (-not $result.sessionDatasetEventCounts.ContainsKey($sessionId)) {
            $result.sessionDatasetEventCounts[$sessionId] = 1
        }
        else {
            $result.sessionDatasetEventCounts[$sessionId] = [int]$result.sessionDatasetEventCounts[$sessionId] + 1
        }

        if (-not $GlobalSessionMap.ContainsKey($sessionId)) {
            $GlobalSessionMap[$sessionId] = Initialize-SessionStats -SessionId $sessionId
        }

        $sessionStats = $GlobalSessionMap[$sessionId]
        $sessionStats.datasetEvents = [int]$sessionStats.datasetEvents + 1
        Update-EventTypeCount -Map $sessionStats.eventTypeCounts -EventType $payloadEventType
        Ensure-SourceInSession -SessionStats $sessionStats -SourcePath $TracePath

        $occurredAtText = [string]$payload.occurredAtUtc
        if ([string]::IsNullOrWhiteSpace($occurredAtText)) {
            $occurredAtText = [string]$record.createdAtUtc
        }

        $occurredAt = Parse-UtcDate -Value $occurredAtText
        if ($null -eq $occurredAt) {
            continue
        }

        $first = Parse-UtcDate -Value ([string]$sessionStats.firstOccurredAtUtc)
        if ($null -eq $first -or $occurredAt -lt $first) {
            $sessionStats.firstOccurredAtUtc = $occurredAt.ToString("o", [System.Globalization.CultureInfo]::InvariantCulture)
        }

        $last = Parse-UtcDate -Value ([string]$sessionStats.lastOccurredAtUtc)
        if ($null -eq $last -or $occurredAt -gt $last) {
            $sessionStats.lastOccurredAtUtc = $occurredAt.ToString("o", [System.Globalization.CultureInfo]::InvariantCulture)
        }
    }

    return $result
}

function Write-PreparedTrace {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [string]$SessionIdFilter
    )

    $written = 0
    $directory = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $writer = New-Object System.IO.StreamWriter($OutputPath, $false, [System.Text.Encoding]::UTF8)
    try {
        foreach ($line in [System.IO.File]::ReadLines($SourcePath)) {
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }

            $record = $null
            try {
                $record = $line | ConvertFrom-Json
            }
            catch {
                continue
            }

            if ($null -eq $record) {
                continue
            }

            $recordEventType = Normalize-Token ([string]$record.eventType)
            if (-not [string]::Equals($recordEventType, "INTERACTION_EVENT", [System.StringComparison]::Ordinal)) {
                continue
            }

            $payloadJson = [string]$record.payloadJson
            if ([string]::IsNullOrWhiteSpace($payloadJson)) {
                continue
            }

            $payload = $null
            try {
                $payload = $payloadJson | ConvertFrom-Json
            }
            catch {
                continue
            }

            if ($null -eq $payload) {
                continue
            }

            $payloadEventType = Normalize-Token ([string]$payload.eventType)
            if (-not $SupportedDatasetEventTypes.Contains($payloadEventType)) {
                continue
            }

            if (-not [string]::IsNullOrWhiteSpace($SessionIdFilter)) {
                $lineSessionId = [string]$payload.sessionId
                if ([string]::IsNullOrWhiteSpace($lineSessionId)) {
                    $lineSessionId = [string]$record.sessionId
                }

                $lineSessionId = if ([string]::IsNullOrWhiteSpace($lineSessionId)) { "" } else { $lineSessionId.Trim() }
                if (-not [string]::Equals($lineSessionId, $SessionIdFilter, [System.StringComparison]::Ordinal)) {
                    continue
                }
            }

            $writer.WriteLine($line)
            $written++
        }
    }
    finally {
        $writer.Dispose()
    }

    return $written
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $timestamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd_HHmmss", [System.Globalization.CultureInfo]::InvariantCulture)
    $OutputDirectory = Join-Path $repoRoot "docs\evidence\$timestamp\artifacts\ops_trace_collect"
}

$outputRoot = Resolve-FullPath -PathValue $OutputDirectory
$pulledDir = Join-Path $outputRoot "pulled"
$preparedDir = Join-Path $outputRoot "prepared"
$reportsDir = Join-Path $outputRoot "reports"

New-Item -ItemType Directory -Force -Path $outputRoot, $pulledDir, $preparedDir, $reportsDir | Out-Null

$candidateFiles = @()
$adbExe = ""
$resolvedSerial = ""

if (-not $SkipQuestPull.IsPresent) {
    $adbExe = Resolve-AdbExecutable
    $resolvedSerial = Resolve-AdbSerial -AdbExecutable $adbExe -ConfiguredSerial $AdbSerial

    foreach ($package in $QuestPackages) {
        if ([string]::IsNullOrWhiteSpace($package)) {
            continue
        }

        $trimmedPackage = $package.Trim()
        $findCommand = "find /sdcard/Android/data/$trimmedPackage/files -type f -name events.ndjson 2>/dev/null"
        $findOutput = & $adbExe -s $resolvedSerial shell $findCommand
        if ($LASTEXITCODE -ne 0 -and ($null -eq $findOutput -or $findOutput.Count -eq 0)) {
            continue
        }

        $remotePaths = @()
        foreach ($line in $findOutput) {
            $textLine = [System.Convert]::ToString($line, [System.Globalization.CultureInfo]::InvariantCulture).Trim()
            if ($textLine.StartsWith("/sdcard/Android/data/", [System.StringComparison]::OrdinalIgnoreCase)) {
                $remotePaths += $textLine
            }
        }

        if ($remotePaths.Count -eq 0) {
            continue
        }

        $remoteIndex = 0
        foreach ($remotePath in $remotePaths) {
            $remoteIndex++
            $suffix = if ($remotePaths.Count -le 1) { "" } else { "_$remoteIndex" }
            $safeName = ("quest_" + ($trimmedPackage -replace "[^A-Za-z0-9_\-]", "_") + $suffix + "_events.ndjson")
            $localPulledPath = Join-Path $pulledDir $safeName

            $pullOutput = & $adbExe -s $resolvedSerial pull $remotePath $localPulledPath 2>&1
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to pull trace from $remotePath. adb output: $($pullOutput -join ' ')"
            }

            $candidateFiles += [PSCustomObject]@{
                sourceType = "QUEST"
                sourceLabel = $trimmedPackage
                remotePath = $remotePath
                localPath = $localPulledPath
            }
        }
    }
}

foreach ($inputPath in $LocalTracePaths) {
    if ([string]::IsNullOrWhiteSpace($inputPath)) {
        continue
    }

    $resolvedInput = Resolve-FullPath -PathValue $inputPath
    if (Test-Path -Path $resolvedInput -PathType Leaf) {
        $safeName = "local_" + [System.IO.Path]::GetFileName($resolvedInput)
        $localCopyPath = Join-Path $pulledDir $safeName
        Copy-Item -Path $resolvedInput -Destination $localCopyPath -Force

        $candidateFiles += [PSCustomObject]@{
            sourceType = "LOCAL_FILE"
            sourceLabel = $resolvedInput
            remotePath = ""
            localPath = $localCopyPath
        }
        continue
    }

    if (Test-Path -Path $resolvedInput -PathType Container) {
        $localFiles = Get-ChildItem -Path $resolvedInput -Recurse -File -Filter *.ndjson
        foreach ($file in $localFiles) {
            $safeName = "local_" + ($file.Name -replace "[^A-Za-z0-9_\-\.]", "_")
            $localCopyPath = Join-Path $pulledDir $safeName
            Copy-Item -Path $file.FullName -Destination $localCopyPath -Force

            $candidateFiles += [PSCustomObject]@{
                sourceType = "LOCAL_DIR"
                sourceLabel = $file.FullName
                remotePath = ""
                localPath = $localCopyPath
            }
        }
    }
}

if ($candidateFiles.Count -eq 0) {
    $emptyReport = [PSCustomObject]@{
        reportSchema = "THERAPLY_OPS_TRACE_DISCOVERY"
        reportVersion = "2026-02-22"
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o", [System.Globalization.CultureInfo]::InvariantCulture)
        adbSerial = $resolvedSerial
        supportedDatasetEventTypes = @($SupportedDatasetEventTypes)
        search = [PSCustomObject]@{
            skipQuestPull = $SkipQuestPull.IsPresent
            localTracePaths = $LocalTracePaths
            traceSessionIdFilter = if ([string]::IsNullOrWhiteSpace($TraceSessionId)) { "" } else { $TraceSessionId.Trim() }
            outputDirectory = $outputRoot
        }
        candidates = @()
        sessions = @()
        selection = [PSCustomObject]@{
            reasonCode = "TRACE_CANDIDATES_NOT_FOUND"
            selectedSessionId = ""
            selectedSourceType = ""
            selectedSourceLabel = ""
            selectedSourcePath = ""
            preparedAllSessionsTracePath = ""
            preparedSessionTracePath = ""
            preparedAllSessionsDatasetEvents = 0
            preparedSessionDatasetEvents = 0
            recommendedTraceInputPath = ""
            recommendedTraceSessionId = ""
            readyForOpsTraceValidation = $false
        }
    }

    $emptyReportJsonPath = Join-Path $reportsDir "trace_discovery_report.json"
    $emptyReport | ConvertTo-Json -Depth 12 | Set-Content -Path $emptyReportJsonPath -Encoding UTF8

    $emptyReportMdPath = Join-Path $reportsDir "trace_discovery_report.md"
    @(
        "# OPS Trace Discovery Report",
        "",
        "- generatedAtUtc: $($emptyReport.generatedAtUtc)",
        "- adbSerial: $($emptyReport.adbSerial)",
        "- selectionReason: TRACE_CANDIDATES_NOT_FOUND",
        "- readyForOpsTraceValidation: FALSE",
        "",
        "No trace candidates found. Provide -LocalTracePaths or connect Quest with a build that writes durable NDJSON."
    ) | Set-Content -Path $emptyReportMdPath -Encoding UTF8

    Write-Host "Trace discovery report JSON: $emptyReportJsonPath"
    Write-Host "Trace discovery report MD: $emptyReportMdPath"
    Write-Host "Ready for OPS trace validation: FALSE"

    if ($AllowNoDatasetEvents.IsPresent) {
        return
    }

    throw "No trace candidates found. Provide -LocalTracePaths or connect Quest and keep -SkipQuestPull disabled."
}

$globalSessionMap = @{}
$candidateReports = @()

foreach ($candidate in $candidateFiles) {
    $analysis = Analyze-TraceFile -TracePath $candidate.localPath -GlobalSessionMap $globalSessionMap

    $candidateReports += [PSCustomObject]@{
        sourceType = $candidate.sourceType
        sourceLabel = $candidate.sourceLabel
        remotePath = $candidate.remotePath
        localPath = $candidate.localPath
        totalLines = [int]$analysis.totalLines
        invalidLines = [int]$analysis.invalidLines
        interactionLines = [int]$analysis.interactionLines
        payloadParseErrors = [int]$analysis.payloadParseErrors
        datasetLines = [int]$analysis.datasetLines
        sessionCount = [int]$analysis.sessionDatasetEventCounts.Count
        eventTypeCounts = $analysis.eventTypeCounts
        sessionDatasetEventCounts = $analysis.sessionDatasetEventCounts
    }
}

$sessionReports = @()
foreach ($session in $globalSessionMap.Values) {
    $last = Parse-UtcDate -Value ([string]$session.lastOccurredAtUtc)
    $lastTicks = if ($null -eq $last) { 0 } else { $last.UtcTicks }

    $sessionReports += [PSCustomObject]@{
        sessionId = [string]$session.sessionId
        datasetEvents = [int]$session.datasetEvents
        firstOccurredAtUtc = [string]$session.firstOccurredAtUtc
        lastOccurredAtUtc = [string]$session.lastOccurredAtUtc
        lastOccurredAtTicks = [long]$lastTicks
        eventTypeCounts = $session.eventTypeCounts
        sourcePaths = @($session.sourcePaths)
    }
}

$selectedSession = $null
if (-not [string]::IsNullOrWhiteSpace($TraceSessionId)) {
    $normalizedFilter = $TraceSessionId.Trim()
    $selectedSession = $sessionReports | Where-Object {
        [string]::Equals([string]$_.sessionId, $normalizedFilter, [System.StringComparison]::Ordinal)
    } | Select-Object -First 1
}
else {
    $selectedSession = $sessionReports |
        Sort-Object `
            @{ Expression = { [int]$_.datasetEvents }; Descending = $true }, `
            @{ Expression = { [long]$_.lastOccurredAtTicks }; Descending = $true } |
        Select-Object -First 1
}

$selectedSessionId = ""
if ($null -ne $selectedSession) {
    $selectedSessionId = [string]$selectedSession.sessionId
}

$selectedCandidate = $null
if (-not [string]::IsNullOrWhiteSpace($selectedSessionId)) {
    $selectedCandidate = $candidateReports |
        Where-Object {
            $counts = $_.sessionDatasetEventCounts
            $null -ne $counts -and $counts.ContainsKey($selectedSessionId)
        } |
        Sort-Object `
            @{ Expression = { [int]$_.sessionDatasetEventCounts[$selectedSessionId] }; Descending = $true }, `
            @{ Expression = { [int]$_.datasetLines }; Descending = $true } |
        Select-Object -First 1
}

$preparedAllSessionsPath = ""
$preparedSessionPath = ""
$preparedAllSessionsCount = 0
$preparedSessionCount = 0
$selectionReasonCode = "TRACE_COLLECTED"

if ($null -ne $selectedCandidate) {
    $preparedAllSessionsPath = Join-Path $preparedDir "dataset_trace_all_sessions.ndjson"
    $preparedAllSessionsCount = Write-PreparedTrace `
        -SourcePath ([string]$selectedCandidate.localPath) `
        -OutputPath $preparedAllSessionsPath `
        -SessionIdFilter ""

    $preparedSessionPath = Join-Path $preparedDir "dataset_trace_selected_session.ndjson"
    $preparedSessionCount = Write-PreparedTrace `
        -SourcePath ([string]$selectedCandidate.localPath) `
        -OutputPath $preparedSessionPath `
        -SessionIdFilter $selectedSessionId

    if ($preparedSessionCount -le 0) {
        $selectionReasonCode = "TRACE_SELECTED_SESSION_EMPTY"
    }
}
else {
    $selectionReasonCode = "TRACE_NO_DATASET_EVENTS"
}

$readyForOpsTraceValidation = ($preparedSessionCount -gt 0)
if (-not $readyForOpsTraceValidation -and -not $AllowNoDatasetEvents.IsPresent) {
    $selectionReasonCode = "TRACE_NO_DATASET_EVENTS"
}

$report = [PSCustomObject]@{
    reportSchema = "THERAPLY_OPS_TRACE_DISCOVERY"
    reportVersion = "2026-02-22"
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o", [System.Globalization.CultureInfo]::InvariantCulture)
    adbSerial = $resolvedSerial
    supportedDatasetEventTypes = @($SupportedDatasetEventTypes)
    search = [PSCustomObject]@{
        skipQuestPull = $SkipQuestPull.IsPresent
        localTracePaths = $LocalTracePaths
        traceSessionIdFilter = if ([string]::IsNullOrWhiteSpace($TraceSessionId)) { "" } else { $TraceSessionId.Trim() }
        outputDirectory = $outputRoot
    }
    candidates = $candidateReports
    sessions = @($sessionReports | ForEach-Object {
            [PSCustomObject]@{
                sessionId = $_.sessionId
                datasetEvents = $_.datasetEvents
                firstOccurredAtUtc = $_.firstOccurredAtUtc
                lastOccurredAtUtc = $_.lastOccurredAtUtc
                eventTypeCounts = $_.eventTypeCounts
                sourcePaths = $_.sourcePaths
            }
        })
    selection = [PSCustomObject]@{
        reasonCode = $selectionReasonCode
        selectedSessionId = $selectedSessionId
        selectedSourceType = if ($null -eq $selectedCandidate) { "" } else { [string]$selectedCandidate.sourceType }
        selectedSourceLabel = if ($null -eq $selectedCandidate) { "" } else { [string]$selectedCandidate.sourceLabel }
        selectedSourcePath = if ($null -eq $selectedCandidate) { "" } else { [string]$selectedCandidate.localPath }
        preparedAllSessionsTracePath = $preparedAllSessionsPath
        preparedSessionTracePath = $preparedSessionPath
        preparedAllSessionsDatasetEvents = $preparedAllSessionsCount
        preparedSessionDatasetEvents = $preparedSessionCount
        recommendedTraceInputPath = $preparedAllSessionsPath
        recommendedTraceSessionId = $selectedSessionId
        readyForOpsTraceValidation = $readyForOpsTraceValidation
    }
}

$reportJsonPath = Join-Path $reportsDir "trace_discovery_report.json"
$report | ConvertTo-Json -Depth 12 | Set-Content -Path $reportJsonPath -Encoding UTF8

$reportMdPath = Join-Path $reportsDir "trace_discovery_report.md"
$mdLines = @(
    "# OPS Trace Discovery Report",
    "",
    "- generatedAtUtc: $($report.generatedAtUtc)",
    "- adbSerial: $($report.adbSerial)",
    "- candidates: $($candidateReports.Count)",
    "- datasetSessions: $($sessionReports.Count)",
    "- selectionReason: $selectionReasonCode",
    "- selectedSessionId: $selectedSessionId",
    "- preparedAllSessionsDatasetEvents: $preparedAllSessionsCount",
    "- preparedSessionDatasetEvents: $preparedSessionCount",
    "- readyForOpsTraceValidation: $($readyForOpsTraceValidation.ToString().ToUpperInvariant())",
    "",
    "## Recommended Input",
    "",
    "- TraceInputPath: $preparedAllSessionsPath",
    "- TraceSessionId: $selectedSessionId",
    "",
    "## Candidate Summary",
    ""
)

foreach ($candidate in $candidateReports) {
    $mdLines += "- [$($candidate.sourceType)] $($candidate.sourceLabel) -> datasetLines=$($candidate.datasetLines), interactionLines=$($candidate.interactionLines), totalLines=$($candidate.totalLines)"
}

$mdLines += ""
$mdLines += "## Sessions"
$mdLines += ""
foreach ($session in ($sessionReports | Sort-Object @{ Expression = { [int]$_.datasetEvents }; Descending = $true })) {
    $mdLines += "- $($session.sessionId): datasetEvents=$($session.datasetEvents), first=$($session.firstOccurredAtUtc), last=$($session.lastOccurredAtUtc)"
}

$mdLines | Set-Content -Path $reportMdPath -Encoding UTF8

if (-not [string]::IsNullOrWhiteSpace($selectedSessionId)) {
    $selectedSessionPath = Join-Path $reportsDir "selected_session_id.txt"
    $selectedSessionId | Set-Content -Path $selectedSessionPath -Encoding UTF8
}

Write-Host "Trace discovery report JSON: $reportJsonPath"
Write-Host "Trace discovery report MD: $reportMdPath"
Write-Host "Recommended TraceInputPath: $preparedAllSessionsPath"
Write-Host "Recommended TraceSessionId: $selectedSessionId"
Write-Host "Prepared session dataset events: $preparedSessionCount"
Write-Host "Ready for OPS trace validation: $($readyForOpsTraceValidation.ToString().ToUpperInvariant())"

if (-not $readyForOpsTraceValidation -and -not $AllowNoDatasetEvents.IsPresent) {
    throw "No dataset-compatible events found. Record a Quest session with task pipeline events and retry."
}
