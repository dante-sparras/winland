# Contributing

## Set up

You need PowerShell 7 (`pwsh`), Git, and the .NET SDK version pinned in `global.json`.

Run `pwsh scripts/dev.ps1` once after cloning. It points git at the repo's two git hooks (see [Git hooks](#git-hooks)).

## Read this first

There is one maintainer, and review time is limited. Opening a pull request does not oblige a review or a merge. It can be closed, deferred, or answered with a smaller change. Meeting this guide makes a pull request eligible for review.

The same requirements apply to a draft. Draft status does not postpone them.

A focused bug fix, a reliability fix, and maintenance fit this repo. A feature or an intentional change to behavior waits for the approval under [Establish the problem and scope first](#establish-the-problem-and-scope-first).

Search open and closed issues and pull requests before starting. When an open pull request already covers the change, continue that one.

## Establish the problem and scope first

A feature, and any intentional change to how Winland behaves, needs a maintainer comment that approves the direction and the scope. Link that comment in the pull request. A link to the issue is not the approval. Calling the change a bug fix does not remove this requirement when the change is a behavior choice. Accepting the problem does not approve every implementation.

A substantial bug fix links an issue a maintainer has triaged. `ready-for-agent` means the behavior, how to see it, and what is out of scope are settled. See `docs/agents/issue-tracker.md`.

A very small, focused fix for an obvious bug can be opened with no prior issue. The pull request says what the defect is and why this exception fits. There is no line-count or file-count cutoff. A few lines that change a default can still be a behavior change that needs approval.

A focused option for a behavior Winland already has can also be opened without that approval. The pull request names the existing behavior, what the option controls, and why the effect stays inside that behavior. A new workflow, a new default, or a wider behavior choice still needs approval. One problem and the evidence requirement still apply.

When the existing config or a dispatcher already does the job, a hard-to-find behavior is a docs problem. Describe that problem. It does not by itself justify a new feature.

## Solve one underlying problem

One pull request solves one underlying problem. Count problems, not linked issues. Several issues can be the same defect.

Include the code, tests, and docs that problem needs. An adjacent cleanup or a second fix is its own pull request unless the first fix cannot land without it. A large diff is not itself a second problem.

The maintainer may batch related fixes. Anyone else splits them.

## Show what you checked

Say how you established the problem, what you ran, and what you observed. Include the reproduction, the environment, the commands, and the results. Say what you could not check. A checkbox or "tests pass" does not show that the change works.

Match the check to the change. Run the tests that cover the behavior. `pwsh -NoProfile -File scripts/check.ps1` is the lint. `dotnet test` on the test project that covers the change is the focused test. CI runs `scripts/test.ps1` across the unit projects, so a repo-wide run is not required to satisfy this guide. Mutation testing and smoke tests stay on their own lanes.

A change someone can see needs before-and-after screenshots. Motion, timing, or a gesture needs a short recording. Upload those on the pull request. Do not commit them.

## Make a change

1. Start from current `main`. Prefix the branch with `fix/`, `feat/`, `docs/`, `test/`, `refactor/`, or `chore/`.

   ```powershell
   git switch main
   git pull
   git switch -c fix/short-description
   ```

2. Solve the one problem. The pull request title is the Conventional Commit a squash uses (see [Commit messages](#commit-messages)). When the branch will be rebase-merged, each commit on it uses that form too.
3. The branch does not have to contain the latest `main`. Rebase onto `origin/main` when you want a rebase merge to apply, or when you are keeping a series of commits. After rebasing a branch you already pushed, push again with `git push --force-with-lease`.

   ```powershell
   git fetch origin main
   git rebase origin/main
   ```

4. Run the lint and the focused tests. Record what you ran and what you saw in the pull request.
5. Push the branch and open a pull request (see [Pull requests](#pull-requests)). An agent opens one only when the maintainer asked.
6. Wait for CI. Fix a failure with new commits on the same branch. When **All required checks pass (v1)** is green, the maintainer squash-merges. Rebase and merge keeps a series of commits. GitHub deletes the branch after the merge. See [Rules on `main` and `release/*`](#rules-on-main-and-release).

## Commit messages

Use Conventional Commits: `type(scope): summary`. The scope is optional and names the area you changed. The summary is plain language about the behavior, for example `fix(workspaces): an empty workspace keeps its name`.

| Type | Use for |
| --- | --- |
| `fix` | A bug fix. |
| `feat` | A new feature. |
| `docs` | Documentation only. |
| `test` | Adding or changing tests. |
| `refactor` | Restructuring code with no behavior change. |
| `chore` | Build, CI, and dependency updates. Dependabot uses `chore(actions)` and `chore(deps)`. |

A squash uses the pull request title as the commit subject and appends the pull request number. An agent ends the pull request body with the model and harness that did the work.

## Pull requests

Fill in the template.

- **Problem.** What is wrong, what you expected, and the reproduction or environment where that matters.
- **Change.** How this fixes that problem. When the diff spans projects, say why each part is the same fix.
- **Scope and approval.** Link the maintainer comment that approves the direction and the scope, or the triaged bug. For an exception in [Establish the problem and scope first](#establish-the-problem-and-scope-first), say why it fits.
- **Verification.** The commands you ran, what you observed, and what you could not check. Screenshots and recordings are uploaded on the pull request, not committed.
- **Issue.** Put `Fixes #123` in the body when this closes an issue. A rebase merge also needs that line in a commit on the branch, because those commits are what land.
- **Review.** Fix problems with new commits on the same branch.

## Report a problem

Open a GitHub issue and use the bug or feature form. A bug says what you ran, what you saw, and what you expected. A feature says the behavior you want and what is out of scope. The feature issue is the proposal. Work starts after a maintainer comment approves the direction and the scope.

This repo has no GitHub Discussions. Proposals stay on issues.

## Documentation

Most code changes do not need a new document. The code, the tests, and `CONTEXT.md` already record the implementation.

Update a document when the change makes it false, and edit the existing text:

- A standing decision is an ADR in `docs/adr/`. Rewrite that ADR when the decision changes, and keep its line in `docs/adr/README.md` in step.
- Honored behavior lives in `docs/behavior.md`.
- A procedure lives in this file or in `docs/releasing.md`.

Leave the document alone when reading the relevant code answers the question. Do not add a file catalog, a narration of control flow, or a second write-up of a change that already landed.

Do not commit implementation plans, research notes, or screenshots that exist only for a pull request.

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

Pull requests run `.github/workflows/ci.yml`, and so does the push a merge makes to `main`. That run checks `main` after the merge and fills the NuGet cache, which a pull request can read only when its own branch or the default branch saved it. A pull request run cancels an older run of that same pull request, and a push to `main` cancels an older run on `main`, so only the newest commit becomes the dev build. A run started by hand or by a release is never cancelled. CI uses the .NET SDK the runner image already has when it is the one `global.json` asks for, and installs it otherwise. A pull request can merge only when the check **All required checks pass (v1)** is green. It is the only check the ruleset requires.

Every lane except Mutation is a step in that one Windows job, because each job pays its own setup. The lint step builds the solution once, and the test step runs every test project side by side on that build (`scripts/test.ps1 -NoBuild`). The pre-push hook runs the same lint and tests locally, so CI should rarely be the first to find a failure.

A test project that fails runs once more on the same build, locally and in CI. If it fails again, the run fails. If it passes, the run passes, and `scripts/test.ps1` names the tests that failed as flaky: a `FLAKY:` line, and in CI a warning annotation and a line in the job summary. A flaky test is a bug. Fix it in its own pull request, usually by waiting for a condition instead of a fixed time, or by giving it more time on a busy machine. Do not leave it to the retry. To hunt for flaky tests, run the test projects many times at once, so the machine is busy as a CI runner is.

| Lane | What it does | When it runs |
| --- | --- | --- |
| History | Fails when the branch shares no commit with `main`. Merging such a branch would add a second root commit to `main` and break `git blame`. | Always. |
| Lint | Parses the PowerShell, rejects file or folder names that differ only by case, builds the solution at the repo root if there is one, and runs `actionlint` on `.github/workflows/*.yml`. | Always. |
| Test | Runs `dotnet test` on each `*.Tests.csproj` under `tests/`, except `*.Smoke.Tests.csproj`. | On a push to `main`, and on manual and release runs. On a pull request, unless every changed file is documentation or repo metadata: `*.md`, `docs/`, `.github/ISSUE_TEMPLATE/`, `.github/PULL_REQUEST_TEMPLATE.md`, `.github/rulesets/`, `.gitignore`, `.gitattributes`, or `LICENSE*`. Any other change, including one to a workflow or a script, runs it. |
| Mutation | Mutation testing, one job per mutation run, then a score gate (see [Mutation testing](#mutation-testing)). It is the slow lane, and its jobs run beside the others. | Only when CI is dispatched by hand with **Release run** turned on. A release does not run it. |

- Test and Mutation pass until a unit test project exists.
- The unit tests that open real windows are in one xunit collection (`DesktopCollection` in `tests/Winland.Infrastructure.Tests`) that runs alone, one test at a time. The ones a running Winland would disturb skip while its pipe `\\.\pipe\winland` exists, so run them with the daemon stopped. CI has no daemon and runs them.
- A release runs Test, and a failure fails the release. A failure in any mutation job, or in the gate, fails Mutation.
- The check job has a 60-minute timeout, and each mutation job has its own 60.
- Smoke tests (`*.Smoke.Tests.csproj`) run only when someone dispatches the `Smoke tests` workflow by hand (`.github/workflows/smoke.yml`). A release run does not execute them, and they are not a required check. `scripts/smoke.ps1` runs them locally. It sets `WINLAND_SMOKE=1`, without which every smoke test skips itself, and it fails when every test in a smoke project was skipped, for example on a host with no desktop.

The Lint lane also checks links in tracked markdown files. A relative link or image must point at a file or folder that exists with exactly that spelling (Linux is case-sensitive). It also checks that each repo path `AGENTS.md` names in backticks exists, except a path in a gitignored reference checkout under `docs/agents/` and the optional files `docs/agents/domain.md` lists. A failure prints as `Broken link: <file>: <target>`.

### Mutation testing

`scripts/mutate.ps1` runs [Stryker.NET](https://stryker-mutator.io/docs/stryker-net/introduction/) (the version in `.config/dotnet-tools.json`; run `dotnet tool restore` once). It has one lane per unit test project, and each lane mutates the one project that test project tests. A long lane is split into shards, each mutating a share of the project's files, so every CI job fits its timeout on a 4-vCPU runner. A lane or one shard of it is a mutation run.

```powershell
pwsh -NoProfile -File scripts/mutate.ps1                  # every run, then the gate
pwsh -NoProfile -File scripts/mutate.ps1 -Project Daemon  # one lane, then the gate
pwsh -NoProfile -File scripts/mutate.ps1 -List            # the run names CI makes jobs from
pwsh -NoProfile -File scripts/mutate.ps1 -Run Domain-2    # one run and no gate, as a CI job runs it
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
- **Squash and rebase are the methods to use.** Squash is how a pull request normally lands: the title becomes the one commit on `main`, and the body becomes that commit's message. Rebase keeps a series of commits. `.github/rulesets/protect-main.json` allows those two. The live ruleset (id 25152346) matches it. A pull request does not need to contain the latest `main`. The merge is a push to `main`, which runs CI again. A later merge cancels that run, because the newer commit contains it. A release run tests the exact commit it ships.
- **The CI check is the gate.** **All required checks pass (v1)** must be green. Approving reviews required: **0**. There is one maintainer, so requiring 1 would make every pull request unmergeable.
- **Admins can bypass the rules when merging a pull request.** Repository admins (role id 5) can bypass them. The bypass covers every rule, including the CI check, so an admin can merge a pull request whose CI is red. Do not. Merging a red check is that bypass. Admins still cannot push straight to `main`.
- **Release tags are locked.** Tags matching `v*`, `rc.*`, and `abandoned-rc.*` cannot be created, moved, or deleted except by a repository admin or the GitHub Actions app. `scripts/release.ps1 publish` creates the receipt tag. See `docs/releasing.md`.

The ruleset files are in `.github/rulesets/`. Editing a file does not change the live rule. To change the required check, change `ci.yml` and `protect-main.json` in the same pull request, and bump the `(v1)` in the check name.

## Releases

The committed version is `0.0.0`. Cut a release with `scripts/release.ps1`. The Stable Release workflow uploads the draft and stops. Publish with `scripts/release.ps1 publish` after that run is green. See `docs/releasing.md`. Do not press Publish in the GitHub UI.
