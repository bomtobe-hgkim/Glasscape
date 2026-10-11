param()

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination = [IO.Path]::GetFullPath((Join-Path $repoRoot 'third_party/mpv'))
$runtime = [IO.Path]::GetFullPath((Join-Path $destination 'win-x64'))
$packageName = 'mpv-v0.41.0-x86_64-w64-mingw32.zip'
$packageUrl = "https://github.com/mpv-player/mpv/releases/download/v0.41.0/$packageName"
$packageSha256 = 'a49811c0752c108b8260636f9c6f6fcb97406641c98b30f1e7b500dfb20177de'
$innerName = 'mpv-git-2025-12-21-41f6a64-x86_64.zip'
$innerSha256 = '443afe07e61e3f742743349d01d8dac95f0c5390a766a9d3d5b1b5f05911bae5'
$sourceUrl = 'https://github.com/mpv-player/mpv/archive/refs/tags/v0.41.0.zip'
$sourceSha256 = '7a37ded4ae1655528525c008736e3abd2f0f360e94140107d62185d056a20c46'

function Assert-ChildPath([string]$Path, [string]$Parent) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($Parent).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Archive target is outside the intended directory: $resolved"
    }
    return $resolved
}

function Get-VerifiedDownload([string]$Url, [string]$Path, [string]$ExpectedSha256) {
    if (-not (Test-Path -LiteralPath $Path)) {
        Invoke-WebRequest -Uri $Url -OutFile $Path
    }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $ExpectedSha256) {
        throw "SHA-256 mismatch for $Path. Expected $ExpectedSha256; found $actual. The file was not extracted."
    }
}

function Expand-CheckedZip([string]$Archive, [string]$Target) {
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        foreach ($entry in $zip.Entries) {
            $entryTarget = Assert-ChildPath (Join-Path $Target $entry.FullName) $Target
            if ($entry.FullName.EndsWith('/')) {
                New-Item -ItemType Directory -Path $entryTarget -Force | Out-Null
                continue
            }
            New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($entryTarget)) -Force | Out-Null
            if (Test-Path -LiteralPath $entryTarget -PathType Leaf) {
                $entryStream = $entry.Open()
                $sha = [Security.Cryptography.SHA256]::Create()
                try { $entryHash = ([BitConverter]::ToString($sha.ComputeHash($entryStream))).Replace('-', '').ToLowerInvariant() }
                finally { $sha.Dispose(); $entryStream.Dispose() }
                if ((Get-FileHash -LiteralPath $entryTarget -Algorithm SHA256).Hash.ToLowerInvariant() -eq $entryHash) {
                    continue # Do not rewrite a verified file; the player may be running.
                }
            }
            $inputStream = $entry.Open()
            $outputStream = [IO.File]::Create($entryTarget)
            try { $inputStream.CopyTo($outputStream) }
            finally { $outputStream.Dispose(); $inputStream.Dispose() }
        }
    }
    finally { $zip.Dispose() }
}

[void](Assert-ChildPath $destination $repoRoot)
[void](Assert-ChildPath $runtime $destination)
New-Item -ItemType Directory -Path $destination, $runtime -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$package = Join-Path $destination $packageName
$inner = Join-Path $destination $innerName
$source = Join-Path $destination 'mpv-v0.41.0-source.zip'
Get-VerifiedDownload $packageUrl $package $packageSha256
$outer = [IO.Compression.ZipFile]::OpenRead($package)
try {
    if ($outer.Entries.Count -ne 1 -or $outer.Entries[0].FullName -ne $innerName) {
        throw 'The pinned release package has an unexpected layout.'
    }
}
finally { $outer.Dispose() }
Expand-CheckedZip $package $destination
if ((Get-FileHash -LiteralPath $inner -Algorithm SHA256).Hash.ToLowerInvariant() -ne $innerSha256) {
    throw 'The nested runtime archive did not match its expected SHA-256.'
}
Expand-CheckedZip $inner $runtime
Get-VerifiedDownload $sourceUrl $source $sourceSha256
New-Item -ItemType Directory -Path (Join-Path $destination 'licenses'), (Join-Path $destination 'provenance'), (Join-Path $destination 'source') -Force | Out-Null
$sourceArchive = [IO.Compression.ZipFile]::OpenRead($source)
try {
    foreach ($pair in @(
        @('Copyright', 'licenses/mpv-Copyright'),
        @('LICENSE.GPL', 'licenses/mpv-LICENSE.GPL'),
        @('LICENSE.LGPL', 'licenses/mpv-LICENSE.LGPL'),
        @('ci/build-mingw64.sh', 'provenance/build-mingw64.sh'),
        @('ci/build-common.sh', 'provenance/build-common.sh'),
        @('.github/workflows/build.yml', 'provenance/build.yml')
    )) {
        $entry = $sourceArchive.GetEntry('mpv-0.41.0/' + $pair[0])
        if ($null -eq $entry) { throw "Missing source provenance file: $($pair[0])" }
        $target = Assert-ChildPath (Join-Path $destination $pair[1]) $destination
        $inputStream = $entry.Open()
        $outputStream = [IO.File]::Create($target)
        try { $inputStream.CopyTo($outputStream) }
        finally { $outputStream.Dispose(); $inputStream.Dispose() }
    }
}
finally { $sourceArchive.Dispose() }
Get-VerifiedDownload 'https://github.com/FFmpeg/FFmpeg/archive/1fcb201.zip' (Join-Path $destination 'source/FFmpeg-1fcb201.zip') '2358b01dd8ec76fa8fe2a07ce8c96b01a471ef1c224221ee77afe545687fb9da'
Get-VerifiedDownload 'https://github.com/haasn/libplacebo/archive/47587a2d47299f7261a01b77e989b74f80a99769.zip' (Join-Path $destination 'source/libplacebo-build-date-snapshot.zip') '7f27922f607f108b80339a9387d3a92dd2d8723965d24ac777bea4544240df89'
Get-VerifiedDownload 'https://ftp.gnu.org/gnu/libiconv/libiconv-1.18.tar.gz' (Join-Path $destination 'source/libiconv-1.18.tar.gz') '3b08f5f4f9b4eb82f151a7040bfd6fe6c6fb922efe4b1659c66ea933276965e8'
if (-not (Test-Path -LiteralPath (Join-Path $runtime 'mpv.exe'))) { throw 'mpv.exe was not extracted.' }
$manifest = Get-ChildItem -LiteralPath $runtime -File | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
$manifest | Set-Content -LiteralPath (Join-Path $destination 'runtime.sha256') -Encoding UTF8
Write-Output "Verified mpv 0.41.0 runtime: $(Join-Path $runtime 'mpv.exe')"
Write-Output 'No file associations, registry changes, or interactive player windows were created.'
