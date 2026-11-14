$ErrorActionPreference = 'Stop'

$sourceRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'source'
$library = Join-Path $sourceRoot 'PhotoViewer.Lib'
$enumPath = Join-Path $library 'Common/Localization/LangId.cs'
$enumText = Get-Content -LiteralPath $enumPath -Raw
$members = [regex]::Match($enumText, '\benum\s+LangId\s*\{(?<members>[^}]+)\}').Groups['members'].Value
if ($members -cnotmatch '\A(?:\s*[A-Za-z_]\w*(?:\s*=\s*\d+)?\s*,)+\s*\z') {
    throw "Could not read the LangId enum members in $enumPath."
}

$keys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($member in [regex]::Matches($members, '(?<name>[A-Za-z_]\w*)(?:\s*=\s*\d+)?\s*,')) {
    [void]$keys.Add($member.Groups['name'].Value)
}

$errors = [System.Collections.Generic.List[string]]::new()
$localizedEnums = @(
    @{ Path = 'Common/Types/Enums.cs'; Names = 'BackdropStyle', 'ImageOrderBy', 'ImageOrderType', 'ImageInfoOverlayMode', 'LayoutPosition' }
    @{ Path = 'Common/Types/EditingApp.cs'; Names = 'AfterEditAppAction' }
    @{ Path = 'Common/Types/MouseEvents.cs'; Names = 'MouseClickEvent', 'MouseWheelAction', 'MouseWheelEvent' }
    @{
        Path = 'Common/Photoing/Photoing_Enums.cs'
        Names = 'ColorProfileOption'
        InvariantMembers = 'AdobeRGB1998', 'AppleRGB', 'CoatedFOGRA39', 'ColorMatchRGB', 'sRGB', 'USWebCoatedSWOP'
    }
    @{ Path = 'UI/Viewer/ViewerControl_Enums.cs'; Names = 'CheckerboardType', 'ImageInterpolation', 'ScrollbarMode' }
    @{ Path = 'Common/Printing/PrintModels.cs'; Names = 'PrintColorMode', 'PrintDuplex', 'PrintFitMode', 'PrintMarginPreset', 'PrintOrientation', 'PrintPageScope', 'PrintRegion', 'PrinterState' }
)
foreach ($group in $localizedEnums) {
    $path = Join-Path $library $group.Path
    $source = Get-Content -LiteralPath $path -Raw
    foreach ($name in $group.Names) {
        $enum = [regex]::Match($source, "\benum\s+$name\b[^{]*\{(?<members>[^}]+)\}")
        if (!$enum.Success) { throw "Could not read the $name enum in $path." }

        $prefix = switch -Wildcard ($name) {
            'Print*' { "_$name" }
            'LayoutPosition' { '_Position' }
            default { $name }
        }
        foreach ($member in ($enum.Groups['members'].Value.Split(',').Trim() | Where-Object { $_ })) {
            if ($member -cnotmatch '^([A-Za-z_]\w*)(?:\s*=\s*.+)?$') {
                throw "Could not read enum member '$member' in $path."
            }
            $memberName = $Matches[1]
            if ($group['InvariantMembers'] -ccontains $memberName) { continue }
            $key = "$($prefix)_$memberName"
            if (!$keys.Contains($key)) { $errors.Add("$($path): Missing LangId '$key'.") }
        }
    }
}

$formats = @{}
foreach ($code in @('en-US', 'zh-CN')) {
    $path = Join-Path $sourceRoot "assets/app/languages/$code.iglang.json"
    $pack = $null
    try {
        $pack = [System.Text.Json.JsonDocument]::Parse([string](Get-Content -LiteralPath $path -Raw))
        $metadata = $pack.RootElement.GetProperty('_Metadata')
        if ($metadata.GetProperty('Code').GetString() -cne $code) {
            $errors.Add("$($path): Expected language code '$code'.")
        }
        if ($metadata.GetProperty('MinVersion').GetSingle() -lt 10) {
            $errors.Add("$($path): Expected a language pack with MinVersion of at least 10.")
        }

        $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($entry in $pack.RootElement.GetProperty('Items').EnumerateObject()) {
            if (!$seen.Add($entry.Name)) { $errors.Add("$($path): Duplicate translation '$($entry.Name)'.") }
            if (!$keys.Contains($entry.Name)) { $errors.Add("$($path): Unknown translation '$($entry.Name)'.") }
            if ($entry.Value.ValueKind -ne 'String' -or [string]::IsNullOrWhiteSpace($entry.Value.GetString())) {
                $errors.Add("$($path): Translation '$($entry.Name)' must be a non-empty string.")
                continue
            }

            $translation = $entry.Value.GetString()
            try {
                $indices = @([regex]::Matches($translation, '(?<!\{)(?:\{\{)*\{(\d+)') |
                    ForEach-Object { [int]$_.Groups[1].Value } | Sort-Object -Unique)
                $argumentCount = if ($indices.Count -gt 0) { $indices[-1] + 1 } else { 0 }
                $null = [string]::Format($translation, [object[]]::new($argumentCount))
                $signature = $indices -join ','
                if ($formats.ContainsKey($entry.Name) -and $formats[$entry.Name] -ne $signature) {
                    $errors.Add("$($path): Translation '$($entry.Name)' has different placeholders from en-US.")
                }
                $formats[$entry.Name] = $signature
            }
            catch {
                $errors.Add("$($path): Translation '$($entry.Name)' has invalid format placeholders.")
            }
        }

        foreach ($key in $keys) {
            if (!$seen.Contains($key)) { $errors.Add("$($path): Missing translation '$key'.") }
        }
    }
    catch {
        $errors.Add("$($path): Could not read language pack. $($_.Exception.Message)")
    }
    finally {
        if ($null -ne $pack) { $pack.Dispose() }
    }
}

if ($errors.Count -gt 0) {
    throw ($errors -join [Environment]::NewLine)
}
