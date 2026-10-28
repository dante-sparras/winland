# Development

## Prerequisites

- Windows 11
- PowerShell 7 (`pwsh`)
- Git
- The .NET SDK version pinned in `global.json` (10.0.401, or a later feature band)

Optional: GitHub CLI (`gh`) for issues and releases, and Node.js for `npx skills` updates.

## First checkout

```sh
git clone https://github.com/dante-sparras/winland.git
cd winland
pwsh scripts/dev.ps1
dotnet tool restore
```

`scripts/dev.ps1` points git at the hooks in `scripts/hooks` (see [Git hooks](#git-hooks)). `dotnet tool restore` installs Stryker.NET for mutation testing.

## Run a dev build

`pwsh -NoProfile -File scripts/run.ps1` publishes this checkout, asks the running Winland to exit cleanly, and starts the new build in its place. A failed publish leaves the running copy alone. Use this script for starts and stops. A running Winland takes over windows on your desktop, including your own.

For an isolated test, give it its own pipe and state file, and a config that ignores every window except the test windows you create. This example manages only windows of class `WinlandDevWindow` titled `WinlandDev 1` or `WinlandDev 2`. Adjust those matches to your test windows. Patterns match the whole string, so `^foo` matches only `foo`.

```powershell
$devId = [guid]::NewGuid().ToString('n')
$env:WINLAND_PIPE = "winland-dev-$devId"
$env:WINLAND_STATE = Join-Path $env:TEMP "winland-dev-$devId.json"
$devConfig = Join-Path $env:TEMP "winland-dev-$devId.conf"
@'
animations:enabled = false
windowrule = ignore, class:.*
windowrule = tile, class:^(WinlandDevWindow)$, title:^(WinlandDev [12])$
windowrule = workspace 1, class:^(WinlandDevWindow)$, title:^(WinlandDev [12])$
'@ | Set-Content -LiteralPath $devConfig
pwsh -NoProfile -File scripts/run.ps1 -Config $devConfig
```

With `WINLAND_PIPE` set, the script replaces only dev builds recorded for that pipe. It leaves older dev builds without a pipe record alone. For placement tests, exit your installed Winland from its tray first so it does not manage the test windows too.

`-Config <file>` reads that `.conf` file instead of `%USERPROFILE%\.winland\winland.conf`. The build starts without administrator rights and cannot place elevated programs' windows. `-Elevated` asks for UAC approval.

Each build has its own folder under `%LOCALAPPDATA%\winland-dev`, with `winland.out.log` and `winland.err.log`. Piping the script's output is safe. Stop the build from the same shell, keeping the same `WINLAND_PIPE`:

```powershell
pwsh -NoProfile -File scripts/run.ps1 -Stop
Remove-Item Env:WINLAND_PIPE, Env:WINLAND_STATE
```

A clean stop shows the taskbar again if `general:hide_taskbar` hid it. If a forced stop left it hidden, run the script with the same pipe, state and config, then use `-Stop`. After stopping the dev build, start your installed Winland again from the Start menu.

## Environment

| Variable | Purpose |
| --- | --- |
| `WINLAND_PIPE` | Override the daemon and CLI pipe name. Give isolated tests a unique name. Unset or blank uses the session's default. |
| `WINLAND_STATE` | Override the recovery state file path. Use a separate file for each test daemon. |
| `WINLAND_NO_ELEVATE` | `1` opts out of automatic elevation, as `--no-elevate` does. Intended for tests. |
| `DOTNET_ROOT` | The .NET runtime root for apphosts and Stryker when .NET is outside its usual location. `run.ps1` resolves it automatically. |

## Commands

| Command | What it does |
| --- | --- |
| `pwsh scripts/dev.ps1` | Point git at the hooks in `scripts/hooks`. Once per clone. |
| `pwsh -NoProfile -File scripts/check.ps1` | The lint: parses the PowerShell, rejects names that differ only by case, checks markdown links and the paths `AGENTS.md` names, and builds the solution. `-SkipBuild` checks docs without a build, as docs-only PRs do in CI. |
| `pwsh -NoProfile -File scripts/test.ps1` | Runs every unit test project side by side, as CI does. |
| `dotnet test tests/<project>` | The focused test for one project. |
| `dotnet format Winland.slnx` | Apply the solution's formatting rules. Add `--verify-no-changes` to check without editing. |
| `pwsh -NoProfile -File scripts/smoke.ps1` | The smoke tests, on a real desktop. |
| `pwsh -NoProfile -File scripts/mutate.ps1` | Mutation testing (see [Mutation testing](#mutation-testing)). |
| `pwsh -NoProfile -File scripts/install.tests.ps1` | Tests the decisions in `scripts/install.ps1`. |
| `pwsh -NoProfile -File scripts/run.ps1` | Publishes this checkout and starts it in place of the running winland. `-Stop` stops it. |
| `pwsh -NoProfile -File scripts/package.ps1 -Version 1.2.3 -Commit (git rev-parse HEAD).Trim()` | Build the self-contained release zip, `version.json` and `SHA256SUMS` without starting Winland. Prints the output folder. `-DisplayVersion` includes dev metadata and `-OutputDirectory` chooses a new output folder. |
| `npx -y skills@1.7.4 update -p` | Update the unpinned agent skills. Pinned ones (with a `ref` in `skills-lock.json`) change only when re-added with a newer `#<sha>`. |

Before opening a PR, run the checks CI runs: `pwsh -NoProfile -File scripts/check.ps1`, then `pwsh -NoProfile -File scripts/test.ps1`.

Warnings fail the build. `Directory.Build.props` enables `TreatWarningsAsErrors`, recommended analyzers with `AnalysisMode`, and the configured code style warnings with `EnforceCodeStyleInBuild`.

## Git hooks

The hooks are tracked in `scripts/hooks`, and git runs them from there. `pwsh scripts/dev.ps1` sets `core.hooksPath` to `scripts/hooks` once per clone. A checkout that still points `core.hooksPath` at `.git/hooks` is fixed by running it again.

| Hook | When it runs | What it does |
| --- | --- | --- |
| pre-push | When you push | Runs the lint, a cold build and the full unit test suite on each branch tip in a temporary worktree. Some tests open windows and take focus. A failed lint skips the tests. Tags and branch deletions are skipped. Without `pwsh` on `PATH` it says so and lets the push through. `git push --no-verify` skips it. |
| post-merge | After a pull or merge updates `main` | Deletes a local branch whose GitHub branch is gone and whose changes are already on `main` or a `release/*` branch. It never deletes `main`, `master`, `release/*`, or a branch checked out in a worktree. |

## CI

Pull requests run `.github/workflows/ci.yml`, and so does the push a merge makes to `main`. The push run also fills the NuGet cache, which a pull request can read only when its own branch or the default branch saved it. A newer run of the same pull request, or of `main`, cancels the older one. Release runs have a separate concurrency group. The ruleset requires **All required checks pass (v1)** and **pr-title**. To change what the combined CI check covers, change `ci.yml` and `.github/rulesets/protect-main.json` in the same pull request, and bump the `(v1)`.

Every lane except Mutation is a step in one Windows job, because each job pays its own setup.

| Lane | What it does | When it runs |
| --- | --- | --- |
| History | Fails when the branch shares no commit with `main`. | Always. |
| Lint | `scripts/check.ps1`, then `actionlint` on `.github/workflows/*.yml`. Docs-only PRs skip the build. | Always. |
| Test | `scripts/test.ps1 -NoBuild` on the lint's build: every `*.Tests.csproj` under `tests/` except `*.Smoke.Tests.csproj`. | Always, except a pull request that changes only docs and repo metadata (`*.md`, `docs/`, `.github/ISSUE_TEMPLATE/`, `.github/pull_request_template.md`, `.github/rulesets/`, `.gitignore`, `.gitattributes`, `LICENSE*`). |
| Mutation | One job per mutation run, then a score gate. | Before publishing a stable release, or when CI is dispatched by hand with **Release run** on. |

- A test project that fails runs once more on the same build. If it passes the second time, `scripts/test.ps1` names the flaky tests in a `FLAKY:` line, and CI adds a warning and a job summary line. A flaky test is a bug. Fix it in its own pull request, usually by waiting for a condition instead of a fixed time.
- The unit tests that open real windows are in one xunit collection (`DesktopCollection` in `tests/Winland.Infrastructure.Tests`) that runs alone. The ones a running Winland would disturb skip while any `winland` process runs, including builds on custom pipes. Run them with the daemon stopped.
- Smoke tests run only when someone dispatches the `Smoke tests` workflow by hand. `scripts/smoke.ps1` runs them locally. It sets `WINLAND_SMOKE=1`, without which every smoke test skips itself.
- The Workflows workflow runs zizmor when `.github/` changes. CI also runs actionlint. PR Title checks the pull request title, and Branch Name checks the branch name as a reminder only.

### Mutation testing

`scripts/mutate.ps1` runs [Stryker.NET](https://stryker-mutator.io/docs/stryker-net/introduction/), the version in `.config/dotnet-tools.json`. It has one lane per unit test project, and each lane mutates the project that test project tests. A long lane is split into shards, so every CI job fits its timeout. A lane or one shard of it is a mutation run.

```powershell
pwsh -NoProfile -File scripts/mutate.ps1                  # every run, then the gate
pwsh -NoProfile -File scripts/mutate.ps1 -Project Daemon  # one lane, then the gate
pwsh -NoProfile -File scripts/mutate.ps1 -List            # the run names CI makes jobs from
pwsh -NoProfile -File scripts/mutate.ps1 -Run Domain-2    # one run and no gate, as a CI job runs it
pwsh -NoProfile -File scripts/mutate.ps1 -Gate <folder>   # gate the run summaries under a folder
```

- The gate is `thresholds.break` in `stryker-config.json`, applied to each lane's whole score across its shards.
- Each run writes `tests/<project>/StrykerOutput/<run>/reports/mutation-report.html`, which CI uploads as `mutation-report-<run>`.
- A file that drives Win32 or the shell directly, with no seam a unit test can reach, is left out in the lane's `Exclude` list in `scripts/mutate.ps1`, with the reason. A Win32-only member in a file that also holds tested logic gets a `// Stryker disable once all : <reason>` comment.
- The Windows lane leaves out the `Desktop` collection (`Category=Desktop`), because windows opened by parallel test processes fight over the foreground.
- Stryker's test host needs `DOTNET_ROOT` when the SDK is not where .NET looks by default, for example under mise: `$env:DOTNET_ROOT = "$env:LOCALAPPDATA\mise\dotnet-root"`.
- A new unit test project fails `scripts/mutate.ps1` until it has a lane.

## Releasing

release-please keeps a release PR open that collects the changes merged into `main`, with the next version and its changelog. Merging that PR tags the release. CI tests the tag, including the mutation score gate, before a job builds and uploads `Winland-win-x64.zip`, `version.json`, and `SHA256SUMS` and publishes it. See [releasing.md](releasing.md).

- **Pick the version yourself:** when merging a PR, add `Release-As: x.y.z` to the end of the squash commit message.
- **The release PR's checks don't start:** without the `RELEASE_PLEASE_TOKEN` secret, close and reopen the release PR right before merging it.
