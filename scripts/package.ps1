# Build the release assets without starting Winland.
param(
    [Parameter(Mandatory)][ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-dev\.(0|[1-9][0-9]*))?$')][string]$Version,
    [string]$DisplayVersion = $Version,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Commit,
    [string]$OutputDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) ('winland-package-' + [guid]::NewGuid().ToString('n')))
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$head = (git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -ne $Commit) { throw "Checked out $head, expected $Commit." }
if ($DisplayVersion -ne $Version -and $DisplayVersion -cne "$Version+$($Commit.Substring(0, 7))") {
    throw "Bad display version '$DisplayVersion'."
}
if (Test-Path -LiteralPath $OutputDirectory) { throw "Package directory already exists: $OutputDirectory" }
$daemon = Join-Path $OutputDirectory 'pub-daemon'
$stage = Join-Path $OutputDirectory 'stage'
$dist = Join-Path $OutputDirectory 'dist'

# The SDK appends SourceRevisionId to the binary version. version.json holds the display version.
dotnet publish (Join-Path $root 'src/Winland.Daemon/Winland.Daemon.csproj') -c Release -r win-x64 --self-contained true "-p:Version=$Version" "-p:SourceRevisionId=$Commit" -p:ContinuousIntegrationBuild=true -o $daemon
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'winland'), $dist | Out-Null
Copy-Item (Join-Path $daemon '*') (Join-Path $stage 'winland') -Recurse
$json = [ordered]@{
    commit = $Commit
    date = [DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssK')
    version = $DisplayVersion
} | ConvertTo-Json -Compress
$jsonPath = Join-Path $dist 'version.json'
[System.IO.File]::WriteAllText($jsonPath, $json + "`n")
Copy-Item $jsonPath (Join-Path $stage 'version.json')
& tar -a -c -f (Join-Path $dist 'Winland-win-x64.zip') -C $stage .
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$sums = foreach ($name in @('Winland-win-x64.zip', 'version.json')) {
    $hash = (Get-FileHash -Algorithm SHA256 (Join-Path $dist $name)).Hash.ToLowerInvariant()
    "$hash  $name"
}
[System.IO.File]::WriteAllText((Join-Path $dist 'SHA256SUMS'), (($sums -join "`n") + "`n"))
Write-Output "Release assets: $dist"
