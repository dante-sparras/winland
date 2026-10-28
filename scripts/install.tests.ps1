# Tests for the decisions in install.ps1. Nothing is downloaded and no Windows setting is changed.
#   pwsh -NoProfile -File scripts/install.tests.ps1
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'install.ps1')

$script:failures = New-Object System.Collections.Generic.List[string]
$script:passed = 0

function Test-Case([string]$Name, [scriptblock]$Body) {
    try {
        & $Body
        $script:passed++
    } catch {
        $script:failures.Add("$Name`: $($_.Exception.Message)")
    }
}

function Assert-Equal($Actual, $Expected) {
    if ($Actual -cne $Expected) { throw "expected '$Expected', got '$Actual'" }
}

function Assert-True($Actual) {
    if ($Actual -ne $true) { throw "expected true, got '$Actual'" }
}

function Assert-False($Actual) {
    if ($Actual -ne $false) { throw "expected false, got '$Actual'" }
}

function Assert-Throws([scriptblock]$Body) {
    $threw = $false
    try { & $Body } catch { $threw = $true }
    if (-not $threw) { throw 'expected an error, got none' }
}

Test-Case 'the banner is a box of plain ASCII that fits an 80-column window' {
    $lines = @(Get-Banner)
    Assert-Equal $lines.Count 9
    foreach ($line in $lines) {
        Assert-Equal $line.Length 46
        if ($line -match '[^\x20-\x7E]') { throw "banner line is not ASCII: $line" }
    }
}

Test-Case 'a yes to both installs with the logon start and starts Winland' {
    Assert-Equal ((Get-InstallArguments $true $true) -join ' ') 'install'
}

Test-Case 'a no leaves out the logon start or the start now' {
    Assert-Equal ((Get-InstallArguments $false $true) -join ' ') 'install --no-logon'
    Assert-Equal ((Get-InstallArguments $true $false) -join ' ') 'install --no-start'
    Assert-Equal ((Get-InstallArguments $false $false) -join ' ') 'install --no-logon --no-start'
}

Test-Case 'the settings are always turned off, and the swipes only with a precision touchpad' {
    Assert-Equal ((Get-TweakArguments $true) -join ' ') 'tweaks --off --touchpad'
    Assert-Equal ((Get-TweakArguments $false) -join ' ') 'tweaks --off'
}

Test-Case 'the dev channel reads the dev release and saves the channel' {
    Assert-Equal (Get-ReleaseUrl 'stable') 'https://api.github.com/repos/dante-sparras/winland/releases/latest'
    Assert-Equal (Get-ReleaseUrl 'dev') 'https://api.github.com/repos/dante-sparras/winland/releases/tags/dev'
    Assert-Equal ((Get-UpdateArguments 'stable') -join ' ') 'update --channel stable'
    Assert-Equal ((Get-UpdateArguments 'dev') -join ' ') 'update --channel dev'
}

Test-Case 'only n and no mean no' {
    Assert-True (Test-Yes '')
    Assert-True (Test-Yes 'y')
    Assert-True (Test-Yes 'yes')
    Assert-False (Test-Yes 'n')
    Assert-False (Test-Yes 'No')
    Assert-False (Test-Yes '  no  ')
}

Test-Case 'a declined UAC prompt has its own message' {
    Assert-Equal (Get-FailureMessage @('install') 1223 @('Elevation was cancelled.')) 'Administrator approval was declined. Nothing was installed. Run the command again to retry.'
    Assert-Equal (Get-FailureMessage @('update') 1223 @()) 'Administrator approval was declined. Winland was not updated. Run the command again to retry.'
}

Test-Case 'another failure shows the last line winland printed' {
    Assert-Equal (Get-FailureMessage @('install') 1 @('first', 'Winland did not exit.', '')) 'Winland did not exit.'
    Assert-Equal (Get-FailureMessage @('install', '--no-start') 3 @()) 'winland install --no-start stopped with exit code 3.'
}

Test-Case 'the summary says Winland is running only when it was started' {
    $folder = 'C:\Program Files\Winland'
    $config = 'C:\Users\me\.winland\winland.conf'
    $running = @(Get-SummaryLines '0.2.1' $folder $config $true)
    $stopped = @(Get-SummaryLines '0.2.1' $folder $config $false)
    Assert-Equal $running.Count 6
    Assert-Equal $running[0] ' Winland 0.2.1 is installed and running.'
    Assert-Equal $running[1] '    Folder     C:\Program Files\Winland'
    Assert-Equal $running[2] '    Config     C:\Users\me\.winland\winland.conf'
    Assert-Equal $stopped.Count 7
    Assert-Equal $stopped[0] ' Winland 0.2.1 is installed.'
    Assert-Equal $stopped[3] '    Start      Winland in the Start menu, or run winland'
}

Test-Case 'the summary says how to quit, where the keys are and how to undo' {
    $lines = @(Get-SummaryLines '0.2.1' 'C:\Program Files\Winland' 'C:\Users\me\.winland\winland.conf' $true)
    Assert-Equal $lines[3] '    Quit       right-click the Winland icon in the notification area (behind ^ if hidden), then Quit'
    Assert-Equal $lines[4] '    Keys       https://github.com/dante-sparras/winland#hotkeys, or the config'
    Assert-Equal $lines[5] '    Undo       winland uninstall, which also turns back on the Snap and swipes the install turned off'
}

$hash = 'ab' * 32
$other = 'cd' * 32

Test-Case 'the zip hash matches its line in SHA256SUMS' {
    $sums = "$hash  Winland-win-x64.zip`n$other  version.json`n"
    Assert-True (Test-ReleaseHash $hash.ToUpperInvariant() $sums)
}

Test-Case 'a binary-mode marker and a byte order mark still match' {
    $sums = [char]0xFEFF + "$hash  *Winland-win-x64.zip`r`n"
    Assert-True (Test-ReleaseHash $hash $sums)
}

Test-Case 'a different hash stops the install' {
    $sums = "$other  Winland-win-x64.zip`n"
    Assert-False (Test-ReleaseHash $hash $sums)
}

Test-Case 'a checksum file that does not name the zip stops the install' {
    $sums = "$hash  version.json`n"
    Assert-False (Test-ReleaseHash $hash $sums)
}

$releaseJson = @'
{
  "draft": false,
  "prerelease": false,
  "assets": [
    {"name": "version.json", "browser_download_url": "https://github.com/dante-sparras/winland/releases/download/v0.1.0/version.json"},
    {"name": "Winland-win-x64.zip", "browser_download_url": "https://github.com/dante-sparras/winland/releases/download/v0.1.0/Winland-win-x64.zip"},
    {"name": "SHA256SUMS", "browser_download_url": "https://github.com/dante-sparras/winland/releases/download/v0.1.0/SHA256SUMS"}
  ]
}
'@

Test-Case 'the release names the zip, the checksums and the version' {
    $downloads = Get-ReleaseDownloads $releaseJson
    Assert-Equal $downloads.Archive 'https://github.com/dante-sparras/winland/releases/download/v0.1.0/Winland-win-x64.zip'
    Assert-Equal $downloads.Sums 'https://github.com/dante-sparras/winland/releases/download/v0.1.0/SHA256SUMS'
    Assert-Equal $downloads.Version 'https://github.com/dante-sparras/winland/releases/download/v0.1.0/version.json'
}

Test-Case 'a draft release is refused' {
    Assert-Throws { Get-ReleaseDownloads ($releaseJson -replace '"draft": false', '"draft": true') }
    Assert-Throws { Get-ReleaseDownloads ($releaseJson -replace '"draft": false', '"draft": true') $true }
}

Test-Case 'a prerelease is refused on stable and read on dev' {
    $dev = $releaseJson -replace '"prerelease": false', '"prerelease": true'
    Assert-Throws { Get-ReleaseDownloads $dev }
    Assert-Equal (Get-ReleaseDownloads $dev $true).Archive 'https://github.com/dante-sparras/winland/releases/download/v0.1.0/Winland-win-x64.zip'
}

Test-Case 'a release without the zip or the version is refused' {
    Assert-Throws { Get-ReleaseDownloads ($releaseJson -replace 'Winland-win-x64.zip', 'Winland-other.zip') }
    Assert-Throws { Get-ReleaseDownloads ($releaseJson -replace '"version.json"', '"other.json"') }
}

Test-Case 'the installed folder is added to the PATH of this terminal once' {
    $separator = [string][System.IO.Path]::PathSeparator
    $before = 'C:\Windows' + $separator + 'C:\Tools'
    Assert-Equal (Get-SessionPath $before 'C:\Program Files\Winland') ($before + $separator + 'C:\Program Files\Winland')
    $present = $before + $separator + 'c:\program files\winland\'
    Assert-Equal (Get-SessionPath $present 'C:\Program Files\Winland') $present
    Assert-Equal (Get-SessionPath '' 'C:\Program Files\Winland') 'C:\Program Files\Winland'
}

Test-Case 'a download that is not https is refused' {
    Assert-Throws { Get-ReleaseDownloads ($releaseJson -replace 'https://github.com', 'http://github.com') }
}

Test-Case 'the script text has no semicolon and no em dash' {
    $text = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'install.ps1') -Raw
    if ($text.Contains([char]0x2014)) { throw 'em dash in install.ps1' }
    if ($text.Contains(';')) { throw 'semicolon in install.ps1' }
}

Test-Case 'Windows PowerShell 5.1 can parse the script' {
    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if (-not (Test-Path -LiteralPath $powershell)) { throw 'Windows PowerShell is not installed' }
    $script = (Join-Path $PSScriptRoot 'install.ps1').Replace("'", "''")
    $command = @"
`$errors = `$null
`$tokens = `$null
[void][System.Management.Automation.Language.Parser]::ParseFile('$script', [ref]`$tokens, [ref]`$errors)
if (`$errors -and `$errors.Count -gt 0) { `$errors | ForEach-Object { Write-Output `$_.ToString() }; exit 1 }
"@
    & $powershell -NoProfile -Command $command
    if ($LASTEXITCODE -ne 0) { throw "Windows PowerShell could not parse install.ps1 (exit $LASTEXITCODE)" }
}

if ($script:failures.Count -gt 0) {
    foreach ($failure in $script:failures) { Write-Output "FAIL $failure" }
    Write-Output "$($script:failures.Count) failed, $($script:passed) passed"
    exit 1
}
Write-Output "$($script:passed) install tests passed"
