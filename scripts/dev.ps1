# Prepares this repo for development. Git does not run scripts on clone.
# After cloning, run: pwsh scripts/dev.ps1
#
# Clones the pinned Hyprland release into docs/agents/hyprland and fetches its wiki into docs/agents/hyprland/site.
# The folder is gitignored, and the checkout is left in place on a second run. Pass -Refresh to download the wiki again.

param([switch]$Refresh)

# The Hyprland release this repo matches. See docs/adr/0008-the-spec-is-a-pinned-hyprland-release.md.
$HyprlandTag = 'v0.56.2'

$ErrorActionPreference = 'Stop'
# This file lives in scripts/. The repo root is the parent of this directory.
$root = Split-Path -Parent $PSScriptRoot

# The hooks are tracked in scripts/hooks. Point git at them.
git -C $root config core.hooksPath scripts/hooks
if ($LASTEXITCODE -ne 0) { throw "git config core.hooksPath failed with $LASTEXITCODE" }
Write-Output "core.hooksPath = scripts/hooks"

$dest = Join-Path $root 'docs/agents/hyprland'
if (-not (Test-Path (Join-Path $dest '.git'))) {
    if (Test-Path $dest) { throw "$dest exists but is not a git checkout" }
    git clone --depth 1 --branch $HyprlandTag https://github.com/hyprwm/Hyprland.git $dest
    if ($LASTEXITCODE -ne 0) { throw "clone of Hyprland failed with $LASTEXITCODE" }
}

# site/README.md is written last and only after a full fetch, so its presence means the wiki is complete.
$readme = Join-Path $dest 'site/README.md'
if (-not $Refresh -and (Test-Path $readme)) {
    Write-Output 'Manual already present'
    return
}
dotnet run (Join-Path $root 'scripts/fetch-reference.cs') -- --root (Join-Path $dest 'site')
if ($LASTEXITCODE -ne 0) {
    # The fetch writes the README even when some pages failed. Remove it so the next run fetches again.
    if (Test-Path $readme) { Remove-Item -LiteralPath $readme -Force }
    throw "Hyprland manual fetch failed with $LASTEXITCODE. Run this again to retry."
}
