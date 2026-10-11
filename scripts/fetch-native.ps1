#Requires -Version 5.1
<#
.SYNOPSIS
Restores the unmodified, SHA256-pinned TranslucentTB 2026.2 x64 native dependency.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$version = '2026.2'
$releaseUrl = "https://github.com/TranslucentTB/TranslucentTB/releases/download/$version/TranslucentTB-portable-x64.zip"
$sourceUrl = "https://github.com/TranslucentTB/TranslucentTB/archive/refs/tags/$version.zip"
$licenseUrl = "https://raw.githubusercontent.com/TranslucentTB/TranslucentTB/$version/LICENSE.md"
$releaseHash = '0dbe8e0255c20e131cde536dcd0ae490d45989a7d360e26d0150dff1922ac420'
$sourceHash = '73ac414e17710de5765f2305bbbc1aaa4d88628c49d9b47a3159795d05f2f257'
$licenseHash = '982b4aa12f1eeb169feae72ae5ada1443e608343f29ccee0ce5ee58f21b1a851'
$dllHash = 'c48f1a06168acd7839269dde0d15ccf5d09c702512b94dd6ba9a05df1b153dc2'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$vendorRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'third_party\TranslucentTB'))
$nativeRoot = [IO.Path]::GetFullPath((Join-Path $vendorRoot 'win-x64'))
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$stage = [IO.Path]::GetFullPath((Join-Path $temporaryRoot ('glasscape-native-' + [guid]::NewGuid())))

function Assert-Sha256([string]$Path, [string]$Expected) {
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Expected) {
        throw "SHA256 mismatch for '$Path': expected $Expected, received $actual."
    }
}

function Get-PinnedFile([string]$Uri, [string]$Path, [string]$Hash) {
    Write-Host "Downloading $Uri"
    Invoke-WebRequest -Uri $Uri -OutFile $Path -UseBasicParsing
    Assert-Sha256 $Path $Hash
}

New-Item -ItemType Directory -Path $stage | Out-Null
try {
    # TLS 1.2 is required by GitHub when running Windows PowerShell 5.1.
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $releaseArchive = Join-Path $stage 'portable.zip'
    $sourceArchive = Join-Path $stage "TranslucentTB-$version-source.zip"
    $license = Join-Path $stage 'LICENSE.md'
    $dll = Join-Path $stage 'ExplorerTAP.dll'

    Get-PinnedFile $releaseUrl $releaseArchive $releaseHash
    Get-PinnedFile $sourceUrl $sourceArchive $sourceHash
    Get-PinnedFile $licenseUrl $license $licenseHash

    # Read only this entry, after verifying the complete release archive.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($releaseArchive)
    try {
        $entry = $archive.GetEntry('ExplorerTAP.dll')
        if ($null -eq $entry) { throw 'The pinned archive does not contain ExplorerTAP.dll.' }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dll, $false)
    }
    finally { $archive.Dispose() }
    Assert-Sha256 $dll $dllHash

    New-Item -ItemType Directory -Path $vendorRoot -Force | Out-Null
    if (Test-Path -LiteralPath $nativeRoot) {
        # This fixed direct child is the only dependency directory we replace.
        $resolvedNativeRoot = (Get-Item -LiteralPath $nativeRoot).FullName
        if ($resolvedNativeRoot -ne $nativeRoot -or [IO.Path]::GetDirectoryName($resolvedNativeRoot) -ne $vendorRoot) {
            throw 'Refusing to replace a dependency folder outside the expected vendor directory.'
        }
        if ((Get-Item -LiteralPath $nativeRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Refusing to replace a linked dependency folder.'
        }
        Remove-Item -LiteralPath $resolvedNativeRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $nativeRoot | Out-Null
    Copy-Item -LiteralPath $dll -Destination (Join-Path $nativeRoot 'ExplorerTAP.dll')
    Copy-Item -LiteralPath $sourceArchive -Destination $vendorRoot -Force
    Copy-Item -LiteralPath $license -Destination $vendorRoot -Force

    $notice = @"
# TranslucentTB native dependency

This directory redistributes the original, unmodified x64 ExplorerTAP.dll from
TranslucentTB **$version**, licensed under the GNU General Public License version 3.
The complete upstream GPL text is in LICENSE.md.

Upstream project: https://github.com/TranslucentTB/TranslucentTB
Release: https://github.com/TranslucentTB/TranslucentTB/releases/tag/$version
Release tag commit: d4636e4

Credits follow the upstream README: @ethanhs, @sylveon (Charles Milette),
@MrAksel, @denosawr, and all TranslucentTB contributors:
https://github.com/TranslucentTB/TranslucentTB/graphs/contributors
The upstream icon credit is @dAKirby309; no upstream icon is included here.
ExplorerTAP uses Microsoft Detours, Windows Implementation Library (WIL), and
C++/WinRT through the build dependencies declared in the upstream source.

## Files and SHA256 provenance

| Artifact | SHA256 |
| --- | --- |
| TranslucentTB-portable-x64.zip (downloaded during restore) | $releaseHash |
| win-x64/ExplorerTAP.dll | $dllHash |
| TranslucentTB-$version-source.zip | $sourceHash |
| LICENSE.md | $licenseHash |

Release archive: $releaseUrl
Source archive: $sourceUrl
License: $licenseUrl

The source archive is distributed beside the DLL. It contains the upstream
ExplorerTAP source, build projects, dependency manifests, and CONTRIBUTING.md
with build instructions. The DLL has not been rebuilt or modified.

## Native dependency inspection

PE machine: AMD64 (0x8664). Exported functions: DllGetClassObject (ordinal 1)
and InjectExplorerTAP (ordinal 2). There are no delay imports.

Imports: ole32.dll, OLEAUT32.dll, RPCRT4.dll, USER32.dll, KERNEL32.dll,
api-ms-win-core-winrt-error-l1-1-1.dll, api-ms-win-core-winrt-l1-1-0.dll,
api-ms-win-core-winrt-error-l1-1-0.dll. These are Windows components.
The source also loads Windows.UI.Xaml.dll from System32. The release project
uses a static CRT; no Visual C++ redistributable DLL or other portable-release
DLL is required for ExplorerTAP.dll.

Restore this exact dependency and its accompanying license/source with
powershell -ExecutionPolicy Bypass -File scripts/fetch-native.ps1.
"@
    [IO.File]::WriteAllText((Join-Path $vendorRoot 'NOTICE.md'), $notice.Replace("`r`n", "`n") + "`n", (New-Object Text.UTF8Encoding($false)))
    Write-Host "Restored TranslucentTB $version ExplorerTAP.dll and upstream license/source."
}
finally {
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    if ($resolvedStage -eq $stage -and [IO.Path]::GetDirectoryName($resolvedStage) -eq $temporaryRoot.TrimEnd('\', '/')) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force -ErrorAction SilentlyContinue
    }
}
