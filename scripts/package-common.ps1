function Assert-PublishedAppVersion([string]$ExePath, [string]$ExpectedVersion) {
    if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
        throw "Publish did not produce $ExePath"
    }

    $actualVersion = (Get-Item -LiteralPath $ExePath).VersionInfo.ProductVersion
    if ($actualVersion -ne $ExpectedVersion) {
        throw "Published program version '$actualVersion' does not match '$ExpectedVersion'. Run again without -SkipPublish."
    }
}

function Copy-PackagePayload([string]$PublishDir, [string]$AssetsDir, [string]$PayloadDir) {
    & (Join-Path $PSScriptRoot 'validate-language-packs.ps1')

    Get-ChildItem -LiteralPath $PublishDir -File |
        Where-Object { $_.Extension -in '.exe', '.dll' } |
        Copy-Item -Destination $PayloadDir
    Get-ChildItem -LiteralPath $AssetsDir -Force |
        Copy-Item -Destination $PayloadDir -Recurse
}
