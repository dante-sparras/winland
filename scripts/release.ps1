# Cuts, publishes, and abandons stable releases. Tags are the source of truth for versions.
# The committed version is 0.0.0. See docs/releasing.md.
#   pwsh -NoProfile -File scripts/release.ps1 release [-Commit <sha>] [-Bump patch|minor|major] [-Autopublish] [-Remote origin] [-DryRun]
#   pwsh -NoProfile -File scripts/release.ps1 publish [-Version X.Y.Z] [-Remote origin] [-DryRun]
#   pwsh -NoProfile -File scripts/release.ps1 abandon <attempt-tag> [-Remote origin] [-DryRun]
#   pwsh -NoProfile -File scripts/release.ps1 status
# -SimulatePublished v0.1.2,v0.2.0 (or: none) pretends those are the published releases.
# It works only with status and -DryRun, so it can never cut a real release.
# Dot-sourcing this file defines the functions and runs nothing. scripts/release.tests.ps1 does that.
param(
    [Parameter(Position = 0)]
    [ValidateSet('release', 'publish', 'abandon', 'status')]
    [string]$Command,
    [Parameter(Position = 1)]
    [string]$AttemptTag,
    [string]$Commit,
    [ValidateSet('patch', 'minor', 'major')]
    [string]$Bump = 'patch',
    [switch]$Autopublish,
    [string]$Version,
    [string]$Remote = 'origin',
    [switch]$DryRun,
    [string]$SimulatePublished
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ---------------------------------------------------------------------------
# Pure parts: tag grammar, versions, claims, warning blocks. No git, no gh.
# ---------------------------------------------------------------------------

# A final tag is v<major>.<minor>.<patch>, lower-case v, no leading zeros, no suffix.
function Get-StableVersion([string]$Tag) {
    if ($Tag -cmatch '^v(0|[1-9][0-9]{0,5})\.(0|[1-9][0-9]{0,5})\.(0|[1-9][0-9]{0,5})$') {
        return $Tag.Substring(1)
    }
    return $null
}

# rc.<N>-vX.Y.Z is a release attempt. The attempt comes first so it never reads as a SemVer prerelease.
function Get-AttemptRef([string]$Ref) {
    if ($Ref -cmatch '^rc\.([1-9][0-9]{0,5})-(v[^/]+)$') {
        $attempt = [int]$Matches[1]
        $version = Get-StableVersion $Matches[2]
        if ($version) {
            return [pscustomobject]@{ Ref = $Ref; Attempt = $attempt; Version = $version }
        }
    }
    return $null
}

# abandoned-rc.<N>-vX.Y.Z clears one attempt.
function Get-MarkerRef([string]$Ref) {
    if ($Ref -cmatch '^abandoned-(rc\..+)$') {
        return Get-AttemptRef $Matches[1]
    }
    return $null
}

function New-AttemptRef([string]$Version, [int]$Attempt) {
    $ref = "rc.$Attempt-v$Version"
    $parsed = Get-AttemptRef $ref
    if (-not $parsed -or $parsed.Version -ne $Version -or $parsed.Attempt -ne $Attempt) {
        throw "Invalid attempt: version '$Version', attempt $Attempt."
    }
    return $ref
}

function New-MarkerRef([string]$Version, [int]$Attempt) {
    return 'abandoned-' + (New-AttemptRef $Version $Attempt)
}

function Compare-StableVersion([string]$A, [string]$B) {
    $left = $A.Split('.')
    $right = $B.Split('.')
    for ($i = 0; $i -lt 3; $i++) {
        $difference = [int]$left[$i] - [int]$right[$i]
        if ($difference -ne 0) { return [Math]::Sign($difference) }
    }
    return 0
}

# The newest version among the stable tags in a list, or $null when it holds none.
function Get-NewestVersion([string[]]$Tags) {
    $newest = $null
    foreach ($tag in $Tags) {
        $version = Get-StableVersion $tag
        if ($version -and ((-not $newest) -or (Compare-StableVersion $version $newest) -gt 0)) {
            $newest = $version
        }
    }
    return $newest
}

function Step-Version([string]$Version, [string]$Bump) {
    $parts = $Version.Split('.')
    $major = [int]$parts[0]
    $minor = [int]$parts[1]
    $patch = [int]$parts[2]
    switch ($Bump) {
        'major' { return "$($major + 1).0.0" }
        'minor' { return "$major.$($minor + 1).0" }
        'patch' { return "$major.$minor.$($patch + 1)" }
        default { throw "Unknown bump '$Bump'." }
    }
}

# With nothing published the base is 0.0.0, so the first release is cut with -Bump minor and is 0.1.0.
function Get-NextVersion([string]$Published, [string]$Bump) {
    $base = if ($Published) { $Published } else { '0.0.0' }
    return Step-Version $base $Bump
}

# One more than the highest attempt for the version. An abandoned attempt keeps its number.
function Get-NextAttempt([string]$Version, [string[]]$Refs) {
    $highest = 0
    foreach ($ref in $Refs) {
        $parsed = Get-AttemptRef $ref
        if ($parsed -and $parsed.Version -eq $Version -and $parsed.Attempt -gt $highest) {
            $highest = $parsed.Attempt
        }
    }
    return $highest + 1
}

# An attempt is outstanding when its version has no final tag and the attempt has no abandon marker.
# $Refs is every tag name on the remote.
function Get-OutstandingAttempts([string[]]$Refs) {
    $final = @{}
    $cleared = @{}
    $attempts = New-Object System.Collections.Generic.List[object]
    foreach ($ref in $Refs) {
        $version = Get-StableVersion $ref
        if ($version) { $final[$version] = $true; continue }
        $marker = Get-MarkerRef $ref
        if ($marker) { $cleared["$($marker.Version)#$($marker.Attempt)"] = $true; continue }
        $attempt = Get-AttemptRef $ref
        if ($attempt) { $attempts.Add($attempt) }
    }
    $outstanding = New-Object System.Collections.Generic.List[object]
    foreach ($attempt in $attempts) {
        if (-not $final.ContainsKey($attempt.Version) -and -not $cleared.ContainsKey("$($attempt.Version)#$($attempt.Attempt)")) {
            $outstanding.Add($attempt)
        }
    }
    return $outstanding.ToArray()
}

# The claim is the message of the attempt tag.
function ConvertTo-ClaimJson([string]$Commit, [int]$Attempt, [string]$Version, [bool]$Autopublish, [long]$Epoch) {
    return ([ordered]@{
            commit      = $Commit
            attempt     = $Attempt
            version     = $Version
            autopublish = $Autopublish
            epoch       = $Epoch
        } | ConvertTo-Json -Compress)
}

function ConvertFrom-ClaimJson([string]$Text) {
    # A signed tag keeps its signature inside the message. The claim is everything before it.
    $json = ($Text -split "`n-----BEGIN ", 2)[0].Trim()
    try {
        $claim = $json | ConvertFrom-Json -ErrorAction Stop
    } catch {
        throw "The tag message is not a release claim: $json"
    }
    if ($claim -isnot [pscustomobject]) { throw "The tag message is not a release claim: $json" }
    $names = @($claim.PSObject.Properties.Name)
    foreach ($required in 'commit', 'attempt', 'version', 'autopublish', 'epoch') {
        if ($names -cnotcontains $required) { throw "The release claim has no '$required': $json" }
    }
    if ($claim.commit -isnot [string] -or $claim.commit -cnotmatch '^[0-9a-f]{40}$') { throw "The release claim has an invalid commit: $json" }
    if ($claim.attempt -isnot [long] -and $claim.attempt -isnot [int]) { throw "The release claim has an invalid attempt: $json" }
    if ($claim.attempt -lt 1) { throw "The release claim has an invalid attempt: $json" }
    if ($claim.version -isnot [string] -or -not (Get-StableVersion "v$($claim.version)")) { throw "The release claim has an invalid version: $json" }
    if ($claim.autopublish -isnot [bool]) { throw "The release claim has an invalid autopublish: $json" }
    if (($claim.epoch -isnot [long] -and $claim.epoch -isnot [int]) -or $claim.epoch -lt 0) { throw "The release claim has an invalid epoch: $json" }
    return [pscustomobject]@{
        Commit      = $claim.commit
        Attempt     = [int]$claim.attempt
        Version     = $claim.version
        Autopublish = [bool]$claim.autopublish
        Epoch       = [long]$claim.epoch
    }
}

$script:WarningBegin = '<!-- release-warning:begin -->'
$script:WarningEnd = '<!-- release-warning:end -->'

function Get-WarningBlock {
    return (@(
            $script:WarningBegin
            '> [!WARNING]'
            '> **Do not publish this draft from the GitHub UI.** Publishing it by hand skips the final `vX.Y.Z` tag.'
            '> `scripts/release.ps1 publish` does it, and the Stable Release workflow calls that for an autopublish release.'
            $script:WarningEnd
        ) -join "`n")
}

function Add-WarningBlocks([string]$Body) {
    $block = Get-WarningBlock
    return "$block`n`n$($Body.Trim())`n`n$block`n"
}

function Test-WarningBlocks([string]$Body) {
    return $Body.Contains($script:WarningBegin) -or $Body.Contains($script:WarningEnd)
}

function Remove-WarningBlocks([string]$Body) {
    $pattern = '(?s)' + [regex]::Escape($script:WarningBegin) + '.*?' + [regex]::Escape($script:WarningEnd)
    return ([regex]::Replace($Body, $pattern, '')).Trim() + "`n"
}

# The text of git ls-remote --tags. An annotated tag lists its tag object, then its commit as <name>^{}.
function ConvertFrom-LsRemoteTags([string[]]$Lines) {
    $tags = @{}
    foreach ($line in $Lines) {
        if ($line -cmatch '^([0-9a-f]{40})\s+refs/tags/(.+?)(\^\{\})?$') {
            $sha = $Matches[1]
            $name = $Matches[2]
            $peeled = $Matches[3] -eq '^{}'
            if (-not $tags.ContainsKey($name)) {
                $tags[$name] = [pscustomobject]@{ Name = $name; Object = $sha; Commit = $sha; Annotated = $false }
            }
            if ($peeled) {
                $tags[$name].Commit = $sha
                $tags[$name].Annotated = $true
            } else {
                $tags[$name].Object = $sha
                if (-not $tags[$name].Annotated) { $tags[$name].Commit = $sha }
            }
        }
    }
    return $tags
}

# https://github.com/owner/repo.git, git@github.com:owner/repo.git, ssh://git@github.com/owner/repo
function ConvertFrom-RemoteUrl([string]$Url) {
    if ($Url -cmatch '^(?:https?://(?:[^/@\s]+@)?github\.com/|ssh://(?:[^/@\s]+@)?github\.com(?::\d+)?/|git://github\.com/|[^/@:\s]+@github\.com:)([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(?:\.git)?/?$') {
        return $Matches[1]
    }
    return $null
}

# ---------------------------------------------------------------------------
# Everything below talks to git and GitHub.
# ---------------------------------------------------------------------------

$script:PreviewOnly = $false
$script:Simulated = $null
$script:RepoRoot = Split-Path $PSScriptRoot -Parent
$script:WorkflowFile = 'stable-release.yml'
$script:AssetNames = @('Winland-win-x64.zip', 'version.json', 'SHA256SUMS')

function Write-Step([string]$Text) {
    Write-Host $Text
}

function Invoke-Native([string]$Tool, [string[]]$Arguments, [switch]$AllowFailure) {
    $errorFile = [System.IO.Path]::GetTempFileName()
    $savedNoColor = $env:NO_COLOR
    $savedForce = $env:GH_FORCE_TTY
    $savedCli = $env:CLICOLOR_FORCE
    $env:NO_COLOR = '1'
    Remove-Item Env:GH_FORCE_TTY -ErrorAction SilentlyContinue
    Remove-Item Env:CLICOLOR_FORCE -ErrorAction SilentlyContinue
    try {
        $lines = @(& $Tool @Arguments 2>$errorFile | ForEach-Object { "$_" })
        $code = $LASTEXITCODE
        $errorText = (Get-Content -Raw -LiteralPath $errorFile -ErrorAction SilentlyContinue)
        if ($null -eq $errorText) { $errorText = '' }
        if ($code -ne 0 -and -not $AllowFailure) {
            throw "$Tool $($Arguments -join ' ') failed ($code): $($errorText.Trim())"
        }
        $output = ($lines -join "`n").Trim()
        # gh paints JSON when it still believes stdout is a terminal. Drop those sequences.
        $output = [regex]::Replace($output, "`e\[[0-9;]*[A-Za-z]", '')
        return [pscustomobject]@{ ExitCode = $code; Output = $output; Error = $errorText.Trim() }
    } finally {
        if ($null -eq $savedNoColor) { Remove-Item Env:NO_COLOR -ErrorAction SilentlyContinue } else { $env:NO_COLOR = $savedNoColor }
        if ($null -eq $savedForce) { Remove-Item Env:GH_FORCE_TTY -ErrorAction SilentlyContinue } else { $env:GH_FORCE_TTY = $savedForce }
        if ($null -eq $savedCli) { Remove-Item Env:CLICOLOR_FORCE -ErrorAction SilentlyContinue } else { $env:CLICOLOR_FORCE = $savedCli }
        Remove-Item -LiteralPath $errorFile -ErrorAction SilentlyContinue
    }
}

function Invoke-Git([string[]]$Arguments, [switch]$AllowFailure) {
    return Invoke-Native 'git' (@('-C', $script:RepoRoot, '-c', 'color.ui=never') + $Arguments) -AllowFailure:$AllowFailure
}

function Invoke-Gh([string[]]$Arguments, [switch]$AllowFailure) {
    return Invoke-Native 'gh' $Arguments -AllowFailure:$AllowFailure
}

# Every change goes through here, so -DryRun can list it and skip it.
function Invoke-Mutation([string]$Description, [scriptblock]$Action) {
    if ($script:PreviewOnly) {
        Write-Step "DRY RUN would: $Description"
        return $null
    }
    Write-Step $Description
    return (& $Action)
}

function Write-TempText([string]$Text) {
    $path = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($path, $Text, (New-Object System.Text.UTF8Encoding $false))
    return $path
}

function Get-Context([string]$Remote) {
    $remotes = @((Invoke-Git @('remote')).Output -split "`n")
    if ($remotes -cnotcontains $Remote) { throw "The remote '$Remote' is not configured here (have: $($remotes -join ', '))." }
    $url = (Invoke-Git @('remote', 'get-url', '--push', $Remote)).Output
    $repo = ConvertFrom-RemoteUrl $url
    if (-not $repo) { throw "The remote '$Remote' is not a GitHub repository: $url" }
    $branch = (Invoke-Gh @('repo', 'view', $repo, '--json', 'defaultBranchRef', '--jq', '.defaultBranchRef.name')).Output
    if (-not $branch) { throw "Could not read the default branch of $repo." }
    return [pscustomobject]@{ Repo = $repo; Remote = $Remote; Branch = $branch }
}

function Update-DefaultBranch($Context) {
    Invoke-Git @('fetch', '--quiet', $Context.Remote, "+refs/heads/$($Context.Branch):refs/remotes/$($Context.Remote)/$($Context.Branch)") | Out-Null
}

function Get-RemoteTags($Context) {
    $listing = (Invoke-Git @('ls-remote', '--tags', $Context.Remote)).Output
    return ConvertFrom-LsRemoteTags ($listing -split "`n")
}

function Confirm-LocalCommit($Context, [string]$Sha) {
    if ((Invoke-Git @('cat-file', '-e', "$Sha^{commit}") -AllowFailure).ExitCode -ne 0) {
        Invoke-Git @('fetch', '--quiet', $Context.Remote, $Sha) | Out-Null
    }
}

function Test-Ancestor($Context, [string]$Ancestor, [string]$Descendant) {
    Confirm-LocalCommit $Context $Ancestor
    Confirm-LocalCommit $Context $Descendant
    return (Invoke-Git @('merge-base', '--is-ancestor', $Ancestor, $Descendant) -AllowFailure).ExitCode -eq 0
}

# The tags of the published releases: not a draft, not a prerelease.
function Get-PublishedTags($Context) {
    if ($null -ne $script:Simulated) {
        return @($script:Simulated)
    }
    $json = (Invoke-Gh @('release', 'list', '-R', $Context.Repo, '--limit', '1000', '--json', 'tagName,isDraft,isPrerelease')).Output
    $rows = @($json | ConvertFrom-Json)
    return @($rows | Where-Object { -not $_.isDraft -and -not $_.isPrerelease } | ForEach-Object { $_.tagName })
}

function Get-Releases($Context) {
    $json = (Invoke-Gh @('api', '--paginate', '--slurp', "repos/$($Context.Repo)/releases?per_page=100")).Output
    $pages = @($json | ConvertFrom-Json)
    return @($pages | ForEach-Object { $_ })
}

function Get-ReleaseById($Context, $Id) {
    return (Invoke-Gh @('api', "repos/$($Context.Repo)/releases/$Id")).Output | ConvertFrom-Json
}

# The draft or published release of an attempt: on the attempt tag until publish retargets it, then on the final tag.
function Find-AttemptRelease($Context, $Attempt) {
    $final = "v$($Attempt.Version)"
    $releases = @(Get-Releases $Context | Where-Object { $_.tag_name -ceq $Attempt.Ref -or $_.tag_name -ceq $final })
    $onFinal = @($releases | Where-Object { $_.tag_name -ceq $final })
    if ($onFinal.Count -gt 0) { return $onFinal[0] }
    if ($releases.Count -gt 0) { return $releases[0] }
    return $null
}

function Update-Release($Context, $Id, [hashtable]$Fields) {
    $path = Write-TempText ($Fields | ConvertTo-Json -Compress)
    try {
        Invoke-Gh @('api', '--method', 'PATCH', "repos/$($Context.Repo)/releases/$Id", '--input', $path) | Out-Null
    } finally {
        Remove-Item -LiteralPath $path -ErrorAction SilentlyContinue
    }
}

function Find-WorkflowRun($Context, [string]$Tag) {
    try {
        $json = (Invoke-Gh @('run', 'list', '-R', $Context.Repo, '--workflow', $script:WorkflowFile, '--limit', '50', '--json', 'databaseId,url,status,conclusion,displayTitle')).Output
        $runs = @($json | ConvertFrom-Json | Where-Object { $_.displayTitle -ceq "Stable release $Tag" })
        if ($runs.Count -gt 0) { return $runs[0] }
    } catch {
        # The workflow is not on the default branch yet, or the listing failed. No run to point at.
    }
    return $null
}

function Get-TagClaim($Context, $Tag) {
    if (-not $Tag.Annotated) { throw "The tag $($Tag.Name) is not annotated, so it carries no claim." }
    $message = (Invoke-Gh @('api', "repos/$($Context.Repo)/git/tags/$($Tag.Object)", '--jq', '.message')).Output
    $claim = ConvertFrom-ClaimJson $message
    if ($claim.Commit -ne $Tag.Commit) { throw "The claim in $($Tag.Name) names $($claim.Commit), but the tag points at $($Tag.Commit)." }
    $parsed = Get-AttemptRef $Tag.Name
    if ($parsed -and ($parsed.Version -ne $claim.Version -or $parsed.Attempt -ne $claim.Attempt)) {
        throw "The claim in $($Tag.Name) names version $($claim.Version) attempt $($claim.Attempt)."
    }
    return $claim
}

# Creates an annotated tag and pushes it atomically. A tag that exists on the remote is never moved, so two callers cannot both win.
function New-RemoteTag($Context, [string]$Name, [string]$Sha, [string]$Message) {
    $file = Write-TempText "$Message`n"
    try {
        Invoke-Git @('tag', '-a', '-f', $Name, $Sha, '-F', $file) | Out-Null
    } finally {
        Remove-Item -LiteralPath $file -ErrorAction SilentlyContinue
    }
    try {
        Invoke-Git @('push', '--atomic', $Context.Remote, "refs/tags/${Name}:refs/tags/${Name}") | Out-Null
    } catch {
        Invoke-Git @('tag', '-d', $Name) -AllowFailure | Out-Null
        throw "Could not push $Name. Someone else may have claimed it first. $($_.Exception.Message)"
    }
}

function Get-OutstandingDescription($Context, $Attempt, $Tags) {
    $tag = $Tags[$Attempt.Ref]
    $run = Find-WorkflowRun $Context $Attempt.Ref
    $lines = @("$($Attempt.Ref) (commit $($tag.Commit.Substring(0, 12)))")
    if ($run) { $lines += "  workflow run: $($run.url) ($($run.status)$(if ($run.conclusion) { ", $($run.conclusion)" }))" }
    return $lines -join "`n"
}

function Get-Published($Context, $Tags) {
    $newest = Get-NewestVersion (Get-PublishedTags $Context)
    if (-not $newest) { return $null }
    $tag = $Tags["v$newest"]
    if (-not $tag) { throw "v$newest is published, but its tag is not on $($Context.Remote)." }
    return [pscustomobject]@{ Version = $newest; Tag = "v$newest"; Commit = $tag.Commit }
}

function Invoke-Status($Context) {
    $tags = Get-RemoteTags $Context
    $published = Get-Published $Context $tags
    Write-Output "Repository: $($Context.Repo)"
    if ($published) {
        Write-Output "Newest published: $($published.Version) ($($published.Tag), commit $($published.Commit.Substring(0, 12)))"
    } else {
        Write-Output 'Newest published: none'
    }
    $outstanding = @(Get-OutstandingAttempts @($tags.Keys))
    if ($outstanding.Count -eq 0) {
        Write-Output 'Outstanding attempt: none'
    } else {
        foreach ($attempt in $outstanding) {
            Write-Output "Outstanding attempt: $(Get-OutstandingDescription $Context $attempt $tags)"
        }
    }
    $base = if ($published) { $published.Version } else { $null }
    foreach ($bump in 'patch', 'minor', 'major') {
        Write-Output ("Next version, -Bump {0}: {1}" -f $bump.PadRight(5), (Get-NextVersion $base $bump))
    }
    if (-not $published) {
        Write-Output 'Nothing is published, so the first release is cut with -Bump minor and is 0.1.0.'
    }
}

function Invoke-Release($Context) {
    Update-DefaultBranch $Context
    $tip = "$($Context.Remote)/$($Context.Branch)"
    if ($Commit) {
        $sha = (Invoke-Git @('rev-parse', '--verify', '--quiet', "$Commit^{commit}") -AllowFailure).Output
        if (-not $sha) { throw "$Commit is not a commit in this checkout. Fetch it first." }
    } else {
        $sha = (Invoke-Git @('rev-parse', '--verify', $tip)).Output
    }
    if (-not (Test-Ancestor $Context $sha $tip)) { throw "$sha is not on $tip. A release is cut from a commit that is on $($Context.Branch)." }

    $tags = Get-RemoteTags $Context
    $outstanding = @(Get-OutstandingAttempts @($tags.Keys))
    if ($outstanding.Count -gt 0) {
        $first = $outstanding[0]
        $message = @("An attempt is outstanding, so no new release can be cut:", (Get-OutstandingDescription $Context $first $tags))
        $message += "Wait for it, publish it with './scripts/release.ps1 publish -Version $($first.Version)', or clear it with:"
        $message += "  ./scripts/release.ps1 abandon $($first.Ref)"
        throw ($message -join "`n")
    }

    $published = Get-Published $Context $tags
    if ($published) {
        if (-not (Test-Ancestor $Context $published.Commit $sha)) {
            throw "$sha does not descend from $($published.Tag) ($($published.Commit))."
        }
        $base = $published.Version
    } else {
        $base = $null
    }
    $next = Get-NextVersion $base $Bump
    if ($tags.ContainsKey("v$next")) {
        throw "v$next already has a final tag. Finish it with './scripts/release.ps1 publish -Version $next'."
    }
    $attempt = Get-NextAttempt $next @($tags.Keys)
    $ref = New-AttemptRef $next $attempt
    $epoch = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $claim = ConvertTo-ClaimJson $sha $attempt $next ([bool]$Autopublish) $epoch

    Write-Step "Release $next (attempt $attempt) of $sha from $(if ($published) { "published $($published.Version)" } else { 'nothing published' }), -Bump $Bump."
    if (-not $published -and $Bump -ne 'minor') {
        Write-Step "Warning: nothing is published, so -Bump $Bump gives $next. The first release is 0.1.0, cut with -Bump minor."
    }
    Write-Step "Claim: $claim"

    Invoke-Mutation "push the annotated tag $ref on $sha to $($Context.Remote)" {
        New-RemoteTag $Context $ref $sha $claim
    } | Out-Null
    try {
        $body = Add-WarningBlocks "Winland $next, built from $sha."
        $bodyFile = Write-TempText $body
        try {
            Invoke-Mutation "create the draft release 'Winland $next' on $ref" {
                Invoke-Gh @('release', 'create', $ref, '--repo', $Context.Repo, '--draft', '--verify-tag', '--title', "Winland $next", '--notes-file', $bodyFile) | Out-Null
            } | Out-Null
        } finally {
            Remove-Item -LiteralPath $bodyFile -ErrorAction SilentlyContinue
        }
        Invoke-Mutation "dispatch $script:WorkflowFile from $($Context.Branch) with tag=$ref" {
            Invoke-Gh @('workflow', 'run', $script:WorkflowFile, '--repo', $Context.Repo, '--ref', $Context.Branch, '-f', "tag=$ref") | Out-Null
        } | Out-Null
    } catch {
        Write-Step "The attempt $ref is claimed but not finished: $($_.Exception.Message)"
        Write-Step "Clear it with './scripts/release.ps1 abandon $ref', fix the cause, and cut again."
        throw
    }
    if (-not $script:PreviewOnly) {
        Write-Step "Started $ref. Watch it: gh run list -R $($Context.Repo) --workflow $script:WorkflowFile"
        if (-not $Autopublish) {
            Write-Step "When the run is green, publish with './scripts/release.ps1 publish -Version $next'. Do not press Publish in the GitHub UI."
        }
    }
}

function Invoke-Publish($Context) {
    $tags = Get-RemoteTags $Context
    $target = $Version
    if ($target) {
        if (-not (Get-StableVersion "v$target")) { throw "'$target' is not a version. Use X.Y.Z." }
    } else {
        $outstanding = @(Get-OutstandingAttempts @($tags.Keys))
        if ($outstanding.Count -eq 0) { throw 'No attempt is outstanding. Pass -Version X.Y.Z to finish a release.' }
        $target = $outstanding[0].Version
    }

    # The newest attempt for the version that nobody abandoned.
    $cleared = @{}
    foreach ($name in $tags.Keys) {
        $marker = Get-MarkerRef $name
        if ($marker -and $marker.Version -eq $target) { $cleared[$marker.Attempt] = $true }
    }
    $live = @($tags.Keys | ForEach-Object { Get-AttemptRef $_ } | Where-Object { $_ -and $_.Version -eq $target -and -not $cleared.ContainsKey($_.Attempt) } | Sort-Object Attempt)
    if ($live.Count -eq 0) { throw "No live attempt exists for $target." }
    $attempt = $live[-1]
    $claim = Get-TagClaim $Context $tags[$attempt.Ref]
    $final = "v$target"

    $release = Find-AttemptRelease $Context $attempt
    if (-not $release) { throw "No release exists on $($attempt.Ref) or $final. Abandon the attempt and cut again." }
    if (-not $release.draft -and $release.tag_name -ceq $final) {
        Write-Step "$final is already published: $($release.html_url)"
        if (Test-WarningBlocks $release.body) {
            Invoke-Mutation "strip the warning blocks from the body of $final" {
                Update-Release $Context $release.id @{ body = (Remove-WarningBlocks $release.body) }
            } | Out-Null
        }
        return
    }
    if (-not $release.draft) { throw "The release on $($release.tag_name) is published but not on $final. Fix it by hand." }
    if ($release.prerelease) { throw "The draft on $($release.tag_name) is marked prerelease. A stable release is not." }

    $present = @($release.assets | Where-Object { $_.state -ceq 'uploaded' } | ForEach-Object { $_.name })
    $missing = @($script:AssetNames | Where-Object { $present -cnotcontains $_ })
    if ($missing.Count -gt 0) {
        throw "The draft lacks $($missing -join ', '). The Stable Release workflow uploads them when its build is green."
    }

    # Each step reads the current state first, so a rerun after a partial failure repeats only what is left.
    if ($tags.ContainsKey($final)) {
        if ($tags[$final].Commit -ne $claim.Commit) {
            throw "$final points at $($tags[$final].Commit), but the claim of $($attempt.Ref) names $($claim.Commit)."
        }
        Write-Step "$final already points at $($claim.Commit)."
    } else {
        $receipt = ConvertTo-ClaimJson $claim.Commit $claim.Attempt $claim.Version $claim.Autopublish $claim.Epoch
        Invoke-Mutation "push the annotated receipt tag $final on $($claim.Commit)" {
            New-RemoteTag $Context $final $claim.Commit $receipt
        } | Out-Null
    }

    if ($release.tag_name -cne $final) {
        Invoke-Mutation "point the draft at $final" {
            Update-Release $Context $release.id @{ tag_name = $final }
        } | Out-Null
    }
    if (Test-WarningBlocks $release.body) {
        Invoke-Mutation 'strip the warning blocks from the draft body' {
            Update-Release $Context $release.id @{ body = (Remove-WarningBlocks $release.body) }
        } | Out-Null
    }
    if (-not $script:PreviewOnly) {
        $draft = Get-ReleaseById $Context $release.id
        if ($draft.tag_name -cne $final) { throw "The draft is on $($draft.tag_name), not $final." }
        if (Test-WarningBlocks $draft.body) { throw 'The draft body still has the warning blocks.' }
    }
    # Making the release public is the last call.
    Invoke-Mutation "publish $final" {
        Update-Release $Context $release.id @{ draft = $false; prerelease = $false }
    } | Out-Null
    if (-not $script:PreviewOnly) {
        $done = Get-ReleaseById $Context $release.id
        if ($done.draft -or $done.tag_name -cne $final) { throw "$final is not published after the update." }
        Write-Step "Published ${final}: $($done.html_url)"
    }
}

function Invoke-Abandon($Context) {
    $attempt = Get-AttemptRef $AttemptTag
    if (-not $attempt) { throw "'$AttemptTag' is not an attempt tag. Use rc.<N>-vX.Y.Z." }
    $tags = Get-RemoteTags $Context
    if (-not $tags.ContainsKey($attempt.Ref)) { throw "$($attempt.Ref) does not exist on $($Context.Remote)." }
    $final = "v$($attempt.Version)"
    if ($tags.ContainsKey($final)) { throw "$final exists, so $($attempt.Ref) is not outstanding. A published release cannot be abandoned." }
    $marker = New-MarkerRef $attempt.Version $attempt.Attempt
    $commit = $tags[$attempt.Ref].Commit

    $run = Find-WorkflowRun $Context $attempt.Ref
    if ($run -and $run.status -cin 'in_progress', 'queued', 'waiting', 'pending') {
        Invoke-Mutation "cancel the workflow run $($run.url)" {
            Invoke-Gh @('run', 'cancel', "$($run.databaseId)", '-R', $Context.Repo) | Out-Null
        } | Out-Null
    }
    if ($tags.ContainsKey($marker)) {
        Write-Step "$marker already exists."
    } else {
        $message = ([ordered]@{ abandoned = $attempt.Ref; commit = $commit; epoch = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() } | ConvertTo-Json -Compress)
        Invoke-Mutation "push the annotated marker $marker on $commit" {
            New-RemoteTag $Context $marker $commit $message
        } | Out-Null
    }
    $release = Find-AttemptRelease $Context $attempt
    if ($release -and -not $release.draft) { throw "The release on $($release.tag_name) is published. Fix it by hand." }
    if ($release) {
        Invoke-Mutation "delete the draft release on $($release.tag_name)" {
            Invoke-Gh @('api', '--method', 'DELETE', "repos/$($Context.Repo)/releases/$($release.id)") | Out-Null
        } | Out-Null
    } else {
        Write-Step "No draft release exists on $($attempt.Ref)."
    }
    Write-Step "$($attempt.Ref) is abandoned. The next release of $($attempt.Version) is attempt $(Get-NextAttempt $attempt.Version @($tags.Keys) )."
}

# Dot-sourcing stops here: the functions above are defined and nothing runs.
if ($MyInvocation.InvocationName -eq '.') { return }

# A hook exports GIT_DIR. Clear it so git commands in this script use the checkout.
foreach ($name in @('GIT_DIR', 'GIT_WORK_TREE', 'GIT_INDEX_FILE', 'GIT_PREFIX')) {
    Remove-Item "Env:$name" -ErrorAction SilentlyContinue
}

if (-not $Command) {
    throw 'Usage: release.ps1 release|publish|abandon|status. See the header of this file and docs/releasing.md.'
}
$script:PreviewOnly = [bool]$DryRun
$script:Simulated = $null
if ($PSBoundParameters.ContainsKey('SimulatePublished')) {
    if ($Command -ne 'status' -and -not $DryRun) { throw '-SimulatePublished works only with status or -DryRun.' }
    $script:Simulated = @($SimulatePublished -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ -and $_ -cne 'none' })
}
if ($Command -ne 'abandon' -and $AttemptTag) { throw "$Command takes no attempt tag." }
if ($Command -eq 'abandon' -and -not $AttemptTag) { throw 'abandon needs an attempt tag: release.ps1 abandon rc.<N>-vX.Y.Z' }

$context = Get-Context $Remote
switch ($Command) {
    'status' { Invoke-Status $context }
    'release' { Invoke-Release $context }
    'publish' { Invoke-Publish $context }
    'abandon' { Invoke-Abandon $context }
}
