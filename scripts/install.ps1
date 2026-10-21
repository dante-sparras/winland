# One-line install for Windows PowerShell 5.1 and PowerShell 7.
#   irm https://raw.githubusercontent.com/dante-sparras/winland/main/scripts/install.ps1 | iex
# Dot-sourcing defines the decisions and runs nothing. scripts/install.tests.ps1 does that.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:ArchiveName = 'Winland-win-x64.zip'
$script:ChecksumsName = 'SHA256SUMS'
$script:LatestReleaseUrl = 'https://api.github.com/repos/dante-sparras/winland/releases/latest'

function Get-AdministratorLines {
    return @(
        'Run Winland as administrator?'
        'It can then arrange administrator windows such as Task Manager.'
    )
}

function Get-SettingsLines([bool]$PrecisionTouchpad) {
    $lines = New-Object System.Collections.Generic.List[string]
    if ($PrecisionTouchpad) {
        $lines.Add('Turn off Snap windows and the three- and four-finger touchpad swipes?')
    } else {
        $lines.Add('Turn off Snap windows?')
    }
    $lines.Add('Snap grabs windows dragged to an edge.')
    if ($PrecisionTouchpad) {
        $lines.Add('Windows reads the same swipe Winland uses to switch Workspace.')
    }
    return @($lines.ToArray())
}

function Get-DefaultLine {
    'No prompt is available. Winland will run as administrator, and the conflicting settings will be turned off.'
}

function Get-InstallQuestions([bool]$Installed, [bool]$PrecisionTouchpad) {
    if ($Installed) { return @() }
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($line in (Get-AdministratorLines)) { [void]$lines.Add($line) }
    foreach ($line in (Get-SettingsLines $PrecisionTouchpad)) { [void]$lines.Add($line) }
    return @($lines.ToArray())
}

function Test-Yes([string]$Answer) {
    if ([string]::IsNullOrWhiteSpace($Answer)) { return $true }
    $text = $Answer.Trim()
    if ($text -eq 'n' -or $text -eq 'no') { return $false }
    return $true
}

function Get-InstallPlan {
    param(
        [bool]$Installed,
        [bool]$PrecisionTouchpad,
        [bool]$Interactive,
        [string]$AdministratorAnswer,
        [string]$SettingsAnswer
    )
    if ($Installed) {
        return [pscustomobject]@{
            Action = 'update'
            User = $false
            TurnOff = $false
            Touchpad = $false
            Asked = $false
            SettingsQuestion = $null
        }
    }

    $adminYes = $true
    $settingsYes = $true
    if ($Interactive) {
        $adminYes = Test-Yes $AdministratorAnswer
        $settingsYes = Test-Yes $SettingsAnswer
    }
    $lines = @(Get-SettingsLines $PrecisionTouchpad)
    $question = $null
    if ($lines.Count -gt 0) { $question = [string]$lines[0] }
    $touchpad = $false
    if ($settingsYes -and $PrecisionTouchpad) { $touchpad = $true }
    return [pscustomobject]@{
        Action = 'install'
        User = -not $adminYes
        TurnOff = [bool]$settingsYes
        Touchpad = [bool]$touchpad
        Asked = $true
        SettingsQuestion = $question
    }
}

function Get-SumsHash([string]$Sums, [string]$Name) {
    if ([string]::IsNullOrEmpty($Sums)) { return $null }
    $text = $Sums.TrimStart([char]0xFEFF)
    foreach ($raw in ($text -split '\r?\n')) {
        $line = $raw.Trim()
        if ($line.Length -lt 64) { continue }
        $hash = $line.Substring(0, 64)
        if ($hash -notmatch '^[0-9A-Fa-f]{64}$') { continue }
        $rest = $line.Substring(64).TrimStart()
        if ($rest.StartsWith('*')) { $rest = $rest.Substring(1) }
        if ($rest -ceq $Name) { return $hash }
    }
    return $null
}

function Test-ReleaseHash([string]$ActualHex, [string]$Sums) {
    $expected = Get-SumsHash $Sums $script:ArchiveName
    if ([string]::IsNullOrEmpty($expected)) { return $false }
    return ($ActualHex -eq $expected)
}

function Get-PropertyValue($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-ReleaseDownloads([string]$Json) {
    if ([string]::IsNullOrWhiteSpace($Json)) {
        throw 'The published release could not be read. Nothing was installed. Run the command again.'
    }
    $release = ConvertFrom-Json $Json
    $draft = Get-PropertyValue $release 'draft'
    $prerelease = Get-PropertyValue $release 'prerelease'
    if ($draft -eq $true -or $prerelease -eq $true) {
        throw 'The latest release is not a published release. Nothing was installed. Wait for a published release and run the command again.'
    }
    $assets = @(Get-PropertyValue $release 'assets')
    $archive = $null
    $sums = $null
    foreach ($asset in $assets) {
        if ($null -eq $asset) { continue }
        $name = [string](Get-PropertyValue $asset 'name')
        $url = [string](Get-PropertyValue $asset 'browser_download_url')
        if ($name -ceq $script:ArchiveName) { $archive = $url }
        elseif ($name -ceq $script:ChecksumsName) { $sums = $url }
    }
    if ([string]::IsNullOrEmpty($archive)) {
        throw 'The published release has no Winland-win-x64.zip. Nothing was installed. Run the command again when the release has that file.'
    }
    if ([string]::IsNullOrEmpty($sums)) {
        throw 'The published release has no SHA256SUMS. Nothing was installed. Run the command again when the release has that file.'
    }
    if ($archive -notmatch '^https://') {
        throw 'The published release has an unsupported download for Winland-win-x64.zip. Nothing was installed.'
    }
    if ($sums -notmatch '^https://') {
        throw 'The published release has an unsupported download for SHA256SUMS. Nothing was installed.'
    }
    return [pscustomobject]@{
        Archive = $archive
        Sums = $sums
    }
}

function Test-PrecisionTouchpad {
    # Microsoft documents this value as the precision touchpad status a driver queries.
    # It exists when Windows has a precision touchpad, including when that touchpad is toggled off.
    # https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/touchpad-enable-or-disable-toggle-button
    $key = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\Status'
    $item = Get-ItemProperty -Path $key -Name 'Enabled' -ErrorAction SilentlyContinue
    return ($null -ne $item)
}

function Test-InstallPrompt {
    if (-not [Environment]::UserInteractive) { return $false }
    try {
        if ([Console]::IsInputRedirected) { return $false }
    } catch {
        return $false
    }
    return $true
}

function Read-Answer([string]$Prompt) {
    try {
        return Read-Host $Prompt
    } catch {
        return ''
    }
}

function Get-InstalledExecutable([bool]$User) {
    if ($User) {
        $programs = Join-Path $env:LOCALAPPDATA 'Programs'
        $folder = Join-Path $programs 'Winland'
        return (Join-Path $folder 'winland.exe')
    }
    $machine = Join-Path $env:ProgramFiles 'Winland'
    return (Join-Path $machine 'winland.exe')
}

function Find-InstalledWinland {
    $machine = Get-InstalledExecutable $false
    if (Test-Path -LiteralPath $machine) { return $machine }
    $user = Get-InstalledExecutable $true
    if (Test-Path -LiteralPath $user) { return $user }
    return $null
}

function Invoke-Winland([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        $shown = $Arguments -join ' '
        throw "winland $shown stopped with an error. Read the lines above and run the command again."
    }
}

function Save-Download([string]$Url, [string]$Path) {
    try {
        $headers = @{ 'User-Agent' = 'winland' }
        Invoke-WebRequest -Uri $Url -OutFile $Path -UseBasicParsing -Headers $headers
    } catch {
        throw "Could not download $Url. Check the network and run the command again."
    }
}

function Get-LatestReleaseJson {
    try {
        $headers = @{
            'User-Agent' = 'winland'
            'Accept' = 'application/vnd.github+json'
        }
        $response = Invoke-WebRequest -Uri $script:LatestReleaseUrl -UseBasicParsing -Headers $headers
        return [string]$response.Content
    } catch {
        throw 'Could not read the newest published release. Check the network and run the command again.'
    }
}

function Read-InstallAnswers([bool]$Interactive, [bool]$PrecisionTouchpad) {
    if (-not $Interactive) {
        Write-Output (Get-DefaultLine)
        return [pscustomobject]@{
            Administrator = ''
            Settings = ''
        }
    }
    foreach ($line in (Get-AdministratorLines)) { Write-Output $line }
    $admin = Read-Answer 'Y or n'
    foreach ($line in (Get-SettingsLines $PrecisionTouchpad)) { Write-Output $line }
    $settings = Read-Answer 'Y or n'
    return [pscustomobject]@{
        Administrator = $admin
        Settings = $settings
    }
}

function Invoke-InstallScript {
    $installed = Find-InstalledWinland
    if ($installed) {
        Write-Output 'Winland is already installed. Updating.'
        Invoke-Winland $installed @('update')
        return
    }

    $touchpad = Test-PrecisionTouchpad
    $interactive = Test-InstallPrompt
    $answers = Read-InstallAnswers $interactive $touchpad
    $plan = Get-InstallPlan -Installed $false -PrecisionTouchpad $touchpad -Interactive $interactive -AdministratorAnswer $answers.Administrator -SettingsAnswer $answers.Settings

    Write-Output 'Downloading the newest release.'
    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ('winland-install-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temp | Out-Null
    try {
        $json = Get-LatestReleaseJson
        $downloads = Get-ReleaseDownloads $json
        $zip = Join-Path $temp $script:ArchiveName
        $sumsPath = Join-Path $temp $script:ChecksumsName
        Save-Download $downloads.Archive $zip
        Save-Download $downloads.Sums $sumsPath
        $sums = Get-Content -LiteralPath $sumsPath -Raw
        $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
        if (-not (Test-ReleaseHash $actual $sums)) {
            throw 'The SHA-256 of Winland-win-x64.zip does not match SHA256SUMS. Nothing was installed. Run the command again.'
        }

        Write-Output 'Installing Winland.'
        $package = Join-Path $temp 'package'
        New-Item -ItemType Directory -Path $package | Out-Null
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $package)
        $staged = Join-Path (Join-Path $package 'winland') 'winland.exe'
        if (-not (Test-Path -LiteralPath $staged)) {
            throw 'The release zip has no winland\winland.exe. Nothing was installed. Run the command again when the release is complete.'
        }
        if ($plan.User) {
            Invoke-Winland $staged @('install', '--user')
        } else {
            Invoke-Winland $staged @('install')
        }

        if ($plan.TurnOff) {
            $copy = Get-InstalledExecutable $plan.User
            if (-not (Test-Path -LiteralPath $copy)) {
                throw 'Winland did not finish installing. Run the command again.'
            }
            $tweakArgs = @('tweaks', '--off')
            if ($plan.Touchpad) { $tweakArgs += '--touchpad' }
            Write-Output 'Turning the settings off.'
            Invoke-Winland $copy $tweakArgs
        }
    } finally {
        if (Test-Path -LiteralPath $temp) {
            Remove-Item -LiteralPath $temp -Recurse -Force
        }
    }
}

# Dot-sourcing stops here. The functions above are defined and nothing is downloaded or installed.
if ($MyInvocation.InvocationName -eq '.') { return }

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
} catch {
    # This host already has a protocol that can reach the release.
}
$ProgressPreference = 'SilentlyContinue'
Invoke-InstallScript
