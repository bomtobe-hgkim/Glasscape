#Requires -Version 5.1
. (Join-Path $PSScriptRoot 'SourceArchivePolicy.ps1')
function New-GlasscapeSourceArchive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$Destination
    )
    $root = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\')
    $destinationPath = [IO.Path]::GetFullPath($Destination)
    $files = @(Get-GlasscapeSourceArchiveFiles -ProjectRoot $root)
    Assert-GlasscapeNoReparsePath -Path $destinationPath
    # Finish traversal before touching an existing archive. A linked source must
    # neither escape the source root nor replace the last valid source package.
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    New-Item -ItemType Directory -Path (Split-Path -Parent $destinationPath) -Force | Out-Null
    $temporaryArchive = $destinationPath + '.tmp-' + [Guid]::NewGuid().ToString('N')
    try {
        $archive = [IO.Compression.ZipFile]::Open($temporaryArchive, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($file in $files | Sort-Object FullName) {
                Assert-GlasscapeNoReparsePath -Path $file.FullName
                $relative = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        }
        finally { $archive.Dispose() }
        Assert-GlasscapeNoReparsePath -Path $destinationPath
        if (Test-Path -LiteralPath $destinationPath) { Remove-Item -LiteralPath $destinationPath }
        Move-Item -LiteralPath $temporaryArchive -Destination $destinationPath
    }
    finally { if (Test-Path -LiteralPath $temporaryArchive) { Remove-Item -LiteralPath $temporaryArchive } }
}
