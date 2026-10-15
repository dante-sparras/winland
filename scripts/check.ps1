# Lint the tip the pull request will lint. One run, not one run per commit.
#   pwsh -NoProfile -File scripts/check.ps1
#   pwsh -NoProfile -File scripts/check.ps1 -InstallHook
#   pwsh -NoProfile -File scripts/check.ps1 -Ref <sha>
param(
    [string]$Ref,
    [switch]$InstallHook
)

$ErrorActionPreference = 'Stop'

# A hook exports GIT_DIR. Clear it so git commands in this script use the checkout.
foreach ($name in @('GIT_DIR', 'GIT_WORK_TREE', 'GIT_INDEX_FILE', 'GIT_PREFIX')) {
    Remove-Item "Env:$name" -ErrorAction SilentlyContinue
}

$repoRoot = Split-Path $PSScriptRoot -Parent

function Get-HooksDirectory([string]$Root) {
    $common = (& git -C $Root rev-parse --git-common-dir).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'This directory is not a git checkout.' }
    if (-not [System.IO.Path]::IsPathRooted($common)) {
        $common = Join-Path $Root $common
    }
    $hooks = Join-Path $common 'hooks'
    New-Item -ItemType Directory -Force -Path $hooks | Out-Null
    & git -C $Root config core.hooksPath $hooks
    if ($LASTEXITCODE -ne 0) { throw 'Could not set core.hooksPath.' }
    return $hooks
}

function Install-PrePushHook {
    $hooks = Get-HooksDirectory $repoRoot
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $path = Join-Path $hooks 'pre-push'
    $body = @"
#!/bin/sh
# Lint each pushed branch tip once. A tag push is not linted.
root=`$(git rev-parse --show-toplevel) || exit 1
status=0
while read -r local_ref local_sha remote_ref remote_sha; do
    if [ "`$local_sha" = "0000000000000000000000000000000000000000" ]; then
        continue
    fi
    case "`$remote_ref" in
        refs/tags/*) continue ;;
    esac
    '$pwsh' -NoProfile -File "`$root/scripts/check.ps1" -Ref "`$local_sha" || status=1
done
exit "`$status"
"@
    $body = $body.Replace("`r`n", "`n")
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($path, $body, $utf8)
    Write-Output "Installed $path"
}

if ($InstallHook) {
    Install-PrePushHook
    if (-not $Ref) { exit 0 }
}

function Get-LintFiles([string]$start, [string]$filter) {
    $found = New-Object System.Collections.Generic.List[string]
    $stack = New-Object System.Collections.Generic.Stack[string]
    $stack.Push($start)
    while ($stack.Count -gt 0) {
        $dir = $stack.Pop()
        $name = Split-Path $dir -Leaf
        if ($name -in @('.git', 'bin', 'obj', 'StrykerOutput', 'node_modules', '__pycache__', 'omarchy', 'quickshell', 'hyprland')) {
            continue
        }
        foreach ($file in [System.IO.Directory]::EnumerateFiles($dir, $filter)) {
            $found.Add($file)
        }
        foreach ($sub in [System.IO.Directory]::EnumerateDirectories($dir)) {
            $stack.Push($sub)
        }
    }
    return $found
}

$failed = 0
$scratch = $null
$root = $repoRoot
try {
    if ($Ref) {
        $scratch = Join-Path ([System.IO.Path]::GetTempPath()) ("repo-check-" + [guid]::NewGuid().ToString('n'))
        & git -C $repoRoot worktree add --detach $scratch $Ref
        if ($LASTEXITCODE -ne 0) { throw "git worktree add $Ref failed" }
        $root = $scratch
    }

    $python = Get-Command python -ErrorAction SilentlyContinue
    if (-not $python) { throw 'python is not on PATH. The lint compiles docs/fetch_reference.py.' }
    $env:PYTHONPYCACHEPREFIX = Join-Path ([System.IO.Path]::GetTempPath()) 'repo-pyc'

    $pyFiles = @(Get-LintFiles $root '*.py')
    if ($pyFiles.Count -eq 0) {
        Write-Output 'No Python files to compile.'
    }
    foreach ($file in $pyFiles) {
        Write-Output "python -m py_compile $file"
        python -m py_compile $file
        if ($LASTEXITCODE -ne 0) { $failed = 1 }
    }

    $psFiles = @(Get-LintFiles $root '*.ps1')
    foreach ($file in $psFiles) {
        $tokens = $null
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$errors)
        if ($errors -and $errors.Count -gt 0) {
            Write-Output "PowerShell parse failed: $file"
            $errors | ForEach-Object { Write-Output $_.ToString() }
            $failed = 1
        } else {
            Write-Output "powershell parse $file"
        }
    }

    $solutions = @()
    $solutions += @(Get-ChildItem -Path $root -Filter *.slnx -File -ErrorAction SilentlyContinue)
    if ($solutions.Count -eq 0) {
        $solutions += @(Get-ChildItem -Path $root -Filter *.sln -File -ErrorAction SilentlyContinue)
    }
    if ($solutions.Count -eq 0) {
        Write-Output 'No solution to build.'
    }
    foreach ($solution in $solutions) {
        Write-Output "dotnet build $($solution.FullName)"
        dotnet build $solution.FullName --configuration Release --nologo
        if ($LASTEXITCODE -ne 0) { $failed = 1 }
    }
} finally {
    if ($scratch) {
        & git -C $repoRoot worktree remove --force $scratch
        if ($LASTEXITCODE -ne 0) {
            Write-Output "Could not remove worktree $scratch"
        }
    }
}

exit $failed
