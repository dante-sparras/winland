# Builds Winland into one folder, the way it installs (ADR 0176):
#
#   <Out>\Winland    Winland.Host.exe, Winland.Watch.exe, winland.exe, version.json
#
# version.json names the commit and the version it was built from (ADR 0177). winland.exe is native code, which needs
# Visual Studio's C++ build tools. NativeAOT finds them through vswhere.exe, so its folder goes on the PATH here.
param(
    [string]$Out = (Join-Path $env:TEMP 'winland-publish'),
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$rid = 'win-x64'
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (Test-Path $installer) { $env:PATH = "$installer;$env:PATH" }
$version = & (Join-Path $root 'version.ps1')

function Invoke-Dotnet([string[]]$arguments) {
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($arguments -join ' ') failed with $LASTEXITCODE" }
}

if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
$winland = Join-Path $Out 'Winland'

Write-Host "Winland $version..."
Invoke-Dotnet @('publish', (Join-Path $root 'src\Winland\Winland.csproj'), '-c', $Configuration, '-r', $rid, '-o', $winland, "-p:Version=$version", '--nologo')
# The native client is published on its own and only its exe is kept: the managed copy's dll and json would be read instead.
$native = Join-Path $Out '.native\winland'
Invoke-Dotnet @('publish', (Join-Path $root 'src\Winland.Cli\Winland.Cli.csproj'), '-c', $Configuration, '-r', $rid, '-o', $native, "-p:Version=$version", '--nologo')
Copy-Item (Join-Path $native 'winland.exe') $winland -Force
Remove-Item (Join-Path $winland 'winland.dll'), (Join-Path $winland 'winland.deps.json'), (Join-Path $winland 'winland.runtimeconfig.json') -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force (Join-Path $Out '.native')

$commit = (& git -C $root rev-parse HEAD).Trim()
$date = (& git -C $root show -s --format=%cI HEAD).Trim()
Set-Content (Join-Path $winland 'version.json') ('{"commit":"' + $commit + '","date":"' + $date + '","version":"' + $version + '"}') -NoNewline -Encoding utf8NoBOM

foreach ($file in 'Winland.Host.exe', 'Winland.Watch.exe', 'winland.exe') {
    if (-not (Test-Path (Join-Path $winland $file))) { throw "The publish has no $file" }
}

Write-Host "Published to $Out"
