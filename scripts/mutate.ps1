# Mutation-test the unit test projects, one lane per project under test. Passes when none exist yet.
# The score gate is thresholds.break in stryker-config.json, applied to each lane's whole score.
# A long lane runs in shards, each mutating a share of the project's files. Stryker does not gate a
# run; the gate adds up a lane's shards and checks the lane, so a shard cannot pass or fail on its own.
# Reports go to tests/<project>/StrykerOutput/<run>/reports, which is gitignored.
#   pwsh -NoProfile -File scripts/mutate.ps1                     every run, one after another, then the gate
#   pwsh -NoProfile -File scripts/mutate.ps1 -Project Daemon     one lane, every shard of it, then the gate
#   pwsh -NoProfile -File scripts/mutate.ps1 -List               every run's name as JSON, for the CI matrix
#   pwsh -NoProfile -File scripts/mutate.ps1 -Run Core-2         one run and no gate, as a CI job runs it
#   pwsh -NoProfile -File scripts/mutate.ps1 -Gate <folder>      gate the run summaries under a folder
param(
    [string[]]$Project,
    [string]$Run,
    [string]$Gate,
    [switch]$List,
    # Test runners at once. 0 keeps Stryker's default, half the logical processors.
    [int]$Concurrency = 0
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

# One lane per project under test. Stryker mutates one project per run, so a test project that
# references several says which one it tests.
#   Tests     the test project's folder under tests/
#   Source    the project it mutates, as the test project references it
#   Shards    how many runs share the project's files, so each fits in a CI job (default 1)
#   Exclude   file name patterns left out of mutation; each one says why
#   Filter    a dotnet test filter for the tests the lane runs
#   CsWin32   the project generates its Win32 interop with CsWin32 (see Set-CsWin32Inputs)
$lanes = @(
    @{
        Name = 'Core'
        Tests = 'Winland.Core.Tests'
        Source = 'Winland.Core.csproj'
        Shards = 4
    }
    @{
        Name = 'Daemon'
        Tests = 'Winland.Daemon.Tests'
        Source = 'Winland.Daemon.csproj'
        Exclude = @(
            # Top-level process wiring: elevation, the instance lock, the window host and its loop.
            # The decisions it calls are in tested classes (ConfigPaths, StateLocation, ManualStartElevation).
            'Program.cs'
            # Starts the process again through UAC. RelaunchPlan, which builds the command, is tested.
            'Elevation.cs'
            # Runs schtasks.exe, which needs administrator rights. SchtasksArguments and
            # LogonTaskDefinition, which build what it passes, are tested.
            'SchtasksCatalog.cs'
        )
    }
    @{
        Name = 'Install'
        Tests = 'Winland.Install.Tests'
        Source = 'Winland.Install.csproj'
    }
    @{
        Name = 'Plugins'
        Tests = 'Winland.Plugins.Tests'
        Source = 'Winland.Plugins.csproj'
    }
    @{
        Name = 'Windows'
        Tests = 'Winland.Windows.Tests'
        Source = 'Winland.Windows.csproj'
        Shards = 2
        CsWin32 = $true
        # Each file left out drives Win32 or the shell directly and has no seam a unit test can
        # reach without a real desktop. The logic they call is in files that stay in.
        Exclude = @(
            # Real top-level windows: WinEvent hooks, SetWindowPos, cloaking, the message loop. Only
            # the Desktop tests reach them, and the lane cannot run those (see Filter).
            'WindowAdapter*.cs'
            'WindowHost.cs'
            'SessionWindow.cs'
            'SessionRecovery.cs'
            # The low-level keyboard hook callback. It needs real key events; the bind matching
            # and modifier tracking it feeds are in Winland.Core.
            'KeyboardHook.cs'
            # Raw input registration and HID report parsing. TouchpadSwipe holds the gesture logic.
            'TouchpadInput.cs'
            # The pointer-device query. It needs a live pen or touch screen; the mapping it feeds is in Winland.Core.
            'PointerDigitizer.cs'
            # The window effects' drawing: a Direct3D 11 device and its shaders, Windows.Graphics.Capture of a window, and the
            # layered window that shows the result. Only the Desktop tests reach them. The mesh, spring, blur and timing they
            # draw are in Winland.Core, and EffectTrack there is tested.
            'GpuDevice.cs'
            'Gpu.cs'
            'WindowCapture.cs'
            'EffectLayer.cs'
            'EffectRenderer.cs'
            'EffectStand.cs'
            'EffectPump.cs'
            # Group bar windows: a layered window and its GDI painting.
            'GroupBarHost.cs'
            'GroupBarPainter.cs'
            # Monitors, the taskbar and other shell windows, the foreground lock, the keyboard layout,
            # idle and last-input queries, and the console close handler.
            'MonitorAdapter.cs'
            'Taskbar.cs'
            'ShellBars.cs'
            'Foreground.cs'
            'KeyboardLayout.cs'
            'IdleKeeper.cs'
            'InputClock.cs'
            'ExitWatch.cs'
            # Store app frames, resolved through ApplicationFrameHost child windows and COM.
            'PackagedIdentity.cs'
            # Routes outcomes to process-wide singletons that only InstallSystem creates, and
            # creating them changes the machine's input settings.
            'SessionEffects.cs'
        )
        # The tests that open real windows fight over the foreground when Stryker runs several test
        # processes at once, and they skip wherever a Winland daemon runs. The lane leaves them out,
        # so it scores the same on every machine.
        Filter = 'Category!=Desktop'
    }
)

function Get-Runs([object[]]$From) {
    foreach ($lane in $From) {
        $count = if ($lane.Shards) { [int]$lane.Shards } else { 1 }
        if ($count -le 1) {
            [pscustomobject]@{ Name = $lane.Name; Lane = $lane.Name; Shard = 0; Of = 1 }
            continue
        }
        foreach ($index in 1..$count) {
            [pscustomobject]@{ Name = "$($lane.Name)-$index"; Lane = $lane.Name; Shard = $index; Of = $count }
        }
    }
}

# Stryker's score: killed and timed-out mutants over every mutant that compiled and was not ignored.
function Get-Counts([string]$Report) {
    $counts = [ordered]@{ Killed = 0; Timeout = 0; Survived = 0; NoCoverage = 0 }
    $files = (Get-Content -Raw -LiteralPath $Report | ConvertFrom-Json).files
    foreach ($file in $files.PSObject.Properties) {
        foreach ($mutant in $file.Value.mutants) {
            if ($counts.Contains($mutant.status)) { $counts[$mutant.status]++ }
        }
    }
    return $counts
}

# Adds up each lane's run summaries and checks the lane's score against the break threshold.
# Prints a line per lane and returns whether every lane passed.
function Test-Gate([string[]]$SummaryFiles, [double]$Break) {
    $summaries = @($SummaryFiles | ForEach-Object { Get-Content -Raw -LiteralPath $_ | ConvertFrom-Json })
    if ($summaries.Count -eq 0) { throw 'No mutation-summary.json to gate.' }

    $passed = $true
    foreach ($group in $summaries | Group-Object Lane | Sort-Object Name) {
        $runs = @($group.Group)
        $expected = [int]$runs[0].Of
        $shards = @($runs | ForEach-Object { [int]$_.Shard } | Sort-Object -Unique)
        if ($shards.Count -ne $runs.Count -or $runs.Count -ne $expected) {
            Write-Host "Lane $($group.Name): $($runs.Count) summaries for $expected runs."
            $passed = $false
            continue
        }
        $detected = 0
        $valid = 0
        foreach ($summary in $runs) {
            $detected += $summary.Killed + $summary.Timeout
            $valid += $summary.Killed + $summary.Timeout + $summary.Survived + $summary.NoCoverage
        }
        $score = if ($valid -gt 0) { 100.0 * $detected / $valid } else { 100.0 }
        $verdict = 'pass'
        if ($score -lt $Break) {
            $verdict = 'FAIL'
            $passed = $false
        }
        Write-Host ('Lane {0}: {1:N2} % ({2} of {3} mutants detected), break at {4}: {5}' -f $group.Name, $score, $detected, $valid, $Break, $verdict)
    }
    return $passed
}

$config = Join-Path $root 'stryker-config.json'
if (-not (Test-Path $config)) { throw "Missing $config" }
$base = Get-Content -Raw -LiteralPath $config | ConvertFrom-Json -AsHashtable
$break = [double]$base['stryker-config']['thresholds']['break']

if ($Gate) {
    $found = @(Get-ChildItem -Path $Gate -Filter 'mutation-summary.json' -Recurse -File | ForEach-Object { $_.FullName })
    if (Test-Gate $found $break) { exit 0 }
    exit 1
}

$tests = Join-Path $root 'tests'
$projects = @()
if (Test-Path $tests) {
    $projects = @(Get-ChildItem -Path $tests -Filter *.csproj -Recurse -File | Where-Object {
        $_.Name -like '*.Tests.csproj' -and $_.Name -notlike '*.Smoke.Tests.csproj'
    })
}
if ($projects.Count -eq 0) {
    if ($List) {
        Write-Output '[]'
        exit 0
    }
    Write-Output 'No unit test project under tests/. Stryker runs when one is added.'
    exit 0
}

# A test project without a lane would never be mutation-tested, and a lane without its project
# would test nothing. Both fail every run, whichever lanes it selects.
foreach ($found in $projects) {
    if (-not ($lanes | Where-Object { $_.Tests -eq $found.BaseName })) {
        throw "$($found.FullName) has no lane in scripts/mutate.ps1. Add one."
    }
}
foreach ($lane in $lanes) {
    if (-not ($projects | Where-Object { $_.BaseName -eq $lane.Tests })) {
        throw "Lane $($lane.Name) names tests/$($lane.Tests), which has no $($lane.Tests).csproj."
    }
}

if ($List) {
    ConvertTo-Json -Compress -InputObject @(Get-Runs $lanes | ForEach-Object { $_.Name })
    exit 0
}

$selected = @($lanes)
if ($Project) {
    # pwsh -File passes "Core,Daemon" as one string.
    $names = @($Project | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $selected = @(foreach ($name in $names) {
        $match = $lanes | Where-Object { $_.Name -eq $name }
        if (-not $match) {
            throw "No lane named '$name'. Lanes: $(($lanes | ForEach-Object { $_.Name }) -join ', ')."
        }
        $match
    })
}
$runs = @(Get-Runs $selected)
if ($Run) {
    $runs = @($runs | Where-Object { $_.Name -eq $Run })
    if ($runs.Count -eq 0) {
        throw "No run named '$Run'. Runs: $((Get-Runs $selected | ForEach-Object { $_.Name }) -join ', ')."
    }
}

# Stryker compiles each mutated version itself and runs the project's source generators, but it
# hands them only the properties MSBuild sets when it evaluates the project. CsWin32 learns where the
# Win32 metadata is from properties a target sets during the build, so under Stryker it would generate
# nothing and the project would not compile. MSBuild reports what that target sets, and the project
# file reads the values back from the environment (see Winland.Windows.csproj).
function Set-CsWin32Inputs([string]$SourceProject) {
    $names = @('CsWin32InputMetadataPaths', 'CsWin32InputDocPaths')
    $query = @('msbuild', $SourceProject, '-restore', '-nologo', '-t:AssembleCsWin32InputPaths') + @($names | ForEach-Object { "-getProperty:$_" })
    $json = (& dotnet @query) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "dotnet msbuild could not read the CsWin32 inputs of ${SourceProject}:`n$json" }
    $properties = ($json | ConvertFrom-Json).Properties
    foreach ($name in $names) {
        $value = $properties.$name
        if (-not $value) { throw "MSBuild set no $name for $SourceProject." }
        Set-Item "Env:$name" $value
    }
    return $names
}

# The files a shard mutates. Files are dealt largest first to the shard with the fewest bytes so far,
# so the shards come out about the same size, and the same tree always deals the same way.
function Get-ShardFiles([string]$ProjectDir, [string[]]$Exclude, [int]$Index, [int]$Count) {
    $files = @(Get-ChildItem -Path $ProjectDir -Filter *.cs -Recurse -File | Where-Object {
        $relative = [System.IO.Path]::GetRelativePath($ProjectDir, $_.FullName)
        $first = ($relative -split '[\\/]')[0]
        $name = $_.Name
        $first -notin @('bin', 'obj') -and -not ($Exclude | Where-Object { $name -like $_ })
    } | Sort-Object @{ Expression = { $_.Length }; Descending = $true }, @{ Expression = { $_.FullName } })
    $sizes = New-Object long[] $Count
    $mine = New-Object System.Collections.Generic.List[string]
    foreach ($file in $files) {
        $target = 0
        for ($i = 1; $i -lt $Count; $i++) {
            if ($sizes[$i] -lt $sizes[$target]) { $target = $i }
        }
        $sizes[$target] += $file.Length
        if ($target -eq $Index - 1) {
            $relative = [System.IO.Path]::GetRelativePath($ProjectDir, $file.FullName).Replace('\', '/')
            $mine.Add($relative)
        }
    }
    return $mine
}

$summaries = New-Object System.Collections.Generic.List[string]
foreach ($entry in $runs) {
    $lane = $lanes | Where-Object { $_.Name -eq $entry.Lane }
    $testDir = Join-Path $tests $lane.Tests
    $source = Get-ChildItem -Path (Join-Path $root 'src') -Filter $lane.Source -Recurse -File | Select-Object -First 1
    if (-not $source) { throw "Lane $($lane.Name) mutates $($lane.Source), which is not under src/." }

    # One config per run: the shared stryker-config.json plus what this lane and shard set.
    $settings = $base.Clone()
    $part = $base['stryker-config'].Clone()
    $settings['stryker-config'] = $part
    $part['project'] = $lane.Source
    $exclude = @($lane.Exclude | Where-Object { $_ })
    if ($entry.Of -gt 1) {
        $part['mutate'] = @(Get-ShardFiles $source.DirectoryName $exclude $entry.Shard $entry.Of | ForEach-Object { "**/$_" })
    } elseif ($exclude.Count -gt 0) {
        $part['mutate'] = @($exclude | ForEach-Object { "!**/$_" })
    }
    if ($lane.Filter) { $part['test-case-filter'] = $lane.Filter }
    $runConfig = Join-Path ([System.IO.Path]::GetTempPath()) "stryker-$($entry.Name)-$([guid]::NewGuid().ToString('n')).json"
    $settings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $runConfig -Encoding utf8

    $output = Join-Path $testDir "StrykerOutput/$($entry.Name)"
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
    # The gate decides, from every run of the lane. --break-at 0 stops Stryker failing one run alone.
    $arguments = @('stryker', '--config-file', $runConfig, '--output', $output, '--break-at', '0')
    if ($Concurrency -gt 0) { $arguments += @('--concurrency', $Concurrency) }
    $exported = @()
    Write-Output "Mutation testing $($lane.Source) with $($lane.Tests) ($($entry.Name))"
    Push-Location $testDir
    try {
        if ($lane.CsWin32) { $exported = Set-CsWin32Inputs $source.FullName }
        dotnet @arguments
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    finally {
        Pop-Location
        foreach ($name in $exported) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
        Remove-Item -LiteralPath $runConfig -ErrorAction SilentlyContinue
    }

    $report = Join-Path $output 'reports/mutation-report.json'
    if (-not (Test-Path -LiteralPath $report)) { throw "Stryker wrote no $report." }
    $summary = [ordered]@{ Lane = $entry.Lane; Shard = $entry.Shard; Of = $entry.Of }
    $counts = Get-Counts $report
    foreach ($key in $counts.Keys) { $summary[$key] = $counts[$key] }
    $summaryFile = Join-Path $output 'mutation-summary.json'
    $summary | ConvertTo-Json | Set-Content -LiteralPath $summaryFile -Encoding utf8
    $summaries.Add($summaryFile)
}

# A CI job runs one run and stops here; the gate job adds the runs up. Otherwise every run of each
# selected lane is done, and the gate checks those lanes now.
if ($Run) { exit 0 }
if (Test-Gate $summaries $break) { exit 0 }
exit 1
