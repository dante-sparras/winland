# Lint the tip the way CI lints a pull request. One build of the current checkout.
# pwsh scripts/check.ps1 -InstallHook pre-push
# installs a hook that runs this script once per push.

param(
    [ValidateSet('pre-push')]
    [string]$InstallHook
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')

if ($InstallHook) {
    $common = (& git -C $root rev-parse --git-common-dir).Trim()
    if (-not [System.IO.Path]::IsPathRooted($common)) {
        $common = Join-Path $root $common
    }
    $hooks = Join-Path $common 'hooks'
    New-Item -ItemType Directory -Force -Path $hooks | Out-Null
    $hook = Join-Path $hooks 'pre-push'
    $body = @'
#!/bin/sh
# Lint the tip once. Skip when this checkout has no check script yet.
if [ -f scripts/check.ps1 ]; then
  pwsh -NoProfile -File scripts/check.ps1
  exit $?
fi
exit 0
'@
    [System.IO.File]::WriteAllText($hook, ($body -replace "`r`n", "`n") + "`n")
    Write-Output "Installed $hook"
    exit 0
}

& dotnet build (Join-Path $root 'Winland.slnx') --configuration Release
exit $LASTEXITCODE
