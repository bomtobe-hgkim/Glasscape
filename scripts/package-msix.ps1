#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]{2,49}$')][string]$IdentityName,
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$Publisher,
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$PublisherDisplayName,
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Z0-9]{12}$')][string]$StoreId,
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$Version = '2.1.0.0',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist\store'),
    [string]$SdkBinDirectory,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
. (Join-Path $projectRoot 'packaging\SourceArchivePolicy.ps1')
$distRoot = Join-Path $projectRoot 'dist'
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
if (-not $outputRoot.StartsWith($distRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'MSIX output must be a child directory of this workspace dist directory.' }
if ($Publisher -notmatch '^CN=\S' -or $Publisher -match '[\r\n\x00]') { throw 'Publisher must be the exact certificate distinguished name from Partner Center.' }
if ($PublisherDisplayName.Trim() -ne $PublisherDisplayName -or $PublisherDisplayName -match '[\r\n\x00]') { throw 'PublisherDisplayName must be the exact single-line Partner Center value.' }
$packageVersion = [Version]$Version
if ($packageVersion.Major -eq 0 -or $packageVersion.Revision -ne 0 -or @($packageVersion.Major, $packageVersion.Minor, $packageVersion.Build, $packageVersion.Revision | Where-Object { $_ -gt 65535 }).Count -gt 0) { throw 'Store versions require four 16-bit components, nonzero major, and revision zero.' }
$stage = Join-Path $outputRoot 'staging'
$bundleInput = Join-Path $outputRoot 'bundle-input'
# Validate persistent staging before build cleanup, document/source copies or SDK calls.
Assert-GlasscapeNoReparsePath -Path $stage
if (Test-Path -LiteralPath $stage) { Assert-GlasscapeReleaseTree -Root $stage }
Assert-GlasscapeNoReparsePath -Path $bundleInput
if (Test-Path -LiteralPath $bundleInput) { Assert-GlasscapeReleaseTree -Root $bundleInput }
$null = Get-GlasscapeSourceArchiveFiles -ProjectRoot $projectRoot
if (-not $SdkBinDirectory) {
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $SdkBinDirectory = Get-ChildItem -LiteralPath $sdkRoot -Directory | Where-Object { $_.Name -match '^10\.0\.\d+\.0$' } | Sort-Object { [Version]$_.Name } -Descending | ForEach-Object { Join-Path $_.FullName 'x64' } | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'MakeAppx.exe') } | Select-Object -First 1
}
if (-not $SdkBinDirectory) { throw 'Windows SDK x64 MakeAppx and MakePri tools are required.' }
$makeAppx = Join-Path $SdkBinDirectory 'MakeAppx.exe'
$makePri = Join-Path $SdkBinDirectory 'MakePri.exe'
foreach ($tool in @($makeAppx, $makePri)) { if (-not (Test-Path -LiteralPath $tool)) { throw "Required tool not found: $tool" } }
foreach ($directory in @($outputRoot, $stage, $bundleInput)) {
    $cursor = $directory
    while ($cursor.Length -gt $projectRoot.Length) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing packaging paths through a reparse point: $cursor" }
        }
        $cursor = Split-Path -Parent $cursor
    }
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
if (-not $SkipBuild -and (Test-Path -LiteralPath $stage)) {
    $resolvedStage = (Resolve-Path -LiteralPath $stage).ProviderPath
    if ($resolvedStage -ne $stage -or -not $resolvedStage.StartsWith($outputRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing to clean an unexpected package staging path.' }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null

# Do not use single-file extraction from the read-only WindowsApps directory.
if (-not $SkipBuild) {
    dotnet publish (Join-Path $projectRoot 'src\TransparentTaskbar\TransparentTaskbar.csproj') -c Release -r win-x64 --self-contained true -o $stage -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -p:Version=$($packageVersion.ToString(3))
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained x64 publish failed.' }
}
Assert-GlasscapeReleaseTree -Root $stage
foreach ($required in @('TransparentTaskbar.exe', 'TransparentTaskbar.dll', 'coreclr.dll', 'hostfxr.dll', 'WindowsBase.dll', 'WinRT.Runtime.dll', 'Microsoft.Windows.SDK.NET.dll', 'ExplorerTAP.dll', 'mpv\mpv.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $stage $required))) { throw "Required self-contained/native payload is missing: $required" }
}
if ((Get-FileHash -LiteralPath (Join-Path $stage 'ExplorerTAP.dll') -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'c48f1a06168acd7839269dde0d15ccf5d09c702512b94dd6ba9a05df1b153dc2') { throw 'Packaged ExplorerTAP.dll does not match the pinned upstream binary.' }
$mpvRoot = Join-Path $projectRoot 'third_party\mpv'
foreach ($line in Get-Content -LiteralPath (Join-Path $mpvRoot 'runtime.sha256')) {
    if ($line -match '^([a-fA-F0-9]{64})\s+\*?(.+\.(?:exe|dll|com))$') {
        $runtimeFile = Join-Path $stage ('mpv\' + $Matches[2])
        if (-not (Test-Path -LiteralPath $runtimeFile) -or (Get-FileHash -LiteralPath $runtimeFile -Algorithm SHA256).Hash -ne $Matches[1]) { throw "Packaged mpv hash mismatch: $runtimeFile" }
    }
}
$allowedSamples = @('aurora-flow.mp4', 'aurora-flow.png', 'ocean-drift.mp4', 'ocean-drift.png')
$sampleRoot = Join-Path $stage 'samples'
$actualSamples = @(Get-ChildItem -LiteralPath $sampleRoot -File | Where-Object { $_.Extension -in @('.mp4', '.png') } | ForEach-Object { $_.Name } | Sort-Object)
if (Compare-Object ($allowedSamples | Sort-Object) $actualSamples) { throw 'Package staging must contain exactly the two allowed video/poster pairs.' }
foreach ($item in Get-ChildItem -LiteralPath $stage -Recurse -File) {
    if ($item.Name -in @('settings.json', 'Install-Glasscape.cmd') -or $item.Extension -in @('.pdb', '.log', '.tmp') -or $item.Name -match '^(rain|rain2|town)\.(mp4|png)$') { throw "Unexpected release payload: $($item.FullName)" }
}

& (Join-Path $projectRoot 'packaging\New-PackageAssets.ps1')
Copy-Item -LiteralPath (Join-Path $projectRoot 'packaging\Assets') -Destination $stage -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'packaging\Strings') -Destination $stage -Recurse -Force
foreach ($name in @('README.md', 'LICENSE.md', 'LICENSE-MIT.txt', 'LICENSE-GPL-3.0.md', 'THIRD_PARTY_NOTICES.md', 'PRIVACY.md', 'SUPPORT.md', 'SECURITY.md')) {
    $document = Join-Path $projectRoot $name
    if (-not (Test-Path -LiteralPath $document)) { throw "Required release document is missing: $name" }
    Copy-Item -LiteralPath $document -Destination $stage -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'third_party\TranslucentTB\NOTICE.md') -Destination (Join-Path $stage 'TranslucentTB-NOTICE.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'third_party\TranslucentTB\LICENSE.md') -Destination (Join-Path $stage 'TranslucentTB-LICENSE.md') -Force
Copy-Item -LiteralPath (Join-Path $mpvRoot 'NOTICE.md') -Destination (Join-Path $stage 'mpv-NOTICE.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\samples\README.md'), (Join-Path $projectRoot 'assets\samples\LICENSE-CC0.txt') -Destination $sampleRoot -Force
Copy-Item -LiteralPath (Join-Path $mpvRoot 'licenses') -Destination $stage -Recurse -Force
$frameworkLicenses = Join-Path $stage 'Framework-Licenses'
New-Item -ItemType Directory -Path $frameworkLicenses -Force | Out-Null
foreach ($license in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'packaging\Licenses') -File) { Copy-Item -LiteralPath $license.FullName -Destination $frameworkLicenses -Force }
$runtimeConfiguration = Get-Content -LiteralPath (Join-Path $stage 'TransparentTaskbar.runtimeconfig.json') -Raw | ConvertFrom-Json
$nugetRoot = (& dotnet msbuild (Join-Path $projectRoot 'src\TransparentTaskbar\TransparentTaskbar.csproj') -getProperty:NuGetPackageRoot | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $nugetRoot)) { throw 'Could not locate the restored runtime license texts.' }
foreach ($framework in $runtimeConfiguration.runtimeOptions.includedFrameworks) {
    $id = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
    $runtimePack = Join-Path $nugetRoot ($id + '\' + $framework.version)
    $destination = Join-Path $frameworkLicenses $framework.name
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $licenseFiles = @(Get-ChildItem -LiteralPath $runtimePack -File | Where-Object { $_.Name -match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$' })
    if ($licenseFiles.Count -eq 0) { throw "No license text found for the bundled $($framework.name) runtime." }
    foreach ($license in $licenseFiles) { Copy-Item -LiteralPath $license.FullName -Destination $destination -Force }
}
$sdkVersion = Split-Path (Split-Path $SdkBinDirectory -Parent) -Leaf
$windowsKitRoot = Split-Path (Split-Path (Split-Path $SdkBinDirectory -Parent) -Parent) -Parent
$sdkLicenseRoot = Join-Path $windowsKitRoot ('Licenses\' + $sdkVersion)
foreach ($name in @('sdk_license.rtf', 'sdk_third_party_notices.rtf')) {
    $license = Join-Path $sdkLicenseRoot $name
    if (-not (Test-Path -LiteralPath $license)) { throw "Windows SDK license text is missing: $license" }
    Copy-Item -LiteralPath $license -Destination $frameworkLicenses -Force
}
$sourceRoot = Join-Path $stage 'Sources'
$mpvSourceRoot = Join-Path $sourceRoot 'mpv'
New-Item -ItemType Directory -Path $mpvSourceRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'third_party\TranslucentTB\TranslucentTB-2026.2-source.zip') -Destination $sourceRoot -Force
Copy-Item -LiteralPath (Join-Path $mpvRoot 'mpv-v0.41.0-source.zip'), (Join-Path $mpvRoot 'runtime.sha256'), (Join-Path $mpvRoot 'NOTICE.md') -Destination $mpvSourceRoot -Force
Copy-Item -LiteralPath (Join-Path $mpvRoot 'source'), (Join-Path $mpvRoot 'provenance') -Destination $mpvSourceRoot -Recurse -Force

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$sourceArchive = Join-Path $sourceRoot 'Glasscape-source.zip'
. (Join-Path $projectRoot 'packaging\SourceArchive.ps1')
Assert-GlasscapeReleaseTree -Root $stage
New-GlasscapeSourceArchive -ProjectRoot $projectRoot -Destination $sourceArchive
foreach ($zip in Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter '*.zip' -File) {
    $readArchive = [IO.Compression.ZipFile]::OpenRead($zip.FullName)
    try {
        foreach ($entry in $readArchive.Entries) {
            if ($entry.FullName -match '(^|/)(rain|rain2|town)\.(mp4|png)$') { throw "Excluded media found in source archive $($zip.Name): $($entry.FullName)" }
        }
    }
    finally { $readArchive.Dispose() }
}

$manifestText = [IO.File]::ReadAllText((Join-Path $projectRoot 'packaging\AppxManifest.xml.template'))
foreach ($replacement in @{ '@IDENTITY_NAME@' = $IdentityName; '@PUBLISHER@' = $Publisher; '@PUBLISHER_DISPLAY_NAME@' = $PublisherDisplayName; '@VERSION@' = $Version }.GetEnumerator()) {
    $manifestText = $manifestText.Replace($replacement.Key, [Security.SecurityElement]::Escape($replacement.Value))
}
[IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'), $manifestText, (New-Object Text.UTF8Encoding($false)))
$priConfig = Join-Path $outputRoot 'priconfig.xml'
Assert-GlasscapeReleaseTree -Root $stage
& $makePri createconfig /cf $priConfig /dq en-US /pv 10.0.0 /o
if ($LASTEXITCODE -ne 0) { throw 'MakePri createconfig failed.' }
# These resources stay in the application package; do not emit orphaned language
# resource-pack PRI files without corresponding resource packages in the bundle.
[xml]$priConfiguration = Get-Content -LiteralPath $priConfig -Raw
[void]$priConfiguration.resources.RemoveChild($priConfiguration.resources.packaging)
$priConfiguration.Save($priConfig)
foreach ($oldPri in Get-ChildItem -LiteralPath $stage -Filter 'resources*.pri' -File) { Remove-Item -LiteralPath $oldPri.FullName }
& $makePri new /pr $stage /cf $priConfig /of (Join-Path $stage 'resources.pri') /o
if ($LASTEXITCODE -ne 0) { throw 'MakePri resource indexing failed.' }

New-Item -ItemType Directory -Path $bundleInput -Force | Out-Null
foreach ($oldPackage in Get-ChildItem -LiteralPath $bundleInput -File) { Remove-Item -LiteralPath $oldPackage.FullName }
$msix = Join-Path $bundleInput ("Glasscape_${Version}_x64.msix")
$bundle = Join-Path $outputRoot ("Glasscape_${Version}_x64.msixbundle")
Assert-GlasscapeReleaseTree -Root $stage
& $makeAppx pack /d $stage /p $msix /o /h SHA256
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx manifest/schema/semantic validation or packing failed.' }
Assert-GlasscapeReleaseTree -Root $bundleInput
& $makeAppx bundle /d $bundleInput /p $bundle /bv $Version /o
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx bundle validation failed.' }
$bundleArchive = [IO.Compression.ZipFile]::OpenRead($bundle)
try {
    $bundleManifestEntry = $bundleArchive.GetEntry('AppxMetadata/AppxBundleManifest.xml')
    if ($null -eq $bundleManifestEntry) { throw 'Bundle manifest was not created.' }
    $reader = New-Object IO.StreamReader($bundleManifestEntry.Open())
    try { [xml]$bundleManifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($bundleManifest.Bundle.Identity.Name -ne $IdentityName -or $bundleManifest.Bundle.Identity.Publisher -ne $Publisher -or $bundleManifest.Bundle.Identity.Version -ne $Version) { throw 'Bundle identity differs from the observed Partner Center inputs.' }
}
finally { $bundleArchive.Dispose() }
$receipt = [ordered]@{
    IdentityName = $IdentityName; Publisher = $Publisher; PublisherDisplayName = $PublisherDisplayName; StoreId = $StoreId
    Version = $Version; Architecture = 'x64'; Languages = @('en-US', 'ko-KR'); SelfContained = $true; Signed = $false
    Package = $msix; Bundle = $bundle; BundleSha256 = (Get-FileHash -LiteralPath $bundle -Algorithm SHA256).Hash.ToLowerInvariant()
    MakeAppx = $makeAppx; MakePri = $makePri; CreatedUtc = [DateTime]::UtcNow.ToString('o')
    Files = @(Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object { $_.FullName.Substring($stage.Length + 1).Replace('\', '/') })
}
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'build-info.json') -Encoding UTF8
Write-Host "Unsigned Store bundle: $bundle"
Write-Host "SHA-256: $($receipt.BundleSha256)"
Write-Host 'The Microsoft Store signs submitted packages. No certificate trust or Windows security setting was changed.'
