# Development

## Prerequisites

- Windows 11
- PowerShell 7 (`pwsh`)
- Git
- The .NET SDK version pinned in `global.json` (10.0.401, or a later feature band)

## First checkout

```sh
git clone https://github.com/dante-sparras/winland.git
cd winland
pwsh scripts/dev.ps1
dotnet tool restore
```

`scripts/dev.ps1` points git at the hooks in `scripts/hooks` (see [Git hooks](#git-hooks)). `dotnet tool restore` installs Stryker.NET for mutation testing.

## Commands

| Command | What it does |
| --- | --- |
| `pwsh scripts/dev.ps1` | Point git at the hooks in `scripts/hooks`. Once per clone. |
| `pwsh -NoProfile -File scripts/check.ps1` | The lint: parses the PowerShell, rejects names that differ only by case, checks markdown links and the paths `AGENTS.md` names, and builds the solution. |
| `pwsh -NoProfile -File scripts/test.ps1` | Runs every unit test project side by side, as CI does. |
| `dotnet test tests/<project>` | The focused test for one project. |
| `pwsh -NoProfile -File scripts/smoke.ps1` | The smoke tests, on a real desktop. |
| `pwsh -NoProfile -File scripts/mutate.ps1` | Mutation testing (see [Mutation testing](#mutation-testing)). |
| `pwsh -NoProfile -File scripts/install.tests.ps1` | Tests the decisions in `scripts/install.ps1`. |
| `pwsh -NoProfile -File scripts/run.ps1` | Publishes this checkout and starts it in place of the running winland. `-Stop` stops it. |
| `npx -y skills@1.7.4 update -p` | Update the unpinned agent skills. Pinned ones (with a `ref` in `skills-lock.json`) change only when re-added with a newer `#<sha>`. |

Before opening a PR, run the checks CI runs: `pwsh -NoProfile -File scripts/check.ps1`, then `pwsh -NoProfile -File scripts/test.ps1`.

The Daemon and CLI use the pipe `\\.\pipe\winland-<SessionId>` for their Windows session. Set `WINLAND_PIPE` to use a different name for both. The run script replaces Winland only in its own session. With `WINLAND_PIPE` set, it limits that to builds it started under `%LOCALAPPDATA%\winland-dev`.

## Git hooks

The hooks are tracked in `scripts/hooks`, and git runs them from there. `pwsh scripts/dev.ps1` sets `core.hooksPath` to `scripts/hooks` once per clone. A checkout that still points `core.hooksPath` at `.git/hooks` is fixed by running it again.

| Hook | When it runs | What it does |
| --- | --- | --- |
| pre-push | When you push | Runs the lint and then the unit tests on the tip of each branch you push, in a temporary worktree. A failed lint skips the tests. It skips a tag and a branch deletion. Without `pwsh` on `PATH` it says so and lets the push through. `git push --no-verify` skips it. |
| post-merge | After a pull or merge updates `main` | Deletes a local branch whose GitHub branch is gone and whose changes are already on `main` or a `release/*` branch. It never deletes `main`, `master`, `release/*`, or a branch checked out in a worktree. |

## CI

Pull requests run `.github/workflows/ci.yml`, and so does the push a merge makes to `main`. The push run also fills the NuGet cache, which a pull request can read only when its own branch or the default branch saved it. A newer run of the same pull request, or of `main`, cancels the older one. The ruleset requires one check, **All required checks pass (v1)**. To change what it covers, change `ci.yml` and `.github/rulesets/protect-main.json` in the same pull request, and bump the `(v1)`.

Every lane except Mutation is a step in one Windows job, because each job pays its own setup.

| Lane | What it does | When it runs |
| --- | --- | --- |
| History | Fails when the branch shares no commit with `main`. | Always. |
| Lint | `scripts/check.ps1`, then `actionlint` on `.github/workflows/*.yml`. | Always. |
| Test | `scripts/test.ps1 -NoBuild` on the lint's build: every `*.Tests.csproj` under `tests/` except `*.Smoke.Tests.csproj`. | Always, except a pull request that changes only docs and repo metadata (`*.md`, `docs/`, `.github/ISSUE_TEMPLATE/`, `.github/rulesets/`, `.gitignore`, `.gitattributes`, `LICENSE*`). |
| Mutation | One job per mutation run, then a score gate. | Only when CI is dispatched by hand with **Release run** on. |

- A test project that fails runs once more on the same build. If it passes the second time, `scripts/test.ps1` names the flaky tests in a `FLAKY:` line, and CI adds a warning and a job summary line. A flaky test is a bug. Fix it in its own pull request, usually by waiting for a condition instead of a fixed time.
- The unit tests that open real windows are in one xunit collection (`DesktopCollection` in `tests/Winland.Infrastructure.Tests`) that runs alone. Stop the Daemon before running them, since it can adopt the windows they open.
- Smoke tests run only when someone dispatches the `Smoke tests` workflow by hand. `scripts/smoke.ps1` runs them locally. It sets `WINLAND_SMOKE=1`, without which every smoke test skips itself.
- The Workflows workflow runs zizmor and actionlint when `.github/` changes. PR Title checks the pull request title, and Branch Name checks the branch name as a reminder only.

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

release-please keeps a release PR open that collects the changes merged into `main`, with the next version and its changelog. Merging that PR tags the release, and a job builds and uploads `Winland-win-x64.zip`, `version.json`, and `SHA256SUMS` before publishing it. See [releasing.md](releasing.md).

- **Pick the version yourself:** when merging a PR, add `Release-As: x.y.z` to the end of the squash commit message.
- **The release PR's checks don't start:** without the `RELEASE_PLEASE_TOKEN` secret, close and reopen the release PR right before merging it.
