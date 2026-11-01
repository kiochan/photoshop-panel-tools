# Shared Windows PowerShell / PowerShell Core helpers; no optional module autoload.
function Get-BuildHash([string]$Path) {
    $hashStream = [IO.File]::OpenRead($Path)
    $hashAlgorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hashAlgorithm.ComputeHash($hashStream))).Replace('-', '').ToLowerInvariant() }
    finally { $hashAlgorithm.Dispose(); $hashStream.Dispose() }
}

function Write-BuildArchive([string]$Source, [string]$Destination) {
    $archiveRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $archiveSource = (Resolve-Path -LiteralPath $Source).Path
    $archiveTarget = [IO.Path]::GetFullPath($Destination)
    $archiveAllowed = @((Join-Path $archiveRoot '.cache\'), (Join-Path $archiveRoot 'dist\'))
    if (-not ($archiveAllowed | Where-Object { $archiveSource.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) })) { throw 'Archive source is outside managed output.' }
    if ([IO.Path]::GetDirectoryName($archiveTarget) -ne (Join-Path $archiveRoot 'artifacts')) { throw 'Archive target is outside artifacts.' }
    if ((Get-Item -LiteralPath $archiveSource -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Archive source cannot be a link.' }
    $archiveItems = @(Get-ChildItem -LiteralPath $archiveSource -Recurse -Force)
    if ($archiveItems | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Archive source cannot contain links.' }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archiveTemp = $archiveTarget + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        $archiveZip = [IO.Compression.ZipFile]::Open($archiveTemp, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($archiveItem in ($archiveItems | Where-Object { -not $_.PSIsContainer } | Sort-Object FullName)) {
                $archiveEntry = [IO.Path]::GetFileName($archiveSource) + '/' + $archiveItem.FullName.Substring($archiveSource.Length + 1).Replace('\', '/')
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archiveZip, $archiveItem.FullName, $archiveEntry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        } finally { $archiveZip.Dispose() }
        if ([IO.File]::Exists($archiveTarget)) { [IO.File]::Replace($archiveTemp, $archiveTarget, [NullString]::Value) }
        else { [IO.File]::Move($archiveTemp, $archiveTarget) }
    } finally { if ([IO.File]::Exists($archiveTemp)) { [IO.File]::Delete($archiveTemp) } }
}
