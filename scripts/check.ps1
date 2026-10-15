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

# Check the inline links in tracked markdown, and the repo paths AGENTS.md names in backticks.
# Prints one line per failure and returns how many there were.
function Test-MarkdownLinks([string]$Root, [string[]]$Tracked) {
    $files = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::Ordinal)
    $dirs = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::Ordinal)
    foreach ($path in $Tracked) {
        [void]$files.Add($path)
        $parts = $path -split '/'
        for ($i = 0; $i -lt $parts.Count - 1; $i++) {
            [void]$dirs.Add($parts[0..$i] -join '/')
        }
    }

    # A file that docs/agents/domain.md says may be missing, and a reference
    # checkout that is gitignored, are not an error in AGENTS.md.
    $optional = @('CONTEXT.md', 'docs/adr', 'docs/adr/README.md')
    $ignoredCheckout = '^docs/agents/(omarchy|quickshell|hyprland)(/|$)'
    $pathExtensions = '\.(md|ps1|json|yml|yaml|props|slnx|csproj)$'

    $linkCount = 0
    $pathCount = 0
    $broken = 0
    $mdFiles = @($Tracked | Where-Object { $_ -like '*.md' -and (Test-Path -LiteralPath (Join-Path $Root $_) -PathType Leaf) })
    foreach ($md in $mdFiles) {
        $folder = if ($md.Contains('/')) { $md.Substring(0, $md.LastIndexOf('/')) } else { '' }
        $fence = $null
        foreach ($line in [System.IO.File]::ReadAllLines((Join-Path $Root $md))) {
            if ($fence) {
                if ($line -match ('^ {0,3}' + [regex]::Escape($fence[0]) + '{' + $fence.Length + ',}\s*$')) { $fence = $null }
                continue
            }
            if ($line -match '^ {0,3}(`{3,}|~{3,})') {
                $fence = $Matches[1]
                continue
            }

            $text = [regex]::Replace($line, '`[^`]*`', '')
            foreach ($match in [regex]::Matches($text, '\]\(\s*(<[^>]*>|[^)\s]*)(?:\s+(?:"[^"]*"|''[^'']*''))?\s*\)')) {
                $target = $match.Groups[1].Value.Trim('<', '>')
                if ($target -match '^[A-Za-z][A-Za-z0-9+.-]*:' -or $target.StartsWith('#')) { continue }
                $target = ($target -split '[#?]')[0]
                if (-not $target) { continue }
                $linkCount++
                $target = [System.Uri]::UnescapeDataString($target)
                $segments = New-Object System.Collections.Generic.List[string]
                if (-not $target.StartsWith('/') -and $folder) {
                    $segments.AddRange([string[]]($folder -split '/'))
                }
                $outside = $false
                foreach ($part in ($target -split '/')) {
                    if ($part -eq '' -or $part -eq '.') { continue }
                    if ($part -eq '..') {
                        if ($segments.Count -eq 0) { $outside = $true; break }
                        $segments.RemoveAt($segments.Count - 1)
                    } else {
                        $segments.Add($part)
                    }
                }
                $resolved = $segments -join '/'
                if ($outside -or ($resolved -and -not $files.Contains($resolved) -and -not $dirs.Contains($resolved))) {
                    Write-Host "Broken link: ${md}: $($match.Groups[1].Value.Trim('<', '>'))"
                    $broken++
                }
            }

            if ($md -ne 'AGENTS.md') { continue }
            foreach ($span in [regex]::Matches($line, '`([^`]+)`')) {
                $candidate = $span.Groups[1].Value
                if ($candidate -match '[\s<>*$|{}()=]' -or $candidate -match '^[A-Za-z][A-Za-z0-9+.-]*:') { continue }
                if (-not ($candidate.Contains('/') -or $candidate -match $pathExtensions)) { continue }
                if ($candidate -match '^dante-sparras/[^/]+$') { continue }
                $path = $candidate.TrimEnd('/')
                if ($path.StartsWith('./')) { $path = $path.Substring(2) }
                if ($path -match $ignoredCheckout -or $candidate.Contains('site/') -or $candidate.Contains('manual/')) { continue }
                if ($path -in $optional) { continue }
                $pathCount++
                if (-not $files.Contains($path) -and -not $dirs.Contains($path)) {
                    Write-Host "Broken link: ${md}: $candidate"
                    $broken++
                }
            }
        }
    }
    if ($broken -eq 0) {
        Write-Host "markdown links ok: $linkCount links in $($mdFiles.Count) files, $pathCount AGENTS.md paths"
    }
    return $broken
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

    # scripts/fetch-reference.cs is a file-based app that no solution builds. Compile it here,
    # without running it, so it stays under the repo's compiler rules.
    Write-Output 'dotnet build scripts/fetch-reference.cs'
    dotnet build (Join-Path $root 'scripts/fetch-reference.cs') --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { $failed = 1 }
    if ((Test-MarkdownLinks $root $tracked) -gt 0) { $failed = 1 }

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
