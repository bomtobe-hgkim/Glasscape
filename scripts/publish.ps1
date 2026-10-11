#Requires -Version 5.1
[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $projectRoot 'packaging\SourceArchive.ps1')
$nativeDll = Join-Path $projectRoot 'third_party\TranslucentTB\win-x64\ExplorerTAP.dll'
if (-not (Test-Path -LiteralPath $nativeDll)) { & (Join-Path $PSScriptRoot 'fetch-native.ps1') }
if ((Get-FileHash -LiteralPath $nativeDll -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'c48f1a06168acd7839269dde0d15ccf5d09c702512b94dd6ba9a05df1b153dc2') {
    throw 'ExplorerTAP.dll does not match the pinned official release. Run scripts/fetch-native.ps1.'
}

$distRoot = Join-Path $projectRoot 'dist'
$publishRoot = Join-Path $distRoot 'Glasscape-win-x64'
$sampleDestination = Join-Path $publishRoot 'samples'
$sampleRoot = Join-Path $projectRoot 'assets\samples'
$sampleFiles = @('aurora-flow.mp4', 'aurora-flow.png', 'ocean-drift.mp4', 'ocean-drift.png')
$excludedSampleFiles = @('rain.mp4', 'rain.png', 'rain2.mp4', 'rain2.png', 'town.mp4', 'town.png')

function Assert-ReleaseStaging {
    foreach ($directory in @($distRoot, $publishRoot, $sampleDestination)) {
        $resolved = [IO.Path]::GetFullPath($directory)
        if ($resolved -ne $distRoot -and -not $resolved.StartsWith($distRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Release staging path escaped dist.' }
        if (Test-Path -LiteralPath $resolved) {
            $item = Get-Item -LiteralPath $resolved -Force
            if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Release staging directory must not be a reparse point: $resolved" }
        }
    }
    if (Test-Path -LiteralPath $publishRoot) { Assert-GlasscapeReleaseTree -Root $publishRoot }
}
Assert-ReleaseStaging
$null = Get-GlasscapeSourceArchiveFiles -ProjectRoot $projectRoot
$mpvRoot = Join-Path $projectRoot 'third_party\mpv'
if (-not (Test-Path -LiteralPath (Join-Path $mpvRoot 'win-x64\mpv.exe'))) { & (Join-Path $PSScriptRoot 'fetch-mpv.ps1') }
foreach ($line in Get-Content -LiteralPath (Join-Path $mpvRoot 'runtime.sha256')) {
    if ($line -match '^([a-fA-F0-9]{64})\s+\*?(.+)$') {
        $expected = $Matches[1]; $name = $Matches[2]
        $runtimeFile = Join-Path $mpvRoot $name
        if (-not (Test-Path -LiteralPath $runtimeFile)) { $runtimeFile = Join-Path (Join-Path $mpvRoot 'win-x64') $name }
        if (-not (Test-Path -LiteralPath $runtimeFile) -or (Get-FileHash -LiteralPath $runtimeFile -Algorithm SHA256).Hash -ne $expected) { throw "mpv runtime hash mismatch: $name" }
    }
}
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null
if (-not $SkipBuild) {
    dotnet publish (Join-Path $projectRoot 'src\Glasscape\Glasscape.csproj') -c Release -r win-x64 --self-contained true -o $publishRoot -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
}
if (-not (Test-Path -LiteralPath (Join-Path $publishRoot 'Glasscape.exe'))) { throw 'Published executable not found. Run without -SkipBuild first.' }

Assert-ReleaseStaging
New-Item -ItemType Directory -Path $sampleDestination -Force | Out-Null
foreach ($name in $sampleFiles) {
    Copy-Item -LiteralPath (Join-Path $sampleRoot $name) -Destination $sampleDestination -Force
}

foreach ($name in @('README.md', 'LICENSE.md', 'LICENSE-MIT.txt', 'LICENSE-GPL-3.0.md', 'THIRD_PARTY_NOTICES.md', 'PRIVACY.md', 'SUPPORT.md', 'SECURITY.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $publishRoot
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'third_party\TranslucentTB\NOTICE.md') -Destination (Join-Path $publishRoot 'TranslucentTB-NOTICE.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'third_party\TranslucentTB\TranslucentTB-2026.2-source.zip') -Destination $publishRoot
Copy-Item -LiteralPath (Join-Path $mpvRoot 'NOTICE.md') -Destination (Join-Path $publishRoot 'mpv-NOTICE.md')
$licenseDestination = Join-Path $publishRoot 'mpv-licenses'
New-Item -ItemType Directory -Path $licenseDestination -Force | Out-Null
Copy-Item -Path (Join-Path $mpvRoot 'licenses\*') -Destination $licenseDestination -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\samples\README.md') -Destination (Join-Path $publishRoot 'samples\README.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\samples\LICENSE-CC0.txt') -Destination (Join-Path $publishRoot 'samples\LICENSE-CC0.txt')
Set-Content -LiteralPath (Join-Path $publishRoot 'Install-Glasscape.cmd') -Value '@start "" "%~dp0Glasscape.exe" --install' -Encoding Ascii

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$sourceArchive = Join-Path $publishRoot 'Glasscape-source.zip'
Assert-ReleaseStaging
New-GlasscapeSourceArchive -ProjectRoot $projectRoot -Destination $sourceArchive

$portableArchive = Join-Path $distRoot 'Glasscape-win-x64.zip'
Assert-ReleaseStaging
Assert-GlasscapeNoReparsePath -Path $portableArchive
if (Test-Path -LiteralPath $portableArchive) { Remove-Item -LiteralPath $portableArchive }
[IO.Compression.ZipFile]::CreateFromDirectory($publishRoot, $portableArchive, [IO.Compression.CompressionLevel]::Optimal, $true)
Write-Host "Executable: $(Join-Path $publishRoot 'Glasscape.exe')"
Write-Host "Portable ZIP: $portableArchive"
Write-Host "C# source: $sourceArchive"
