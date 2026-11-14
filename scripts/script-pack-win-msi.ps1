#Requires -Version 7.0

[CmdletBinding()]
param(
    [ValidateSet('x64')]
    [string]$Platform = 'x64',

    [switch]$Sign,

    [string]$CertSubject = 'Photo Viewer',
    [string]$CertFile = '',
    [string]$CertPassword = '',
    [string]$TimestampUrl = 'http://timestamp.sectigo.com',

    [string]$ProductVersion = '',

    [ValidateSet('none', 'low', 'medium', 'high', 'mszip')]
    [string]$CompressionLevel = 'high',

    [switch]$SkipPublish,
    [switch]$SkipValidation,
    [switch]$BootstrapWix
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'package-common.ps1')

$RepoRoot     = Split-Path $PSScriptRoot -Parent
$ProjectFile  = Join-Path $RepoRoot 'source\PhotoViewer.Win32\PhotoViewer.Win32.csproj'
$BuildProps   = Join-Path $RepoRoot 'source\Directory.Build.props'
$AppExtras    = Join-Path $RepoRoot 'source\assets\app'
$ArtifactsDir = Join-Path $RepoRoot '.artifacts'
$DistDir      = Join-Path $ArtifactsDir 'bundle'
$MsiDir       = Join-Path $RepoRoot 'source\assets\win\msi'
$WxiFile      = Join-Path $MsiDir 'Variables.wxi'
$WxsFiles     = @((Join-Path $MsiDir 'Package.wxs'), (Join-Path $MsiDir 'UI.wxs'))
$WxlFile      = Join-Path $MsiDir 'en-us.wxl'

$PortableMarker = '.igportable'

$WixVersion = '5.0.2'
$WixUiExt   = 'WixToolset.UI.wixext'

$SuppressIce = @('ICE57', 'ICE61', 'ICE105')

function Get-BuildProp([string]$Tag) {
    $m = Select-String -Path $BuildProps -Pattern "<$Tag>(.*?)</$Tag>" | Select-Object -First 1
    if ($m) { return $m.Matches[0].Groups[1].Value.Trim() }
    return ''
}

function Get-WxiDefine([string]$Name) {
    $m = Select-String -Path $WxiFile -Pattern "\?define\s+$Name\s*=\s*`"(.*?)`"" | Select-Object -First 1
    if ($m) { return $m.Matches[0].Groups[1].Value.Trim() }
    return ''
}

function Find-SdkTool([string]$Name) {
    $roots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "${env:ProgramFiles}\Windows Kits\10\bin"
    ) | Where-Object { $_ -and (Test-Path $_) }

    foreach ($root in $roots) {
        $hit = Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^10\.' } |
            Sort-Object { [version]$_.Name } -Descending |
            ForEach-Object { Join-Path $_.FullName "x64\$Name" } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
        if ($hit) { return $hit }
    }

    $onPath = Get-Command $Name -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    throw "Could not find $Name. Install the Windows 10/11 SDK (includes signtool)."
}

function Find-WixTool {
    $onPath = Get-Command 'wix' -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($onPath) { return $onPath.Source }

    $globalTool = Join-Path $env:USERPROFILE '.dotnet\tools\wix.exe'
    if (Test-Path $globalTool) { return $globalTool }

    if ($BootstrapWix) {
        Write-Host "    installing the wix dotnet tool $WixVersion"
        & dotnet tool install --global wix --version $WixVersion | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "dotnet tool install wix failed (exit $LASTEXITCODE)." }
        if (Test-Path $globalTool) { return $globalTool }
    }

    throw @"
Could not find 'wix'. Install the pinned WiX Toolset (or re-run with -BootstrapWix):
  dotnet tool install --global wix --version $WixVersion
  wix extension add -g $WixUiExt/$WixVersion
"@
}

function Assert-WixVersion([string]$Wix) {
    $raw = & $Wix --version
    if ($LASTEXITCODE -ne 0) { throw "wix --version failed (exit $LASTEXITCODE)." }

    $text  = ($raw | Out-String).Trim()
    $match = [regex]::Match($text, '(\d+\.\d+\.\d+)')
    $found = if ($match.Success) { $match.Groups[1].Value } else { $text }

    if ($found -ne $WixVersion) {
        throw @"
WiX $found found at '$Wix', but this installer is authored against $WixVersion. Re-pin it:
  dotnet tool uninstall --global wix
  dotnet tool install --global wix --version $WixVersion
"@
    }
    return $found
}

function Assert-WixUiExtension([string]$Wix) {
    $pattern = "^\s*$([regex]::Escape($WixUiExt))\s+$([regex]::Escape($WixVersion))\s*$"
    $found = @(& $Wix extension list -g | Where-Object { "$_" -match $pattern })

    if ($found.Count -eq 0 -and $BootstrapWix) {
        Write-Host "    adding the wix UI extension $WixUiExt/$WixVersion"
        & $Wix extension add -g "$WixUiExt/$WixVersion"
        if ($LASTEXITCODE -ne 0) { throw "wix extension add failed (exit $LASTEXITCODE)." }
        $found = @(& $Wix extension list -g | Where-Object { "$_" -match $pattern })
    }

    if ($found.Count -eq 0) {
        throw @"
WiX extension $WixUiExt/$WixVersion is not in the cache (or re-run with -BootstrapWix):
  wix extension add -g $WixUiExt/$WixVersion
"@
    }
}

$script:UseMachineStore = $false
function Test-SigningCert {
    if ($CertFile) {
        if (Test-Path $CertFile) { return $true }
        Write-Warning "Certificate file not found: $CertFile"
        return $false
    }
    foreach ($store in @(
            @{ Path = 'Cert:\CurrentUser\My';  Machine = $false },
            @{ Path = 'Cert:\LocalMachine\My'; Machine = $true })) {
        $cert = Get-ChildItem $store.Path -CodeSigningCert -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -like "*$CertSubject*" -and $_.HasPrivateKey } |
            Select-Object -First 1
        if ($cert) {
            $script:UseMachineStore = $store.Machine
            return $true
        }
    }
    return $false
}

function Invoke-SignTool([string]$SignTool, [string[]]$Files) {
    if ($Files.Count -eq 0) { return $true }
    $common = @('sign', '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256')
    if ($CertFile) {
        $common += @('/f', $CertFile)
        if ($CertPassword) { $common += @('/p', $CertPassword) }
    }
    else {
        $common += @('/n', $CertSubject, '/a')
        if ($script:UseMachineStore) { $common += '/sm' }
    }
    & $SignTool @common @Files | Out-Host
    return ($LASTEXITCODE -eq 0)
}

function New-DeterministicGuid([guid]$Namespace, [string]$Name) {
    $ns = $Namespace.ToByteArray()
    [Array]::Reverse($ns, 0, 4)
    [Array]::Reverse($ns, 4, 2)
    [Array]::Reverse($ns, 6, 2)

    $sha1 = [System.Security.Cryptography.SHA1]::Create()
    try { $hash = $sha1.ComputeHash($ns + [System.Text.Encoding]::UTF8.GetBytes($Name)) }
    finally { $sha1.Dispose() }

    $g = [byte[]]$hash[0..15]
    $g[6] = ($g[6] -band 0x0F) -bor 0x50
    $g[8] = ($g[8] -band 0x3F) -bor 0x80
    [Array]::Reverse($g, 0, 4)
    [Array]::Reverse($g, 4, 2)
    [Array]::Reverse($g, 6, 2)
    return [guid]::new($g)
}

function Get-MsiRows([object]$Database, [string]$Sql, [int]$FieldCount) {
    $rows = @()
    try { $view = $Database.OpenView($Sql) } catch { return , $rows }
    try {
        $view.Execute() | Out-Null
        while ($null -ne ($record = $view.Fetch())) {
            try {
                $row = @()
                for ($i = 1; $i -le $FieldCount; $i++) {
                    $row += [string]$record.GetType().InvokeMember(
                        'StringData', 'GetProperty', $null, $record, @($i))
                }
                $rows += , $row
            }
            finally {
                [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
            }
        }
        $view.Close() | Out-Null
    }
    finally {
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    }
    return , $rows
}

function Assert-DualScopeSummary([string]$MsiPath) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $summary   = $installer.SummaryInformation($MsiPath, 0)
    $wordCount = [int]$summary.Property(15)
    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary)
    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
    [GC]::Collect()

    if (($wordCount -band 8) -ne 0) {
        throw ("Word Count is ${wordCount}: bit 3 marks this a per-user-only package, which " +
               "silently disables the per-machine install option.")
    }
    return $wordCount
}

function Invoke-FileOpWithRetry([scriptblock]$Action, [string]$What) {
    $attempts = 15
    for ($attempt = 1; $attempt -le $attempts; $attempt++) {
        try {
            & $Action
            return
        }
        catch {
            if ($attempt -eq $attempts) {
                throw "$What failed after $attempts attempts: $($_.Exception.Message)"
            }
            if ($attempt -eq 1) { Write-Host "    waiting for a file lock to clear ($What)" }
            Start-Sleep -Seconds 2
        }
    }
}

function Test-Ice105Invariants([string]$MsiPath) {
    $bannedDirs = @(
        'AdminToolsFolder', 'CommonAppDataFolder', 'FontsFolder', 'System16Folder',
        'System64Folder', 'SystemFolder', 'TempFolder', 'WindowsFolder', 'WindowsVolume')

    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database  = $installer.OpenDatabase($MsiPath, 0)
    try {
        $problems = @()

        $registryRows = Get-MsiRows $database 'SELECT `Registry`, `Root`, `Key` FROM `Registry`' 3
        foreach ($row in $registryRows) {
            if ($row[1] -eq '2') { $problems += "HKLM registry row '$($row[0])' ($($row[2]))" }
        }

        foreach ($table in @('ServiceInstall', 'ServiceControl', 'ODBCDataSource', 'MsiAssembly')) {
            $rows = Get-MsiRows $database "SELECT * FROM ``$table``" 1
            if ($rows.Count -gt 0) { $problems += "$table has $($rows.Count) row(s)" }
        }

        $directoryRows = Get-MsiRows $database 'SELECT `Directory` FROM `Directory`' 1
        foreach ($row in $directoryRows) {
            if ($bannedDirs -contains $row[0]) { $problems += "system directory '$($row[0])'" }
        }

        if ($problems.Count -gt 0) {
            throw ("ICE105 invariants broken, so the package is no longer a valid dual-scope " +
                   "installer: " + ($problems -join '; '))
        }
    }
    finally {
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
    }
}

$igVersion = Get-BuildProp 'IgVersion'
if (-not $igVersion) { throw "Could not read <IgVersion> from $BuildProps" }
if ($igVersion -cnotmatch '^(?:0|[1-35-9][0-35-9]*)(?:\.(?:0|[1-35-9][0-35-9]*)){2}\z') {
    throw "IgVersion '$igVersion' must use major.minor.patch with digits 0 to 9 except 4."
}

$msiVersion = if ($ProductVersion) { $ProductVersion } else { $igVersion }

if ($msiVersion -cnotmatch '^(?:0|[1-35-9][0-35-9]*)(?:\.(?:0|[1-35-9][0-35-9]*)){2}\z') {
    throw "ProductVersion '$msiVersion' must use major.minor.patch with digits 0 to 9 except 4."
}
$versionFields = $msiVersion.Split('.')
$fieldLimits = @(255, 255, 65535)
for ($i = 0; $i -lt 3; $i++) {
    $parsed = 0
    if (-not [int]::TryParse($versionFields[$i], [ref]$parsed)) {
        throw "ProductVersion '$msiVersion' field $($i + 1) is not a number."
    }
    if ($parsed -lt 0 -or $parsed -gt $fieldLimits[$i]) {
        throw "ProductVersion '$msiVersion' field $($i + 1) must be 0..$($fieldLimits[$i])."
    }
}

$upgradeCode = Get-WxiDefine 'IgUpgradeCode'
if (-not $upgradeCode) { throw "Could not read IgUpgradeCode from $WxiFile" }
$productCode = New-DeterministicGuid ([guid]$upgradeCode) "PhotoViewer-msi-$Platform-$msiVersion"

$rid         = "win-$Platform"
$msbuildPlat = 'x64'
$publishDir  = Join-Path $ArtifactsDir "publish\$rid"
$stagingDir  = Join-Path $ArtifactsDir "staging\$rid-msi"
$payloadDir  = Join-Path $stagingDir 'PhotoViewer'
$objDir      = Join-Path $stagingDir 'obj'
$wixPdb      = Join-Path $stagingDir 'PhotoViewer.wixpdb'
$stagedMsi   = Join-Path $stagingDir "PhotoViewer_${msiVersion}_${rid}.msi"
$validationDir = Join-Path $ArtifactsDir 'staging\msi-validation'
$checkMsi = Join-Path $validationDir "PhotoViewer-check-$([guid]::NewGuid().ToString('N')).msi"
$outMsi      = Join-Path $DistDir "PhotoViewer_${msiVersion}_${rid}.msi"

if ($Sign -and -not (Test-SigningCert)) {
    throw 'No signing certificate found. MSI packaging stopped.'
}
$signtool = if ($Sign) { Find-SdkTool 'signtool.exe' } else { '' }

$wix = Find-WixTool
$wixFound = Assert-WixVersion $wix
Assert-WixUiExtension $wix

Write-Host "==> Packing Photo Viewer $msiVersion as MSI ($Platform)"
Write-Host "    Version     : $msiVersion (ProductVersion)"
Write-Host "    ProductCode : {$($productCode.ToString().ToUpperInvariant())}"
Write-Host "    UpgradeCode : {$upgradeCode}"
Write-Host "    Signed      : $(if ($Sign) { 'yes (payload binaries + the .msi)' } else { 'no' })"
Write-Host "    Compression : $CompressionLevel"
Write-Host "    Validation  : $(if ($SkipValidation) { 'skipped' } else { "on (suppressing $($SuppressIce -join ', '))" })"
Write-Host "    Output      : $outMsi"
Write-Host "    wix         : $wix ($wixFound)"
if ($Sign) { Write-Host "    signtool    : $signtool" }

if ($SkipPublish -and (Test-Path (Join-Path $publishDir 'PhotoViewer.exe'))) {
    Write-Host "    reusing publish output: $publishDir"
}
else {
    Write-Host "    publishing $rid (Release, AOT, self-contained)"
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    & dotnet publish $ProjectFile -c Release -r $rid -p:Platform=$msbuildPlat -p:IgVersion=$msiVersion -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid (exit $LASTEXITCODE)." }
}
Assert-PublishedAppVersion (Join-Path $publishDir 'PhotoViewer.exe') $msiVersion
$portablePath = Join-Path $publishDir $PortableMarker
if (Test-Path -LiteralPath $portablePath) {
    throw "Refusing to build: '$portablePath' exists. Publish the application without the portable marker."
}

if (Test-Path $stagingDir) {
    Invoke-FileOpWithRetry { Remove-Item $stagingDir -Recurse -Force } "Clearing $stagingDir"
}
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null
Copy-PackagePayload $publishDir $AppExtras $payloadDir

if ($Sign) {
    $binaries = @(Get-ChildItem -Path $payloadDir -Recurse -Include '*.exe', '*.dll' -File |
        Select-Object -ExpandProperty FullName)
    Write-Host "    signing $($binaries.Count) payload binary file(s)"
    if (-not (Invoke-SignTool -SignTool $signtool -Files $binaries)) {
        throw 'Could not sign all payload binaries. MSI packaging stopped.'
    }
}

Write-Host ''
Write-Host '==> Building the installer'
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

$wixArgs = @(
    'build'
) + $WxsFiles + @(
    '-arch', $Platform,
    '-ext', "$WixUiExt/$WixVersion",
    '-culture', 'en-US',
    '-loc', $WxlFile,
    '-d', "ProductVersion=$msiVersion",
    '-d', "ProductCode=$($productCode.ToString().ToUpperInvariant())",
    '-d', "PayloadDir=$payloadDir",
    '-d', "Cabinet=$CompressionLevel",
    '-intermediatefolder', $objDir,
    '-pdb', $wixPdb,
    '-sw1076',
    '-nologo',
    '-o', $stagedMsi
)
& $wix @wixArgs
if ($LASTEXITCODE -ne 0) { throw "wix build failed (exit $LASTEXITCODE)." }
Write-Host "    built: $stagedMsi"

New-Item -ItemType Directory -Path $validationDir -Force | Out-Null
Copy-Item -LiteralPath $stagedMsi -Destination $checkMsi -Force

$wordCount = Assert-DualScopeSummary $checkMsi
Write-Host "    summary WordCount: $wordCount (bit 3 clear, so both install scopes stay available)"

Test-Ice105Invariants $checkMsi
Write-Host '    ICE105 invariants re-asserted by hand (no HKLM rows, services or system dirs)'

if ($SkipValidation) {
    Write-Host '    skipping ICE validation (-SkipValidation)'
}
else {
    Write-Host "    validating (suppressing $($SuppressIce -join ', '))"
    $valArgs = @('msi', 'validate', $checkMsi, '-pdb', $wixPdb, '-nologo')
    foreach ($ice in $SuppressIce) { $valArgs += @('-sice', $ice) }
    & $wix @valArgs
    if ($LASTEXITCODE -ne 0) { throw "wix msi validate failed (exit $LASTEXITCODE)." }
}
Remove-Item $checkMsi -Force -ErrorAction SilentlyContinue

if ($Sign) {
    Write-Host '    signing the .msi'
    if (-not (Invoke-SignTool -SignTool $signtool -Files @($stagedMsi))) {
        throw 'Could not sign the installer. MSI packaging stopped.'
    }
    & $signtool verify /pa $stagedMsi | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "signtool verify failed (exit $LASTEXITCODE)." }
}

Invoke-FileOpWithRetry { [System.IO.File]::Move($stagedMsi, $outMsi, $true) } "Writing $outMsi"

$msiSizeMb = [math]::Round((Get-Item $outMsi).Length / 1MB, 1)

Remove-Item $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path $stagingDir) { Write-Warning "Could not remove '$stagingDir'." }

Write-Host ''
Write-Host 'Done.'
Write-Host "  Package : $outMsi ($msiSizeMb MB)"
Write-Host "  Version : $msiVersion  (ProductCode {$($productCode.ToString().ToUpperInvariant())})"
if ($Sign) {
    Write-Host '  Signed  : yes (payload binaries + the .msi)'
    Write-Host '  Next    : upload to the GitHub release for this version.'
}
