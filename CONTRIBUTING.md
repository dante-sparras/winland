# Contributing

## Branches

- `main` is the only integration branch.
- Create `fix/…`, `feat/…`, `docs/…`, `test/…`, or `refactor/…` from current `main`.
- One logical change per pull request.
- Commit messages follow Conventional Commits: `fix(scope): summary`.

## Before you open the pull request

1. Rebase onto current `main`.
2. Run the lint and the tests:

   ```powershell
   pwsh -NoProfile -File scripts/check.ps1
   pwsh -NoProfile -File scripts/test.ps1
   ```

3. Fill in the pull request template.

`pwsh scripts/dev.ps1` installs both hooks. You can install them on their own.

The pre-push hook runs the lint against the tip of each branch you push, not against every commit in the push:

```powershell
pwsh -NoProfile -File scripts/check.ps1 -InstallHook
```

The post-merge hook runs after a pull or merge updates `main`. It deletes a local feature branch whose GitHub head branch is gone when those changes are already on `main` or on a `release/*` line. It leaves `main`, `master`, `release/*`, a branch checked out in a worktree, and a branch that still has work that has not landed.

```powershell
pwsh -NoProfile -File scripts/install-post-merge.ps1
```

Maintainers merge with **Rebase and merge** so your commits stay yours. GitHub deletes the head branch when the pull request merges.

## What CI runs

Pull requests and pushes to `main` run `.github/workflows/ci.yml`. The required check is **All required checks pass (v1)**. That job is the only name the ruleset knows.

Lint compiles the Python in this repo, parses the PowerShell, and builds a solution at the repo root when one exists. The test lane runs `dotnet test` on each `*.Tests.csproj` under `tests/`, and it skips `*.Smoke.Tests.csproj`. Until a unit test project exists, that lane passes. A pull request that does not touch .NET files skips the test lane. A push to `main`, and a manual run, run the test lane.

Mutation testing is the slow lane. It runs only when CI is dispatched with **Release run** turned on, and it passes until a unit test project exists. On that release run, a lane that skipped is a failure.

## Rules on `main` and `release/*`

`main` and `release/*` reject a direct push, a force-push, and a deletion. A pull request can merge, squash, or rebase. It does not have to contain the latest `main`. The next push to `main` runs CI again, and that run is not cancelled by a later push.

Approving reviews required: **0**. This account has one maintainer, so a count of 1 would make every pull request unmergeable. The aggregate check is the gate.

Code owners are required on the paths in `.github/CODEOWNERS`. The owner is `@dante-sparras`, and a pull request author cannot approve their own pull request. Repository admins (role id 5) may bypass the rules when they merge a pull request. They still cannot push straight to `main`. That bypass is how a code-owned file can land. Turn the bypass off when a second maintainer can approve those paths.

The ruleset files live in `.github/rulesets/`. Changing the required check means changing `ci.yml` and `protect-main.json` in the same pull request, and bumping the `(v1)` name.

## Releases

Tags and patch branches are described in `docs/releasing.md`. There is no publish workflow in this repo yet.
