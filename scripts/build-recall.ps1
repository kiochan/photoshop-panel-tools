param([switch]$Package)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'build-archive.ps1')
$recallRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$recallSource = Join-Path $recallRoot 'src\PanelRecall'
$recallOutput = Join-Path $recallRoot 'dist\panel-recall'
$recallDestination = $recallOutput
# Compile into a fresh directory so a failed or older build cannot enter the ZIP.
$recallOutput = Join-Path $recallRoot ('.cache\panel-recall-build\' + [Guid]::NewGuid().ToString('N') + '\panel-recall')
$recallCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $recallCompiler)) { throw 'Windows .NET Framework C# compiler is required.' }
# A dedicated output directory; no installation, process launch or runtime tests.
New-Item -ItemType Directory -Path $recallOutput -Force | Out-Null
$recallSources = @(Get-ChildItem -LiteralPath $recallSource -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName)
$recallShared = @(
    (Join-Path $recallRoot 'shared\windows-native\CanvasClickHook.cs'),
    (Join-Path $recallRoot 'shared\windows-native\ProcessIdentity.cs')
)
& $recallCompiler /nologo /codepage:65001 /langversion:5 /warnaserror+ /optimize+ /target:winexe /platform:x64 `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll `
    "/win32manifest:$(Join-Path $recallSource 'app.manifest')" "/out:$(Join-Path $recallOutput 'PanelRecall.exe')" `
    $recallSources $recallShared
if ($LASTEXITCODE -ne 0) { throw "Panel Recall compilation failed ($LASTEXITCODE)." }
Copy-Item -LiteralPath (Join-Path $recallSource 'app.config') -Destination (Join-Path $recallOutput 'PanelRecall.exe.config') -Force
Copy-Item -LiteralPath (Join-Path $recallSource 'README.md') -Destination (Join-Path $recallOutput 'README.md') -Force
foreach ($recallDocument in @('LICENSE', 'THIRD_PARTY.md', 'COMPATIBILITY.md')) {
    Copy-Item -LiteralPath (Join-Path $recallRoot $recallDocument) -Destination $recallOutput
}
$recallInventory = @(Get-ChildItem -LiteralPath $recallOutput -File | Where-Object Name -ne 'build-manifest.json' | ForEach-Object {
    [ordered]@{ path = $_.Name; bytes = $_.Length; sha256 = Get-BuildHash $_.FullName }
})
$recallManifest = [ordered]@{
    name = 'Panel Recall'; version = '0.1.0'; runtimeVerified = $false
    route = 'move-existing-floating-window'; target = 'Windows x64 / .NET Framework 4.8'
    compatibilityPolicy = 'Photoshop 2019 (20.x) and later'; compatibilityTargets = @('PS 2019', 'PS 2020', 'PS 2025'); files = $recallInventory
    sharedSources = @($recallShared | ForEach-Object { [ordered]@{ path = $_.Substring($recallRoot.Length + 1); sha256 = Get-BuildHash $_ } })
}
$recallUtf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $recallOutput 'build-manifest.json'), ($recallManifest | ConvertTo-Json -Depth 6), $recallUtf8)
New-Item -ItemType Directory -Path $recallDestination -Force | Out-Null
Get-ChildItem -LiteralPath $recallOutput -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $recallDestination $_.Name) -Force
}
Write-Output "Built $recallDestination (not launched)."
if ($Package) {
    $recallArtifacts = Join-Path $recallRoot 'artifacts'
    New-Item -ItemType Directory -Path $recallArtifacts -Force | Out-Null
    $recallZip = Join-Path $recallArtifacts 'panel-recall-0.1.0.zip'
    Write-BuildArchive $recallOutput $recallZip
    $recallHash = Get-BuildHash $recallZip
    [IO.File]::WriteAllText(($recallZip + '.sha256'), ($recallHash + '  panel-recall-0.1.0.zip' + [Environment]::NewLine), $recallUtf8)
    Write-Output "Packaged $recallZip`nSHA256 $recallHash"
}
