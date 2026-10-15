# Prints this checkout's version: 0.1.<commits on HEAD>.
# A checkout whose tracked files have changes not committed is
# 0.1.<commits>-dev.<time>, so a local pack never takes the name of a version CI published.
$ErrorActionPreference = 'Stop'
$count = (& git -C $PSScriptRoot rev-list --count HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'git could not count the commits on HEAD' }
$dirty = & git -C $PSScriptRoot status --porcelain --untracked-files=no
if ($dirty) { "0.1.$count-dev.$((Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss'))" } else { "0.1.$count" }
