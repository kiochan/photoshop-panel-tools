param([Parameter(Mandatory=$true)][string]$Source, [Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'build-archive.ps1')
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourceStage = (Resolve-Path -LiteralPath $Source).Path
$sourceArchive = [IO.Path]::GetFullPath($Destination)
$sourceBase = Join-Path $sourceRoot '.cache\build\'
if (-not $sourceStage.StartsWith($sourceBase, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($sourceStage) -ne 'photoshop-panel-tools') { throw 'Unexpected source stage.' }
if ($sourceArchive -ne (Join-Path $sourceRoot 'artifacts\photoshop-panel-tools-source.zip')) { throw 'Unexpected source archive.' }
Write-BuildArchive $sourceStage $sourceArchive
