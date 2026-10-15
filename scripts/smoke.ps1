# Run smoke test projects under tests/. Passes when none exist yet.
# These start real windows and send real input. scripts/test.ps1 and CI skip them on purpose.
# Results go to TestResults/ at the repo root, which is gitignored.
#   pwsh -NoProfile -File scripts/smoke.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$tests = Join-Path $root 'tests'
if (-not (Test-Path $tests)) {
    Write-Output 'No tests directory. The smoke lane passes until a project exists under tests/.'
    exit 0
}
$projects = @(Get-ChildItem -Path $tests -Filter *.Smoke.Tests.csproj -Recurse -File)
if ($projects.Count -eq 0) {
    Write-Output 'No smoke test project under tests/. The smoke lane passes until one is added.'
    exit 0
}
$results = Join-Path $root 'TestResults'
foreach ($project in $projects) {
    Write-Output "dotnet test $($project.FullName)"
    dotnet test $project.FullName --configuration Release --nologo --logger trx --results-directory $results
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
