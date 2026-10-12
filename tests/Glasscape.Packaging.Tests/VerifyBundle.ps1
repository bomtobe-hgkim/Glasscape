#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Bundle,
    [string]$IdentityName = 'BOMTOBE.Glasscape',
    [string]$Publisher = 'CN=20298E27-3AA6-4580-9D83-5FC1AF024E55',
    [string]$PublisherDisplayName = 'BOMTOBE',
    [string]$Version = '2.1.3.0'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $Bundle) { $Bundle = Join-Path $PSScriptRoot '..\..\dist\store\Glasscape_2.1.3.0_x64.msixbundle' }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$privateReleasePathPattern = '(^|/)[^/]*\.env[^/]*$|\.(log|suo|publishsettings|pfx|p12|key|pem)$|(^|/)[^/]*(credential|secret)[^/]*\.json$|(^|/)settings\.json$'
function Assert([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message }; Write-Host "PASS: $Message" }
function Read-XmlEntry($Archive, [string]$Name) {
    $entry = $Archive.GetEntry($Name)
    if ($null -eq $entry) { throw "Missing XML entry: $Name" }
    $reader = New-Object IO.StreamReader($entry.Open())
    try { [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
}
$bundleArchive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Bundle))
try {
    $bundleManifest = Read-XmlEntry $bundleArchive 'AppxMetadata/AppxBundleManifest.xml'
    Assert ($bundleManifest.Bundle.Identity.Name -eq $IdentityName -and $bundleManifest.Bundle.Identity.Publisher -eq $Publisher -and $bundleManifest.Bundle.Identity.Version -eq $Version) 'Bundle identity equals the observed Store identity and release version.'
    $payloads = @($bundleManifest.Bundle.Packages.Package)
    Assert ($payloads.Count -eq 1 -and $payloads[0].Architecture -eq 'x64') 'The bundle contains exactly one x64 application package.'
    $stream = New-Object IO.MemoryStream
    $entryStream = $bundleArchive.GetEntry($payloads[0].FileName).Open()
    try { $entryStream.CopyTo($stream) } finally { $entryStream.Dispose() }
    $stream.Position = 0
    $package = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Read)
    try {
        $manifest = Read-XmlEntry $package 'AppxManifest.xml'
        Assert ($manifest.Package.Identity.Name -eq $IdentityName -and $manifest.Package.Identity.Publisher -eq $Publisher -and $manifest.Package.Identity.Version -eq $Version -and $manifest.Package.Properties.PublisherDisplayName -eq $PublisherDisplayName) 'The packaged manifest preserves all Partner Center identity values.'
        $languages = @($manifest.Package.Resources.Resource | ForEach-Object { $_.Language } | Sort-Object)
        Assert (($languages -join ',') -eq 'en-US,ko-KR') 'The package declares English and Korean.'
        $application = $manifest.Package.Applications.Application
        Assert ($application.Executable -eq 'Glasscape.exe' -and $application.GetAttribute('TrustLevel', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/10') -eq 'mediumIL') 'The packaged WinForms executable runs at normal user integrity.'
        $capabilities = @($manifest.Package.Capabilities.ChildNodes | Where-Object { $_.NodeType -eq 'Element' })
        Assert ($capabilities.Count -eq 1 -and $capabilities[0].Name -eq 'runFullTrust') 'Only the required runFullTrust capability is declared.'
        $startup = $application.Extensions.ChildNodes | Where-Object { $_.Category -eq 'windows.startupTask' }
        Assert ($startup.StartupTask.TaskId -eq 'GlasscapeStartup' -and $startup.StartupTask.Enabled -eq 'false' -and $startup.GetAttribute('Parameters', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/10') -eq '--background') 'Windows sign-in startup is opt-in and launches directly into the tray.'
        $files = @($package.Entries | ForEach-Object { $_.FullName })
        $samples = @($files | Where-Object { $_ -match '^samples/.*\.(mp4|png)$' } | Sort-Object)
        Assert (($samples -join ',') -eq 'samples/aurora-flow.mp4,samples/aurora-flow.png,samples/ocean-drift.mp4,samples/ocean-drift.png') 'The shipped videos and posters are exactly the two approved originals.'
        Assert (@($files | Where-Object { $_ -match '(^|/)(rain|rain2|town)\.(mp4|png)$|(^|/)(settings\.json|Install-Glasscape\.cmd)$|\.(pdb|log|tmp)$' }).Count -eq 0) 'No excluded media, user settings, installer command, or debug artifacts are packaged.'
        Assert (@($files | Where-Object { $_ -match $privateReleasePathPattern }).Count -eq 0) 'No environment variant, private log/key/profile, credential JSON, or staged user settings are packaged.'
        Assert (@($files | Where-Object { $_ -match '^resources.*\.pri$' }).Count -eq 1) 'Manifest resources are delivered in one complete PRI index.'
        foreach ($required in @('coreclr.dll', 'hostfxr.dll', 'WindowsBase.dll', 'Microsoft.Windows.SDK.NET.dll', 'WinRT.Runtime.dll', 'ExplorerTAP.dll', 'mpv/mpv.exe', 'LICENSE.md', 'TranslucentTB-LICENSE.md', 'TranslucentTB-NOTICE.md', 'mpv-NOTICE.md', 'Framework-Licenses/Microsoft.NETCore.App/LICENSE.TXT', 'Framework-Licenses/Microsoft.NETCore.App/THIRD-PARTY-NOTICES.TXT', 'Framework-Licenses/Microsoft.WindowsDesktop.App/LICENSE', 'Framework-Licenses/CsWinRT-LICENSE.txt', 'Framework-Licenses/sdk_license.rtf', 'Sources/Glasscape-source.zip', 'Sources/TranslucentTB-2026.2-source.zip', 'Sources/mpv/mpv-v0.41.0-source.zip', 'Sources/mpv/source/FFmpeg-1fcb201.zip', 'Sources/mpv/source/libiconv-1.18.tar.gz', 'Sources/mpv/provenance/build-mingw64.sh')) {
            Assert ($files -contains $required) "The package includes $required."
        }
        foreach ($required in @('LICENSE-MIT.txt', 'LICENSE-GPL-3.0.md', 'THIRD_PARTY_NOTICES.md', 'PRIVACY.md', 'SUPPORT.md', 'SECURITY.md', 'SECURITY_REVIEW.md', 'Framework-Licenses/WindowsSDK-NET-Ref-LICENSE.rtf')) {
            Assert ($files -contains $required) "The package includes $required."
        }
        $runtimeEntry = $package.GetEntry('Glasscape.runtimeconfig.json')
        $reader = New-Object IO.StreamReader($runtimeEntry.Open())
        try { $runtime = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        Assert (@($runtime.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.WindowsDesktop.App' -and $_.version -like '10.*' }).Count -eq 1) '.NET 10 Windows Desktop is bundled for self-contained execution.'
        $sourceCount = 0
        foreach ($zipEntry in $package.Entries | Where-Object { $_.FullName -like 'Sources/*.zip' }) {
            $zip = New-Object IO.Compression.ZipArchive($zipEntry.Open(), [IO.Compression.ZipArchiveMode]::Read)
            try {
                foreach ($sourceEntry in $zip.Entries) {
                    if ($sourceEntry.FullName -match '(^|/)(rain|rain2|town)\.(mp4|png)$') { throw "Excluded media inside source archive $($zipEntry.FullName): $($sourceEntry.FullName)" }
                    if ($zipEntry.FullName -eq 'Sources/Glasscape-source.zip' -and ($sourceEntry.FullName -match '(^|/)(docs/superpowers|\.superpowers|bin|obj)/' -or $sourceEntry.FullName -match $privateReleasePathPattern)) { throw "Private/internal file inside application source archive: $($sourceEntry.FullName)" }
                }
                $sourceCount++
            }
            finally { $zip.Dispose() }
        }
        Assert ($sourceCount -ge 4) 'All included source ZIPs were checked for excluded media.'
    }
    finally { $package.Dispose(); $stream.Dispose() }
}
finally { $bundleArchive.Dispose() }
