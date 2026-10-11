#Requires -Version 5.1
# One policy for source archives and both release producers.
function Test-GlasscapePrivatePath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)
    $path = $RelativePath.Replace('\', '/')
    $name = ($path -split '/')[-1]
    if ($path -match '(^|/)(\.aws|\.ssh|\.azure|\.gnupg|\.secrets|credentials|secrets)(/|$)') { return $true }
    if ($name -match '(^|\.)env($|\.)|^\.env|(?:credential|secret).*\.json$') { return $true }
    if ($name -match '\.(pfx|p12|key|pem|log|dmp|pdb|user|suo|publishsettings|kdbx|jks|keystore|userprefs|cache|tmp|bak)$') { return $true }
    return $name -match '^(settings\.json|\.npmrc|\.pypirc|\.netrc|\.git-credentials|id_rsa|id_ed25519)$'
}

function Test-GlasscapeExcludedMedia {
    param([Parameter(Mandatory = $true)][string]$RelativePath, [switch]$Source)
    $path = $RelativePath.Replace('\', '/')
    $name = ($path -split '/')[-1]
    $samplePrefix = if ($Source) { 'assets/samples/' } else { 'samples/' }
    $allowed = @('aurora-flow.mp4', 'aurora-flow.png', 'ocean-drift.mp4', 'ocean-drift.png') | ForEach-Object { $samplePrefix + $_ }
    if ($name -match '^(rain|rain2|town)\.(mp4|png)$') { return $true }
    if ($name -match '\.(mp4|mkv|webm|mov|avi|wmv)$') { return $path -notin $allowed }
    if ($path.StartsWith($samplePrefix, [StringComparison]::OrdinalIgnoreCase) -and $name -match '\.png$') { return $path -notin $allowed }
    return $false
}

function Assert-GlasscapeNoReparsePath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        $item = Get-Item -LiteralPath $cursor -Force -ErrorAction SilentlyContinue
        if ($null -ne $item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Unsafe path through a reparse point: $cursor" }
        $parent = Split-Path -Parent $cursor
        if ($parent -eq $cursor) { break }
        $cursor = $parent
    }
}

function Assert-GlasscapeReleaseTree {
    param([Parameter(Mandatory = $true)][string]$Root)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    Assert-GlasscapeNoReparsePath -Path $rootPath
    $rootItem = Get-Item -LiteralPath $rootPath -Force
    if (-not $rootItem.PSIsContainer) { throw 'Unsafe release staging: expected a directory.' }
    $directories = [Collections.Generic.Stack[IO.DirectoryInfo]]::new()
    $directories.Push($rootItem)
    while ($directories.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath $directories.Pop().FullName -Force) {
            $fullPath = [IO.Path]::GetFullPath($item.FullName)
            if (-not $fullPath.StartsWith($rootPath + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe release staging: descendant escaped its root.' }
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Unsafe release staging: reparse point: $fullPath" }
            $relative = $fullPath.Substring($rootPath.Length + 1).Replace('\', '/')
            if (Test-GlasscapePrivatePath -RelativePath $relative) { throw "Unsafe release staging: private file or directory: $relative" }
            if (Test-GlasscapeExcludedMedia -RelativePath $relative) { throw "Unsafe release staging: excluded media: $relative" }
            if ($item.PSIsContainer) { $directories.Push($item) }
        }
    }
}

function Get-GlasscapeSourceArchiveFiles {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot)
    $root = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\')
    Assert-GlasscapeNoReparsePath -Path $root
    $sourceRoots = @('src', 'tests', 'scripts', 'packaging', 'design', 'assets', 'docs', 'third_party')
    $rootFiles = @('README.md', 'LICENSE.md', 'LICENSE-MIT.txt', 'LICENSE-GPL-3.0.md', 'THIRD_PARTY_NOTICES.md', 'PRIVACY.md', 'SUPPORT.md', 'SECURITY.md', 'SECURITY_REVIEW.md', 'CONTRIBUTING.md', 'CHANGELOG.md', 'CODE_OF_CONDUCT.md', 'TransparentTaskbar.slnx', 'global.json', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'NuGet.config', '.gitignore', '.gitattributes')
    $ignoredDirectories = @('bin', 'obj', 'dist', 'artifacts', '.git', '.vs', '.superpowers', 'superpowers', 'generation-tools', 'win-x64', 'node_modules')
    $files = [Collections.Generic.List[IO.FileInfo]]::new()
    $directories = [Collections.Generic.Stack[IO.DirectoryInfo]]::new()
    $directories.Push((Get-Item -LiteralPath $root -Force))
    while ($directories.Count -gt 0) {
        $directory = $directories.Pop()
        foreach ($item in Get-ChildItem -LiteralPath $directory.FullName -Force) {
            if ($directory.FullName -eq $root) {
                if ($item.PSIsContainer -and $item.Name -notin $sourceRoots) { continue }
                if (-not $item.PSIsContainer -and $item.Name -notin $rootFiles) { continue }
            }
            if ($item.PSIsContainer -and $item.Name -in $ignoredDirectories) { continue }
            $fullPath = [IO.Path]::GetFullPath($item.FullName)
            if (-not $fullPath.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Source archive path escaped its root.' }
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Source archive item is a reparse point: $fullPath" }
            $relative = $fullPath.Substring($root.Length + 1).Replace('\', '/')
            if (Test-GlasscapePrivatePath -RelativePath $relative) { continue }
            if (Test-GlasscapeExcludedMedia -RelativePath $relative -Source) { continue }
            if ($item.PSIsContainer) { $directories.Push($item); continue }
            if ($item.Extension -in @('.exe', '.dll', '.com') -or $item.Name -in @('mpv-v0.41.0-x86_64-w64-mingw32.zip', 'mpv-git-2025-12-21-41f6a64-x86_64.zip')) { continue }
            $files.Add($item)
        }
    }
    return $files
}
