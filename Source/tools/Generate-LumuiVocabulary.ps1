param(
    [switch]$Check
)

$ErrorActionPreference = "Stop"
$browserRoot = Split-Path -Parent $PSScriptRoot
$resourceRoot = Join-Path $browserRoot "resources\lumui"
$schemaRoot = Join-Path $resourceRoot "schemas"
$outputRoot = Join-Path $browserRoot "src\Lumui.Client\Generated"
$expectedOutputPaths = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase
)

$protocol = Get-Content -Raw -Path (Join-Path $resourceRoot "protocol.json") | ConvertFrom-Json
$catalog = Get-Content -Raw -Path (Join-Path $resourceRoot "component-catalog.json") | ConvertFrom-Json
$surfaceSchema = Get-Content -Raw -Path (Join-Path $schemaRoot "surface.schema.json") | ConvertFrom-Json
$descriptorSchema = Get-Content -Raw -Path (Join-Path $schemaRoot "service-descriptor.schema.json") | ConvertFrom-Json
$discoverySchema = Get-Content -Raw -Path (Join-Path $schemaRoot "discovery.schema.json") | ConvertFrom-Json
$actionSchema = Get-Content -Raw -Path (Join-Path $schemaRoot "action-message.schema.json") | ConvertFrom-Json

function ConvertTo-Identifier {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $identifier = ""
    foreach ($segment in $Value.Split(
        [char[]]@("_", "-", "."),
        [System.StringSplitOptions]::RemoveEmptyEntries
    )) {
        if ($segment -eq "ms") {
            $identifier += "Milliseconds"
            continue
        }
        $identifier += $segment.Substring(0, 1).ToUpperInvariant() + $segment.Substring(1)
    }
    return $identifier
}

function Add-PropertyNames {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value,
        [Parameter(Mandatory = $true)]
        [System.Collections.Generic.HashSet[string]]$Destination
    )

    if ($null -eq $Value) {
        return
    }
    if ($Value -is [System.Array]) {
        foreach ($item in $Value) {
            Add-PropertyNames -Value $item -Destination $Destination
        }
        return
    }
    if ($Value -isnot [System.Management.Automation.PSCustomObject]) {
        return
    }

    $propertiesMember = $Value.PSObject.Properties["properties"]
    if ($null -ne $propertiesMember) {
        foreach ($propertyName in $propertiesMember.Value.PSObject.Properties.Name) {
            [void]$Destination.Add([string]$propertyName)
        }
    }
    foreach ($property in $Value.PSObject.Properties) {
        Add-PropertyNames -Value $property.Value -Destination $Destination
    }
}

function Get-ReferencedMembers {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Category
    )

    $members = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal
    )
    $pattern = "LumuiProtocol\." + [regex]::Escape($Category) + "\.([A-Za-z0-9_]+)"
    $sourceFiles = Get-ChildItem `
        -Path (Join-Path $browserRoot "src") `
        -Filter "*.cs" `
        -File `
        -Recurse |
        Where-Object { $_.DirectoryName -notlike "*\Generated" }
    foreach ($sourceFile in $sourceFiles) {
        $source = Get-Content -Raw -Path $sourceFile.FullName
        foreach ($match in [regex]::Matches($source, $pattern)) {
            [void]$members.Add($match.Groups[1].Value)
        }
    }
    return $members
}

function ConvertTo-MemberMap {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Values
    )

    $members = [ordered]@{}
    foreach ($value in $Values) {
        $identifier = ConvertTo-Identifier -Value $value
        if ($members.Contains($identifier) -and $members[$identifier] -ne $value) {
            throw "Protocol values '$($members[$identifier])' and '$value' produce the same C# identifier '$identifier'."
        }
        $members[$identifier] = $value
    }
    return $members
}

function Select-ReferencedMembers {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary]$Available,
        [Parameter(Mandatory = $true)]
        [string]$Category
    )

    $selected = [ordered]@{}
    $referencedMembers = Get-ReferencedMembers -Category $Category
    foreach ($identifier in $referencedMembers) {
        if (-not $Available.Contains($identifier)) {
            throw "C# code references undefined LUMUI $Category member '$identifier'."
        }
        $selected[$identifier] = $Available[$identifier]
    }
    return $selected
}

function Select-ReferencedValues {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Values,
        [Parameter(Mandatory = $true)]
        [string]$Category
    )

    $selected = [ordered]@{}
    $referencedMembers = Get-ReferencedMembers -Category $Category
    foreach ($value in $Values) {
        $identifier = ConvertTo-Identifier -Value $value
        if (-not $referencedMembers.Contains($identifier)) {
            continue
        }
        if ($selected.Contains($identifier) -and $selected[$identifier] -ne $value) {
            throw "Referenced protocol values '$($selected[$identifier])' and '$value' produce the same C# identifier '$identifier'."
        }
        $selected[$identifier] = $value
    }
    foreach ($identifier in $referencedMembers) {
        if (-not $selected.Contains($identifier)) {
            throw "C# code references undefined LUMUI $Category member '$identifier'."
        }
    }
    return $selected
}

function Convert-ObjectToMemberMap {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value
    )

    $members = [ordered]@{}
    foreach ($property in $Value.PSObject.Properties) {
        $identifier = ConvertTo-Identifier -Value $property.Name
        $wireValue = [string]$property.Value
        if ($members.Contains($identifier) -and $members[$identifier] -ne $wireValue) {
            throw "Protocol members produce the same C# identifier '$identifier'."
        }
        $members[$identifier] = $wireValue
    }
    return $members
}

function New-GeneratedSource {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TypeName,
        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary]$Members
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("namespace Lumui.Client.LumuiProtocol;")
    $lines.Add("")
    $lines.Add("public static class $TypeName")
    $lines.Add("{")
    $identifiers = [string[]]@($Members.Keys)
    [Array]::Sort($identifiers, [System.StringComparer]::Ordinal)
    foreach ($identifier in $identifiers) {
        $escaped = ([string]$Members[$identifier]).Replace("\", "\\").Replace('"', '\"')
        $lines.Add("    public const String $identifier = `"$escaped`";")
    }
    $lines.Add("}")
    return [string]::Join([Environment]::NewLine, $lines) + [Environment]::NewLine
}

function Write-GeneratedSource {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TypeName,
        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary]$Members
    )

    $path = Join-Path $outputRoot "$TypeName.g.cs"
    [void]$script:expectedOutputPaths.Add($path)
    $expected = New-GeneratedSource -TypeName $TypeName -Members $Members
    if ($Check) {
        if (-not (Test-Path $path)) {
            throw "Generated protocol file '$path' is missing."
        }
        $actual = Get-Content -Raw -Path $path
        $normalizedActual = $actual.Replace("`r`n", "`n")
        $normalizedExpected = $expected.Replace("`r`n", "`n")
        if ($normalizedActual -ne $normalizedExpected) {
            throw "Generated protocol file '$path' is stale. Run tools\Generate-LumuiVocabulary.ps1."
        }
        return
    }
    if (-not (Test-Path $outputRoot)) {
        [void](New-Item -ItemType Directory -Path $outputRoot)
    }
    Set-Content -Path $path -Value $expected -NoNewline -Encoding utf8
}

$fieldValues = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal
)
foreach ($schema in @(
    $surfaceSchema,
    $descriptorSchema,
    $discoverySchema,
    $actionSchema
)) {
    Add-PropertyNames -Value $schema -Destination $fieldValues
}
foreach ($propertyName in $catalog.PSObject.Properties.Name) {
    [void]$fieldValues.Add([string]$propertyName)
}
foreach ($component in $catalog.components.PSObject.Properties.Value) {
    foreach ($propertyName in $component.PSObject.Properties.Name) {
        [void]$fieldValues.Add([string]$propertyName)
    }
}
Write-GeneratedSource `
    -TypeName "Fields" `
    -Members (Select-ReferencedValues -Values @($fieldValues) -Category "Fields")

$componentKinds = ConvertTo-MemberMap -Values @(
    $catalog.components.PSObject.Properties.Name
)
Write-GeneratedSource -TypeName "ComponentKinds" -Members $componentKinds

$schemaDefinitionValues = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal
)
foreach ($schema in @(
    $surfaceSchema,
    $descriptorSchema,
    $discoverySchema,
    $actionSchema
)) {
    foreach ($definitionName in $schema.'$defs'.PSObject.Properties.Name) {
        [void]$schemaDefinitionValues.Add([string]$definitionName)
    }
}
$schemaDefinitions = ConvertTo-MemberMap -Values @($schemaDefinitionValues)
Write-GeneratedSource `
    -TypeName "SchemaDefinitions" `
    -Members (Select-ReferencedMembers `
        -Available $schemaDefinitions `
        -Category "SchemaDefinitions")

Write-GeneratedSource `
    -TypeName "MediaTypes" `
    -Members (Convert-ObjectToMemberMap -Value $protocol.media_types)
$lumuiMediaType = [string]$protocol.media_types.lumui_json
foreach ($schemaMediaType in @(
    [string]$descriptorSchema.properties.route_surface.properties.type.const,
    [string]$descriptorSchema.properties.actions.properties.type.const,
    [string]$actionSchema.'$defs'.result.properties.status_resource.properties.type.const
)) {
    if ($schemaMediaType -ne $lumuiMediaType) {
        throw "A schema media type differs from the protocol manifest."
    }
}

Write-GeneratedSource `
    -TypeName "Relations" `
    -Members (Convert-ObjectToMemberMap -Value $protocol.relations)
$manifestRelations = [string[]]@(
    $protocol.relations.PSObject.Properties.Value
)
$schemaRelations = [string[]]@(
    $descriptorSchema.properties.discovery.properties.html_link_relations.items.enum
)
[Array]::Sort($manifestRelations, [System.StringComparer]::Ordinal)
[Array]::Sort($schemaRelations, [System.StringComparer]::Ordinal)
if ([string]::Join("`n", $manifestRelations) -ne [string]::Join("`n", $schemaRelations)) {
    throw "The protocol manifest and service descriptor link relations differ."
}

Write-GeneratedSource `
    -TypeName "Paths" `
    -Members (Convert-ObjectToMemberMap -Value $protocol.paths)
Write-GeneratedSource `
    -TypeName "Schemes" `
    -Members (Convert-ObjectToMemberMap -Value $protocol.schemes)
Write-GeneratedSource `
    -TypeName "RegionRoles" `
    -Members (Convert-ObjectToMemberMap -Value $protocol.region_roles)
Write-GeneratedSource `
    -TypeName "Symbols" `
    -Members (Convert-ObjectToMemberMap -Value $protocol.symbols)
Write-GeneratedSource `
    -TypeName "BrandMotifs" `
    -Members (Convert-ObjectToMemberMap -Value $protocol.brand_motifs)
Write-GeneratedSource `
    -TypeName "RenderProfiles" `
    -Members (Convert-ObjectToMemberMap -Value $protocol.render_profiles)
Write-GeneratedSource `
    -TypeName "OutputModes" `
    -Members (Convert-ObjectToMemberMap -Value $protocol.output_modes)
Write-GeneratedSource `
    -TypeName "InteractionModes" `
    -Members (Convert-ObjectToMemberMap -Value $protocol.interaction_modes)
Write-GeneratedSource `
    -TypeName "AccessibilityPreferences" `
    -Members (Convert-ObjectToMemberMap -Value $protocol.accessibility_preferences)

$inputMethods = ConvertTo-MemberMap -Values @(
    [string]$protocol.defaults.input_method
)
Write-GeneratedSource -TypeName "InputMethods" -Members $inputMethods

Write-GeneratedSource `
    -TypeName "SurfaceModes" `
    -Members (ConvertTo-MemberMap -Values @($surfaceSchema.properties.mode.enum))
Write-GeneratedSource `
    -TypeName "ConfirmationPolicies" `
    -Members (ConvertTo-MemberMap -Values @(
        $surfaceSchema.'$defs'.actionDefinition.properties.confirmation.enum
    ))
Write-GeneratedSource `
    -TypeName "TextRoles" `
    -Members (ConvertTo-MemberMap -Values @(
        $surfaceSchema.'$defs'.component.properties.text_role.enum
    ))
Write-GeneratedSource `
    -TypeName "Priorities" `
    -Members (ConvertTo-MemberMap -Values @(
        $surfaceSchema.'$defs'.priority.enum
    ))
Write-GeneratedSource `
    -TypeName "AuthenticationModes" `
    -Members (ConvertTo-MemberMap -Values @(
        $descriptorSchema.properties.authentication.properties.mode.enum
    ))
Write-GeneratedSource `
    -TypeName "ActionStatuses" `
    -Members (ConvertTo-MemberMap -Values @(
        $actionSchema.'$defs'.result.properties.status.enum
    ))
Write-GeneratedSource `
    -TypeName "Sources" `
    -Members (ConvertTo-MemberMap -Values @(
        $actionSchema.'$defs'.invoke.properties.source.properties.kind.enum
    ))

$messageTypes = ConvertTo-MemberMap -Values @(
    [string]$actionSchema.'$defs'.invoke.properties.message_type.const,
    [string]$actionSchema.'$defs'.result.properties.message_type.const
)
Write-GeneratedSource -TypeName "MessageTypes" -Members $messageTypes

$versions = [ordered]@{
    ComponentCatalog = [string]$catalog.lumui_component_catalog
    Message = [string]$actionSchema.'$defs'.invoke.properties.lumui_message.const
    Surface = [string]$surfaceSchema.properties.lumui_surface.const
    Web = [string]$descriptorSchema.properties.lumui_web.const
}
if ([string]$discoverySchema.properties.lumui_discovery.const -ne $versions.Web) {
    throw "The discovery and service descriptor protocol versions differ."
}
if ([string]$protocol.lumui_protocol -ne $versions.Web) {
    throw "The protocol manifest and service descriptor protocol versions differ."
}
Write-GeneratedSource -TypeName "Versions" -Members $versions

if (Test-Path $outputRoot) {
    foreach ($generatedFile in Get-ChildItem -Path $outputRoot -Filter "*.g.cs" -File) {
        if ($expectedOutputPaths.Contains($generatedFile.FullName)) {
            continue
        }
        if ($Check) {
            throw "Unexpected generated protocol file '$($generatedFile.FullName)'."
        }
        Remove-Item -Path $generatedFile.FullName
    }
}

Write-Host "LUMUI C# protocol vocabulary is current."
