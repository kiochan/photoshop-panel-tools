param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Package
)
$ErrorActionPreference = 'Stop'
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appPackage = Get-Content -LiteralPath (Join-Path $appRoot 'package.json') -Raw | ConvertFrom-Json
$appVersion = $appPackage.version
if ($appVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid Panel Tools version.' }
. (Join-Path $workspaceRoot 'scripts\build-archive.ps1')
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Panel Mirror requires .NET SDK 8 or later; the .NET Framework compiler used by Panel Recall cannot build this project.'
}
$mirrorSdks = @(& dotnet --list-sdks)
if ($LASTEXITCODE -ne 0 -or -not @($mirrorSdks | Where-Object { $_ -match '^(\d+)\.' -and [int]$Matches[1] -ge 8 }).Count) {
    throw 'Panel Mirror requires .NET SDK 8 or later. No suitable SDK is installed; no publish or runtime test was started.'
}
$taskProject = Join-Path $appRoot 'src\PanelMirror\PanelMirror.csproj'
$taskStage = Join-Path $workspaceRoot ('.cache\build\panel-tools-' + [Guid]::NewGuid().ToString('N') + '\panel-tools')
$taskOutput = Join-Path $taskStage 'helper'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
# Build only. This script neither installs the extension nor launches the helper.
& dotnet publish $taskProject -c $Configuration -r win-x64 --self-contained true -o $taskOutput `
    -p:Version=$appVersion -p:DebugType=None -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
foreach ($taskName in @('index.html', 'panel.js', 'panel.css', 'CSXS')) {
    Copy-Item -LiteralPath (Join-Path $appRoot ('cep\' + $taskName)) -Destination $taskStage -Recurse
}
Copy-Item -LiteralPath (Join-Path $appRoot 'README.md') -Destination $taskStage
Copy-Item -LiteralPath (Join-Path $appRoot 'README.zh-CN.md') -Destination $taskStage
Copy-Item -LiteralPath (Join-Path $appRoot 'screenshots') -Destination $taskStage -Recurse
Copy-Item -LiteralPath (Join-Path $appRoot 'docs') -Destination $taskStage -Recurse
foreach ($taskDocument in @('COMPATIBILITY.md', 'LICENSE', 'THIRD_PARTY.md')) { Copy-Item -LiteralPath (Join-Path $workspaceRoot $taskDocument) -Destination $taskStage }
$stagedManifestPath = Join-Path $taskStage 'CSXS\manifest.xml'
$stagedManifest = [xml](Get-Content -LiteralPath $stagedManifestPath -Raw)
$stagedManifest.ExtensionManifest.ExtensionBundleVersion = $appVersion
foreach ($extension in $stagedManifest.ExtensionManifest.ExtensionList.Extension) { $extension.Version = $appVersion }
$stagedManifest.Save($stagedManifestPath)
$taskInventory = @(Get-ChildItem -LiteralPath $taskStage -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = $_.FullName.Substring($taskStage.Length + 1).Replace('\', '/'); bytes = $_.Length; sha256 = Get-BuildHash $_.FullName }
})
$taskManifest = [ordered]@{
    name = 'Panel Tools'; version = $appVersion; runtimeVerified = $false
    compatibilityPolicy = 'Photoshop 2019 (20.x) and later; host runtime validation pending'
    target = 'Windows x64 / self-contained .NET 8'; files = $taskInventory
}
$taskUtf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $taskStage 'build-manifest.json'), ($taskManifest | ConvertTo-Json -Depth 6), $taskUtf8)
$taskDistribution = Join-Path $workspaceRoot 'dist\panel-tools'
New-Item -ItemType Directory -Path $taskDistribution -Force | Out-Null
Get-ChildItem -LiteralPath $taskStage | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $taskDistribution -Recurse -Force }
# Preserve the original direct-launch build location too.
$taskLauncherOutput = Join-Path $appRoot 'cep\helper'
New-Item -ItemType Directory -Path $taskLauncherOutput -Force | Out-Null
Get-ChildItem -LiteralPath $taskOutput -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $taskLauncherOutput -Force }
Write-Output "Build output: $taskOutput"
if ($Package) {
    $taskArtifacts = Join-Path $workspaceRoot 'artifacts'
    New-Item -ItemType Directory -Path $taskArtifacts -Force | Out-Null
    $taskArchive = Join-Path $taskArtifacts ('panel-tools-' + $appVersion + '.zip')
    # Package only this successful, fresh stage, never an old dist directory.
    Write-BuildArchive $taskStage $taskArchive
    $taskDigest = Get-BuildHash $taskArchive
    [IO.File]::WriteAllText(($taskArchive + '.sha256'), ($taskDigest + ('  panel-tools-' + $appVersion + '.zip') + [Environment]::NewLine), $taskUtf8)
    Write-Output "Packaged $taskArchive (not installed or launched)."
}
