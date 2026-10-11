#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('glasscape-publish-test-' + [Guid]::NewGuid().ToString('N'))
$failures = [Collections.Generic.List[string]]::new()
$allowedMedia = @('aurora-flow.mp4', 'aurora-flow.png', 'ocean-drift.mp4', 'ocean-drift.png')
$excludedMedia = @('rain.mp4', 'rain.png', 'rain2.mp4', 'rain2.png', 'town.mp4', 'town.png')

function Check([bool]$condition, [string]$message) {
    if ($condition) { Write-Host "PASS $message" }
    else { $failures.Add($message); Write-Host "FAIL $message" }
}

function New-PublishFixture([string]$name) {
    $root = Join-Path $fixtureRoot $name
    foreach ($directory in @('scripts', 'packaging', 'src/Example', 'assets/samples', 'docs/public', 'docs/superpowers', '.superpowers', 'private', 'third_party/TranslucentTB/win-x64', 'third_party/mpv/win-x64', 'third_party/mpv/licenses', 'third_party/mpv/source', 'dist/Glasscape-win-x64')) {
        New-Item -ItemType Directory -Path (Join-Path $root $directory) -Force | Out-Null
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts/publish.ps1') -Destination (Join-Path $root 'scripts/publish.ps1')
    foreach ($helper in @('SourceArchive.ps1', 'SourceArchivePolicy.ps1')) {
        if (Test-Path -LiteralPath (Join-Path $projectRoot "packaging/$helper")) { Copy-Item -LiteralPath (Join-Path $projectRoot "packaging/$helper") -Destination (Join-Path $root "packaging/$helper") }
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'third_party/TranslucentTB/win-x64/ExplorerTAP.dll') -Destination (Join-Path $root 'third_party/TranslucentTB/win-x64/ExplorerTAP.dll')
    foreach ($file in @('README.md', 'LICENSE.md', 'LICENSE-MIT.txt', 'LICENSE-GPL-3.0.md', 'THIRD_PARTY_NOTICES.md', 'PRIVACY.md', 'SUPPORT.md', 'SECURITY.md', 'src/Example/Program.cs', 'docs/public/guide.md', 'assets/samples/README.md', 'assets/samples/LICENSE-CC0.txt', 'third_party/TranslucentTB/NOTICE.md', 'third_party/TranslucentTB/TranslucentTB-2026.2-source.zip', 'third_party/mpv/NOTICE.md', 'third_party/mpv/licenses/LICENSE.txt', 'third_party/mpv/source/libexample.tar.gz', 'third_party/mpv/win-x64/mpv.exe', 'dist/Glasscape-win-x64/Glasscape.exe')) {
        Set-Content -LiteralPath (Join-Path $root $file) -Value 'fixture' -Encoding Ascii
    }
    $mpvHash = (Get-FileHash -LiteralPath (Join-Path $root 'third_party/mpv/win-x64/mpv.exe') -Algorithm SHA256).Hash
    Set-Content -LiteralPath (Join-Path $root 'third_party/mpv/runtime.sha256') -Value "$mpvHash  mpv.exe" -Encoding Ascii
    foreach ($file in $allowedMedia + $excludedMedia) {
        Set-Content -LiteralPath (Join-Path $root "assets/samples/$file") -Value "source $file" -Encoding Ascii
    }
    foreach ($file in @('.env', '.env.production', 'src/Example/.env.local', 'src/Example/signing.pfx', 'src/Example/signing.p12', 'src/Example/private.key', 'src/Example/private.pem', 'src/Example/credential-cache.json', 'src/Example/credentials.json', 'src/Example/.credentials.json', 'docs/superpowers/private-plan.md', '.superpowers/session.md', 'src/Example/private.log', 'private/session.txt')) {
        Set-Content -LiteralPath (Join-Path $root $file) -Value 'inert excluded fixture' -Encoding Ascii
    }
    foreach ($file in @('cloud-credentials.json', 'user-secrets.json', 'prod.env', 'user.suo', 'site.publishsettings')) {
        Set-Content -LiteralPath (Join-Path $root "src/Example/$file") -Value 'inert excluded fixture' -Encoding Ascii
    }
    return $root
}

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
try {
    $root = New-PublishFixture 'clean'
    $stagingSamples = Join-Path $root 'dist/Glasscape-win-x64/samples'
    New-Item -ItemType Directory -Path $stagingSamples -Force | Out-Null
    foreach ($file in $allowedMedia) {
        Set-Content -LiteralPath (Join-Path $stagingSamples $file) -Value 'stale' -Encoding Ascii
    }
    & (Join-Path $root 'scripts/publish.ps1') -SkipBuild
    $actualMedia = @(Get-ChildItem -LiteralPath $stagingSamples -File | Where-Object Extension -in @('.mp4', '.png') | ForEach-Object Name | Sort-Object)
    Check (($actualMedia -join ',') -eq (($allowedMedia | Sort-Object) -join ',')) 'Staging contains exactly the two allowed video/poster pairs.'
    foreach ($file in $allowedMedia + $excludedMedia) {
        Check ((Get-Content -LiteralPath (Join-Path $root "assets/samples/$file") -Raw).Trim() -eq "source $file") "Source media is preserved: $file"
    }
    foreach ($archivePath in @((Join-Path $root 'dist/Glasscape-win-x64/Glasscape-source.zip'), (Join-Path $root 'dist/Glasscape-win-x64.zip'))) {
        $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
        try {
            Check (@($archive.Entries | Where-Object { $_.Name -in $excludedMedia }).Count -eq 0) "Excluded media is absent from $([IO.Path]::GetFileName($archivePath))."
            foreach ($file in $allowedMedia) {
                Check (@($archive.Entries | Where-Object { $_.Name -eq $file }).Count -eq 1) "Allowed media is included once in $([IO.Path]::GetFileName($archivePath)): $file"
            }
            foreach ($file in @('LICENSE.md', 'LICENSE-MIT.txt', 'LICENSE-GPL-3.0.md', 'THIRD_PARTY_NOTICES.md', 'PRIVACY.md', 'SUPPORT.md', 'SECURITY.md')) {
                Check (@($archive.Entries | Where-Object { $_.Name -eq $file }).Count -eq 1) "Licensing notice is included in $([IO.Path]::GetFileName($archivePath)): $file"
            }
            if ([IO.Path]::GetFileName($archivePath) -eq 'Glasscape-source.zip') {
                foreach ($file in @('cloud-credentials.json', 'user-secrets.json', 'prod.env', 'user.suo', 'site.publishsettings')) {
                    Check (@($archive.Entries | Where-Object { $_.Name -eq $file }).Count -eq 0) "Shared private policy excludes $file from the portable source ZIP."
                }
                foreach ($file in @('.env', '.env.production', 'src/Example/.env.local', 'src/Example/signing.pfx', 'src/Example/signing.p12', 'src/Example/private.key', 'src/Example/private.pem', 'src/Example/credential-cache.json', 'src/Example/credentials.json', 'src/Example/.credentials.json', 'docs/superpowers/private-plan.md', '.superpowers/session.md', 'src/Example/private.log', 'private/session.txt')) {
                    Check (@($archive.Entries | Where-Object { $_.FullName -eq $file }).Count -eq 0) "Sensitive or internal fixture is absent from the source ZIP: $file"
                }
                foreach ($file in @('src/Example/Program.cs', 'docs/public/guide.md', 'third_party/TranslucentTB/TranslucentTB-2026.2-source.zip', 'third_party/mpv/source/libexample.tar.gz')) {
                    Check (@($archive.Entries | Where-Object { $_.FullName -eq $file }).Count -eq 1) "Required source/document material is retained: $file"
                }
            }
        }
        finally { $archive.Dispose() }
    }

    $junctionRoot = New-PublishFixture 'junction'
    $personalRoot = Join-Path $fixtureRoot 'personal'
    New-Item -ItemType Directory -Path $personalRoot -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $personalRoot 'rain.mp4') -Value 'personal video' -Encoding Ascii
    $junction = Join-Path $junctionRoot 'dist/Glasscape-win-x64/samples'
    New-Item -ItemType Junction -Path $junction -Target $personalRoot | Out-Null
    $rejected = $false
    try { & (Join-Path $junctionRoot 'scripts/publish.ps1') -SkipBuild }
    catch { $rejected = $_.Exception.Message -match 'reparse|staging' }
    Check $rejected 'Publishing refuses a staging sample directory redirected through a junction.'
    Check ((Get-Content -LiteralPath (Join-Path $personalRoot 'rain.mp4') -Raw).Trim() -eq 'personal video' -and @(Get-ChildItem -LiteralPath $personalRoot).Count -eq 1) 'A rejected redirected staging directory leaves personal files unchanged.'
    [IO.Directory]::Delete($junction)

    $sourceJunctionRoot = New-PublishFixture 'source-junction'
    $outsideRoot = Join-Path $fixtureRoot 'outside-source'
    New-Item -ItemType Directory -Path $outsideRoot -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $outsideRoot 'outside.cs') -Value 'inert outside source fixture' -Encoding Ascii
    $sourceJunction = Join-Path $sourceJunctionRoot 'src/Example/redirected'
    New-Item -ItemType Junction -Path $sourceJunction -Target $outsideRoot | Out-Null
    $sourceRejected = $false
    try { & (Join-Path $sourceJunctionRoot 'scripts/publish.ps1') -SkipBuild }
    catch { $sourceRejected = $_.Exception.Message -match 'reparse|source' }
    Check $sourceRejected 'Source archive creation refuses a source directory redirected through a junction.'
    Check ((Get-Content -LiteralPath (Join-Path $outsideRoot 'outside.cs') -Raw).Trim() -eq 'inert outside source fixture' -and @(Get-ChildItem -LiteralPath $outsideRoot).Count -eq 1) 'Rejected source enumeration leaves the external target unchanged.'
    [IO.Directory]::Delete($sourceJunction)

    foreach ($bypass in @('nested/settings.json', 'nested/.env', 'samples/old/rain.mp4', 'nested/cloud-credentials.json', 'nested/user-secrets.json', 'nested/prod.env', 'nested/private.log', 'nested/user.suo', 'nested/site.publishsettings')) {
        foreach ($release in @('portable', 'msix')) {
            $caseRoot = New-PublishFixture ('stage-' + $release + '-' + [Guid]::NewGuid().ToString('N'))
            if ($release -eq 'portable') { $stage = Join-Path $caseRoot 'dist/Glasscape-win-x64' }
            else { $stage = Join-Path $caseRoot 'dist/store/staging' }
            $privateFile = Join-Path $stage $bypass
            New-Item -ItemType Directory -Path (Split-Path -Parent $privateFile) -Force | Out-Null
            Set-Content -LiteralPath $privateFile -Value 'inert staged private fixture' -Encoding Ascii
            $previousArchive = if ($release -eq 'portable') { Join-Path $caseRoot 'dist/Glasscape-win-x64.zip' } else { Join-Path $caseRoot 'dist/store/Glasscape_2.1.0.0_x64.msixbundle' }
            Copy-Item -LiteralPath (Join-Path $root 'dist/Glasscape-win-x64.zip') -Destination $previousArchive
            $previousHash = (Get-FileHash -LiteralPath $previousArchive -Algorithm SHA256).Hash
            $blocked = $false
            try {
                if ($release -eq 'portable') { & (Join-Path $caseRoot 'scripts/publish.ps1') -SkipBuild }
                else {
                    Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts/package-msix.ps1') -Destination (Join-Path $caseRoot 'scripts/package-msix.ps1')
                    $sdk = Join-Path $caseRoot 'fake-sdk'
                    New-Item -ItemType Directory -Path $sdk -Force | Out-Null
                    Set-Content -LiteralPath (Join-Path $sdk 'MakeAppx.exe'), (Join-Path $sdk 'MakePri.exe') -Value 'never executed fixture' -Encoding Ascii
                    & (Join-Path $caseRoot 'scripts/package-msix.ps1') -IdentityName 'Fixture.Test' -Publisher 'CN=Fixture' -PublisherDisplayName 'Fixture' -StoreId '000000000000' -SdkBinDirectory $sdk -SkipBuild
                }
            }
            catch { $blocked = $_.Exception.Message -match 'private|excluded media|unsafe release' }
            Check $blocked "$release refuses staged descendant $bypass before release operations."
            Check ((Get-Content -LiteralPath $privateFile -Raw).Trim() -eq 'inert staged private fixture') "$release preserves the rejected staged file $bypass."
            Check ((Get-FileHash -LiteralPath $previousArchive -Algorithm SHA256).Hash -eq $previousHash) "$release preserves its previous archive when staged $bypass is rejected."
        }
    }
    foreach ($release in @('portable', 'msix')) {
        foreach ($linkType in @('Junction', 'SymbolicLink')) {
            $caseRoot = New-PublishFixture ('stage-link-' + [Guid]::NewGuid().ToString('N'))
            $stage = if ($release -eq 'portable') { Join-Path $caseRoot 'dist/Glasscape-win-x64' } else { Join-Path $caseRoot 'dist/store/staging' }
            $nested = Join-Path $stage 'nested'
            New-Item -ItemType Directory -Path $nested -Force | Out-Null
            $outside = Join-Path $fixtureRoot ('outside-stage-' + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $outside -Force | Out-Null
            Set-Content -LiteralPath (Join-Path $outside 'settings.json') -Value 'inert external fixture' -Encoding Ascii
            $nestedLink = Join-Path $nested 'redirected'
            New-Item -ItemType $linkType -Path $nestedLink -Target $outside | Out-Null
            $blocked = $false
            try {
                if ($release -eq 'portable') { & (Join-Path $caseRoot 'scripts/publish.ps1') -SkipBuild }
                else {
                    Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts/package-msix.ps1') -Destination (Join-Path $caseRoot 'scripts/package-msix.ps1')
                    $sdk = Join-Path $caseRoot 'fake-sdk'
                    New-Item -ItemType Directory -Path $sdk -Force | Out-Null
                    Set-Content -LiteralPath (Join-Path $sdk 'MakeAppx.exe'), (Join-Path $sdk 'MakePri.exe') -Value 'never executed fixture' -Encoding Ascii
                    & (Join-Path $caseRoot 'scripts/package-msix.ps1') -IdentityName 'Fixture.Test' -Publisher 'CN=Fixture' -PublisherDisplayName 'Fixture' -StoreId '000000000000' -SdkBinDirectory $sdk -SkipBuild
                }
            }
            catch { $blocked = $_.Exception.Message -match 'reparse point' }
            Check $blocked "$release refuses a nested $linkType before release operations."
            if ($release -eq 'msix') {
                $normalBuildBlocked = $false
                try { & (Join-Path $caseRoot 'scripts/package-msix.ps1') -IdentityName 'Fixture.Test' -Publisher 'CN=Fixture' -PublisherDisplayName 'Fixture' -StoreId '000000000000' -SdkBinDirectory $sdk }
                catch { $normalBuildBlocked = $_.Exception.Message -match 'reparse point' }
                Check $normalBuildBlocked "msix normal build refuses a nested $linkType before recursive staging cleanup."
            }
            Check ((Get-Content -LiteralPath (Join-Path $outside 'settings.json') -Raw).Trim() -eq 'inert external fixture') "$release leaves the $linkType target unchanged."
            [IO.Directory]::Delete($nestedLink)
        }
    }
}
finally {
    $resolvedRoot = [IO.Path]::GetFullPath($fixtureRoot)
    $expectedParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedRoot.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture cleanup escaped the temporary directory.' }
    if (Test-Path -LiteralPath $resolvedRoot) { Remove-Item -LiteralPath $resolvedRoot -Recurse -Force }
}
if ($failures.Count -gt 0) { throw "$($failures.Count) publish verification assertions failed." }
Write-Host 'All publish fixture checks passed.'
