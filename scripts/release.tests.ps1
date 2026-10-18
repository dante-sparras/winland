# Tests for the pure functions in release.ps1. Plain if/throw, so nothing needs installing.
#   pwsh -NoProfile -File scripts/release.tests.ps1
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release.ps1')

$script:failures = New-Object System.Collections.Generic.List[string]
$script:passed = 0

function Test-Case([string]$Name, [scriptblock]$Body) {
    try {
        & $Body
        $script:passed++
    } catch {
        $script:failures.Add("$Name`: $($_.Exception.Message)")
    }
}

function Assert-Equal($Actual, $Expected) {
    if ($Actual -cne $Expected) { throw "expected '$Expected', got '$Actual'" }
}

function Assert-Null($Actual) {
    if ($null -ne $Actual) { throw "expected null, got '$Actual'" }
}

function Assert-Throws([scriptblock]$Body) {
    $threw = $false
    try { & $Body } catch { $threw = $true }
    if (-not $threw) { throw 'expected an error, got none' }
}

$sha = 'a' * 40

Test-Case 'next version, patch' { Assert-Equal (Get-NextVersion '0.1.2' 'patch') '0.1.3' }
Test-Case 'next version, minor resets patch' { Assert-Equal (Get-NextVersion '0.1.2' 'minor') '0.2.0' }
Test-Case 'next version, major resets minor and patch' { Assert-Equal (Get-NextVersion '1.4.9' 'major') '2.0.0' }
Test-Case 'next version does not sort as text' { Assert-Equal (Get-NextVersion '0.9.9' 'patch') '0.9.10' }
Test-Case 'first release with -Bump minor is 0.1.0' { Assert-Equal (Get-NextVersion $null 'minor') '0.1.0' }
Test-Case 'first release with -Bump patch is 0.0.1' { Assert-Equal (Get-NextVersion '' 'patch') '0.0.1' }
Test-Case 'first release with -Bump major is 1.0.0' { Assert-Equal (Get-NextVersion $null 'major') '1.0.0' }
Test-Case 'unknown bump is rejected' { Assert-Throws { Get-NextVersion '1.0.0' 'huge' } }

Test-Case 'newest version compares numbers, not text' {
    Assert-Equal (Get-NewestVersion @('v0.9.0', 'v0.10.0', 'v0.2.0')) '0.10.0'
}
Test-Case 'newest version ignores other tags' {
    Assert-Equal (Get-NewestVersion @('rc.1-v9.9.9', 'abandoned-rc.1-v9.9.9', 'v1.2.3-beta', 'v1.2', 'v0.1.2')) '0.1.2'
}
Test-Case 'newest version of nothing is null' { Assert-Null (Get-NewestVersion @()) }

Test-Case 'first attempt is 1' { Assert-Equal (Get-NextAttempt '0.1.0' @()) 1 }
Test-Case 'attempt is one past the highest' {
    Assert-Equal (Get-NextAttempt '0.1.0' @('rc.1-v0.1.0', 'rc.3-v0.1.0', 'rc.2-v0.1.0')) 4
}
Test-Case 'an abandoned attempt keeps its number' {
    Assert-Equal (Get-NextAttempt '0.1.0' @('rc.1-v0.1.0', 'abandoned-rc.1-v0.1.0')) 2
}
Test-Case 'attempts of another version do not count' {
    Assert-Equal (Get-NextAttempt '0.1.1' @('rc.5-v0.1.0', 'rc.2-v0.2.0')) 1
}

Test-Case 'no tags, nothing outstanding' { Assert-Equal @(Get-OutstandingAttempts @()).Count 0 }
Test-Case 'an attempt alone is outstanding' {
    $found = @(Get-OutstandingAttempts @('v0.1.0', 'rc.1-v0.1.1'))
    Assert-Equal $found.Count 1
    Assert-Equal $found[0].Ref 'rc.1-v0.1.1'
    Assert-Equal $found[0].Version '0.1.1'
    Assert-Equal $found[0].Attempt 1
}
Test-Case 'a final tag clears every attempt of its version' {
    Assert-Equal @(Get-OutstandingAttempts @('rc.1-v0.1.0', 'rc.2-v0.1.0', 'v0.1.0')).Count 0
}
Test-Case 'a marker clears only its attempt' {
    $found = @(Get-OutstandingAttempts @('rc.1-v0.1.0', 'abandoned-rc.1-v0.1.0', 'rc.2-v0.1.0'))
    Assert-Equal $found.Count 1
    Assert-Equal $found[0].Ref 'rc.2-v0.1.0'
}
Test-Case 'an attempt of an older version is still outstanding' {
    $found = @(Get-OutstandingAttempts @('v0.2.0', 'rc.1-v0.1.0'))
    Assert-Equal $found.Count 1
}
Test-Case 'a malformed attempt is not outstanding' {
    Assert-Equal @(Get-OutstandingAttempts @('rc.1-v1.2.3-beta', 'RC.1-v1.2.3', 'rc.0-v1.2.3', 'rc.1-v1.2')).Count 0
}

Test-Case 'claim round trip' {
    $text = ConvertTo-ClaimJson $sha 2 '0.1.0' $true 1790000000
    Assert-Equal $text ('{"commit":"' + $sha + '","attempt":2,"version":"0.1.0","autopublish":true,"epoch":1790000000}')
    $claim = ConvertFrom-ClaimJson $text
    Assert-Equal $claim.Commit $sha
    Assert-Equal $claim.Attempt 2
    Assert-Equal $claim.Version '0.1.0'
    Assert-Equal $claim.Autopublish $true
    Assert-Equal $claim.Epoch 1790000000
}
Test-Case 'claim round trip, autopublish false' {
    $claim = ConvertFrom-ClaimJson (ConvertTo-ClaimJson $sha 1 '1.0.0' $false 0)
    Assert-Equal $claim.Autopublish $false
}
Test-Case 'claim parse ignores a signature after the message' {
    $text = (ConvertTo-ClaimJson $sha 1 '0.1.0' $false 5) + "`n-----BEGIN SSH SIGNATURE-----`nabc`n-----END SSH SIGNATURE-----"
    Assert-Equal (ConvertFrom-ClaimJson $text).Version '0.1.0'
}
Test-Case 'claim parse rejects text' { Assert-Throws { ConvertFrom-ClaimJson 'release 0.1.0' } }
Test-Case 'claim parse rejects a missing field' {
    Assert-Throws { ConvertFrom-ClaimJson ('{"commit":"' + $sha + '","attempt":1,"version":"0.1.0","epoch":1}') }
}
Test-Case 'claim parse rejects a short commit' {
    Assert-Throws { ConvertFrom-ClaimJson '{"commit":"abc","attempt":1,"version":"0.1.0","autopublish":false,"epoch":1}' }
}
Test-Case 'claim parse rejects attempt 0' {
    Assert-Throws { ConvertFrom-ClaimJson ('{"commit":"' + $sha + '","attempt":0,"version":"0.1.0","autopublish":false,"epoch":1}') }
}
Test-Case 'claim parse rejects a text autopublish' {
    Assert-Throws { ConvertFrom-ClaimJson ('{"commit":"' + $sha + '","attempt":1,"version":"0.1.0","autopublish":"yes","epoch":1}') }
}
Test-Case 'claim parse rejects a bad version' {
    Assert-Throws { ConvertFrom-ClaimJson ('{"commit":"' + $sha + '","attempt":1,"version":"0.1","autopublish":false,"epoch":1}') }
}

Test-Case 'final tag v1.2.3 parses' { Assert-Equal (Get-StableVersion 'v1.2.3') '1.2.3' }
Test-Case 'final tag v0.0.0 parses' { Assert-Equal (Get-StableVersion 'v0.0.0') '0.0.0' }
Test-Case 'final tag v1.2 is rejected' { Assert-Null (Get-StableVersion 'v1.2') }
Test-Case 'final tag V1.2.3 is rejected' { Assert-Null (Get-StableVersion 'V1.2.3') }
Test-Case 'final tag 1.2.3 is rejected' { Assert-Null (Get-StableVersion '1.2.3') }
Test-Case 'final tag v1.2.3-beta is rejected' { Assert-Null (Get-StableVersion 'v1.2.3-beta') }
Test-Case 'final tag v01.2.3 is rejected' { Assert-Null (Get-StableVersion 'v01.2.3') }
Test-Case 'final tag v1.2.3+meta is rejected' { Assert-Null (Get-StableVersion 'v1.2.3+meta') }
Test-Case 'attempt rc.1-v1.2.3 parses' {
    $parsed = Get-AttemptRef 'rc.1-v1.2.3'
    Assert-Equal $parsed.Version '1.2.3'
    Assert-Equal $parsed.Attempt 1
}
Test-Case 'attempt rc.1-v1.2 is rejected' { Assert-Null (Get-AttemptRef 'rc.1-v1.2') }
Test-Case 'attempt rc.1-v1.2.3-beta is rejected' { Assert-Null (Get-AttemptRef 'rc.1-v1.2.3-beta') }
Test-Case 'attempt RC.1-v1.2.3 is rejected' { Assert-Null (Get-AttemptRef 'RC.1-v1.2.3') }
Test-Case 'attempt v1.2.3-rc.1 is rejected' { Assert-Null (Get-AttemptRef 'v1.2.3-rc.1') }
Test-Case 'attempt rc.0-v1.2.3 is rejected' { Assert-Null (Get-AttemptRef 'rc.0-v1.2.3') }
Test-Case 'attempt rc.01-v1.2.3 is rejected' { Assert-Null (Get-AttemptRef 'rc.01-v1.2.3') }
Test-Case 'marker abandoned-rc.2-v1.2.3 parses' {
    $parsed = Get-MarkerRef 'abandoned-rc.2-v1.2.3'
    Assert-Equal $parsed.Version '1.2.3'
    Assert-Equal $parsed.Attempt 2
}
Test-Case 'marker without the prefix is rejected' { Assert-Null (Get-MarkerRef 'rc.2-v1.2.3') }
Test-Case 'marker ABANDONED-rc.2-v1.2.3 is rejected' { Assert-Null (Get-MarkerRef 'ABANDONED-rc.2-v1.2.3') }
Test-Case 'a marker is not an attempt' { Assert-Null (Get-AttemptRef 'abandoned-rc.2-v1.2.3') }
Test-Case 'attempt and marker names round trip' {
    Assert-Equal (New-AttemptRef '1.2.3' 4) 'rc.4-v1.2.3'
    Assert-Equal (New-MarkerRef '1.2.3' 4) 'abandoned-rc.4-v1.2.3'
}
Test-Case 'attempt name of a bad version is rejected' { Assert-Throws { New-AttemptRef '1.2' 1 } }

Test-Case 'warning blocks go on and come off' {
    $body = Add-WarningBlocks 'Winland 0.1.0, built from abc.'
    if (-not (Test-WarningBlocks $body)) { throw 'no blocks' }
    if (([regex]::Matches($body, 'Do not publish this draft')).Count -ne 2) { throw 'expected one block at the top and one at the bottom' }
    if (-not $body.StartsWith($script:WarningBegin)) { throw 'the first block is not at the top' }
    if (-not $body.TrimEnd().EndsWith($script:WarningEnd)) { throw 'the second block is not at the bottom' }
    $clean = Remove-WarningBlocks $body
    if (Test-WarningBlocks $clean) { throw 'blocks left' }
    Assert-Equal $clean "Winland 0.1.0, built from abc.`n"
}
Test-Case 'removing blocks from a clean body changes nothing' {
    Assert-Equal (Remove-WarningBlocks "Winland 0.1.0.`n") "Winland 0.1.0.`n"
}
Test-Case 'warning blocks wrap release notes and come off' {
    $notes = "First stable release v0.1.0.`n`n* keep the name"
    $body = Add-WarningBlocks $notes
    if (([regex]::Matches($body, 'scripts/release.ps1 publish')).Count -ne 2) { throw 'expected the publish line in both blocks' }
    if ($body.Contains('autopublish release')) { throw 'the warning still says the workflow publishes' }
    Assert-Equal (Remove-WarningBlocks $body) "$notes`n"
}
Test-Case 'notes request uses the previous stable tag' {
    $request = Get-ReleaseNotesRequest '0.2.0' $sha 'v0.1.0'
    Assert-Equal $request.tag_name 'v0.2.0'
    Assert-Equal $request.target_commitish $sha
    Assert-Equal $request.previous_tag_name 'v0.1.0'
}
Test-Case 'notes request for the first release names no previous tag' {
    $request = Get-ReleaseNotesRequest '0.1.0' $sha ''
    Assert-Equal $request.tag_name 'v0.1.0'
    Assert-Equal $request.target_commitish $sha
    if ($request.Contains('previous_tag_name')) { throw 'the first release named a previous tag' }
}
Test-Case 'notes request rejects an attempt tag as the previous release' {
    Assert-Throws { Get-ReleaseNotesRequest '0.2.0' $sha 'rc.1-v0.1.0' }
}
Test-Case 'notes request rejects an abandoned attempt as the previous release' {
    Assert-Throws { Get-ReleaseNotesRequest '0.2.0' $sha 'abandoned-rc.1-v0.1.0' }
}
Test-Case 'notes request rejects a bad version' {
    Assert-Throws { Get-ReleaseNotesRequest '0.1' $sha '' }
}

Test-Case 'ls-remote lists annotated and lightweight tags' {
    $b = 'b' * 40
    $c = 'c' * 40
    $tags = ConvertFrom-LsRemoteTags @("$sha`trefs/tags/rc.1-v0.1.0", "$b`trefs/tags/rc.1-v0.1.0^{}", "$c`trefs/tags/v0.0.1")
    Assert-Equal $tags.Count 2
    Assert-Equal $tags['rc.1-v0.1.0'].Object $sha
    Assert-Equal $tags['rc.1-v0.1.0'].Commit $b
    Assert-Equal $tags['rc.1-v0.1.0'].Annotated $true
    Assert-Equal $tags['v0.0.1'].Commit $c
    Assert-Equal $tags['v0.0.1'].Annotated $false
}
Test-Case 'ls-remote lists the peeled commit first too' {
    $b = 'b' * 40
    $tags = ConvertFrom-LsRemoteTags @("$b`trefs/tags/v1.0.0^{}", "$sha`trefs/tags/v1.0.0")
    Assert-Equal $tags['v1.0.0'].Object $sha
    Assert-Equal $tags['v1.0.0'].Commit $b
}

Test-Case 'remote url, https' { Assert-Equal (ConvertFrom-RemoteUrl 'https://github.com/dante-sparras/winland.git') 'dante-sparras/winland' }
Test-Case 'remote url, ssh' { Assert-Equal (ConvertFrom-RemoteUrl 'git@github.com:dante-sparras/winland.git') 'dante-sparras/winland' }
Test-Case 'remote url, not github' { Assert-Null (ConvertFrom-RemoteUrl 'https://example.com/a/b.git') }

# The functions below stand in for the remote. They are defined in the test's own scope, which
# Invoke-Abandon sees when it runs, so nothing outside the test changes.
Test-Case 'abandon refuses a published release before it changes anything' {
    $AttemptTag = 'rc.1-v0.1.0'
    $mutations = New-Object System.Collections.Generic.List[string]
    function Get-RemoteTags($Context) { @{ 'rc.1-v0.1.0' = [pscustomobject]@{ Commit = $sha } } }
    function Find-WorkflowRun($Context, $Ref) { [pscustomobject]@{ status = 'in_progress'; databaseId = 1; url = 'run' } }
    function Find-AttemptRelease($Context, $Attempt) { [pscustomobject]@{ draft = $false; tag_name = 'rc.1-v0.1.0'; id = 1 } }
    function Invoke-Mutation([string]$Description, [scriptblock]$Action) { $mutations.Add($Description) }
    function Write-Step($Text) { }

    Assert-Throws { Invoke-Abandon @{ Remote = 'origin'; Repo = 'dante-sparras/winland' } }
    Assert-Equal $mutations.Count 0
}

if ($script:failures.Count -gt 0) {
    foreach ($failure in $script:failures) { Write-Output "FAIL $failure" }
    Write-Output "$($script:failures.Count) failed, $($script:passed) passed"
    exit 1
}
Write-Output "$($script:passed) release tests passed"
