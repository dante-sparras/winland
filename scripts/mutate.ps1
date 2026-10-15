# Mutation-test each unit test project. Passes when none exist yet.
# The score gate is thresholds.break in stryker-config.json.
#   pwsh -NoProfile -File scripts/mutate.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$config = Join-Path $root 'stryker-config.json'
if (-not (Test-Path $config)) { throw "Missing $config" }
$tests = Join-Path $root 'tests'
if (-not (Test-Path $tests)) {
    Write-Output 'No tests directory. Stryker runs when a project exists under tests/.'
    exit 0
}
$projects = @(Get-ChildItem -Path $tests -Filter *.csproj -Recurse -File | Where-Object {
    $_.Name -like '*.Tests.csproj' -and $_.Name -notlike '*.Smoke.Tests.csproj'
})
if ($projects.Count -eq 0) {
    Write-Output 'No unit test project under tests/. Stryker runs when one is added.'
    exit 0
}
foreach ($project in $projects) {
    $relative = [System.IO.Path]::GetRelativePath($project.DirectoryName, $config)
    Write-Output "Mutation testing $($project.FullName)"
    Push-Location $project.DirectoryName
    try {
        dotnet stryker --config-file $relative
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    finally {
        Pop-Location
    }
}
