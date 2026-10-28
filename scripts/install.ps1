# One-line install for Windows PowerShell 5.1 and PowerShell 7.
#   irm https://raw.githubusercontent.com/dante-sparras/winland/main/scripts/install.ps1 | iex
# The dev channel, built from main:
#   & ([scriptblock]::Create((irm https://raw.githubusercontent.com/dante-sparras/winland/main/scripts/install.ps1))) -Channel dev
# Dot-sourcing defines the decisions and runs nothing. scripts/install.tests.ps1 does that.
param(
    [ValidateSet('stable', 'dev')]
    [string]$Channel = 'stable',
    [switch]$NoTweaks
)

$script:ArchiveName = 'Winland-win-x64.zip'
$script:ChecksumsName = 'SHA256SUMS'
$script:VersionName = 'version.json'
$script:ReleasesUrl = 'https://api.github.com/repos/dante-sparras/winland/releases'
$script:StepOpen = $false

function Get-Banner {
    $art = @'
+--------------------------------------------+
|                                            |
|  __        ___       _                 _   |
|  \ \      / (_)_ __ | | __ _ _ __   __| |  |
|   \ \ /\ / /| | '_ \| |/ _` | '_ \ / _` |  |
|    \ V  V / | | | | | | (_| | | | | (_| |  |
|     \_/\_/  |_|_| |_|_|\__,_|_| |_|\__,_|  |
|                                            |
+--------------------------------------------+
'@
    return @($art -split '\r?\n')
}

function Test-Yes([string]$Answer) {
    if ([string]::IsNullOrWhiteSpace($Answer)) { return $true }
    $text = $Answer.Trim()
    if ($text -eq 'n' -or $text -eq 'no') { return $false }
    return $true
}

function Get-ReleaseUrl([string]$Channel) {
    if ($Channel -eq 'dev') { return "$script:ReleasesUrl/tags/dev" }
    return "$script:ReleasesUrl/latest"
}

function Get-InstallArguments([bool]$Logon, [bool]$Start) {
    $arguments = @('install')
    if (-not $Logon) { $arguments += '--no-logon' }
    if (-not $Start) { $arguments += '--no-start' }
    return $arguments
}

function Get-TweakArguments([bool]$PrecisionTouchpad) {
    $arguments = @('tweaks', '--off')
    if ($PrecisionTouchpad) { $arguments += '--touchpad' }
    return $arguments
}

# The channel is always named, so the stable one-liner also takes an install on dev back to stable.
function Get-UpdateArguments([string]$Channel) {
    if ($Channel -eq 'dev') { return @('update', '--channel', 'dev') }
    return @('update', '--channel', 'stable')
}

function Get-FailureMessage([string[]]$Arguments, [int]$ExitCode, [string[]]$Output) {
    # winland exits with 1223, ERROR_CANCELLED, when the UAC prompt is declined.
    if ($ExitCode -eq 1223) {
        if ($Arguments[0] -eq 'update') {
            return 'Administrator approval was declined. Winland was not updated. Run the command again to retry.'
        }
        return 'Administrator approval was declined. Nothing was installed. Run the command again to retry.'
    }
    $last = @($Output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -notmatch '^\s+at\s' } | Select-Object -Last 1)
    if ($last.Count -gt 0) { return ([string]$last[0]).Trim() }
    $shown = $Arguments -join ' '
    return "winland $shown stopped with exit code $ExitCode."
}

function Get-SummaryLines([string]$Version, [string]$Folder, [string]$Config, [bool]$Running) {
    $lines = New-Object System.Collections.Generic.List[string]
    if ($Running) {
        $lines.Add(" Winland $Version is installed and running.")
    } else {
        $lines.Add(" Winland $Version is installed.")
    }
    $lines.Add("    Folder     $Folder")
    $lines.Add("    Config     $Config")
    if (-not $Running) {
        $lines.Add('    Start      Winland in the Start menu, or run winland')
    }
    $lines.Add('    Quit       right-click the Winland icon in the notification area (behind ^ if hidden), then Quit')
    $lines.Add('    Keys       https://github.com/dante-sparras/winland#hotkeys, or the config')
    $lines.Add('    Undo       winland uninstall, which also turns back on the Snap and swipes the install turned off')
    return @($lines.ToArray())
}

# The install writes PATH to the registry, which only a new terminal reads. This is the PATH for the terminal that ran the script.
function Get-SessionPath([string]$Path, [string]$Folder) {
    $separator = [System.IO.Path]::PathSeparator
    $entries = @($Path -split [regex]::Escape([string]$separator) | Where-Object { $_ -ne '' })
    foreach ($entry in $entries) {
        if ($entry.TrimEnd('\') -eq $Folder.TrimEnd('\')) { return $Path }
    }
    return (@($entries) + $Folder) -join $separator
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

function Get-ReleaseDownloads([string]$Json, [bool]$AllowPrerelease = $false) {
    if ([string]::IsNullOrWhiteSpace($Json)) {
        throw 'The release could not be read. Nothing was installed. Run the command again.'
    }
    $release = ConvertFrom-Json $Json
    $draft = Get-PropertyValue $release 'draft'
    $prerelease = Get-PropertyValue $release 'prerelease'
    if ($draft -eq $true -or ($prerelease -eq $true -and -not $AllowPrerelease)) {
        throw 'The latest release is not a published release. Nothing was installed. Wait for a published release and run the command again.'
    }
    $assets = @(Get-PropertyValue $release 'assets')
    $found = @{}
    foreach ($asset in $assets) {
        if ($null -eq $asset) { continue }
        $name = [string](Get-PropertyValue $asset 'name')
        $url = [string](Get-PropertyValue $asset 'browser_download_url')
        foreach ($wanted in @($script:ArchiveName, $script:ChecksumsName, $script:VersionName)) {
            if ($name -ceq $wanted) { $found[$wanted] = $url }
        }
    }
    foreach ($wanted in @($script:ArchiveName, $script:ChecksumsName, $script:VersionName)) {
        if ([string]::IsNullOrEmpty($found[$wanted])) {
            throw "The release has no $wanted. Nothing was installed. Run the command again when the release has that file."
        }
        if ($found[$wanted] -notmatch '^https://') {
            throw "The release has an unsupported download for $wanted. Nothing was installed."
        }
    }
    return [pscustomobject]@{
        Archive = $found[$script:ArchiveName]
        Sums = $found[$script:ChecksumsName]
        Version = $found[$script:VersionName]
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

function Read-Answer {
    try {
        return Read-Host
    } catch {
        return ''
    }
}

function Read-YesNo([string]$Question) {
    Write-Host ''
    Write-Host "    $Question [Y/n] " -NoNewline
    return (Test-Yes (Read-Answer))
}

function Write-Step([string]$Text) {
    Write-Host '==> ' -ForegroundColor Cyan -NoNewline
    Write-Host ($Text.PadRight(32)) -NoNewline
    $script:StepOpen = $true
}

function Write-Done([string]$Result = 'OK') {
    Write-Host $Result -ForegroundColor Green
    $script:StepOpen = $false
}

function Write-Failure([string]$Message) {
    if ($script:StepOpen) {
        Write-Host ''
        $script:StepOpen = $false
    }
    Write-Host ''
    Write-Host 'error  ' -ForegroundColor Red -NoNewline
    Write-Host $Message
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

# An install has its uninstall record as well as winland.exe. An uninstall that could not delete every file
# leaves winland.exe behind without the record, and that folder gets a fresh install, not an update.
function Find-InstalledWinland {
    $record = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Winland'
    $machine = Get-InstalledExecutable $false
    if ((Test-Path -LiteralPath $machine) -and (Test-Path -LiteralPath "HKLM:\$record")) { return $machine }
    $user = Get-InstalledExecutable $true
    if ((Test-Path -LiteralPath $user) -and (Test-Path -LiteralPath "HKCU:\$record")) { return $user }
    return $null
}

function Invoke-Winland([string]$Executable, [string[]]$Arguments) {
    # Windows PowerShell 5.1 turns redirected stderr into errors, which Stop would make fatal.
    $ErrorActionPreference = 'Continue'
    $output = @(& $Executable @Arguments 2>&1 | ForEach-Object { [string]$_ })
    $code = $LASTEXITCODE
    if ($code -ne 0) { throw (Get-FailureMessage $Arguments $code $output) }
    return $output
}

function Get-Download([string]$Url, [string]$Path) {
    try {
        $headers = @{ 'User-Agent' = 'winland' }
        Invoke-WebRequest -Uri $Url -OutFile $Path -UseBasicParsing -Headers $headers
    } catch {
        throw "Could not download $Url. Check the network and run the command again."
    }
}

function Get-RateLimitMessage($Response) {
    if ($null -eq $Response) { return $null }
    if ([int]$Response.StatusCode -notin @(403, 429)) { return $null }
    try {
        if (@($Response.Headers.GetValues('x-ratelimit-remaining')) -notcontains '0') { return $null }
    } catch {
        return $null
    }
    try {
        $reset = @($Response.Headers.GetValues('x-ratelimit-reset'))
        $seconds = 0L
        if ($reset.Count -gt 0 -and [long]::TryParse([string]$reset[0], [ref]$seconds) -and $seconds -ge 0 -and $seconds -le 253402300799) {
            $when = [DateTimeOffset]::FromUnixTimeSeconds($seconds).UtcDateTime.ToString('yyyy-MM-dd HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture)
            return "The GitHub API rate limit is used up. Try again after $when UTC."
        }
    } catch {
        # An absent reset header still gets a rate-limit explanation.
    }
    return 'The GitHub API rate limit is used up. Try again later.'
}

function Get-ReleaseJson([string]$Url) {
    try {
        $headers = @{
            'User-Agent' = 'winland'
            'Accept' = 'application/vnd.github+json'
        }
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -Headers $headers
        return [string]$response.Content
    } catch {
        $rateLimit = Get-RateLimitMessage (Get-PropertyValue $_.Exception 'Response')
        if ($rateLimit) { throw $rateLimit }
        throw 'Could not read the newest release. Check the network and run the command again.'
    }
}

function Update-Winland([string]$Executable, [string]$Channel) {
    Write-Step 'Updating Winland'
    $output = @(Invoke-Winland $Executable (Get-UpdateArguments $Channel))
    Write-Done
    Write-Host ''
    $env:Path = Get-SessionPath $env:Path (Split-Path -Parent $Executable)
    if ($output.Count -gt 0) {
        foreach ($line in $output) { Write-Host " $line" }
    } else {
        Write-Host ' Winland is updated.'
    }
}

# The elevated host receives this script in its command line, never from a user-writable .ps1 file.
function Get-ProtectedInstallScript([string]$Zip, [string]$Hash, [string]$Account, [bool]$Logon, [bool]$Start) {
    $body = @'
$ErrorActionPreference = 'Stop'
$folder = $null
try {
    $security = New-Object Security.AccessControl.DirectorySecurity
    $security.SetAccessRuleProtection($true, $false)
    $security.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
    $inherit = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {
        $identity = New-Object Security.Principal.SecurityIdentifier($sid)
        $rule = New-Object Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', $inherit, 'None', 'Allow')
        $security.AddAccessRule($rule)
    }
    $users = New-Object Security.Principal.SecurityIdentifier('S-1-5-32-545')
    $security.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($users, 'ReadAndExecute', $inherit, 'None', 'Allow')))
    $windows = [Environment]::GetFolderPath('Windows')
    $folder = Join-Path (Join-Path $windows 'Temp') ('winland-install-' + [guid]::NewGuid().ToString('N'))
    $directory = New-Object IO.DirectoryInfo($folder)
    $directory.Create($security)
    $zip = Join-Path $folder 'release.zip'
    [IO.File]::Copy('__ZIP__', $zip)
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne '__HASH__') {
        throw 'The release changed after its SHA-256 check. Nothing was installed.'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $package = Join-Path $folder 'package'
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $package)
    $executable = Join-Path (Join-Path $package 'winland') 'winland-launcher.exe'
    if (-not (Test-Path -LiteralPath $executable)) { throw 'The release zip has no winland launcher.' }
    $arguments = @('install', '__ACCOUNT__')
    if (-not __LOGON__) { $arguments += '--no-logon' }
    if (-not __START__) { $arguments += '--no-start' }
    & $executable @arguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
} finally {
    if ($folder -and (Test-Path -LiteralPath $folder)) { Remove-Item -LiteralPath $folder -Recurse -Force }
}
exit 0
'@
    $logonText = '$false'
    $startText = '$false'
    if ($Logon) { $logonText = '$true' }
    if ($Start) { $startText = '$true' }
    return $body.Replace('__ZIP__', $Zip.Replace("'", "''")).Replace('__HASH__', $Hash.Replace("'", "''")).Replace('__ACCOUNT__', $Account.Replace("'", "''")).Replace('__LOGON__', $logonText).Replace('__START__', $startText)
}

function Install-ProtectedPackage([string]$Zip, [string]$Hash, [bool]$Logon, [bool]$Start) {
    $account = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $body = Get-ProtectedInstallScript $Zip $Hash $account $Logon $Start
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($body))
    $powershell = Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell\v1.0\powershell.exe'
    try {
        $process = Start-Process -FilePath $powershell -ArgumentList @('-NoProfile', '-EncodedCommand', $encoded) -Verb RunAs -WindowStyle Hidden -Wait -PassThru
    } catch {
        if ($_.Exception.NativeErrorCode -eq 1223) { throw (Get-FailureMessage @('install') 1223 @()) }
        throw
    }
    if ($process.ExitCode -ne 0) { throw (Get-FailureMessage @('install') $process.ExitCode @()) }
}

function Get-InstallChoices([bool]$ChangeSettings = $true) {
    $logon = $true
    $start = $true
    if (Test-InstallPrompt) {
        $logon = Read-YesNo 'Start Winland when you sign in?'
        $start = Read-YesNo 'Start Winland now?'
        if ($ChangeSettings) { $ChangeSettings = Read-YesNo 'Turn off Windows Snap and touchpad workspace swipes? Uninstall puts them back.' }
    }
    return [pscustomobject]@{ Logon = $logon
        Start = $start
        ChangeSettings = $ChangeSettings }
}

function Install-Winland([string]$Channel, [string]$Temp, [bool]$ChangeSettings = $true) {
    if ($Channel -eq 'dev') { Write-Step 'Looking up the dev release' } else { Write-Step 'Looking up the newest release' }
    $downloads = Get-ReleaseDownloads (Get-ReleaseJson (Get-ReleaseUrl $Channel)) ($Channel -eq 'dev')
    $versionPath = Join-Path $Temp $script:VersionName
    Get-Download $downloads.Version $versionPath
    $version = [string](Get-PropertyValue (Get-Content -LiteralPath $versionPath -Raw | ConvertFrom-Json) 'version')
    Write-Done "Winland $version"

    $choices = Get-InstallChoices $ChangeSettings
    $logon = $choices.Logon
    $start = $choices.Start
    $ChangeSettings = $choices.ChangeSettings
    Write-Host ''

    Write-Step 'Downloading'
    $zip = Join-Path $Temp $script:ArchiveName
    $sumsPath = Join-Path $Temp $script:ChecksumsName
    Get-Download $downloads.Archive $zip
    Get-Download $downloads.Sums $sumsPath
    Write-Done

    Write-Step 'Checking SHA-256'
    $sums = Get-Content -LiteralPath $sumsPath -Raw
    $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    if (-not (Test-ReleaseHash $actual $sums)) {
        throw 'The SHA-256 of Winland-win-x64.zip does not match SHA256SUMS. Nothing was installed. Run the command again.'
    }
    Write-Done

    Write-Step 'Installing'
    Install-ProtectedPackage $zip $actual $logon $start
    $copy = Get-InstalledExecutable $false
    if (-not (Test-Path -LiteralPath $copy)) {
        throw 'Winland did not finish installing. Run the command again.'
    }
    Invoke-Winland $copy @('install', '--winland-cleanup-user') | Out-Null
    # Unelevated, so these settings belong to the signed-in user and not to an administrator who approved UAC.
    if ($ChangeSettings) { Invoke-Winland $copy (Get-TweakArguments (Test-PrecisionTouchpad)) | Out-Null }
    if ($Channel -eq 'dev') {
        # Saves the dev channel for winland update. The copy is that commit, so nothing is downloaded.
        Invoke-Winland $copy (Get-UpdateArguments 'dev') | Out-Null
    }
    Write-Done
    $env:Path = Get-SessionPath $env:Path (Split-Path -Parent $copy)

    $config = Join-Path (Join-Path $env:USERPROFILE '.winland') 'winland.conf'
    Write-Host ''
    $summary = @(Get-SummaryLines $version (Split-Path -Parent $copy) $config $start)
    Write-Host $summary[0] -ForegroundColor Green
    foreach ($line in ($summary | Select-Object -Skip 1)) { Write-Host $line }
}

function Invoke-InstallScript([string]$Channel, [bool]$ChangeSettings = $true) {
    # These stay inside the function, so the terminal that ran irm | iex keeps its own settings.
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'
    Set-StrictMode -Version Latest
    Write-Host ''
    foreach ($line in (Get-Banner)) { Write-Host $line -ForegroundColor Cyan }
    Write-Host ''

    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ('winland-install-' + [guid]::NewGuid().ToString('N'))
    try {
        # No exit, because under irm | iex that would close the terminal.
        $installed = Find-InstalledWinland
        if ($installed) {
            Update-Winland $installed $Channel
        } else {
            New-Item -ItemType Directory -Path $temp | Out-Null
            Install-Winland $Channel $temp $ChangeSettings
        }
    } catch {
        Write-Failure $_.Exception.Message
    } finally {
        if (Test-Path -LiteralPath $temp) {
            Remove-Item -LiteralPath $temp -Recurse -Force
        }
    }
    Write-Host ''
}

# Dot-sourcing stops here. The functions above are defined and nothing is downloaded or installed.
if ($MyInvocation.InvocationName -eq '.') { return }

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
} catch {
    # This host already has a protocol that can reach the release.
}
Invoke-InstallScript $Channel (-not $NoTweaks)
