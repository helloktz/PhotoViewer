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

    [switch]$NoPortable,

    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot     = Split-Path $PSScriptRoot -Parent
$ProjectFile  = Join-Path $RepoRoot 'source\PhotoViewer.Win32\PhotoViewer.Win32.csproj'
$BuildProps   = Join-Path $RepoRoot 'source\Directory.Build.props'
$AppExtras    = Join-Path $RepoRoot 'source\assets\app'
$ArtifactsDir = Join-Path $RepoRoot '.artifacts'
$DistDir      = Join-Path $ArtifactsDir 'bundle'

$PortableMarker = '.igportable'

function Get-BuildProp([string]$Tag) {
    $m = Select-String -Path $BuildProps -Pattern "<$Tag>(.*?)</$Tag>" | Select-Object -First 1
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

$igVersion = Get-BuildProp 'IgVersion'
if (-not $igVersion) { throw "Could not read <IgVersion> from $BuildProps" }
if ($igVersion -cnotmatch '^(?:0|[1-35-9][0-35-9]*)(?:\.(?:0|[1-35-9][0-35-9]*)){2}\z') {
    throw "IgVersion '$igVersion' must use major.minor.patch with digits 0 to 9 except 4."
}

$rid         = "win-$Platform"
$msbuildPlat = 'x64'
$publishDir  = Join-Path $ArtifactsDir "publish\$rid"
$stagingDir  = Join-Path $ArtifactsDir "staging\$rid-zip"
$archiveName = "PhotoViewer_${igVersion}_${rid}"
$payloadDir  = Join-Path $stagingDir $archiveName
$outZip      = Join-Path $DistDir "$archiveName.zip"

$script:doSign = [bool]$Sign
if ($script:doSign -and -not (Test-SigningCert)) {
    Write-Warning "No signing certificate found: building an UNSIGNED archive."
    $script:doSign = $false
}
$signtool = if ($script:doSign) { Find-SdkTool 'signtool.exe' } else { '' }

Write-Host "==> Packing Photo Viewer $igVersion as ZIP ($Platform)"
Write-Host "    Portable    : $(if ($NoPortable) { 'no (no marker file)' } else { "yes ($PortableMarker)" })"
Write-Host "    Signed      : $(if ($script:doSign) { 'yes (payload binaries)' } else { 'no' })"
Write-Host "    Output      : $outZip"
if ($script:doSign) { Write-Host "    signtool    : $signtool" }

if ($SkipPublish -and (Test-Path (Join-Path $publishDir 'PhotoViewer.exe'))) {
    Write-Host "    reusing publish output: $publishDir"
}
else {
    Write-Host "    publishing $rid (Release, AOT, self-contained)"
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    & dotnet publish $ProjectFile -c Release -r $rid -p:Platform=$msbuildPlat -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid (exit $LASTEXITCODE)." }
    Copy-Item -Path (Join-Path $AppExtras '*') -Destination $publishDir -Recurse -Force
}
if (-not (Test-Path (Join-Path $publishDir 'PhotoViewer.exe'))) {
    throw "Publish did not produce PhotoViewer.exe in $publishDir"
}

if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir '*') -Destination $payloadDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $RepoRoot 'LICENSE') -Destination $payloadDir

Get-ChildItem -Path $payloadDir -Recurse -Include '*.pdb' -File -ErrorAction SilentlyContinue |
    Remove-Item -Force

if (-not $NoPortable) {
    [System.IO.File]::WriteAllBytes((Join-Path $payloadDir $PortableMarker), @())
    Write-Host "    wrote portable marker: $PortableMarker"
}

if ($script:doSign) {
    $binaries = Get-ChildItem -Path $payloadDir -Recurse -Include '*.exe', '*.dll' -File |
        Select-Object -ExpandProperty FullName
    Write-Host "    signing $($binaries.Count) payload binary file(s)"
    if (-not (Invoke-SignTool -SignTool $signtool -Files $binaries)) {
        throw 'Could not sign all payload binaries. ZIP packaging stopped.'
    }
}

New-Item -ItemType Directory -Path $DistDir -Force | Out-Null
if (Test-Path $outZip) { Remove-Item $outZip -Force }

[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $payloadDir, $outZip,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $true)

$zipSizeMb = [math]::Round((Get-Item $outZip).Length / 1MB, 1)

Remove-Item $stagingDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Done.'
Write-Host "  Archive : $outZip ($zipSizeMb MB)"
Write-Host "  Portable: $(if ($NoPortable) { 'no' } else { "yes (extract to a writable folder)" })"
if ($Sign -and -not $script:doSign) {
    Write-Host '  Signed  : no (no signing certificate was found)'
    Write-Host '  Next    : sign the binaries and repack before publishing the release.'
}
elseif ($script:doSign) {
    Write-Host '  Signed  : yes (payload binaries)'
    Write-Host '  Next    : upload to the GitHub release for this version.'
}
