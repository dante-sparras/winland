# Contributing

## Set up

You need PowerShell 7 (`pwsh`), Git, and the .NET SDK version pinned in `global.json`.

Run `pwsh scripts/dev.ps1` once after cloning. It installs the repo's two git hooks (see [Git hooks](#git-hooks)). It also clones the reference checkout and fetches its docs, as `AGENTS.md` describes.

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

`scripts/dev.ps1` installs both hooks. To install one on its own, run its command below.

| Hook | When it runs | What it does | Install on its own |
| --- | --- | --- | --- |
| pre-push | When you push | Runs the lint on the tip of each branch you push, not on every commit in the push. | `pwsh -NoProfile -File scripts/check.ps1 -InstallHook` |
| post-merge | After a pull or merge updates `main` | Deletes local feature branches that are finished (see below). | `pwsh -NoProfile -File scripts/install-post-merge.ps1` |

The post-merge hook deletes a local branch only when both are true:

- Its GitHub branch is gone.
- Its changes are already on `main` or on a `release/*` branch.

It never deletes `main`, `master`, `release/*`, a branch checked out in a worktree, or a branch with work that has not landed.

## CI

Pull requests and pushes to `main` run `.github/workflows/ci.yml`. A pull request can merge only when the check **All required checks pass (v1)** is green. It is the only check the ruleset requires.

| Lane | What it does | When it runs |
| --- | --- | --- |
| Detect | Looks at the pull request's diff and decides whether the Test lane is needed. | Always. |
| Lint | Parses the PowerShell, rejects file or folder names that differ only by case, builds the solution at the repo root if there is one, and runs `actionlint` on `.github/workflows/*.yml`. | Always. |
| History | Fails when the branch shares no commit with `main`. Merging such a branch would add a second root commit to `main` and break `git blame`. | Always. |
| Test | Runs `dotnet test` on each `*.Tests.csproj` under `tests/`, except `*.Smoke.Tests.csproj`. | On pushes to `main` and on manual runs. On a pull request, unless every changed file is documentation or repo metadata: `*.md`, `docs/`, `.github/ISSUE_TEMPLATE/`, `.github/PULL_REQUEST_TEMPLATE.md`, `.github/CODEOWNERS`, `.github/rulesets/`, `.gitignore`, `.gitattributes`, or `LICENSE*`. Any other change, including one to a workflow or a script, runs it. |
| Mutation | Mutation testing. It is the slow lane. | Only when CI is dispatched with **Release run** turned on. |

- Test and Mutation pass until a unit test project exists.
- On a release run, a lane that skipped counts as a failure.
- Every job has a timeout: Detect and History 5 minutes, Lint 20, Test 40, Mutation 60.

## Rules on `main` and `release/*`

- **No direct changes.** Direct pushes, force-pushes, and deletions are rejected. Everything lands through a pull request.
- **Rebase and merge is the only allowed method.** The ruleset and the repository settings both reject a merge commit and a squash. A pull request does not need to contain the latest `main`. The merge is a push to `main`, which runs CI again, and a later push does not cancel that run.
- **The CI check is the gate.** **All required checks pass (v1)** must be green. Approving reviews required: **0**. There is one maintainer, so requiring 1 would make every pull request unmergeable.
- **Code owners must review code-owned paths.** The paths are in `.github/CODEOWNERS`, and the owner is `@dante-sparras`. A pull request author cannot approve their own pull request.
- **Admins can bypass the rules when merging a pull request.** Repository admins (role id 5) can bypass them, which is how a code-owned file lands without a second approver. The bypass covers every rule, including the CI check, so an admin can merge a pull request whose CI is red. Do not. Admins still cannot push straight to `main`. Turn the bypass off once a second maintainer can approve those paths.
- **Release tags are locked.** Tags matching `v*`, `rc.*`, and `abandoned-rc.*` cannot be created, moved, or deleted except by a repository admin. See `docs/releasing.md`.

The ruleset files are in `.github/rulesets/`. To change the required check, change `ci.yml` and `protect-main.json` in the same pull request, and bump the `(v1)` in the check name.

## Releases

Tags and patch branches are described in `docs/releasing.md`. There is no publish workflow in this repo yet.
