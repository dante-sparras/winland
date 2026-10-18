# Contributing

## Set up

You need PowerShell 7 (`pwsh`), Git, and the .NET SDK version pinned in `global.json`.

Run `pwsh scripts/dev.ps1` once after cloning. It points git at the repo's two git hooks (see [Git hooks](#git-hooks)). It also clones the reference checkout and fetches its docs, as `AGENTS.md` describes.

## Make a change

1. Start from current `main` and create a branch. Prefix its name with `fix/`, `feat/`, `docs/`, `test/`, `refactor/`, or `chore/`.

   ```powershell
   git switch main
   git pull
   git switch -c fix/short-description
   ```

2. Make one logical change. Commit as you go, and write each commit message as a Conventional Commit (see [Commit messages](#commit-messages)).
3. If `main` has moved, rebase onto it so CI tests what will land and the merge cannot undo newer fixes. If you already pushed the branch, push again with `git push --force-with-lease`.

   ```powershell
   git fetch origin main
   git rebase origin/main
   ```

4. Run the lint and the tests. Both must pass.

   ```powershell
   pwsh -NoProfile -File scripts/check.ps1
   pwsh -NoProfile -File scripts/test.ps1
   ```

5. Push the branch and open a pull request (see [Pull requests](#pull-requests)).
6. Wait for CI. If it fails, fix it with new commits on the same branch. When **All required checks pass (v1)** is green, a maintainer merges with **Rebase and merge**. Every commit on your branch lands on `main` as its own commit with its author, so keep the history clean. GitHub deletes the branch after the merge.

## Commit messages

Use Conventional Commits: `type(scope): summary`. The scope is optional and names the area you changed, for example `docs(contributing): explain the commit types`. Write the summary in the imperative, as what the commit does.

| Type | Use for |
| --- | --- |
| `fix` | A bug fix. |
| `feat` | A new feature. |
| `docs` | Documentation only. |
| `test` | Adding or changing tests. |
| `refactor` | Restructuring code with no behavior change. |
| `chore` | Build, CI, and dependency updates. Dependabot uses `chore(actions)` and `chore(deps)`. |

## Pull requests

- **Search first.** Look through open and closed pull requests and issues before you start. If an open pull request already covers the change, review or improve that one instead of opening a duplicate.
- **One logical change.** Do not mix a fix, a refactor, and a new feature in one pull request.
- **Title.** Use the same form as a commit message: `fix(scope): summary`. Nothing enforces it, but it keeps the list readable.
- **Description.** Fill in the template. Say what changed and why, and how to test it: the steps to reproduce a bug and what shows the fix works, or how to use a new feature. Link the issue with `Fixes #123` so it closes when the pull request merges.
- **Tests.** Once the repo has a test project, a bug fix comes with a test that fails without the fix and passes with it.
- **Review.** Fix problems with new commits on the same branch. Do not open a new pull request for the same change.

## Report a problem

Open a GitHub issue and pick the bug or feature form. Say what you ran, what you saw, and what you expected.

## Git hooks

The hooks are tracked in `scripts/hooks`, and git runs them from there. `pwsh scripts/dev.ps1` sets `core.hooksPath` to `scripts/hooks` once per clone, so there is nothing to install per hook. A checkout that still points `core.hooksPath` at `.git/hooks` is fixed by running `pwsh scripts/dev.ps1` again.

| Hook | When it runs | What it does |
| --- | --- | --- |
| pre-push | When you push | Runs the lint and then the unit tests (`scripts/test.ps1`) on the tip of each branch you push, not on every commit in the push. It checks the pushed commit in a temporary worktree, not your working copy. A failed lint skips the tests. It skips a tag and a branch deletion. If `pwsh` is not on `PATH`, it says so and lets the push through. `git push --no-verify` skips it. |
| post-merge | After a pull or merge updates `main` | Deletes local feature branches that are finished (see below). |

The post-merge hook deletes a local branch only when both are true:

- Its GitHub branch is gone.
- Its changes are already on `main` or on a `release/*` branch.

It never deletes `main`, `master`, `release/*`, a branch checked out in a worktree, or a branch with work that has not landed.

## CI

Pull requests run `.github/workflows/ci.yml`, and so does the push a merge makes to `main`. That run checks `main` after the merge and fills the NuGet cache, which a pull request can read only when its own branch or the default branch saved it. CI uses the .NET SDK the runner image already has when it is the one `global.json` asks for, and installs it otherwise. A pull request can merge only when the check **All required checks pass (v1)** is green. It is the only check the ruleset requires.

Every lane except Mutation is a step in that one Windows job, because each job pays its own setup. The lint step builds the solution once, and the test step runs every test project side by side on that build (`scripts/test.ps1 -NoBuild`). The pre-push hook runs the same lint and tests locally, so CI should rarely be the first to find a failure.

| Lane | What it does | When it runs |
| --- | --- | --- |
| History | Fails when the branch shares no commit with `main`. Merging such a branch would add a second root commit to `main` and break `git blame`. | Always. |
| Lint | Parses the PowerShell, rejects file or folder names that differ only by case, builds the solution at the repo root if there is one, and runs `actionlint` on `.github/workflows/*.yml`. | Always. |
| Test | Runs `dotnet test` on each `*.Tests.csproj` under `tests/`, except `*.Smoke.Tests.csproj`. | On manual and release runs. On a pull request, unless every changed file is documentation or repo metadata: `*.md`, `docs/`, `.github/ISSUE_TEMPLATE/`, `.github/PULL_REQUEST_TEMPLATE.md`, `.github/CODEOWNERS`, `.github/rulesets/`, `.gitignore`, `.gitattributes`, or `LICENSE*`. Any other change, including one to a workflow or a script, runs it. |
| Mutation | Mutation testing, one job per mutation run, then a score gate (see [Mutation testing](#mutation-testing)). It is the slow lane, and its jobs run beside the others. | Only on a release run: CI dispatched with **Release run** turned on, or called by a release workflow. |

- Test and Mutation pass until a unit test project exists.
- The unit tests that open real windows are in one xunit collection (`DesktopCollection` in `tests/Winland.Windows.Tests`) that runs alone, one test at a time. The ones a running Winland would disturb skip while its pipe `\\.\pipe\winland` exists, so run them with the daemon stopped. CI has no daemon and runs them.
- A release run always runs Test and Mutation, and a failure in either fails the run. A failure in any mutation job, or in the gate, fails Mutation.
- The check job has a 60-minute timeout, and each mutation job has its own 60.
- Smoke tests (`*.Smoke.Tests.csproj`) run only when someone dispatches the `Smoke tests` workflow by hand (`.github/workflows/smoke.yml`). A release run does not execute them, and they are not a required check. `scripts/smoke.ps1` runs them locally. It sets `WINLAND_SMOKE=1`, without which every smoke test skips itself, and it fails when every test in a smoke project was skipped, for example on a host with no desktop.

The Lint lane also checks links in tracked markdown files. A relative link or image must point at a file or folder that exists with exactly that spelling (Linux is case-sensitive). It also checks that each repo path `AGENTS.md` names in backticks exists, except a path in a gitignored reference checkout under `docs/agents/` and the optional files `docs/agents/domain.md` lists. A failure prints as `Broken link: <file>: <target>`.

### Mutation testing

`scripts/mutate.ps1` runs [Stryker.NET](https://stryker-mutator.io/docs/stryker-net/introduction/) (the version in `.config/dotnet-tools.json`; run `dotnet tool restore` once). It has one lane per unit test project, and each lane mutates the one project that test project tests. A long lane is split into shards, each mutating a share of the project's files, so every CI job fits its timeout on a 4-vCPU runner. A lane or one shard of it is a mutation run.

```powershell
pwsh -NoProfile -File scripts/mutate.ps1                  # every run, then the gate
pwsh -NoProfile -File scripts/mutate.ps1 -Project Daemon  # one lane, then the gate
pwsh -NoProfile -File scripts/mutate.ps1 -List            # the run names CI makes jobs from
pwsh -NoProfile -File scripts/mutate.ps1 -Run Core-2      # one run and no gate, as a CI job runs it
pwsh -NoProfile -File scripts/mutate.ps1 -Gate <folder>   # gate the run summaries under a folder
```

- The gate is `thresholds.break` in `stryker-config.json`, applied to each lane's whole score: the killed and timed-out mutants of all its shards over every mutant that compiled and was not ignored. A shard never passes or fails on its own score. In CI the `Mutation score gate` job adds up the summaries the run jobs upload.
- Each run writes `tests/<project>/StrykerOutput/<run>/reports/mutation-report.html`, which CI uploads as `mutation-report-<run>`.
- A file that drives Win32 or the shell directly, with no seam a unit test can reach without a real desktop, is left out in the lane's `Exclude` list in `scripts/mutate.ps1`, with the reason. A Win32-only member in a file that also holds tested logic is left out with a `// Stryker disable once all : <reason>` comment on it. Test the logic instead of leaving it out.
- The Windows lane leaves out the `Desktop` collection (`Category=Desktop`): Stryker runs several test processes at once, and windows they open at the same time fight over the foreground.
- Stryker's test host needs `DOTNET_ROOT` when the SDK is not installed where .NET looks by default, for example under mise: `$env:DOTNET_ROOT = "$env:LOCALAPPDATA\mise\dotnet-root"`. CI's `setup-dotnet` sets it.
- A new unit test project fails `scripts/mutate.ps1` until it has a lane.

## Rules on `main` and `release/*`

- **No direct changes.** Direct pushes, force-pushes, and deletions are rejected. Everything lands through a pull request.
- **Rebase and merge is the only allowed method.** The ruleset and the repository settings both reject a merge commit and a squash. A pull request does not need to contain the latest `main`, and the pull request is not tested again with it. The push to `main` that the merge makes runs CI. Two pull requests that each pass alone can still break `main` together. A release run tests the exact commit it ships, so such a break is caught before a release at the latest.
- **The CI check is the gate.** **All required checks pass (v1)** must be green. Approving reviews required: **0**. There is one maintainer, so requiring 1 would make every pull request unmergeable.
- **Code owners must review code-owned paths.** The paths are in `.github/CODEOWNERS`, and the owner is `@dante-sparras`. A pull request author cannot approve their own pull request.
- **Admins can bypass the rules when merging a pull request.** Repository admins (role id 5) can bypass them, which is how a code-owned file lands without a second approver. The bypass covers every rule, including the CI check, so an admin can merge a pull request whose CI is red. Do not. Admins still cannot push straight to `main`. Turn the bypass off once a second maintainer can approve those paths.
- **Release tags are locked.** Tags matching `v*`, `rc.*`, and `abandoned-rc.*` cannot be created, moved, or deleted except by a repository admin or the GitHub Actions app, which the release workflow uses to create the receipt tag. See `docs/releasing.md`.

The ruleset files are in `.github/rulesets/`. To change the required check, change `ci.yml` and `protect-main.json` in the same pull request, and bump the `(v1)` in the check name.

## Releases

The committed version is `0.0.0`. Cut a release with `scripts/release.ps1`. See `docs/releasing.md`. Do not press Publish in the GitHub UI.
