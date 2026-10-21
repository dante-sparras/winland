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

function Assert-Null($Actual) {
    if ($null -ne $Actual) { throw "expected null, got '$Actual'" }
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

function Get-Plan([bool]$Installed, [bool]$Touchpad, [bool]$Interactive, [string]$Admin, [string]$Settings) {
    Get-InstallPlan -Installed $Installed -PrecisionTouchpad $Touchpad -Interactive $Interactive -AdministratorAnswer $Admin -SettingsAnswer $Settings
}

Test-Case 'a new install asks two questions' {
    $lines = @(Get-InstallQuestions $false $true)
    $questions = @($lines | Where-Object { $_.EndsWith('?') })
    Assert-Equal $questions.Count 2
    Assert-Equal $questions[0] 'Run Winland as administrator?'
    Assert-Equal $questions[1] 'Turn off Snap windows and the three- and four-finger touchpad swipes?'
    Assert-True ($lines -contains 'It can then arrange administrator windows such as Task Manager.')
    Assert-True ($lines -contains 'Snap grabs windows dragged to an edge.')
    Assert-True ($lines -contains 'Windows reads the same swipe Winland uses to switch Workspace.')
}

Test-Case 'the touchpad sentence is omitted when there is no precision touchpad' {
    $lines = @(Get-InstallQuestions $false $false)
    $questions = @($lines | Where-Object { $_.EndsWith('?') })
    Assert-Equal $questions.Count 2
    Assert-Equal $questions[1] 'Turn off Snap windows?'
    Assert-False ($lines -contains 'Windows reads the same swipe Winland uses to switch Workspace.')
    Assert-True ($lines -contains 'Snap grabs windows dragged to an edge.')
}

Test-Case 'an installed copy asks nothing' {
    $lines = @(Get-InstallQuestions $true $true)
    Assert-Equal $lines.Count 0
}

Test-Case 'Enter installs elevated and turns every named setting off' {
    $plan = Get-Plan $false $true $true '' ''
    Assert-Equal $plan.Action 'install'
    Assert-False $plan.User
    Assert-True $plan.TurnOff
    Assert-True $plan.Touchpad
    Assert-True $plan.Asked
    Assert-Equal $plan.SettingsQuestion 'Turn off Snap windows and the three- and four-finger touchpad swipes?'
}

Test-Case 'no to administrator is a per-user install' {
    $plan = Get-Plan $false $true $true 'no' 'yes'
    Assert-True $plan.User
    Assert-True $plan.TurnOff
    Assert-True $plan.Touchpad
}

Test-Case 'n to settings changes nothing and does not ask again' {
    $plan = Get-Plan $false $true $true '' 'N'
    Assert-False $plan.User
    Assert-False $plan.TurnOff
    Assert-False $plan.Touchpad
    Assert-True $plan.Asked
}

Test-Case 'one settings answer covers snap and both swipes' {
    $plan = Get-Plan $false $true $true 'y' ''
    Assert-True $plan.TurnOff
    Assert-True $plan.Touchpad
}

Test-Case 'without a precision touchpad the same yes leaves the swipes alone' {
    $plan = Get-Plan $false $false $true '' ''
    Assert-True $plan.TurnOff
    Assert-False $plan.Touchpad
    Assert-Equal $plan.SettingsQuestion 'Turn off Snap windows?'
}

Test-Case 'a machine that is already installed is only updated' {
    $plan = Get-Plan $true $true $true 'no' 'no'
    Assert-Equal $plan.Action 'update'
    Assert-False $plan.Asked
    Assert-False $plan.TurnOff
    Assert-False $plan.Touchpad
    Assert-Null $plan.SettingsQuestion
}

Test-Case 'a prompt that is not interactive takes the defaults' {
    $plan = Get-Plan $false $true $false 'no' 'no'
    Assert-False $plan.User
    Assert-True $plan.TurnOff
    Assert-True $plan.Touchpad
}

Test-Case 'defaults on a PC without a precision touchpad leave the swipes alone' {
    $plan = Get-Plan $false $false $false '' ''
    Assert-True $plan.TurnOff
    Assert-False $plan.Touchpad
}

Test-Case 'only n and no mean no' {
    Assert-True (Test-Yes '')
    Assert-True (Test-Yes 'y')
    Assert-True (Test-Yes 'yes')
    Assert-False (Test-Yes 'n')
    Assert-False (Test-Yes 'No')
    Assert-False (Test-Yes '  no  ')
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

Test-Case 'the release names the zip and the checksums' {
    $downloads = Get-ReleaseDownloads $releaseJson
    Assert-Equal $downloads.Archive 'https://github.com/dante-sparras/winland/releases/download/v0.1.0/Winland-win-x64.zip'
    Assert-Equal $downloads.Sums 'https://github.com/dante-sparras/winland/releases/download/v0.1.0/SHA256SUMS'
}

Test-Case 'a draft release is refused' {
    Assert-Throws { Get-ReleaseDownloads ($releaseJson -replace '"draft": false', '"draft": true') }
}

Test-Case 'a release without the zip is refused' {
    Assert-Throws { Get-ReleaseDownloads ($releaseJson -replace 'Winland-win-x64.zip', 'Winland-other.zip') }
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
