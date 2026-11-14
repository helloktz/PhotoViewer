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
    $assets = @(Get-ChildItem -LiteralPath $AssetsDir -Force)
    $assetNames = $assets | ForEach-Object Name
    Get-ChildItem -LiteralPath $PublishDir -Force |
        Where-Object { $_.Name -notin $assetNames } |
        Copy-Item -Destination $PayloadDir -Recurse -Force
    $assets | Copy-Item -Destination $PayloadDir -Recurse -Force
}
