# Prepares this repo for development. Git does not run scripts on clone.
# After cloning, run: pwsh scripts/dev.ps1
#
# Clones the pinned Hyprland commit into docs/agents/hyprland and fetches its wiki into docs/agents/hyprland/site.
# The folder is gitignored, and the checkout is left in place on a second run. Pass -Refresh to download the wiki again.
# When the pinned commit below changes, a second run moves the checkout to it and downloads the wiki again.

param([switch]$Refresh)

# The Hyprland commit this repo matches. See docs/adr/0008-the-spec-is-a-pinned-hyprland-release.md.
$HyprlandCommit = 'e2d94f54ff784f55654e52f62aece2832c1468d4'
$HyprlandUrl = 'https://github.com/hyprwm/Hyprland.git'

$ErrorActionPreference = 'Stop'
# This file lives in scripts/. The repo root is the parent of this directory.
$root = Split-Path -Parent $PSScriptRoot

# The hooks are tracked in scripts/hooks. Point git at them.
git -C $root config core.hooksPath scripts/hooks
if ($LASTEXITCODE -ne 0) { throw "git config core.hooksPath failed with $LASTEXITCODE" }
Write-Output "core.hooksPath = scripts/hooks"

# Fetches only the pinned commit, which GitHub serves by its hash, and checks it out detached.
function Use-PinnedCommit([string]$checkout) {
    git -C $checkout fetch --depth 1 origin $HyprlandCommit
    if ($LASTEXITCODE -ne 0) { throw "fetch of $HyprlandCommit failed with $LASTEXITCODE" }
    git -C $checkout checkout --detach $HyprlandCommit
    if ($LASTEXITCODE -ne 0) { throw "checkout of $HyprlandCommit in $checkout failed with $LASTEXITCODE. Remove $checkout and run this again." }
}

$dest = Join-Path $root 'docs/agents/hyprland'
if (-not (Test-Path (Join-Path $dest '.git'))) {
    if (Test-Path $dest) { throw "$dest exists but is not a git checkout" }
    git init --quiet $dest
    if ($LASTEXITCODE -ne 0) { throw "git init of $dest failed with $LASTEXITCODE" }
    git -C $dest remote add origin $HyprlandUrl
    if ($LASTEXITCODE -ne 0) { throw "git remote add in $dest failed with $LASTEXITCODE" }
    Use-PinnedCommit $dest
}
else {
    $head = git -C $dest rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or -not $head) { throw "$dest has no HEAD commit" }
    $head = $head.Trim()
    if ($head -ne $HyprlandCommit) {
        Write-Output "$dest is at $head, not $HyprlandCommit. Moving it there."
        Use-PinnedCommit $dest
        # The wiki is fetched for the pinned commit, so a new one fetches it again.
        $Refresh = $true
    }
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
