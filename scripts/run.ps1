# Builds this checkout and runs it as the Winland of this computer, in place of the one that is running.
#   pwsh -NoProfile -File scripts/run.ps1                 build, stop the running winland, start the new build
#   pwsh -NoProfile -File scripts/run.ps1 -Elevated       start it with administrator rights, which asks for one UAC approval
#   pwsh -NoProfile -File scripts/run.ps1 -Stop           stop the running winland and start nothing
#   pwsh -NoProfile -File scripts/run.ps1 -Config <file>  read this .conf file instead of the one in %APPDATA%\winland
#
# Without -Elevated the build starts as the normal user and no prompt appears, so it can place no window of an
# elevated program. A build is published to a folder of its own first, so a build that fails leaves the running
# winland alone and a running one never locks the files of the next. The running winland is asked to exit
# through its pipe, which puts every window back, and is stopped by force only when it does not.
# WINLAND_PIPE and WINLAND_STATE are passed on to it, so a second pipe name runs a second copy.
param(
    [string]$Configuration = 'Release',
    [switch]$Elevated,
    [switch]$Stop,
    [string]$Config
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dev = Join-Path $env:LOCALAPPDATA 'winland-dev'
$keep = 3

# The apphost finds the runtime through DOTNET_ROOT, and the dotnet on PATH is not always the one with it.
function Resolve-DotnetRoot {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $beside = Split-Path -Parent $dotnet
    if (Test-Path (Join-Path $beside 'shared/Microsoft.NETCore.App')) { return $beside }
    foreach ($line in (& $dotnet --list-runtimes)) {
        if ($line -match '^Microsoft\.NETCore\.App \S+ \[(.+)\]$') {
            $shared = Split-Path -Parent $Matches[1]
            return Split-Path -Parent $shared
        }
    }
    throw 'Could not find a .NET install to set DOTNET_ROOT.'
}

# Every winland on this computer is the one to replace, whatever build it is. With WINLAND_PIPE set this is a
# second copy on a pipe of its own, so only the builds this script started are touched.
function Get-Winland {
    $found = @(Get-CimInstance Win32_Process -Filter "Name = 'winland.exe'" -ErrorAction SilentlyContinue)
    if ($env:WINLAND_PIPE) {
        $found = @($found | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($dev, [StringComparison]::OrdinalIgnoreCase) })
    }
    @($found | ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue } | Where-Object { $_ })
}

function Stop-Winland([string]$ctl) {
    $running = Get-Winland
    if ($running.Count -eq 0) { return }

    Write-Output "Stopping winland ($(($running | ForEach-Object { $_.Id }) -join ', '))."
    if ($ctl -and (Test-Path -LiteralPath $ctl)) {
        # The pipe takes the current user, elevated or not, so this reaches an elevated winland too.
        & $ctl dispatch exit *> $null
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while ((Get-Winland).Count -gt 0 -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }

    $left = Get-Winland
    if ($left.Count -eq 0) { return }

    Write-Warning 'winland did not exit when asked. Stopping it.'
    foreach ($process in $left) {
        try { Stop-Process -Id $process.Id -Force -ErrorAction Stop }
        catch { throw "Could not stop winland (process $($process.Id)): $($_.Exception.Message) Close it by hand, or run this from an elevated shell." }
    }
    Start-Sleep -Milliseconds 500
}

$env:DOTNET_ROOT = Resolve-DotnetRoot

if ($Stop) {
    $ctl = Get-ChildItem -Path $dev -Filter winlandctl.exe -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Stop-Winland ($ctl.FullName)
    Write-Output 'winland is stopped.'
    return
}

$stamp = [DateTime]::Now.ToString('yyyyMMdd-HHmmss')
$build = Join-Path $dev $stamp
New-Item -ItemType Directory -Force -Path $build | Out-Null

foreach ($project in @(@('Winland.Daemon', 'winland'), @('winlandctl', 'winlandctl'))) {
    $out = Join-Path $build $project[1]
    Write-Output "Publishing $($project[0])."
    $log = dotnet publish (Join-Path $root "src/$($project[0])/$($project[0]).csproj") -c $Configuration -o $out --nologo -v q 2>&1
    if ($LASTEXITCODE -ne 0) {
        $log | Select-Object -Last 40 | ForEach-Object { Write-Output $_ }
        Remove-Item -LiteralPath $build -Recurse -Force -ErrorAction SilentlyContinue
        throw "dotnet publish of $($project[0]) failed. The running winland was left alone."
    }
}

$exe = Join-Path $build 'winland/winland.exe'
$ctl = Join-Path $build 'winlandctl/winlandctl.exe'
Stop-Winland $ctl

$arguments = @()
if (-not $Elevated) { $arguments += '--no-elevate' }
if ($Config) {
    if (-not $Config.EndsWith('.conf', [StringComparison]::OrdinalIgnoreCase)) {
        throw "--config takes a .conf file, not '$Config'."
    }

    $arguments += @('--config', (Resolve-Path -LiteralPath $Config).Path)
}

$stdout = Join-Path $build 'winland.out.log'
$stderr = Join-Path $build 'winland.err.log'

# The shell starts it, not this process, so it inherits none of this process's handles. One that did would hold
# open the pipe a caller reads this script's output from, and the caller would wait for winland to exit.
# A start without elevation goes through cmd to put the output in files. An elevated one relaunches itself,
# which cannot be redirected.
if ($Elevated) {
    Write-Output 'Approve the UAC prompt to start winland.'
    $start = @{ FilePath = $exe; WorkingDirectory = $build; WindowStyle = 'Hidden'; PassThru = $true }
    if ($arguments.Count -gt 0) { $start.ArgumentList = $arguments }
}
else {
    $quoted = ($arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $command = '/c ""{0}" {1} > "{2}" 2> "{3}""' -f $exe, $quoted, $stdout, $stderr
    $start = @{ FilePath = 'cmd.exe'; ArgumentList = $command; WorkingDirectory = $build; WindowStyle = 'Hidden'; PassThru = $true }
}

$process = Start-Process @start

# Ready when the pipe answers. An elevated start waits for the approval first.
$deadline = [DateTime]::UtcNow.AddSeconds($(if ($Elevated) { 90 } else { 30 }))
$ready = $false
while ([DateTime]::UtcNow -lt $deadline) {
    if ($process.HasExited -and -not $Elevated) { break }
    & $ctl clients *> $null
    if ($LASTEXITCODE -eq 0) { $ready = $true; break }
    Start-Sleep -Milliseconds 250
}

if (-not $ready) {
    foreach ($file in @($stdout, $stderr)) {
        if (Test-Path -LiteralPath $file) { Get-Content -LiteralPath $file -Tail 30 | ForEach-Object { Write-Output $_ } }
    }
    throw 'The new winland did not come up.'
}

# Older builds are left for a later run, since a folder in use cannot be removed.
Get-ChildItem -Path $dev -Directory | Where-Object { $_.Name -ne $stamp } | Sort-Object Name -Descending |
    Select-Object -Skip ($keep - 1) | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }

Write-Output "winland is running from $exe"
if (-not $Elevated) { Write-Output "Logs: $stdout and $stderr" }
Write-Output "Stop it with: pwsh -NoProfile -File scripts/run.ps1 -Stop"
