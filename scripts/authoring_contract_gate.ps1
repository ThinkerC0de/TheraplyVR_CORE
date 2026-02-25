[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [string]$CatalogPath = "",
    [string]$ManifestPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:Errors = New-Object System.Collections.Generic.List[string]
$script:Warnings = New-Object System.Collections.Generic.List[string]

function Add-ValidationError {
    param([string]$Message)
    $script:Errors.Add($Message)
}

function Add-ValidationWarning {
    param([string]$Message)
    $script:Warnings.Add($Message)
}

function Get-PropertyValue {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)] [string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-TrimmedString {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)] [string]$Name
    )

    $value = Get-PropertyValue -Object $Object -Name $Name
    if ($null -eq $value) {
        return ""
    }

    return ([string]$value).Trim()
}

function Read-JsonFile {
    param([Parameter(Mandatory = $true)] [string]$Path)

    if (-not (Test-Path -Path $Path -PathType Leaf)) {
        Add-ValidationError "Missing JSON file: $Path"
        return $null
    }

    try {
        return Get-Content -Raw -Path $Path | ConvertFrom-Json
    }
    catch {
        Add-ValidationError "Failed to parse JSON file: $Path; $($_.Exception.Message)"
        return $null
    }
}

function Normalize-JsonObject {
    param($Object)

    if ($null -eq $Object) {
        return "null"
    }

    return ($Object | ConvertTo-Json -Depth 100 -Compress)
}

function Resolve-RelativePath {
    param(
        [Parameter(Mandatory = $true)] [string]$BasePath,
        [Parameter(Mandatory = $true)] [string]$RelativePath
    )

    if ([System.IO.Path]::IsPathRooted($RelativePath)) {
        return $RelativePath
    }

    $normalized = $RelativePath -replace '/', [System.IO.Path]::DirectorySeparatorChar
    return Join-Path $BasePath $normalized
}

function Is-NumberOrEmpty {
    param(
        [Parameter(Mandatory = $true)] [string]$Value,
        [Parameter(Mandatory = $true)] [string]$Context
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $true
    }

    $parsed = 0.0
    if (-not [double]::TryParse(
            $Value,
            [System.Globalization.NumberStyles]::Float,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [ref]$parsed)) {
        Add-ValidationError "$Context must be numeric when provided."
        return $false
    }

    return $true
}

function Has-MobileControlSchemaPayload {
    param($Schema)

    if ($null -eq $Schema) {
        return $false
    }

    $normalized = Normalize-JsonObject -Object $Schema
    return -not [string]::Equals($normalized, "{}", [System.StringComparison]::Ordinal) `
        -and -not [string]::Equals($normalized, "null", [System.StringComparison]::Ordinal)
}

function Test-MobileControlSchema {
    param(
        [Parameter(Mandatory = $true)] $Schema,
        [Parameter(Mandatory = $true)] [string]$Context,
        [string]$ExpectedGameId = ""
    )

    $supportedControlTypes = @("slider", "toggle", "select", "number", "text", "button")
    $supportedValueTypes = @("string", "bool", "int", "double")

    $schemaId = Get-TrimmedString -Object $Schema -Name "schema"
    if (-not [string]::Equals($schemaId, "THERAPLY_MOBILE_CONTROL_SCHEMA", [System.StringComparison]::Ordinal)) {
        Add-ValidationError "$Context has unsupported schema id: $schemaId"
    }

    $schemaVersion = Get-TrimmedString -Object $Schema -Name "schemaVersion"
    if ([string]::IsNullOrWhiteSpace($schemaVersion)) {
        Add-ValidationError "$Context schemaVersion is required."
    }

    $gameId = Get-TrimmedString -Object $Schema -Name "gameId"
    if ([string]::IsNullOrWhiteSpace($gameId)) {
        Add-ValidationError "$Context gameId is required."
    }
    elseif (-not [string]::IsNullOrWhiteSpace($ExpectedGameId) -and
        -not [string]::Equals($gameId, $ExpectedGameId, [System.StringComparison]::OrdinalIgnoreCase)) {
        Add-ValidationError "$Context gameId mismatch. expected=$ExpectedGameId actual=$gameId"
    }

    $payload = Get-PropertyValue -Object $Schema -Name "payload"
    if ($null -eq $payload) {
        Add-ValidationError "$Context payload is required."
    }
    else {
        $payloadTarget = Get-TrimmedString -Object $payload -Name "target"
        if (-not [string]::Equals($payloadTarget, "game_config", [System.StringComparison]::OrdinalIgnoreCase)) {
            Add-ValidationError "$Context payload.target must be game_config."
        }

        $payloadType = Get-TrimmedString -Object $payload -Name "gameConfigType"
        if ([string]::IsNullOrWhiteSpace($payloadType)) {
            Add-ValidationError "$Context payload.gameConfigType is required."
        }
    }

    $sectionsRaw = Get-PropertyValue -Object $Schema -Name "sections"
    $sections = @()
    if ($null -ne $sectionsRaw) {
        $sections = @($sectionsRaw)
    }

    $sectionIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    for ($sectionIndex = 0; $sectionIndex -lt $sections.Count; $sectionIndex++) {
        $section = $sections[$sectionIndex]
        if ($null -eq $section) {
            continue
        }

        $sectionId = Get-TrimmedString -Object $section -Name "sectionId"
        if ([string]::IsNullOrWhiteSpace($sectionId)) {
            Add-ValidationError "$Context sections[$sectionIndex].sectionId is required."
            continue
        }

        if (-not $sectionIds.Add($sectionId)) {
            Add-ValidationError "$Context duplicate sectionId: $sectionId"
        }
    }

    $controlsRaw = Get-PropertyValue -Object $Schema -Name "controls"
    $controls = @($controlsRaw)
    if ($controls.Count -eq 0) {
        Add-ValidationError "$Context controls array is required and must be non-empty."
        return
    }

    $controlIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    for ($controlIndex = 0; $controlIndex -lt $controls.Count; $controlIndex++) {
        $control = $controls[$controlIndex]
        if ($null -eq $control) {
            Add-ValidationError "$Context controls[$controlIndex] is null."
            continue
        }

        $controlId = Get-TrimmedString -Object $control -Name "controlId"
        if ([string]::IsNullOrWhiteSpace($controlId)) {
            Add-ValidationError "$Context controls[$controlIndex].controlId is required."
        }
        elseif (-not $controlIds.Add($controlId)) {
            Add-ValidationError "$Context duplicate controlId: $controlId"
        }

        $type = (Get-TrimmedString -Object $control -Name "type").ToLowerInvariant()
        if (-not ($supportedControlTypes -contains $type)) {
            Add-ValidationError "$Context control '$controlId' has unsupported type: $type"
            continue
        }

        $sectionId = Get-TrimmedString -Object $control -Name "sectionId"
        if (-not [string]::IsNullOrWhiteSpace($sectionId) -and $sectionIds.Count -gt 0 -and -not $sectionIds.Contains($sectionId)) {
            Add-ValidationError "$Context control '$controlId' references unknown sectionId: $sectionId"
        }

        if ([string]::Equals($type, "button", [System.StringComparison]::Ordinal)) {
            $buttonCommandId = Get-TrimmedString -Object $control -Name "buttonCommandId"
            if ([string]::IsNullOrWhiteSpace($buttonCommandId)) {
                Add-ValidationError "$Context button control '$controlId' requires buttonCommandId."
            }
            continue
        }

        $binding = Get-PropertyValue -Object $control -Name "binding"
        if ($null -eq $binding) {
            Add-ValidationError "$Context control '$controlId' requires binding."
            continue
        }

        $bindingPath = Get-TrimmedString -Object $binding -Name "path"
        if ([string]::IsNullOrWhiteSpace($bindingPath)) {
            Add-ValidationError "$Context control '$controlId' requires binding.path."
        }

        $bindingTarget = Get-TrimmedString -Object $binding -Name "target"
        if (-not [string]::IsNullOrWhiteSpace($bindingTarget) -and
            -not [string]::Equals($bindingTarget, "game_config", [System.StringComparison]::OrdinalIgnoreCase)) {
            Add-ValidationError "$Context control '$controlId' has unsupported binding.target: $bindingTarget"
        }

        $valueType = (Get-TrimmedString -Object $binding -Name "valueType").ToLowerInvariant()
        if (-not ($supportedValueTypes -contains $valueType)) {
            Add-ValidationError "$Context control '$controlId' has unsupported binding.valueType: $valueType"
        }

        if ([string]::Equals($type, "select", [System.StringComparison]::Ordinal)) {
            $options = @(Get-PropertyValue -Object $control -Name "options")
            if ($options.Count -eq 0) {
                Add-ValidationError "$Context select control '$controlId' must define options."
            }
            else {
                for ($optionIndex = 0; $optionIndex -lt $options.Count; $optionIndex++) {
                    $option = $options[$optionIndex]
                    if ($null -eq $option) {
                        Add-ValidationError "$Context select control '$controlId' has null option at index $optionIndex."
                        continue
                    }

                    $optionValue = Get-TrimmedString -Object $option -Name "value"
                    $optionLabel = Get-TrimmedString -Object $option -Name "label"
                    if ([string]::IsNullOrWhiteSpace($optionValue) -or [string]::IsNullOrWhiteSpace($optionLabel)) {
                        Add-ValidationError "$Context select control '$controlId' option index $optionIndex requires value and label."
                    }
                }
            }
        }

        if ([string]::Equals($type, "slider", [System.StringComparison]::Ordinal) -or
            [string]::Equals($type, "number", [System.StringComparison]::Ordinal)) {
            $validation = Get-PropertyValue -Object $control -Name "validation"
            if ($null -ne $validation) {
                [void](Is-NumberOrEmpty -Value (Get-TrimmedString -Object $validation -Name "minValue") -Context "$Context control '$controlId' validation.minValue")
                [void](Is-NumberOrEmpty -Value (Get-TrimmedString -Object $validation -Name "maxValue") -Context "$Context control '$controlId' validation.maxValue")
                [void](Is-NumberOrEmpty -Value (Get-TrimmedString -Object $validation -Name "step") -Context "$Context control '$controlId' validation.step")
            }
        }
    }
}

function Test-GameDefinition {
    param(
        [Parameter(Mandatory = $true)] $Definition,
        [Parameter(Mandatory = $true)] [string]$Context
    )

    $supportedControlModes = @("remote_only", "local_only", "hybrid")
    $supportedNodeTypes = @("Action", "Condition", "Branch", "Timer", "Complete", "Fail")

    $gameId = Get-TrimmedString -Object $Definition -Name "gameId"
    if ([string]::IsNullOrWhiteSpace($gameId)) {
        Add-ValidationError "$Context gameId is required."
    }

    $displayName = Get-TrimmedString -Object $Definition -Name "displayName"
    if ([string]::IsNullOrWhiteSpace($displayName)) {
        Add-ValidationError "$Context displayName is required."
    }

    $controlMode = (Get-TrimmedString -Object $Definition -Name "controlMode").ToLowerInvariant()
    if (-not ($supportedControlModes -contains $controlMode)) {
        Add-ValidationError "$Context has unsupported controlMode: $controlMode"
    }

    $taskGraph = Get-PropertyValue -Object $Definition -Name "taskGraph"
    if ($null -eq $taskGraph) {
        Add-ValidationError "$Context taskGraph is required."
        return
    }

    $entryNodeId = Get-TrimmedString -Object $taskGraph -Name "entryNodeId"
    if ([string]::IsNullOrWhiteSpace($entryNodeId)) {
        Add-ValidationError "$Context taskGraph.entryNodeId is required."
    }

    $nodes = @(Get-PropertyValue -Object $taskGraph -Name "nodes")
    if ($nodes.Count -eq 0) {
        Add-ValidationError "$Context taskGraph.nodes must be non-empty."
        return
    }

    $nodeIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    for ($nodeIndex = 0; $nodeIndex -lt $nodes.Count; $nodeIndex++) {
        $node = $nodes[$nodeIndex]
        if ($null -eq $node) {
            Add-ValidationError "$Context taskGraph.nodes[$nodeIndex] is null."
            continue
        }

        $nodeId = Get-TrimmedString -Object $node -Name "nodeId"
        if ([string]::IsNullOrWhiteSpace($nodeId)) {
            Add-ValidationError "$Context taskGraph.nodes[$nodeIndex].nodeId is required."
            continue
        }

        if (-not $nodeIds.Add($nodeId)) {
            Add-ValidationError "$Context has duplicate nodeId: $nodeId"
        }

        $nodeType = Get-TrimmedString -Object $node -Name "nodeType"
        if (-not ($supportedNodeTypes -contains $nodeType)) {
            Add-ValidationError "$Context node '$nodeId' has unsupported nodeType: $nodeType"
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($entryNodeId) -and -not $nodeIds.Contains($entryNodeId)) {
        Add-ValidationError "$Context taskGraph.entryNodeId '$entryNodeId' does not exist in nodes."
    }

    foreach ($node in $nodes) {
        if ($null -eq $node) {
            continue
        }

        $nodeId = Get-TrimmedString -Object $node -Name "nodeId"
        if ([string]::IsNullOrWhiteSpace($nodeId)) {
            continue
        }

        foreach ($transitionName in @("nextOnSuccess", "nextOnFail", "nextOnTimeout")) {
            $transitionTarget = Get-TrimmedString -Object $node -Name $transitionName
            if (-not [string]::IsNullOrWhiteSpace($transitionTarget) -and -not $nodeIds.Contains($transitionTarget)) {
                Add-ValidationError "$Context node '$nodeId' transition '$transitionName' points to unknown node '$transitionTarget'."
            }
        }

        $conditions = @(Get-PropertyValue -Object $node -Name "conditions")
        for ($conditionIndex = 0; $conditionIndex -lt $conditions.Count; $conditionIndex++) {
            $condition = $conditions[$conditionIndex]
            if ($null -eq $condition) {
                continue
            }

            $nextNodeId = Get-TrimmedString -Object $condition -Name "nextNodeId"
            if (-not [string]::IsNullOrWhiteSpace($nextNodeId) -and -not $nodeIds.Contains($nextNodeId)) {
                Add-ValidationError "$Context node '$nodeId' condition[$conditionIndex] points to unknown nextNodeId '$nextNodeId'."
            }
        }
    }

    $mobileControlSchema = Get-PropertyValue -Object $Definition -Name "mobileControlSchema"
    if (Has-MobileControlSchemaPayload -Schema $mobileControlSchema) {
        Test-MobileControlSchema -Schema $mobileControlSchema -Context "$Context mobileControlSchema" -ExpectedGameId $gameId
    }
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $scriptRoot
}
$RepoRoot = (Resolve-Path $RepoRoot).Path

if ([string]::IsNullOrWhiteSpace($CatalogPath)) {
    $CatalogPath = Join-Path $RepoRoot "contracts\game_catalog_seed.json"
}
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $RepoRoot "contracts\game_definition_export_manifest.json"
}
$adminCatalogAssetPath = Join-Path $RepoRoot "admin_console_web\assets\contracts\game_catalog_seed.json"

Write-Host "Repo root: $RepoRoot"
Write-Host "Catalog path: $CatalogPath"
Write-Host "Manifest path: $ManifestPath"
Write-Host "Admin catalog asset: $adminCatalogAssetPath"

$catalog = Read-JsonFile -Path $CatalogPath
$manifest = Read-JsonFile -Path $ManifestPath
$adminCatalogAsset = Read-JsonFile -Path $adminCatalogAssetPath

$catalogEntriesByGameId = @{}
$catalogSchemaByGameId = @{}

if ($null -ne $catalog) {
    $collection = Get-TrimmedString -Object $catalog -Name "collection"
    if (-not [string]::Equals($collection, "game_catalog", [System.StringComparison]::Ordinal)) {
        Add-ValidationError "Catalog collection must be 'game_catalog'. actual=$collection"
    }

    $entries = @(Get-PropertyValue -Object $catalog -Name "entries")
    if ($entries.Count -eq 0) {
        Add-ValidationError "Catalog entries must be non-empty."
    }
    else {
        foreach ($entry in $entries) {
            if ($null -eq $entry) {
                continue
            }

            $gameId = Get-TrimmedString -Object $entry -Name "gameId"
            if ([string]::IsNullOrWhiteSpace($gameId)) {
                Add-ValidationError "Catalog entry has empty gameId."
                continue
            }

            if ($catalogEntriesByGameId.ContainsKey($gameId.ToLowerInvariant())) {
                Add-ValidationError "Catalog has duplicate gameId: $gameId"
                continue
            }

            $catalogEntriesByGameId[$gameId.ToLowerInvariant()] = $entry
            $embeddedSchema = Get-PropertyValue -Object $entry -Name "mobileControlSchema"
            if (Has-MobileControlSchemaPayload -Schema $embeddedSchema) {
                Test-MobileControlSchema -Schema $embeddedSchema -Context "catalog entry '$gameId' embedded schema" -ExpectedGameId $gameId
                $catalogSchemaByGameId[$gameId.ToLowerInvariant()] = $embeddedSchema
            }
        }
    }
}

if ($null -ne $catalog -and $null -ne $adminCatalogAsset) {
    $catalogNormalized = Normalize-JsonObject -Object $catalog
    $adminCatalogNormalized = Normalize-JsonObject -Object $adminCatalogAsset
    if (-not [string]::Equals($catalogNormalized, $adminCatalogNormalized, [System.StringComparison]::Ordinal)) {
        Add-ValidationError "admin_console_web catalog seed asset is out of sync with contracts/game_catalog_seed.json."
    }
}

$contractsDir = Join-Path $RepoRoot "contracts"
$standaloneSchemaFiles = @(Get-ChildItem -Path $contractsDir -File -Filter "mobile_control_schema_*.json" |
    Sort-Object Name)
$standaloneSchemaByGameId = @{}

foreach ($schemaFile in $standaloneSchemaFiles) {
    $schema = Read-JsonFile -Path $schemaFile.FullName
    if ($null -eq $schema) {
        continue
    }

    $gameId = Get-TrimmedString -Object $schema -Name "gameId"
    if ([string]::IsNullOrWhiteSpace($gameId)) {
        Add-ValidationError "Standalone schema missing gameId: $($schemaFile.Name)"
        continue
    }

    Test-MobileControlSchema -Schema $schema -Context "standalone schema '$($schemaFile.Name)'" -ExpectedGameId $gameId

    $key = $gameId.ToLowerInvariant()
    if ($standaloneSchemaByGameId.ContainsKey($key)) {
        Add-ValidationError "Duplicate standalone schema gameId: $gameId"
        continue
    }

    $standaloneSchemaByGameId[$key] = $schema

    if (-not $catalogEntriesByGameId.ContainsKey($key)) {
        Add-ValidationError "Standalone schema '$($schemaFile.Name)' has no matching catalog entry for gameId '$gameId'."
        continue
    }

    if (-not $catalogSchemaByGameId.ContainsKey($key)) {
        Add-ValidationError "Catalog entry '$gameId' is missing embedded mobileControlSchema while standalone schema exists."
        continue
    }

    $embeddedNormalized = Normalize-JsonObject -Object $catalogSchemaByGameId[$key]
    $standaloneNormalized = Normalize-JsonObject -Object $schema
    if (-not [string]::Equals($embeddedNormalized, $standaloneNormalized, [System.StringComparison]::Ordinal)) {
        Add-ValidationError "Catalog embedded mobileControlSchema for '$gameId' is out of sync with standalone file '$($schemaFile.Name)'."
    }
}

if ($null -ne $manifest) {
    $manifestSchema = Get-TrimmedString -Object $manifest -Name "schema"
    if (-not [string]::Equals($manifestSchema, "THERAPLY_GAME_DEFINITION_EXPORT_MANIFEST", [System.StringComparison]::Ordinal)) {
        Add-ValidationError "Export manifest has unsupported schema id: $manifestSchema"
    }

    $manifestEntries = @(Get-PropertyValue -Object $manifest -Name "entries")
    if ($manifestEntries.Count -eq 0) {
        Add-ValidationError "Export manifest entries must be non-empty."
    }
    else {
        $manifestGameIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($manifestEntry in $manifestEntries) {
            if ($null -eq $manifestEntry) {
                continue
            }

            $gameId = Get-TrimmedString -Object $manifestEntry -Name "gameId"
            if ([string]::IsNullOrWhiteSpace($gameId)) {
                Add-ValidationError "Manifest entry has empty gameId."
                continue
            }

            if (-not $manifestGameIds.Add($gameId)) {
                Add-ValidationError "Manifest has duplicate gameId: $gameId"
                continue
            }

            $definitionJsonPathRelative = Get-TrimmedString -Object $manifestEntry -Name "definitionJsonPath"
            if ([string]::IsNullOrWhiteSpace($definitionJsonPathRelative)) {
                Add-ValidationError "Manifest entry '$gameId' missing definitionJsonPath."
                continue
            }

            $definitionJsonPath = Resolve-RelativePath -BasePath $RepoRoot -RelativePath $definitionJsonPathRelative
            $definition = Read-JsonFile -Path $definitionJsonPath
            if ($null -eq $definition) {
                continue
            }

            Test-GameDefinition -Definition $definition -Context "definition '$definitionJsonPathRelative'"

            $definitionGameId = Get-TrimmedString -Object $definition -Name "gameId"
            if (-not [string]::Equals($definitionGameId, $gameId, [System.StringComparison]::OrdinalIgnoreCase)) {
                Add-ValidationError "Manifest gameId '$gameId' does not match definition gameId '$definitionGameId' for '$definitionJsonPathRelative'."
            }

            if (-not $catalogEntriesByGameId.ContainsKey($gameId.ToLowerInvariant())) {
                Add-ValidationWarning "Definition '$gameId' is exported but missing from catalog seed."
            }

            $schemaPathRelative = Get-TrimmedString -Object $manifestEntry -Name "mobileControlSchemaJsonPath"
            $definitionSchema = Get-PropertyValue -Object $definition -Name "mobileControlSchema"
            $definitionHasSchema = Has-MobileControlSchemaPayload -Schema $definitionSchema

            if ([string]::IsNullOrWhiteSpace($schemaPathRelative)) {
                if ($definitionHasSchema) {
                    Add-ValidationError "Definition '$gameId' has mobileControlSchema but manifest entry has empty mobileControlSchemaJsonPath."
                }
                continue
            }

            $schemaPath = Resolve-RelativePath -BasePath $RepoRoot -RelativePath $schemaPathRelative
            $schemaObject = Read-JsonFile -Path $schemaPath
            if ($null -eq $schemaObject) {
                continue
            }

            Test-MobileControlSchema -Schema $schemaObject -Context "manifest schema '$schemaPathRelative'" -ExpectedGameId $gameId

            if (-not $definitionHasSchema) {
                Add-ValidationError "Manifest entry '$gameId' references schema file but definition has no mobileControlSchema payload."
                continue
            }

            $definitionSchemaNormalized = Normalize-JsonObject -Object $definitionSchema
            $schemaFileNormalized = Normalize-JsonObject -Object $schemaObject
            if (-not [string]::Equals($definitionSchemaNormalized, $schemaFileNormalized, [System.StringComparison]::Ordinal)) {
                Add-ValidationError "Definition '$gameId' mobileControlSchema is out of sync with '$schemaPathRelative'."
            }
        }
    }
}

if ($script:Warnings.Count -gt 0) {
    Write-Host "[WARN] Authoring contract gate warnings:"
    foreach ($warning in $script:Warnings) {
        Write-Host ("  - " + $warning)
    }
}

if ($script:Errors.Count -gt 0) {
    Write-Host "[FAIL] Authoring contract gate detected issues:"
    foreach ($errorItem in $script:Errors) {
        Write-Host ("  - " + $errorItem)
    }
    exit 1
}

Write-Host "[PASS] Authoring contract gate passed."
Write-Host ("Standalone schema files: " + $standaloneSchemaFiles.Count)
Write-Host ("Catalog entries: " + $catalogEntriesByGameId.Count)
