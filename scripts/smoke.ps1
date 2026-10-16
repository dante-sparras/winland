# Run smoke test projects under tests/. Passes when none exist yet.
# These start real windows and send real input. scripts/test.ps1 and CI skip them on purpose.
# Results go to TestResults/ at the repo root, which is gitignored.
# A project whose every test skipped fails the run, so a green run means the tests ran.
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
# The smoke facts skip themselves unless this is set, so a plain dotnet test stays harmless.
$env:WINLAND_SMOKE = '1'
$results = Join-Path $root 'TestResults'
foreach ($project in $projects) {
    $report = Join-Path $results "$($project.BaseName).trx"
    if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report -Force }
    Write-Output "dotnet test $($project.FullName)"
    dotnet test $project.FullName --configuration Release --nologo --logger "trx;LogFileName=$($project.BaseName).trx" --results-directory $results
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if (-not (Test-Path -LiteralPath $report)) {
        Write-Output "No test report at $report."
        exit 1
    }
    [xml]$trx = Get-Content -LiteralPath $report -Raw
    $outcomes = @($trx.TestRun.Results.UnitTestResult)
    $ran = @($outcomes | Where-Object { $_ -and $_.outcome -ne 'NotExecuted' })
    if ($ran.Count -eq 0) {
        Write-Output "Every test in $($project.Name) was skipped, so the smoke lane did not test anything."
        foreach ($result in $outcomes | Where-Object { $_ }) {
            $message = $result.Output.ErrorInfo.Message
            Write-Output "  $($result.testName): $message"
        }
        exit 1
    }
}
