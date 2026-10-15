# Lint the tip the pull request will lint. One run, not one run per commit.
#   pwsh -NoProfile -File scripts/check.ps1
#   pwsh -NoProfile -File scripts/check.ps1 -Ref <sha>
param(
    [string]$Ref
)

$ErrorActionPreference = 'Stop'

# A hook exports GIT_DIR. Clear it so git commands in this script use the checkout.
foreach ($name in @('GIT_DIR', 'GIT_WORK_TREE', 'GIT_INDEX_FILE', 'GIT_PREFIX')) {
    Remove-Item "Env:$name" -ErrorAction SilentlyContinue
}

$repoRoot = Split-Path $PSScriptRoot -Parent

function Get-LintFiles([string]$start, [string]$filter) {
    $found = New-Object System.Collections.Generic.List[string]
    $stack = New-Object System.Collections.Generic.Stack[string]
    $stack.Push($start)
    while ($stack.Count -gt 0) {
        $dir = $stack.Pop()
        $name = Split-Path $dir -Leaf
        if ($name -in @('.git', 'bin', 'obj', 'StrykerOutput', 'node_modules', 'omarchy', 'quickshell', 'hyprland')) {
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

    # Windows and macOS keep one file where Linux keeps two. Reject tracked paths,
    # and the folders on the way to them, that differ only by case.
    $tracked = @(& git -c core.quotepath=off -C $root ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed' }
    $spellings = @{}
    foreach ($path in $tracked) {
        $parts = $path -split '/'
        for ($i = 0; $i -lt $parts.Count; $i++) {
            $prefix = $parts[0..$i] -join '/'
            $key = $prefix.ToLowerInvariant()
            if (-not $spellings.ContainsKey($key)) {
                $spellings[$key] = New-Object System.Collections.Generic.HashSet[string]
            }
            [void]$spellings[$key].Add($prefix)
        }
    }
    $collisions = @($spellings.Values | Where-Object { $_.Count -gt 1 })
    foreach ($names in $collisions) {
        Write-Output "Case collision: $(($names | Sort-Object) -join ' | ')"
        $failed = 1
    }
    if ($collisions.Count -eq 0) {
        Write-Output "no case collisions in $($tracked.Count) tracked files"
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
