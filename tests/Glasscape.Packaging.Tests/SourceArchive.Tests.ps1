#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$helper = Join-Path $PSScriptRoot '..\..\packaging\SourceArchive.ps1'
if (Test-Path -LiteralPath $helper) { . $helper }
if (-not (Get-Command New-GlasscapeSourceArchive -ErrorAction SilentlyContinue)) { throw 'Source packaging has no safe archive builder.' }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('glasscape-source-package-test-' + [Guid]::NewGuid().ToString('N'))
$fixtureRoot = Join-Path $temporaryRoot 'fixture'
$outsideRoot = Join-Path $temporaryRoot 'outside'
$archivePath = Join-Path $temporaryRoot 'source.zip'
$link = Join-Path $fixtureRoot 'src\outside-link'
New-Item -ItemType Directory -Path $fixtureRoot, $outsideRoot -Force | Out-Null
$fixtureFiles = @(
    'README.md', 'LICENSE.md', 'LICENSE-GPL-3.0.md', 'LICENSE-MIT.txt', 'THIRD_PARTY_NOTICES.md', 'PRIVACY.md', 'SUPPORT.md',
    'src/Program.cs', 'src/nested/Useful.cs', 'scripts/build.ps1', 'packaging/AppxManifest.xml.template', 'tests/Program.cs', 'docs/public-guide.md',
    'assets/app.ico', 'assets/samples/aurora-flow.mp4', 'assets/samples/ocean-drift.png',
    'assets/samples/rain.mp4', 'assets/samples/rain2.png', 'assets/samples/town.mp4', 'assets/personal.mp4',
    'docs/superpowers/private-plan.md', '.superpowers/internal.md', 'src/bin/stale.dll', 'src/obj/stale.cs',
    'src/.env', 'src/.env.local', 'src/prod.env', 'src/key.pfx', 'src/key.p12', 'src/key.key', 'src/key.pem', 'src/cloud-credentials.json', 'src/user-secrets.json',
    'src/private.log', 'src/user.suo', 'src/site.publishsettings', 'src/nested/settings.json',
    'third_party/mpv/provenance/license-sources.json', 'third_party/mpv/source/FFmpeg-source.zip', 'third_party/TranslucentTB/TranslucentTB-2026.2-source.zip'
)
try {
    foreach ($relative in $fixtureFiles) {
        $path = Join-Path $fixtureRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        [IO.File]::WriteAllText($path, 'Controlled fixture; no real credentials or user media.')
    }
    New-GlasscapeSourceArchive -ProjectRoot $fixtureRoot -Destination $archivePath
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName } | Sort-Object)
        $expected = @('LICENSE-GPL-3.0.md', 'LICENSE-MIT.txt', 'LICENSE.md', 'PRIVACY.md', 'README.md', 'SUPPORT.md', 'THIRD_PARTY_NOTICES.md', 'assets/app.ico', 'assets/samples/aurora-flow.mp4', 'assets/samples/ocean-drift.png', 'docs/public-guide.md', 'packaging/AppxManifest.xml.template', 'scripts/build.ps1', 'src/Program.cs', 'src/nested/Useful.cs', 'tests/Program.cs', 'third_party/mpv/provenance/license-sources.json', 'third_party/mpv/source/FFmpeg-source.zip', 'third_party/TranslucentTB/TranslucentTB-2026.2-source.zip') | Sort-Object
        if (Compare-Object $expected $entries) { throw ('Unexpected source archive entries: ' + ($entries -join ', ')) }
    }
    finally { $archive.Dispose() }
    foreach ($relative in $fixtureFiles) { if (-not (Test-Path -LiteralPath (Join-Path $fixtureRoot $relative))) { throw "Source packaging removed an original file: $relative" } }
    Write-Output 'PASS: source ZIP includes public build inputs and excludes private media, internal plans, credentials and generated files without deleting originals.'

    $originalHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    [IO.File]::WriteAllText((Join-Path $outsideRoot 'private.cs'), 'Controlled outside-root fixture.')
    New-Item -ItemType Junction -Path $link -Target $outsideRoot | Out-Null
    $rejected = $false
    try { New-GlasscapeSourceArchive -ProjectRoot $fixtureRoot -Destination $archivePath }
    catch { if ($_.Exception.Message -notlike '*reparse point*') { throw }; $rejected = $true }
    if (-not $rejected) { throw 'Source packaging followed an out-of-root junction.' }
    if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $originalHash) { throw 'Unsafe source traversal replaced the previously valid archive.' }
    Write-Output 'PASS: source junctions are rejected before replacing a valid archive.'
}
finally {
    if (Test-Path -LiteralPath $link) { [IO.Directory]::Delete($link) }
    $resolved = [IO.Path]::GetFullPath($temporaryRoot)
    if (-not $resolved.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase) -or -not (Split-Path -Leaf $resolved).StartsWith('glasscape-source-package-test-')) { throw 'Unsafe fixture cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
