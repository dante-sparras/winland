# Copy the post-merge hook into the common hooks directory and point this
# clone at it. The installed file is LF-only. Linked worktrees share it.
#   pwsh -NoProfile -File scripts/install-post-merge.ps1
$ErrorActionPreference = 'Stop'

foreach ($name in @('GIT_DIR', 'GIT_WORK_TREE', 'GIT_INDEX_FILE', 'GIT_PREFIX')) {
    Remove-Item "Env:$name" -ErrorAction SilentlyContinue
}

$root = Split-Path $PSScriptRoot -Parent
$common = (& git -C $root rev-parse --git-common-dir).Trim()
if ($LASTEXITCODE -ne 0) { throw 'This directory is not a git checkout.' }
if (-not [System.IO.Path]::IsPathRooted($common)) {
    $common = Join-Path $root $common
}
$hooks = Join-Path $common 'hooks'
New-Item -ItemType Directory -Force -Path $hooks | Out-Null
& git -C $root config core.hooksPath $hooks
if ($LASTEXITCODE -ne 0) { throw 'Could not set core.hooksPath.' }

$source = Join-Path $PSScriptRoot 'hooks/post-merge'
if (-not (Test-Path -LiteralPath $source)) { throw "Missing $source" }

$bytes = [System.IO.File]::ReadAllBytes($source)
$text = [System.Text.Encoding]::UTF8.GetString($bytes)
if ($text.Length -gt 0 -and [int][char]$text[0] -eq 0xFEFF) { $text = $text.Substring(1) }
$text = $text.Replace("`r`n", "`n").Replace("`r", "`n")

$dest = Join-Path $hooks 'post-merge'
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($dest, $text, $utf8)

$chmod = Get-Command chmod -ErrorAction SilentlyContinue
if ($chmod) {
    & $chmod.Source +x $dest
    if ($LASTEXITCODE -ne 0) { throw "chmod +x failed for $dest" }
}

Write-Output "Installed $dest"
