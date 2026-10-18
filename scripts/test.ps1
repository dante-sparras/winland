# Run unit test projects under tests/. Passes when none exist yet.
# Smoke projects stay off this lane. A release run does not execute them either.
# scripts/release.tests.ps1 runs first when it exists.
#   pwsh -NoProfile -File scripts/test.ps1            builds once, then runs every project at the same time
#   pwsh -NoProfile -File scripts/test.ps1 -NoBuild   runs them on the Release build already there, as check.ps1 leaves it
param(
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$releaseTests = Join-Path $PSScriptRoot 'release.tests.ps1'
if (Test-Path $releaseTests) {
    Write-Output "pwsh $releaseTests"
    pwsh -NoProfile -File $releaseTests
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$tests = Join-Path $root 'tests'
if (-not (Test-Path $tests)) {
    Write-Output 'No tests directory. The test lane passes until a project exists under tests/.'
    exit 0
}
$projects = @(Get-ChildItem -Path $tests -Filter *.csproj -Recurse -File | Where-Object {
    $_.Name -like '*.Tests.csproj' -and $_.Name -notlike '*.Smoke.Tests.csproj'
} | Sort-Object FullName)
if ($projects.Count -eq 0) {
    Write-Output 'No unit test project under tests/. The test lane passes until one is added.'
    exit 0
}

# Building once first lets the projects run side by side: each one only runs its tests, and none rebuilds the
# projects they share. Without a solution each project is built in turn.
if (-not $NoBuild) {
    $solutions = @(Get-ChildItem -Path $root -Filter *.slnx -File -ErrorAction SilentlyContinue)
    if ($solutions.Count -eq 0) {
        $solutions = @(Get-ChildItem -Path $root -Filter *.sln -File -ErrorAction SilentlyContinue)
    }
    $targets = if ($solutions.Count -gt 0) { $solutions } else { $projects }
    foreach ($target in $targets) {
        Write-Output "dotnet build $($target.FullName)"
        dotnet build $target.FullName --configuration Release --nologo
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}

# Each project's output is printed whole once it finishes, so the logs do not interleave.
$results = $projects | ForEach-Object -ThrottleLimit ([Math]::Max(2, [Environment]::ProcessorCount)) -Parallel {
    $output = & dotnet test $_.FullName --configuration Release --nologo --no-build 2>&1 | Out-String
    [pscustomobject]@{ Project = $_.FullName; ExitCode = $LASTEXITCODE; Output = $output }
}

# A project that fails runs once more on the same build. Failing again fails the run. Passing the second time
# means a test is flaky: the run passes, and the tests that failed are named as flaky so they get fixed. On CI that
# is a warning annotation and a line in the job summary.
$failed = 0
$flaky = [System.Collections.Generic.List[string]]::new()
foreach ($result in $results | Sort-Object Project) {
    Write-Output "dotnet test $($result.Project)"
    Write-Output $result.Output.TrimEnd()
    if ($result.ExitCode -eq 0) { continue }

    $names = @([regex]::Matches($result.Output, '(?m)^\s+Failed (\S+) \[') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    Write-Output "dotnet test $($result.Project) (second run)"
    $again = & dotnet test $result.Project --configuration Release --nologo --no-build 2>&1 | Out-String
    $code = $LASTEXITCODE
    Write-Output $again.TrimEnd()
    if ($code -ne 0) {
        if ($failed -eq 0) { $failed = $code }
        continue
    }

    $listed = if ($names.Count -gt 0) { $names -join ', ' } else { 'a test the output did not name' }
    $flaky.Add("$([System.IO.Path]::GetFileNameWithoutExtension($result.Project)): $listed")
}

foreach ($entry in $flaky) {
    Write-Output "FLAKY: $entry failed, then passed on a second run. Fix it; a retry is not a pass."
    if ($env:GITHUB_ACTIONS -eq 'true') {
        Write-Output "::warning title=Flaky test::$entry failed, then passed on a second run."
        if ($env:GITHUB_STEP_SUMMARY) {
            Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value "- Flaky: $entry failed, then passed on a second run."
        }
    }
}

exit $failed
