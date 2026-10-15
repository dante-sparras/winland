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

pwsh -NoProfile -File (Join-Path $root 'scripts/check.ps1') -InstallHook
if ($LASTEXITCODE -ne 0) { throw "pre-push hook install failed with $LASTEXITCODE" }
pwsh -NoProfile -File (Join-Path $root 'scripts/install-post-merge.ps1')
if ($LASTEXITCODE -ne 0) { throw "post-merge hook install failed with $LASTEXITCODE" }

$dest = Join-Path $root 'docs/agents/hyprland'
if (-not (Test-Path (Join-Path $dest '.git'))) {
    if (Test-Path $dest) { throw "$dest exists but is not a git checkout" }
    git clone --depth 1 --branch $HyprlandTag https://github.com/hyprwm/Hyprland.git $dest
    if ($LASTEXITCODE -ne 0) { throw "clone of Hyprland failed with $LASTEXITCODE" }
}

if (-not $Refresh -and (Test-Path (Join-Path $dest 'site/README.md'))) {
    Write-Output 'Manual already present'
    return
}
python -c "import markdownify, bs4" 2>$null
if ($LASTEXITCODE -ne 0) {
    python -m pip install markdownify beautifulsoup4
    if ($LASTEXITCODE -ne 0) { throw 'markdownify and beautifulsoup4 are required' }
}
python (Join-Path $root 'docs/fetch_reference.py') --root (Join-Path $dest 'site')
if ($LASTEXITCODE -ne 0) { throw "Hyprland manual fetch failed with $LASTEXITCODE" }
