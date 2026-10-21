# Prepares this repo for development. Git does not run scripts on clone.
# After cloning, run: pwsh scripts/dev.ps1
#
# Points git at the hooks in scripts/hooks.

$ErrorActionPreference = 'Stop'
# This file lives in scripts/. The repo root is the parent of this directory.
$root = Split-Path -Parent $PSScriptRoot

# The hooks are tracked in scripts/hooks. Point git at them.
git -C $root config core.hooksPath scripts/hooks
if ($LASTEXITCODE -ne 0) { throw "git config core.hooksPath failed with $LASTEXITCODE" }
Write-Output "core.hooksPath = scripts/hooks"
