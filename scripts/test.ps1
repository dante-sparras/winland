# Run unit test projects under tests/. Passes when none exist yet.
# Smoke projects stay off this lane. A release run does not execute them either.
#   pwsh -NoProfile -File scripts/test.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$tests = Join-Path $root 'tests'
if (-not (Test-Path $tests)) {
    Write-Output 'No tests directory. The test lane passes until a project exists under tests/.'
    exit 0
}
$projects = @(Get-ChildItem -Path $tests -Filter *.csproj -Recurse -File | Where-Object {
    $_.Name -like '*.Tests.csproj' -and $_.Name -notlike '*.Smoke.Tests.csproj'
})
if ($projects.Count -eq 0) {
    Write-Output 'No unit test project under tests/. The test lane passes until one is added.'
    exit 0
}
foreach ($project in $projects) {
    Write-Output "dotnet test $($project.FullName)"
    dotnet test $project.FullName --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
