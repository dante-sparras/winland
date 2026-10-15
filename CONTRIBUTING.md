# Contributing

## Branches

- `main` is the only integration branch.
- Create `fix/…`, `feat/…`, `docs/…`, `test/…`, `refactor/…`, or `ci/…` from current `main`.
- One logical change per pull request.
- Commit messages follow Conventional Commits: `fix(scope): summary`.

## Before you open the pull request

1. Rebase onto current `main`.
2. Run the lint command and the test command below.
3. Fill in the pull request template.

Maintainers merge with **Rebase and merge** so your commits stay yours. Merge and squash stay available. Squash keeps one commit and drops the per-commit author. Head branches are deleted when the pull request merges.

## Commands

Lint. Warnings are already errors. The pre-push hook runs this once, against the tip, not against every commit in the push.

```powershell
dotnet build Winland.slnx --configuration Release
```

Tests. CI runs this on `windows-latest`. It does not set `WINLAND_SMOKE` or `WINLAND_FOCUS`. Those desktop checks stay local.

```powershell
dotnet test Winland.slnx --configuration Release --filter "FullyQualifiedName!~Winland.Smoke"
```

Install the hook. `pwsh dev.ps1` does this too.

```powershell
pwsh scripts/check.ps1 -InstallHook pre-push
```

## Reviews

The branch ruleset on `main` and `release/*` blocks deletion and force-push, and it requires a pull request. The only required status check is **All required checks pass (v1)**. A pull request does not have to contain the latest `main`. The same workflow runs again on the push to `main`, and that run is not cancelled by the next push.

Approving reviews required: 0. That is a one-maintainer repository. Requiring one approval would make a pull request unmergeable. `.github/CODEOWNERS` still requests a review on the trust boundary (workflows, the SDK pin, the version stamp, `SECURITY.md`). Repository admins can bypass the branch ruleset while merging a pull request, which is how a change on a code-owned path lands. The bypass does not allow a direct push.

When a required job is added, bump the `(v1)` name in `.github/workflows/ci.yml` and in `.github/rulesets/protect-main.json` in the same change.

## Releases

An unpacked build is `0.0.0-dev`. `version.ps1` prints `0.1.<commits on HEAD>` when you pack. Do not commit a version bump for a release.

A published version is an annotated tag `vX.Y.Z` on a commit that `main` or some `release/*` branch contains. The tag is the record of what shipped. A branch named `release/X.Y.Z` is not that record by itself.

There is no source-checkout update channel and no canary build. Desktop smoke stays out of pull-request CI.

Cut `release/X.Y.Z` only when a patch must ship without everything that has landed on `main`:

```powershell
git fetch origin tag vX.Y.Z
git switch -c release/X.Y.(Z+1) vX.Y.Z
git push -u origin release/X.Y.(Z+1)
```

Cherry-pick or open a pull request into that branch. Do not merge `main` into it. Merge the release branch back to `main` before the next release is cut from `main`.

Tags matching `v*`, `rc.*`, and `abandoned-rc.*` cannot be created, moved, or deleted except by a repository admin.
